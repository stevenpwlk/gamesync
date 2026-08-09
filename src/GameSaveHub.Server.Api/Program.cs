using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSaveHub.Contracts;
using GameSaveHub.Core;
using GameSaveHub.Server.Api;
using GameSaveHub.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<AuthenticationOptions>(builder.Configuration.GetSection("Authentication"));
builder.Services.Configure<FeatureGateOptions>(builder.Configuration.GetSection("FeatureGates"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ImmutableArtifactStore>();
builder.Services.AddDbContext<GameSaveHubDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GameSaveHub")));

var authentication = builder.Configuration.GetSection("Authentication").Get<AuthenticationOptions>()
    ?? throw new InvalidOperationException("Configuration Authentication absente.");
if (Encoding.UTF8.GetByteCount(authentication.SigningKey) < 32 || authentication.SigningKey.StartsWith("REPLACE_", StringComparison.Ordinal))
{
    throw new InvalidOperationException("Authentication:SigningKey doit contenir au moins 32 octets aléatoires.");
}

var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(authentication.SigningKey));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = authentication.Issuer,
            ValidAudience = authentication.Audience,
            IssuerSigningKey = signingKey,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var deviceId))
                {
                    context.Fail("Identifiant d'appareil absent.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<GameSaveHubDbContext>();
                var active = await db.Devices.AnyAsync(x => x.Id == deviceId && x.RevokedAtUtc == null, context.HttpContext.RequestAborted);
                if (!active) context.Fail("Appareil révoqué.");
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ApiError("authentication_required", "Authentification d'appareil requise.", context.HttpContext.TraceIdentifier));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ApiError("forbidden", "Accès interdit.", context.HttpContext.TraceIdentifier));
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddHostedService<PublicationReconciler>();
builder.Services.AddHostedService<SessionWatchdog>();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Correlation-Id"] = context.TraceIdentifier;
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        ApiLog.Unhandled(app.Logger, context.TraceIdentifier, exception);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(new ApiError("internal_error", "Erreur interne.", context.TraceIdentifier));
        }
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") &&
        (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)))
    {
        var key = context.Request.Headers["Idempotency-Key"].ToString().Trim();
        if (key.Length is < 8 or > 128)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ApiError("idempotency_key_required", "En-tête Idempotency-Key requis (8 à 128 caractères).", context.TraceIdentifier));
            return;
        }
    }
    await next();
});
app.MapHealthChecks("/healthz");

app.MapPost("/api/v1/enrollments/redeem", async (
    EnrollmentRedeemRequest request,
    GameSaveHubDbContext db,
    TimeProvider clock,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.DeviceName))
        return Error(context, 400, "invalid_request", "Code et nom d'appareil requis.");

    try
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(request.PublicKeyPem);
        if (ecdsa.KeySize != 256) return Error(context, 400, "invalid_public_key", "Une clé ECDSA P-256 est requise.");
    }
    catch (CryptographicException)
    {
        return Error(context, 400, "invalid_public_key", "Clé publique ECDSA invalide.");
    }

    var codeHash = Sha256(request.Code.Trim());
    var now = clock.GetUtcNow();
    var enrollment = await db.Enrollments.SingleOrDefaultAsync(x => x.CodeHash == codeHash, cancellationToken);
    if (enrollment is null || enrollment.ExpiresAtUtc <= now || enrollment.RedeemedAtUtc is not null)
        return Error(context, 401, "invalid_enrollment", "Invitation invalide, expirée ou déjà utilisée.");

    var device = new DeviceEntity
    {
        Id = Guid.NewGuid(),
        Name = request.DeviceName.Trim(),
        PublicKeyPem = request.PublicKeyPem.Trim(),
        EnrolledAtUtc = now
    };
    db.Devices.Add(device);
    enrollment.RedeemedAtUtc = now;
    enrollment.DeviceId = device.Id;
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new EnrollmentRedeemResponse(device.Id));
});

app.MapPost("/api/v1/auth/challenges", async (
    AuthChallengeRequest request,
    GameSaveHubDbContext db,
    TimeProvider clock,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var device = await db.Devices.FindAsync([request.DeviceId], cancellationToken);
    if (device is null || device.RevokedAtUtc is not null)
        return Error(context, 401, "device_unknown", "Appareil inconnu ou révoqué.");

    var challenge = new AuthChallengeEntity
    {
        Id = Guid.NewGuid(),
        DeviceId = device.Id,
        Nonce = RandomNumberGenerator.GetBytes(32),
        ExpiresAtUtc = clock.GetUtcNow().AddMinutes(2)
    };
    db.AuthChallenges.Add(challenge);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new AuthChallengeResponse(challenge.Id, Convert.ToBase64String(challenge.Nonce), challenge.ExpiresAtUtc));
});

app.MapPost("/api/v1/auth/tokens", async (
    AuthTokenRequest request,
    GameSaveHubDbContext db,
    TimeProvider clock,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var now = clock.GetUtcNow();
    var device = await db.Devices.FindAsync([request.DeviceId], cancellationToken);
    var challenge = await db.AuthChallenges.FindAsync([request.ChallengeId], cancellationToken);
    if (device is null || device.RevokedAtUtc is not null || challenge is null || challenge.DeviceId != device.Id ||
        challenge.UsedAtUtc is not null || challenge.ExpiresAtUtc <= now)
        return Error(context, 401, "challenge_invalid", "Challenge invalide ou expiré.");

    bool signatureValid;
    try
    {
        using var verifier = ECDsa.Create();
        verifier.ImportFromPem(device.PublicKeyPem);
        signatureValid = verifier.VerifyData(challenge.Nonce, Convert.FromBase64String(request.Signature), HashAlgorithmName.SHA256);
    }
    catch (FormatException)
    {
        signatureValid = false;
    }

    if (!signatureValid) return Error(context, 401, "signature_invalid", "Signature invalide.");
    challenge.UsedAtUtc = now;
    await db.SaveChangesAsync(cancellationToken);

    var expires = now.AddMinutes(15);
    var token = new JwtSecurityToken(
        authentication.Issuer,
        authentication.Audience,
        [new Claim(JwtRegisteredClaimNames.Sub, device.Id.ToString("D")), new Claim("device_name", device.Name)],
        now.UtcDateTime,
        expires.UtcDateTime,
        new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new AuthTokenResponse(new JwtSecurityTokenHandler().WriteToken(token), expires));
});

var protectedApi = app.MapGroup("/api/v1").RequireAuthorization();

protectedApi.MapGet("/worlds", async (
    GameSaveHubDbContext db,
    CancellationToken cancellationToken) =>
{
    var worlds = await db.Worlds.OrderBy(world => world.Name).ThenBy(world => world.Id).ToListAsync(cancellationToken);
    var activeSessions = await db.Sessions
        .Where(session => session.ReleasedAtUtc == null)
        .Select(session => new { session.WorldId, session.State })
        .ToListAsync(cancellationToken);
    var stateByWorld = activeSessions
        .GroupBy(item => item.WorldId)
        .ToDictionary(group => group.Key, group => group.First().State.ToString());

    return Results.Ok(worlds.Select(world => new WorldCatalogItemResponse(
        world.Id,
        world.Name,
        stateByWorld.GetValueOrDefault(world.Id, "Available"),
        world.CurrentVersionId,
        world.CurrentVersionId is not null)).ToArray());
});

protectedApi.MapGet("/worlds/{id:guid}/preview", async (
    Guid id,
    GameSaveHubDbContext db,
    ImmutableArtifactStore store,
    IOptions<StorageOptions> storage,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    var world = await db.Worlds.FindAsync([id], cancellationToken);
    if (world is null) return Error(context, 404, "world_not_found", "Monde introuvable.");

    var activeSession = await db.Sessions.SingleOrDefaultAsync(
        session => session.WorldId == id && session.ReleasedAtUtc == null,
        cancellationToken);
    var status = activeSession?.State.ToString() ?? "Available";

    if (world.CurrentVersionId is not Guid versionId)
    {
        return Results.Ok(new WorldPreviewResponse(
            world.Id, world.Name, status, null, null, null, null, null, [], false));
    }

    var version = await db.SaveVersions.FindAsync([versionId], cancellationToken);
    if (version is null) return Error(context, 409, "artifact_missing", "Version courante introuvable.");

    var path = store.GetObjectPath(version.Sha256);
    if (!File.Exists(path)) return Error(context, 409, "artifact_missing", "Objet immuable absent.");

    try
    {
        var summary = await ArtifactEnvelopeValidator.ReadSummaryAsync(
            path,
            storage.Value.MaxArtifactBytes,
            cancellationToken);
        return Results.Ok(new WorldPreviewResponse(
            world.Id,
            world.Name,
            status,
            version.Id,
            version.Sha256,
            summary.AdapterId,
            summary.DisplayName,
            summary.WorldSeed,
            summary.Players.Select(player => new WorldPreviewPlayerResponse(
                player.Id,
                player.Name,
                player.IsHost,
                player.InventoryId,
                player.EquipmentId)).ToArray(),
            true));
    }
    catch (InvalidOperationException)
    {
        return Error(context, 409, "artifact_invalid", "L'artefact courant est invalide ou corrompu.");
    }
});

protectedApi.MapGet("/worlds/{id:guid}/status", async (Guid id, GameSaveHubDbContext db, HttpContext context, CancellationToken cancellationToken) =>
{
    var world = await db.Worlds.FindAsync([id], cancellationToken);
    if (world is null) return Error(context, 404, "world_not_found", "Monde introuvable.");
    var session = await db.Sessions.SingleOrDefaultAsync(x => x.WorldId == id && x.ReleasedAtUtc == null, cancellationToken);
    SaveVersionEntity? currentVersion = null;
    if (world.CurrentVersionId is Guid currentVersionId)
        currentVersion = await db.SaveVersions.FindAsync([currentVersionId], cancellationToken);
    return Results.Ok(new WorldStatusResponse(
        world.Id,
        world.Name,
        session?.State.ToString() ?? "Available",
        world.CurrentVersionId,
        session?.Id,
        session is null
            ? null
            : new ActiveWorldSessionResponse(
                session.Id,
                session.DeviceId,
                session.PlayerName,
                session.State.ToString(),
                session.CreatedAtUtc,
                session.LastHeartbeatAtUtc),
        currentVersion is null
            ? null
            : new WorldLastActivityResponse(
                currentVersion.Id,
                currentVersion.PublishedByPlayerName,
                currentVersion.CreatedAtUtc)));
});

protectedApi.MapPost("/worlds/{id:guid}/acquire", async (
    Guid id,
    AcquireWorldRequest request,
    GameSaveHubDbContext db,
    IOptions<FeatureGateOptions> gates,
    TimeProvider clock,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (!gates.Value.AllowHostTransfer)
        return Error(context, 409, "host_transfer_not_validated", "Le transfert d'hôte reste bloqué par le feature gate de production.");
    if (!TryDeviceId(context.User, out var deviceId)) return Error(context, 401, "token_invalid", "Jeton d'appareil invalide.");
    var idempotencyKey = RequireIdempotencyKey(context);
    if (idempotencyKey is null) return Error(context, 400, "idempotency_key_required", "En-tête Idempotency-Key requis.");

    await db.Database.OpenConnectionAsync(cancellationToken);
    await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
    db.Database.UseTransaction(transaction);
    var route = $"worlds/{id:D}/acquire";
    var replay = await db.Idempotency.FindAsync([deviceId, idempotencyKey, route], cancellationToken);
    if (replay is not null)
    {
        await transaction.CommitAsync(cancellationToken);
        return Results.Content(replay.ResponseJson, "application/json", statusCode: replay.StatusCode);
    }

    var world = await db.Worlds.FindAsync([id], cancellationToken);
    if (world is null) return Error(context, 404, "world_not_found", "Monde introuvable.");
    if (world.CurrentVersionId != request.ExpectedVersionId)
        return Error(context, 409, "base_version_mismatch", "La version courante a changé.");
    if (await db.Sessions.AnyAsync(x => x.WorldId == id && x.ReleasedAtUtc == null, cancellationToken))
        return Error(context, 409, "world_locked", "Le monde est déjà verrouillé.");

    string? canonicalPlayerName = null;
    if (!string.IsNullOrWhiteSpace(request.PlayerName))
    {
        if (world.CurrentVersionId is not Guid versionId)
            return Error(context, 409, "artifact_missing", "Le monde ne possède aucune sauvegarde courante.");
        var version = await db.SaveVersions.FindAsync([versionId], cancellationToken);
        if (version is null)
            return Error(context, 409, "artifact_missing", "La version courante est introuvable.");
        var artifactPath = context.RequestServices.GetRequiredService<ImmutableArtifactStore>().GetObjectPath(version.Sha256);
        if (!File.Exists(artifactPath))
            return Error(context, 409, "artifact_missing", "L'artefact courant est absent.");
        var storageOptions = context.RequestServices.GetRequiredService<IOptions<StorageOptions>>().Value;
        ArtifactEnvelopeSummary summary;
        try
        {
            summary = await ArtifactEnvelopeValidator.ReadSummaryAsync(
                artifactPath,
                storageOptions.MaxArtifactBytes,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Error(context, 409, "artifact_invalid", "L'artefact courant est invalide.");
        }

        var identity = AcquisitionPlayerIdentityResolver.Resolve(
            request.PlayerName,
            summary.Players.Select(player => new WorldPreviewPlayerResponse(
                player.Id,
                player.Name,
                player.IsHost,
                player.InventoryId,
                player.EquipmentId)).ToArray());
        if (!identity.Success)
            return Error(context, 409, identity.Code, identity.Message);
        canonicalPlayerName = identity.CanonicalPlayerName;
    }

    var now = clock.GetUtcNow();
    var session = new SessionEntity
    {
        Id = Guid.NewGuid(),
        WorldId = id,
        DeviceId = deviceId,
        PlayerName = canonicalPlayerName,
        BaseVersionId = world.CurrentVersionId,
        State = WorldSessionState.Preparing,
        LastClientState = "acquired",
        CreatedAtUtc = now,
        LastHeartbeatAtUtc = now
    };
    db.Sessions.Add(session);
    var response = new AcquireWorldResponse(session.Id, world.CurrentVersionId, session.State.ToString());
    var json = JsonSerializer.Serialize(response);
    db.Idempotency.Add(new IdempotencyEntity
    {
        DeviceId = deviceId,
        Key = idempotencyKey,
        Route = route,
        StatusCode = 200,
        ResponseJson = json,
        CreatedAtUtc = now
    });
    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return Results.Content(json, "application/json");
});

protectedApi.MapPost("/sessions/{id:guid}/import-starting", async (Guid id, GameSaveHubDbContext db, TimeProvider clock, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = await OwnedSession(id, db, context, cancellationToken);
    if (session is null) return Error(context, 404, "session_not_found", "Session introuvable.");
    if (session.ImportStarted && session.State == WorldSessionState.InGame) return Results.NoContent();
    if (session.State is not WorldSessionState.Preparing and not WorldSessionState.Interrupted)
        return Error(context, 409, "invalid_session_state", "La session n'est pas en préparation ou en reprise.");
    session.ImportStarted = true;
    session.State = WorldSessionState.InGame;
    session.FailureCode = null;
    session.FailureMessage = null;
    session.LastHeartbeatAtUtc = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

protectedApi.MapGet("/sessions/{id:guid}/artifact", async (Guid id, GameSaveHubDbContext db, ImmutableArtifactStore store, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = await OwnedSession(id, db, context, cancellationToken);
    if (session is null) return Error(context, 404, "session_not_found", "Session introuvable.");
    if (session.BaseVersionId is null) return Results.NoContent();
    var version = await db.SaveVersions.FindAsync([session.BaseVersionId.Value], cancellationToken);
    if (version is null) return Error(context, 409, "artifact_missing", "Artefact de la version introuvable.");
    var path = store.GetObjectPath(version.Sha256);
    return File.Exists(path) ? Results.File(path, "application/octet-stream", enableRangeProcessing: true) : Error(context, 409, "artifact_missing", "Objet immuable absent.");
});

protectedApi.MapPost("/sessions/{id:guid}/heartbeat", async (Guid id, SessionHeartbeatRequest request, GameSaveHubDbContext db, TimeProvider clock, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = await OwnedSession(id, db, context, cancellationToken);
    if (session is null) return Error(context, 404, "session_not_found", "Session introuvable.");
    session.LastHeartbeatAtUtc = clock.GetUtcNow();
    session.LastClientState = request.ClientState[..Math.Min(request.ClientState.Length, 128)];
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

protectedApi.MapPost("/sessions/{id:guid}/uploads", async (Guid id, CreateUploadRequest request, GameSaveHubDbContext db, IOptions<StorageOptions> storage, TimeProvider clock, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = await OwnedSession(id, db, context, cancellationToken);
    if (session is null) return Error(context, 404, "session_not_found", "Session introuvable.");
    if (request.BaseVersionId != session.BaseVersionId) return Error(context, 409, "base_version_mismatch", "Version de base incorrecte.");
    if (request.Length <= 0 || request.Length > storage.Value.MaxArtifactBytes || request.ChunkSize <= 0 || request.ChunkSize > storage.Value.MaxChunkBytes || !IsSha256(request.Sha256))
        return Error(context, 400, "upload_manifest_invalid", "Manifeste d'upload invalide.");
    var existing = await db.Uploads.SingleOrDefaultAsync(x => x.SessionId == id && x.Sha256 == request.Sha256 && x.Length == request.Length, cancellationToken);
    if (existing is not null)
    {
        if (session.State is WorldSessionState.InGame or WorldSessionState.Interrupted)
        {
            session.State = WorldSessionState.UploadPending;
            session.FailureCode = null;
            session.FailureMessage = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        var chunks = await db.UploadChunks.Where(x => x.UploadId == existing.Id).Select(x => x.Index).OrderBy(x => x).ToListAsync(cancellationToken);
        return Results.Ok(new CreateUploadResponse(existing.Id, existing.ChunkSize, chunks));
    }
    if (session.State is WorldSessionState.InGame or WorldSessionState.Interrupted)
    {
        session.State = WorldSessionState.UploadPending;
        session.FailureCode = null;
        session.FailureMessage = null;
    }
    if (session.State != WorldSessionState.UploadPending) return Error(context, 409, "invalid_session_state", "Upload interdit dans cet état.");
    var upload = new UploadEntity
    {
        Id = Guid.NewGuid(),
        SessionId = id,
        BaseVersionId = request.BaseVersionId,
        Sha256 = request.Sha256.ToLowerInvariant(),
        Length = request.Length,
        ChunkSize = request.ChunkSize,
        CreatedAtUtc = clock.GetUtcNow()
    };
    db.Uploads.Add(upload);
    await db.SaveChangesAsync(cancellationToken);
    return Results.Ok(new CreateUploadResponse(upload.Id, upload.ChunkSize, []));
});

protectedApi.MapPut("/uploads/{id:guid}/chunks/{index:int}", async (Guid id, int index, HttpRequest request, GameSaveHubDbContext db, ImmutableArtifactStore store, TimeProvider clock, HttpContext context, CancellationToken cancellationToken) =>
{
    var upload = await db.Uploads.FindAsync([id], cancellationToken);
    if (upload is null) return Error(context, 404, "upload_not_found", "Upload introuvable.");
    var session = await OwnedSession(upload.SessionId, db, context, cancellationToken);
    if (session is null || upload.CommittedAtUtc is not null) return Error(context, 409, "upload_closed", "Upload fermé.");
    var chunkCount = checked((int)((upload.Length + upload.ChunkSize - 1) / upload.ChunkSize));
    if (index < 0 || index >= chunkCount) return Error(context, 400, "chunk_index_invalid", "Index de chunk invalide.");
    var expectedMaximum = index == chunkCount - 1 ? checked((int)(upload.Length - (long)index * upload.ChunkSize)) : upload.ChunkSize;
    var result = await store.PutChunkAsync(upload.Id, index, request.Body, expectedMaximum, cancellationToken);
    var entity = await db.UploadChunks.FindAsync([id, index], cancellationToken);
    if (entity is null)
    {
        db.UploadChunks.Add(new UploadChunkEntity { UploadId = id, Index = index, Length = result.Length, Sha256 = result.Sha256, ReceivedAtUtc = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
    else if (entity.Length != result.Length || entity.Sha256 != result.Sha256)
    {
        return Error(context, 409, "chunk_conflict", "Chunk déjà reçu avec un autre contenu.");
    }
    return Results.NoContent();
});

protectedApi.MapPost("/uploads/{id:guid}/commit", async (Guid id, GameSaveHubDbContext db, ImmutableArtifactStore store, TimeProvider clock, HttpContext context, CancellationToken cancellationToken) =>
{
    var upload = await db.Uploads.FindAsync([id], cancellationToken);
    if (upload is null) return Error(context, 404, "upload_not_found", "Upload introuvable.");
    var session = await AnyOwnedSession(upload.SessionId, db, context, cancellationToken);
    if (session is null) return Error(context, 404, "session_not_found", "Session introuvable.");
    if (upload.ResultVersionId is Guid priorVersion)
    {
        return Results.Ok(new CommitUploadResponse(priorVersion, upload.Sha256, upload.Length));
    }
    if (session.State is not WorldSessionState.UploadPending and not WorldSessionState.Publishing)
        return Error(context, 409, "invalid_session_state", "Session non prête à publier.");
    if (session.State == WorldSessionState.UploadPending)
    {
        session.State = WorldSessionState.Publishing;
        await db.SaveChangesAsync(cancellationToken);
    }
    await store.AssembleAndPublishAsync(upload, cancellationToken);

    await db.Database.OpenConnectionAsync(cancellationToken);
    await using var transaction = ((SqliteConnection)db.Database.GetDbConnection()).BeginTransaction(deferred: false);
    db.Database.UseTransaction(transaction);
    var world = await db.Worlds.FindAsync([session.WorldId], cancellationToken) ?? throw new InvalidOperationException("Monde absent.");
    if (world.CurrentVersionId != upload.BaseVersionId) return Error(context, 409, "base_version_mismatch", "La version courante a changé.");
    var now = clock.GetUtcNow();
    var version = new SaveVersionEntity
    {
        Id = Guid.NewGuid(),
        WorldId = world.Id,
        SessionId = session.Id,
        Sha256 = upload.Sha256,
        Length = upload.Length,
        StorageKey = upload.Sha256,
        CreatedAtUtc = now,
        PublishedByPlayerName = session.PlayerName
    };
    db.SaveVersions.Add(version);
    world.CurrentVersionId = version.Id;
    upload.ResultVersionId = version.Id;
    upload.CommittedAtUtc = now;
    session.State = WorldSessionState.Completed;
    session.ReleasedAtUtc = now;
    await db.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    // Publication définitivement acquise : les chunks ne servent plus. Les conserver
    // laissait dans pending/ une copie intégrale de la sauvegarde à chaque transfert.
    // Le nettoyage vient après le commit, jamais avant : un commit rejoué doit pouvoir
    // réassembler l'artefact depuis ces mêmes chunks.
    store.TryCleanupPending(upload.Id);

    return Results.Ok(new CommitUploadResponse(version.Id, version.Sha256, version.Length));
});

protectedApi.MapPost("/sessions/{id:guid}/abort", async (Guid id, GameSaveHubDbContext db, TimeProvider clock, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = await AnyOwnedSession(id, db, context, cancellationToken);
    if (session is null) return Error(context, 404, "session_not_found", "Session introuvable.");
    if (session.State == WorldSessionState.Aborted) return Results.NoContent();
    if (session.ReleasedAtUtc is not null) return Error(context, 409, "session_closed", "Session déjà fermée.");
    if (!WorldSessionStateMachine.CanUserAbort(session.State, session.ImportStarted))
        return Error(context, 409, "abort_forbidden", "Après import-starting, seule une publication réussie ou l'administration peut libérer le monde.");
    session.State = WorldSessionState.Aborted;
    session.ReleasedAtUtc = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

protectedApi.MapPost("/sessions/{id:guid}/report-failure", async (Guid id, ReportFailureRequest request, GameSaveHubDbContext db, HttpContext context, CancellationToken cancellationToken) =>
{
    var session = await OwnedSession(id, db, context, cancellationToken);
    if (session is null) return Error(context, 404, "session_not_found", "Session introuvable.");
    session.FailureCode = request.Code[..Math.Min(request.Code.Length, 64)];
    session.FailureMessage = request.Message[..Math.Min(request.Message.Length, 1024)];
    session.State = WorldSessionState.Interrupted;
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});

await VerifyDatabaseAsync(app.Services);
await app.RunAsync();

static IResult Error(HttpContext context, int status, string code, string message) =>
    Results.Json(new ApiError(code, message, context.TraceIdentifier), statusCode: status);

static string? RequireIdempotencyKey(HttpContext context)
{
    var value = context.Request.Headers["Idempotency-Key"].ToString().Trim();
    return value.Length is >= 8 and <= 128 ? value : null;
}

static bool TryDeviceId(ClaimsPrincipal principal, out Guid deviceId) =>
    Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out deviceId);

static async Task<SessionEntity?> OwnedSession(Guid id, GameSaveHubDbContext db, HttpContext context, CancellationToken cancellationToken)
{
    if (!TryDeviceId(context.User, out var deviceId)) return null;
    return await db.Sessions.SingleOrDefaultAsync(x => x.Id == id && x.DeviceId == deviceId && x.ReleasedAtUtc == null, cancellationToken);
}

static async Task<SessionEntity?> AnyOwnedSession(Guid id, GameSaveHubDbContext db, HttpContext context, CancellationToken cancellationToken)
{
    if (!TryDeviceId(context.User, out var deviceId)) return null;
    return await db.Sessions.SingleOrDefaultAsync(x => x.Id == id && x.DeviceId == deviceId, cancellationToken);
}

static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

static async Task VerifyDatabaseAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<GameSaveHubDbContext>();
    var pending = await db.Database.GetPendingMigrationsAsync();
    if (pending.Any())
    {
        throw new InvalidOperationException("Migrations SQLite en attente. Exécutez GameSaveHub.Server.Admin database migrate avant de démarrer l'API.");
    }

    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=ON;");
}

public partial class Program;

internal static partial class ApiLog
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Erreur non gérée ({CorrelationId}).")]
    public static partial void Unhandled(ILogger logger, string correlationId, Exception exception);
}
