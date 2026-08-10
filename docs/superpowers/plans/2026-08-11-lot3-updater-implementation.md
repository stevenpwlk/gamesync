# Lot 3 — installateur unique et mises à jour à distance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `GameSaveHub-Setup.exe`, a single signed executable that installs, silently self-updates, and completely uninstalls the GameSave Hub client, backed by new server endpoints for release distribution and self-service device revocation.

**Architecture:** One new Windows-only console project (`GameSaveHub.Client.Setup`, single-file, self-contained) drives three modes (interactive install, silent `--auto-update`, `--uninstall`) by reusing existing Lot 2 primitives (`DeviceIdentity`, `ClientServiceOptions`, `ClientStateStore`, `AuthenticatedTransferServerClient`, `maintenance-status`). New pure logic (release signature verification, folder-swap reconciliation) lives in the existing cross-platform `GameSaveHub.Contracts` and `GameSaveHub.Client.Orchestration` projects so it stays unit-testable without Windows, matching how `ManagedSlotResolver` and `ClientCompatibilityPolicy` are already split from their Windows-hosting callers. Server-side additions are three new endpoints on the already-deployed `GameSaveHub.Server.Api` plus two new `GameSaveHub.Server.Admin` CLI commands, following the exact patterns already used by `world replace` and `device revoke`.

**Tech Stack:** .NET 10 / C# 14, xUnit, ASP.NET Core minimal APIs, EF Core + SQLite, ECDSA P-256 (`System.Security.Cryptography`), Windows Task Scheduler (`schtasks.exe`), `System.ServiceProcess.ServiceController`.

## Global Constraints

- TDD strict: every task that adds testable logic writes the failing test first. One commit per task.
- Test floor: the suite currently has 322 passing tests (`dotnet test GameSaveHub.slnx`). It must never drop below its value at the start of the task, and should grow with every task that adds testable logic.
- `dotnet build GameSaveHub.slnx` must stay at 0 warnings / 0 errors after every task (`TreatWarningsAsErrors=true` is already set repo-wide via `Directory.Build.props`).
- No placeholders, no `TODO`, no stub methods left throwing `NotImplementedException` at the end of a task — every task leaves the solution compiling and every code path it touches doing something real.
- Regenerate `SOURCE-SHA256SUMS.txt` before every commit that touches a tracked file (no generator script exists; use the manual `git ls-files | ... | sha256sum` loop already used in this repo — see Task 13 for the exact command).
- The release-signing **private** key never gets committed to git and never gets deployed to the NAS (spec §4). Only a **public** key is compiled into the client and configured on the NAS.
- `GET /api/v1/client/latest` and `GET /api/v1/client/packages/{version}` are unauthenticated (spec §5). `POST /api/v1/device/revoke-self` is authenticated and must reject a device that currently holds an active session with `409 device_has_active_session` (spec §7).
- The folder swap in `--auto-update` is exactly two renames (`Client`→`Client.old`, `Client.new`→`Client`); no automatic multi-version rollback engine (spec §6, explicitly out of scope in spec §9).
- `--auto-update` and `--uninstall` must query the running service's `maintenance-status` pipe command first and do nothing risky (no download-apply, no removal) unless `SafeToUpdate=true` (spec §3.2, §3.3).
- `--uninstall` must attempt `POST /api/v1/device/revoke-self` before local removal, but must still complete local removal if that call fails (offline fallback, spec §3.3, §7).
- Real installation, real scheduled-task registration, and real key generation on Steven's own PC are **out of this plan's automated scope** — Task 13 hands off to an explicit external validation phase requiring the user's approval before any real Windows service replacement or NAS redeploy, mirroring how Lot 2's Tasks 12–14 were run.

---

## File Structure

New files:
- `src/GameSaveHub.Contracts/ClientReleaseManifest.cs` — wire DTOs for the release manifest.
- `src/GameSaveHub.Contracts/ClientReleaseSignature.cs` — sign/verify pure logic, shared by admin CLI and client.
- `src/GameSaveHub.Server.Infrastructure/ClientReleaseObjectStore.cs` — content-addressed storage for release `.zip` files, sibling to `ImmutableArtifactStore` but without the gshsave-envelope validation that store performs (a client release is a plain zip, not a save artifact).
- `src/GameSaveHub.Server.Infrastructure/Migrations/*_AddClientReleases.cs` (+ `.Designer.cs`) — EF migration.
- `src/GameSaveHub.Client.Orchestration/FolderSwapReconciler.cs` — pure 3-state resolver mirroring `ManagedSlotResolver`'s style.
- `src/GameSaveHub.Client.Orchestration/ServiceAccountGuard.cs` — pure SID check extracted from `INSTALL-GAMESAVEHUB-CLIENT.ps1`.
- `src/GameSaveHub.Client.Setup/GameSaveHub.Client.Setup.csproj`, `Program.cs`, `Installer.cs`, `Updater.cs`, `Uninstaller.cs`, `ScheduledTaskManager.cs`, `ClientReleasePublicKey.cs` — the new single-file tool.
- `tests/Unit/ClientReleaseSignatureTests.cs`, `ClientReleaseObjectStoreTests.cs`, `FolderSwapReconcilerTests.cs`, `ServiceAccountGuardTests.cs`.
- `tools/build-lot3-setup.ps1` — publishes `GameSaveHub-Setup.exe` single-file.
- `docs/operations/LOT3-SETUP-UPDATER.md`, `docs/operations/LOT3-VALIDATION-CHECKLIST.md`.

Modified files:
- `src/GameSaveHub.Server.Infrastructure/Entities.cs`, `GameSaveHubDbContext.cs` — new `ClientReleaseEntity`.
- `src/GameSaveHub.Server.Api/Program.cs` — three new endpoints.
- `src/GameSaveHub.Server.Admin/Program.cs` — `client-release sign`/`client-release publish` commands.
- `src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs` — `RevokeSelfAsync`.
- `GameSaveHub.slnx` — register the new project.
- `README.md` — point to the new runbook.

---

### Task 1: Contrat et signature de release client

**Files:**
- Create: `src/GameSaveHub.Contracts/ClientReleaseManifest.cs`
- Create: `src/GameSaveHub.Contracts/ClientReleaseSignature.cs`
- Test: `tests/Unit/ClientReleaseSignatureTests.cs`

**Interfaces:**
- Produces: `ClientReleaseManifest(string Version, string Sha256, string DownloadUrl)`, `SignedClientReleaseManifest(string Version, string Sha256, string DownloadUrl, string Signature)`, `ClientReleaseSignature.Sign(ClientReleaseManifest, string privateKeyPem) : string`, `ClientReleaseSignature.Verify(SignedClientReleaseManifest, string publicKeyPem) : bool`. Used by Task 2 (nothing), Task 5/6 (admin CLI sign/publish), Task 9 (client verify before applying an update).

- [ ] **Step 1: Write the failing tests**

```csharp
using GameSaveHub.Contracts;

namespace GameSaveHub.UnitTests;

public sealed class ClientReleaseSignatureTests
{
    private const string PrivateKeyPem = """
        -----BEGIN EC PRIVATE KEY-----
        MHcCAQEEIELItSsvZN+XIooeE5iykbJT2lzxMYoFgsSsXxtA3OPRoAoGCCqGSM49
        AwEHoUQDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoXNoq6SqgSKbUfHB/ziUYl+bs3
        slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
        -----END EC PRIVATE KEY-----
        """;

    private const string PublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoX
        Noq6SqgSKbUfHB/ziUYl+bs3slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
        -----END PUBLIC KEY-----
        """;

    private const string WrongPublicKeyPem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAErm4k2WQlZ+NoeSgH5GRxW4cQ5x9L
        nyF6CPjS0jV7IYLSG6Bb8LHQ22XHoAR/s6TBWGZbHoMMaBALp8LFXu5alg==
        -----END PUBLIC KEY-----
        """;

    private static ClientReleaseManifest Manifest() =>
        new("0.5.0", new string('a', 64), "/api/v1/client/packages/0.5.0");

    [Fact]
    public void SignThenVerifyRoundTrips()
    {
        var manifest = Manifest();
        var signature = ClientReleaseSignature.Sign(manifest, PrivateKeyPem);
        var signed = new SignedClientReleaseManifest(manifest.Version, manifest.Sha256, manifest.DownloadUrl, signature);

        Assert.True(ClientReleaseSignature.Verify(signed, PublicKeyPem));
    }

    [Fact]
    public void TamperedShaFailsVerification()
    {
        var manifest = Manifest();
        var signature = ClientReleaseSignature.Sign(manifest, PrivateKeyPem);
        var tampered = new SignedClientReleaseManifest(manifest.Version, new string('b', 64), manifest.DownloadUrl, signature);

        Assert.False(ClientReleaseSignature.Verify(tampered, PublicKeyPem));
    }

    [Fact]
    public void WrongPublicKeyFailsVerification()
    {
        var manifest = Manifest();
        var signature = ClientReleaseSignature.Sign(manifest, PrivateKeyPem);
        var signed = new SignedClientReleaseManifest(manifest.Version, manifest.Sha256, manifest.DownloadUrl, signature);

        Assert.False(ClientReleaseSignature.Verify(signed, WrongPublicKeyPem));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64!!")]
    [InlineData("YWJj")]
    public void MalformedSignatureFailsWithoutThrowing(string signature)
    {
        var manifest = Manifest();
        var signed = new SignedClientReleaseManifest(manifest.Version, manifest.Sha256, manifest.DownloadUrl, signature);

        Assert.False(ClientReleaseSignature.Verify(signed, PublicKeyPem));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Unit --filter ClientReleaseSignatureTests`
Expected: FAIL — `ClientReleaseManifest`/`ClientReleaseSignature` do not exist yet.

- [ ] **Step 3: Implement**

```csharp
namespace GameSaveHub.Contracts;

public sealed record ClientReleaseManifest(string Version, string Sha256, string DownloadUrl);

public sealed record SignedClientReleaseManifest(string Version, string Sha256, string DownloadUrl, string Signature);
```

```csharp
using System.Security.Cryptography;
using System.Text;

namespace GameSaveHub.Contracts;

public static class ClientReleaseSignature
{
    public static string Sign(ClientReleaseManifest manifest, string privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        return Convert.ToBase64String(key.SignData(CanonicalBytes(manifest), HashAlgorithmName.SHA256));
    }

    public static bool Verify(SignedClientReleaseManifest signed, string publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(signed);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyPem);
        try
        {
            using var key = ECDsa.Create();
            key.ImportFromPem(publicKeyPem);
            var manifest = new ClientReleaseManifest(signed.Version, signed.Sha256, signed.DownloadUrl);
            return key.VerifyData(CanonicalBytes(manifest), Convert.FromBase64String(signed.Signature), HashAlgorithmName.SHA256);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }

    private static byte[] CanonicalBytes(ClientReleaseManifest manifest) =>
        Encoding.UTF8.GetBytes($"{manifest.Version}\n{manifest.Sha256}\n{manifest.DownloadUrl}");
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Unit --filter ClientReleaseSignatureTests`
Expected: PASS, 6 tests (1 fact + 1 fact + 1 fact + 3 theory cases).

- [ ] **Step 5: Commit**

```bash
git add src/GameSaveHub.Contracts/ClientReleaseManifest.cs src/GameSaveHub.Contracts/ClientReleaseSignature.cs tests/Unit/ClientReleaseSignatureTests.cs
git commit -m "feat: add signed client release manifest contract"
```

---

### Task 2: Entité, migration et stockage des paquets de release

**Files:**
- Modify: `src/GameSaveHub.Server.Infrastructure/Entities.cs`
- Modify: `src/GameSaveHub.Server.Infrastructure/GameSaveHubDbContext.cs`
- Create: `src/GameSaveHub.Server.Infrastructure/ClientReleaseObjectStore.cs`
- Create: `src/GameSaveHub.Server.Infrastructure/Migrations/*_AddClientReleases.cs` (generated)
- Test: `tests/Unit/ClientReleaseObjectStoreTests.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `ClientReleaseEntity { Guid Id, string Version, string Sha256, string Signature, long Length, DateTimeOffset PublishedAtUtc }`, `GameSaveHubDbContext.ClientReleases`, `ClientReleaseObjectStore.GetObjectPath(string sha256, string version) : string`, `ClientReleaseObjectStore.PutAsync(string sourcePath, string sha256, string version, CancellationToken) : Task<string>`. Used by Task 3 (API read), Task 6 (admin publish).

- [ ] **Step 1: Write the failing test**

```csharp
using GameSaveHub.Core;
using GameSaveHub.Server.Infrastructure;
using Microsoft.Extensions.Options;

namespace GameSaveHub.UnitTests;

public sealed class ClientReleaseObjectStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gsh-release-store-" + Guid.NewGuid().ToString("N"));

    private ClientReleaseObjectStore CreateStore() =>
        new(Options.Create(new StorageOptions { Root = _root, MaxArtifactBytes = 64 * 1024 * 1024 }));

    [Fact]
    public async Task PutAsyncStoresFileUnderContentAddressedPath()
    {
        var source = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(source, "fake-zip-contents"u8.ToArray());
        var sha256 = await FileSafety.ComputeSha256Async(source);
        var store = CreateStore();

        var destination = await store.PutAsync(source, sha256, "0.5.0");

        Assert.True(File.Exists(destination));
        Assert.Equal(store.GetObjectPath(sha256, "0.5.0"), destination);
        Assert.Equal("fake-zip-contents"u8.ToArray(), await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task PutAsyncRejectsHashMismatch()
    {
        var source = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(source, "fake-zip-contents"u8.ToArray());
        var store = CreateStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.PutAsync(source, new string('0', 64), "0.5.0"));
    }

    [Fact]
    public async Task PutAsyncIsIdempotentForIdenticalContent()
    {
        var source = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(source, "fake-zip-contents"u8.ToArray());
        var sha256 = await FileSafety.ComputeSha256Async(source);
        var store = CreateStore();

        await store.PutAsync(source, sha256, "0.5.0");
        var second = Path.Combine(Path.GetTempPath(), "release-" + Guid.NewGuid().ToString("N") + ".zip");
        await File.WriteAllBytesAsync(second, "fake-zip-contents"u8.ToArray());
        var destination = await store.PutAsync(second, sha256, "0.5.0");

        Assert.True(File.Exists(destination));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Unit --filter ClientReleaseObjectStoreTests`
Expected: FAIL — `ClientReleaseObjectStore` does not exist.

- [ ] **Step 3: Implement the entity, DbContext wiring, and store**

Add to `src/GameSaveHub.Server.Infrastructure/Entities.cs` (append at end of file):

```csharp
public sealed class ClientReleaseEntity
{
    public Guid Id { get; set; }
    public required string Version { get; set; }
    public required string Sha256 { get; set; }
    public required string Signature { get; set; }
    public long Length { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; }
}
```

Modify `src/GameSaveHub.Server.Infrastructure/GameSaveHubDbContext.cs`: add `public DbSet<ClientReleaseEntity> ClientReleases => Set<ClientReleaseEntity>();` next to the other `DbSet` properties, and inside `OnModelCreating` add:

```csharp
        modelBuilder.Entity<ClientReleaseEntity>().HasIndex(x => x.Version).IsUnique();
        modelBuilder.Entity<ClientReleaseEntity>().HasIndex(x => x.PublishedAtUtc);
        modelBuilder.Entity<ClientReleaseEntity>().Property(x => x.PublishedAtUtc).HasConversion<long>();
```

Create `src/GameSaveHub.Server.Infrastructure/ClientReleaseObjectStore.cs`:

```csharp
using GameSaveHub.Core;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Server.Infrastructure;

/// <summary>
/// Stockage adressé par contenu pour les paquets de release client (.zip). Ne valide
/// aucune enveloppe .gshsave — contrairement à <see cref="ImmutableArtifactStore"/>,
/// dont l'objet stocké est toujours une sauvegarde de jeu, ici c'est un installateur.
/// </summary>
public sealed class ClientReleaseObjectStore(IOptions<StorageOptions> options)
{
    private readonly StorageOptions _options = options.Value;

    public string GetObjectPath(string sha256, string version) => Path.Combine(
        GetRoot(), "objects", "client-releases", sha256[..2], sha256[2..4], $"{version}.zip");

    public async Task<string> PutAsync(string sourcePath, string sha256, string version, CancellationToken cancellationToken = default)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists || sourceInfo.Length <= 0 || sourceInfo.Length > _options.MaxArtifactBytes)
            throw new InvalidOperationException("Taille de paquet de release invalide.");

        var actualHash = await FileSafety.ComputeSha256Async(sourcePath, cancellationToken);
        if (!actualHash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le hash du paquet ne correspond pas au manifeste.");

        var destination = GetObjectPath(sha256, version);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            var existingHash = await FileSafety.ComputeSha256Async(destination, cancellationToken);
            if (!existingHash.Equals(sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Collision ou objet de release existant corrompu.");
            return destination;
        }

        var temporary = destination + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourcePath, temporary, overwrite: false);
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return destination;
    }

    private string GetRoot() => Path.GetFullPath(_options.Root);
}
```

- [ ] **Step 4: Generate the EF migration**

```bash
dotnet tool install --global dotnet-ef --version 10.0.* 2>/dev/null; \
dotnet ef migrations add AddClientReleases \
  --project src/GameSaveHub.Server.Infrastructure \
  --startup-project src/GameSaveHub.Server.Api
```

Confirm two new files appear under `src/GameSaveHub.Server.Infrastructure/Migrations/`: `<timestamp>_AddClientReleases.cs` and `<timestamp>_AddClientReleases.Designer.cs`, and that `GameSaveHubDbContextModelSnapshot.cs` now contains a `ClientReleaseEntity` block. Open the generated migration and confirm it only creates the `ClientReleases` table with columns `Id, Version, Sha256, Signature, Length, PublishedAtUtc` and a unique index on `Version` — if `dotnet ef` produced anything touching another table, stop and investigate before continuing (it would mean the model and a prior migration have drifted).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/Unit --filter ClientReleaseObjectStoreTests`
Expected: PASS, 3 tests.

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors (confirms the migration and DbContext changes compile).

- [ ] **Step 6: Commit**

```bash
git add src/GameSaveHub.Server.Infrastructure/ tests/Unit/ClientReleaseObjectStoreTests.cs
git commit -m "feat: add client release entity, storage and migration"
```

---

### Task 3: Endpoints de distribution des mises à jour

**Files:**
- Modify: `src/GameSaveHub.Server.Api/Program.cs`

**Interfaces:**
- Consumes: `GameSaveHubDbContext.ClientReleases` (Task 2), `ClientReleaseObjectStore` (Task 2).
- Produces: `GET /api/v1/client/latest`, `GET /api/v1/client/packages/{version}` (both unauthenticated).

No new automated test for this task: no endpoint in `Program.cs` has a dedicated automated test anywhere in this repo today (`/worlds`, `/acquire`, etc. are all verified through real pilot deployment, not `WebApplicationFactory` — confirmed by inspecting `tests/Unit/ApiContractCompatibilityTests.cs`, which only round-trips JSON, never calls the running API). This task follows that existing precedent; verification is `dotnet build` plus the real-deployment check in the external validation phase (Task 13).

- [ ] **Step 1: Register the store in DI**

In `src/GameSaveHub.Server.Api/Program.cs`, add next to the existing `builder.Services.AddScoped<ImmutableArtifactStore>();`:

```csharp
builder.Services.AddScoped<ClientReleaseObjectStore>();
```

Add `using GameSaveHub.Server.Infrastructure;` is already present in the file (it defines `ImmutableArtifactStore` from that namespace already), no new `using` needed.

- [ ] **Step 2: Add the two endpoints**

Add after the `app.MapHealthChecks("/healthz");` line and before `app.MapPost("/api/v1/enrollments/redeem", ...)` (both are unauthenticated, so they belong with the other pre-auth routes, not inside `protectedApi`):

```csharp
app.MapGet("/api/v1/client/latest", async (GameSaveHubDbContext db, HttpContext context, CancellationToken cancellationToken) =>
{
    var latest = await db.ClientReleases.OrderByDescending(x => x.PublishedAtUtc).FirstOrDefaultAsync(cancellationToken);
    if (latest is null) return Error(context, 404, "no_release_published", "Aucune version cliente n'a encore été publiée.");
    return Results.Ok(new SignedClientReleaseManifest(
        latest.Version,
        latest.Sha256,
        $"/api/v1/client/packages/{latest.Version}",
        latest.Signature));
});

app.MapGet("/api/v1/client/packages/{version}", async (string version, GameSaveHubDbContext db, ClientReleaseObjectStore store, HttpContext context, CancellationToken cancellationToken) =>
{
    var release = await db.ClientReleases.SingleOrDefaultAsync(x => x.Version == version, cancellationToken);
    if (release is null) return Error(context, 404, "release_not_found", "Version cliente introuvable.");
    var path = store.GetObjectPath(release.Sha256, release.Version);
    return File.Exists(path)
        ? Results.File(path, "application/zip", $"GameSaveHub-Setup-{release.Version}.zip", enableRangeProcessing: true)
        : Error(context, 409, "release_object_missing", "Objet de release absent du stockage.");
});
```

- [ ] **Step 3: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/GameSaveHub.Server.Api/Program.cs
git commit -m "feat: add unauthenticated client release distribution endpoints"
```

---

### Task 4: Endpoint de révocation en libre-service

**Files:**
- Modify: `src/GameSaveHub.Server.Api/Program.cs`

**Interfaces:**
- Produces: `POST /api/v1/device/revoke-self` (authenticated), `204` on success, `409 device_has_active_session` if the caller's device holds an active session.

Same testing note as Task 3: no automated endpoint test, verified in the external validation phase.

- [ ] **Step 1: Add the endpoint**

Add inside the `protectedApi` group in `src/GameSaveHub.Server.Api/Program.cs`, after `protectedApi.MapPost("/sessions/{id:guid}/report-failure", ...)`:

```csharp
protectedApi.MapPost("/device/revoke-self", async (
    GameSaveHubDbContext db,
    TimeProvider clock,
    HttpContext context,
    CancellationToken cancellationToken) =>
{
    if (!TryDeviceId(context.User, out var deviceId)) return Error(context, 401, "token_invalid", "Jeton d'appareil invalide.");
    var device = await db.Devices.FindAsync([deviceId], cancellationToken);
    if (device is null || device.RevokedAtUtc is not null) return Results.NoContent();
    if (await db.Sessions.AnyAsync(x => x.DeviceId == deviceId && x.ReleasedAtUtc == null, cancellationToken))
        return Error(context, 409, "device_has_active_session", "Cet appareil détient une session active : elle doit se terminer avant la révocation.");
    device.RevokedAtUtc = clock.GetUtcNow();
    await db.SaveChangesAsync(cancellationToken);
    return Results.NoContent();
});
```

- [ ] **Step 2: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/GameSaveHub.Server.Api/Program.cs
git commit -m "feat: add self-service device revocation endpoint"
```

---

### Task 5: Commande admin — signer une release (locale, hors NAS)

**Files:**
- Modify: `src/GameSaveHub.Server.Admin/Program.cs`

**Interfaces:**
- Consumes: `ClientReleaseSignature.Sign` (Task 1).
- Produces: CLI command `client-release sign <fichier.zip> <version> <cle-privee.pem>`, writes `<fichier.zip>.manifest.json` next to the zip.

Runs entirely locally on Steven's PC (never touches the NAS or its database): `Server.Admin` targets plain `net10.0`, so `dotnet run --project src/GameSaveHub.Server.Admin -- client-release sign ...` works on Windows without Docker. No automated test: this command's only job is gluing already-tested pure functions (`ClientReleaseSignature.Sign`, `FileSafety.ComputeSha256Async`) to file I/O and console output, matching every other command in this file (none of which are unit tested individually — `WorldReplaceCommandParser` is the one exception, and it already has its own test file, unaffected by this task).

- [ ] **Step 1: Add the command**

In `src/GameSaveHub.Server.Admin/Program.cs`, add a new case in the `switch` statement, right after the `case "device revoke":` block:

```csharp
    case "client-release sign":
        RequireArgCount(args, 5);
        var signZipPath = Path.GetFullPath(args[2]);
        var signVersion = args[3].Trim();
        var signPrivateKeyPem = await File.ReadAllTextAsync(args[4]);
        if (!File.Exists(signZipPath)) throw new InvalidOperationException("Fichier de paquet introuvable.");
        var signSha256 = await FileSafety.ComputeSha256Async(signZipPath);
        var signManifest = new ClientReleaseManifest(signVersion, signSha256, $"/api/v1/client/packages/{signVersion}");
        var signSignature = ClientReleaseSignature.Sign(signManifest, signPrivateKeyPem);
        var signedManifest = new SignedClientReleaseManifest(signVersion, signSha256, signManifest.DownloadUrl, signSignature);
        var signOutputPath = signZipPath + ".manifest.json";
        await File.WriteAllTextAsync(signOutputPath, JsonSerializer.Serialize(signedManifest, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Manifeste signé écrit : {signOutputPath}");
        Console.WriteLine($"Version {signVersion}, sha256 {signSha256}");
        return 0;
```

Add `using GameSaveHub.Contracts;` at the top of the file (it currently doesn't reference `GameSaveHub.Contracts`; check first with `grep -n "^using" src/GameSaveHub.Server.Admin/Program.cs` — if absent, add it as a new line after the existing `using` block).

Add the project reference: in `src/GameSaveHub.Server.Admin/GameSaveHub.Server.Admin.csproj`, add `<ProjectReference Include="..\GameSaveHub.Contracts\GameSaveHub.Contracts.csproj" />` inside the existing `<ItemGroup>` of `ProjectReference` entries (check first with `grep -n ProjectReference src/GameSaveHub.Server.Admin/GameSaveHub.Server.Admin.csproj` — `GameSaveHub.Server.Infrastructure` already references `GameSaveHub.Core`, but confirm `GameSaveHub.Contracts` isn't already transitively exposed before adding a duplicate reference).

- [ ] **Step 2: Update Usage()**

Add a line to the `Usage()` heredoc string, after `session list|release <session-id> <justification>`:

```
          client-release sign <fichier.zip> <version> <cle-privee.pem>
          client-release publish <fichier.zip> <manifeste-signe.json>
```

(The `publish` line is added now even though Task 6 implements it, so the usage text is complete in one place — matches how `world replace`'s usage line was written in one shot in the existing codebase.)

- [ ] **Step 3: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors.

- [ ] **Step 4: Manual smoke test with the Task 1 test keypair**

```bash
mkdir -p /tmp/gsh-sign-smoke && echo "fake-zip-contents" > /tmp/gsh-sign-smoke/GameSaveHub-Setup-0.5.0.zip
cat > /tmp/gsh-sign-smoke/key.pem <<'EOF'
-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIELItSsvZN+XIooeE5iykbJT2lzxMYoFgsSsXxtA3OPRoAoGCCqGSM49
AwEHoUQDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoXNoq6SqgSKbUfHB/ziUYl+bs3
slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
-----END EC PRIVATE KEY-----
EOF
dotnet run --project src/GameSaveHub.Server.Admin -- client-release sign /tmp/gsh-sign-smoke/GameSaveHub-Setup-0.5.0.zip 0.5.0 /tmp/gsh-sign-smoke/key.pem
cat /tmp/gsh-sign-smoke/GameSaveHub-Setup-0.5.0.zip.manifest.json
rm -rf /tmp/gsh-sign-smoke
```

Expected: prints the manifest path and a `sha256`/`signature` pair; the `.manifest.json` file contains `version`, `sha256`, `downloadUrl`, `signature`.

- [ ] **Step 5: Commit**

```bash
git add src/GameSaveHub.Server.Admin/
git commit -m "feat: add client-release sign admin command"
```

---

### Task 6: Commande admin — publier une release (NAS)

**Files:**
- Modify: `src/GameSaveHub.Server.Admin/Program.cs`

**Interfaces:**
- Consumes: `ClientReleaseSignature.Verify` (Task 1), `ClientReleaseObjectStore` (Task 2).
- Produces: CLI command `client-release publish <fichier.zip> <manifeste-signe.json>`.

- [ ] **Step 1: Add the command**

Add right after the `client-release sign` case:

```csharp
    case "client-release publish":
        RequireArgCount(args, 4);
        var publishZipPath = Path.GetFullPath(args[2]);
        var publishManifestPath = Path.GetFullPath(args[3]);
        if (!File.Exists(publishZipPath)) throw new InvalidOperationException("Fichier de paquet introuvable.");
        if (!File.Exists(publishManifestPath)) throw new InvalidOperationException("Fichier de manifeste introuvable.");
        var publishSignedManifest = JsonSerializer.Deserialize<SignedClientReleaseManifest>(await File.ReadAllTextAsync(publishManifestPath))
            ?? throw new InvalidOperationException("Manifeste illisible.");
        var publicKeyPem = Environment.GetEnvironmentVariable("GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM")
            ?? throw new InvalidOperationException("GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM absente : impossible de vérifier la signature.");
        if (!ClientReleaseSignature.Verify(publishSignedManifest, publicKeyPem))
            throw new InvalidOperationException("Signature du manifeste invalide : publication refusée.");
        var publishActualHash = await FileSafety.ComputeSha256Async(publishZipPath);
        if (!publishActualHash.Equals(publishSignedManifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le hash du paquet ne correspond pas au manifeste signé.");
        if (await db.ClientReleases.AnyAsync(x => x.Version == publishSignedManifest.Version))
            throw new InvalidOperationException($"La version {publishSignedManifest.Version} est déjà publiée.");
        var publishStore = new ClientReleaseObjectStore(Options.Create(new StorageOptions
        {
            Root = ReadStorageRoot(),
            MaxArtifactBytes = ReadMaximumArtifactBytes()
        }));
        await publishStore.PutAsync(publishZipPath, publishActualHash, publishSignedManifest.Version);
        var publishLength = new FileInfo(publishZipPath).Length;
        db.ClientReleases.Add(new ClientReleaseEntity
        {
            Id = Guid.NewGuid(),
            Version = publishSignedManifest.Version,
            Sha256 = publishActualHash,
            Signature = publishSignedManifest.Signature,
            Length = publishLength,
            PublishedAtUtc = DateTimeOffset.UtcNow
        });
        Audit(db, "client-release.publish", publishSignedManifest.Version, "Publication d'une nouvelle version cliente signée.", new { sha256 = publishActualHash, length = publishLength });
        await db.SaveChangesAsync();
        Console.WriteLine($"Version {publishSignedManifest.Version} publiée ({publishLength} octets, sha256 {publishActualHash}).");
        return 0;
```

- [ ] **Step 2: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Manual smoke test end-to-end (sign then publish) against a throwaway local SQLite file**

```bash
mkdir -p /tmp/gsh-publish-smoke/data
echo "fake-zip-contents" > /tmp/gsh-publish-smoke/GameSaveHub-Setup-0.5.0.zip
cat > /tmp/gsh-publish-smoke/key.pem <<'EOF'
-----BEGIN EC PRIVATE KEY-----
MHcCAQEEIELItSsvZN+XIooeE5iykbJT2lzxMYoFgsSsXxtA3OPRoAoGCCqGSM49
AwEHoUQDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoXNoq6SqgSKbUfHB/ziUYl+bs3
slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
-----END EC PRIVATE KEY-----
EOF
export GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM='-----BEGIN PUBLIC KEY-----
MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoX
Noq6SqgSKbUfHB/ziUYl+bs3slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
-----END PUBLIC KEY-----'
export GSH_CONNECTION_STRING="Data Source=/tmp/gsh-publish-smoke/data/gamesavehub.db;Cache=Shared;Pooling=True"
export GSH_STORAGE_ROOT=/tmp/gsh-publish-smoke/data
dotnet run --project src/GameSaveHub.Server.Admin -- database migrate
dotnet run --project src/GameSaveHub.Server.Admin -- client-release sign /tmp/gsh-publish-smoke/GameSaveHub-Setup-0.5.0.zip 0.5.0 /tmp/gsh-publish-smoke/key.pem
dotnet run --project src/GameSaveHub.Server.Admin -- client-release publish /tmp/gsh-publish-smoke/GameSaveHub-Setup-0.5.0.zip /tmp/gsh-publish-smoke/GameSaveHub-Setup-0.5.0.zip.manifest.json
find /tmp/gsh-publish-smoke/data/objects -type f
rm -rf /tmp/gsh-publish-smoke
unset GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM GSH_CONNECTION_STRING GSH_STORAGE_ROOT
```

Expected: `publish` prints `Version 0.5.0 publiée (...)`, and the zip appears under `data/objects/client-releases/<sha2>/<sha4>/0.5.0.zip`. Re-running the same `publish` command a second time must fail with "La version 0.5.0 est déjà publiée." (confirms the uniqueness guard).

- [ ] **Step 4: Commit**

```bash
git add src/GameSaveHub.Server.Admin/
git commit -m "feat: add client-release publish admin command"
```

---

### Task 7: Réconciliation de bascule de dossier et garde-fou de compte de service

**Files:**
- Create: `src/GameSaveHub.Client.Orchestration/FolderSwapReconciler.cs`
- Create: `src/GameSaveHub.Client.Orchestration/ServiceAccountGuard.cs`
- Test: `tests/Unit/FolderSwapReconcilerTests.cs`, `tests/Unit/ServiceAccountGuardTests.cs`

**Interfaces:**
- Produces: `FolderSwapReconciliationAction` enum (`NoActionNeeded`, `CleanupOldFolder`, `RestoreFromOld`, `ManualReviewRequired`), `FolderSwapReconciler.Resolve(bool clientExists, bool clientOldExists) : FolderSwapReconciliationAction`, `ServiceAccountGuard.IsReservedAccount(string sid) : bool`. Both consumed by Task 8/9 (`GameSaveHub.Client.Setup`).

- [ ] **Step 1: Write the failing tests**

```csharp
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class FolderSwapReconcilerTests
{
    [Fact]
    public void ClientOnlyMeansNoActionNeeded() =>
        Assert.Equal(FolderSwapReconciliationAction.NoActionNeeded, FolderSwapReconciler.Resolve(clientExists: true, clientOldExists: false));

    [Fact]
    public void BothPresentMeansCleanupOld() =>
        Assert.Equal(FolderSwapReconciliationAction.CleanupOldFolder, FolderSwapReconciler.Resolve(clientExists: true, clientOldExists: true));

    [Fact]
    public void OnlyOldPresentMeansRestoreFromOld() =>
        Assert.Equal(FolderSwapReconciliationAction.RestoreFromOld, FolderSwapReconciler.Resolve(clientExists: false, clientOldExists: true));

    [Fact]
    public void NeitherPresentRequiresManualReview() =>
        Assert.Equal(FolderSwapReconciliationAction.ManualReviewRequired, FolderSwapReconciler.Resolve(clientExists: false, clientOldExists: false));
}
```

```csharp
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.UnitTests;

public sealed class ServiceAccountGuardTests
{
    [Theory]
    [InlineData("S-1-5-18")]
    [InlineData("S-1-5-19")]
    [InlineData("S-1-5-20")]
    public void ReservedAccountsAreRejected(string sid) =>
        Assert.True(ServiceAccountGuard.IsReservedAccount(sid));

    [Theory]
    [InlineData("S-1-5-21-111111111-222222222-333333333-1001")]
    [InlineData("")]
    public void OrdinaryAccountsAreAllowed(string sid) =>
        Assert.False(ServiceAccountGuard.IsReservedAccount(sid));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Unit --filter "FolderSwapReconcilerTests|ServiceAccountGuardTests"`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Implement**

```csharp
namespace GameSaveHub.Client.Orchestration;

public enum FolderSwapReconciliationAction
{
    NoActionNeeded,
    CleanupOldFolder,
    RestoreFromOld,
    ManualReviewRequired
}

/// <summary>
/// Résout, sans I/O, l'état d'une bascule de dossier `Client`/`Client.old` après une
/// éventuelle coupure. Même style que <see cref="ManagedSlotResolver"/> : une fonction
/// pure prenant l'observation déjà faite par l'appelant, jamais de lecture disque ici.
/// </summary>
public static class FolderSwapReconciler
{
    public static FolderSwapReconciliationAction Resolve(bool clientExists, bool clientOldExists) => (clientExists, clientOldExists) switch
    {
        (true, false) => FolderSwapReconciliationAction.NoActionNeeded,
        (true, true) => FolderSwapReconciliationAction.CleanupOldFolder,
        (false, true) => FolderSwapReconciliationAction.RestoreFromOld,
        (false, false) => FolderSwapReconciliationAction.ManualReviewRequired
    };
}
```

```csharp
namespace GameSaveHub.Client.Orchestration;

/// <summary>
/// Extrait de la vérification déjà faite par <c>INSTALL-GAMESAVEHUB-CLIENT.ps1</c> :
/// le compte joueur enregistré ne peut jamais être LocalSystem/LocalService/NetworkService.
/// </summary>
public static class ServiceAccountGuard
{
    private static readonly HashSet<string> ReservedSids = ["S-1-5-18", "S-1-5-19", "S-1-5-20"];

    public static bool IsReservedAccount(string sid) => ReservedSids.Contains(sid);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Unit --filter "FolderSwapReconcilerTests|ServiceAccountGuardTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/GameSaveHub.Client.Orchestration/FolderSwapReconciler.cs src/GameSaveHub.Client.Orchestration/ServiceAccountGuard.cs tests/Unit/FolderSwapReconcilerTests.cs tests/Unit/ServiceAccountGuardTests.cs
git commit -m "feat: add pure folder-swap reconciliation and service account guard"
```

---

### Task 8: Révocation côté client

**Files:**
- Modify: `src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs`

**Interfaces:**
- Produces: `AuthenticatedTransferServerClient.RevokeSelfAsync(CancellationToken) : Task<bool>` — `true` on confirmed revocation (`204`), `false` if the call could not be completed (network failure, non-success status). Used by Task 11 (`Uninstaller`).

No automated test: `AuthenticatedTransferServerClient` has no existing unit tests in this repo (it lives in the `net10.0-windows` `Client.Service` project, which the cross-platform `net10.0` test project cannot reference — the same boundary that already keeps `PipeServerWorker`, `DeviceIdentity`, and every other `Client.Service` class out of the automated suite). Verified through the real installation/uninstall pilot in Task 13's external phase, exactly like the rest of this project's Windows-hosted I/O.

- [ ] **Step 1: Add the method**

Add to `src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs`, after `ReportFailureAsync`:

```csharp
    /// <summary>
    /// Révoque cet appareil côté serveur avant une désinstallation. Ne lève jamais :
    /// un échec (hors ligne, serveur injoignable, déjà révoqué) doit laisser
    /// l'appelant décider de continuer la suppression locale quand même.
    /// </summary>
    public async Task<bool> RevokeSelfAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Api("device/revoke-self"));
            using var response = await SendAuthorizedAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or TransferServerException)
        {
            return false;
        }
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/GameSaveHub.Client.Service/AuthenticatedTransferServerClient.cs
git commit -m "feat: add self-revocation call for client uninstall"
```

---

### Task 9: Projet GameSaveHub.Client.Setup — installation interactive

**Files:**
- Create: `src/GameSaveHub.Client.Setup/GameSaveHub.Client.Setup.csproj`
- Create: `src/GameSaveHub.Client.Setup/Program.cs`
- Create: `src/GameSaveHub.Client.Setup/Installer.cs`
- Create: `src/GameSaveHub.Client.Setup/ScheduledTaskManager.cs`
- Modify: `GameSaveHub.slnx`

**Interfaces:**
- Consumes: `ServiceAccountGuard.IsReservedAccount` (Task 7), `DeviceIdentity`, `ClientServiceOptions` (existing, from `Client.Service`).
- Produces: `Installer.RunAsync(string serverBaseUrl, CancellationToken) : Task<int>`, `ScheduledTaskManager.Register(string exePath) : void`, `ScheduledTaskManager.Remove() : void`. `Register`/`Remove` are consumed by Task 9 itself (install) and Task 11 (uninstall).

No automated unit test: this task is Windows service/file/registry orchestration (elevation check, service install, scheduled task registration), the same category as `INSTALL-GAMESAVEHUB-CLIENT.ps1` which also has no automated test today — its correctness has always been verified by running it for real (Lot 2 Task 12). This task ports that exact script's logic to C# without changing its behavior, so the existing manual verification procedure in `PERMANENT-SLOT-PILOT.md` continues to apply; Task 13 schedules the real run.

- [ ] **Step 1: Create the project**

`src/GameSaveHub.Client.Setup/GameSaveHub.Client.Setup.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
    <AssemblyName>GameSaveHub-Setup</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Http" Version="10.0.10" />
    <ProjectReference Include="..\GameSaveHub.Contracts\GameSaveHub.Contracts.csproj" />
    <ProjectReference Include="..\GameSaveHub.Client.Orchestration\GameSaveHub.Client.Orchestration.csproj" />
    <ProjectReference Include="..\GameSaveHub.Client.Service\GameSaveHub.Client.Service.csproj" />
  </ItemGroup>
</Project>
```

Register it in `GameSaveHub.slnx`: add `<Project Path="src/GameSaveHub.Client.Setup/GameSaveHub.Client.Setup.csproj" />` to the `/src/` folder, alphabetically after `GameSaveHub.Client.Service`.

- [ ] **Step 2: Implement the scheduled task manager**

`src/GameSaveHub.Client.Setup/ScheduledTaskManager.cs`:

```csharp
using System.Diagnostics;

namespace GameSaveHub.Client.Setup;

/// <summary>
/// Enregistre la tâche planifiée qui relance ce même exécutable en mode silencieux.
/// Passe par <c>schtasks.exe</c> plutôt que par l'API TaskScheduler COM : une seule
/// commande, pas de dépendance native supplémentaire dans un exécutable single-file.
/// </summary>
public static class ScheduledTaskManager
{
    private const string TaskName = "GameSaveHubUpdater";

    public static void Register(string exePath)
    {
        RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\" --auto-update\" /SC HOURLY /MO 6 /RL HIGHEST /RU SYSTEM /F");
    }

    public static void Remove()
    {
        RunSchtasks($"/Delete /TN \"{TaskName}\" /F", ignoreMissing: true);
    }

    private static void RunSchtasks(string arguments, bool ignoreMissing = false)
    {
        using var process = Process.Start(new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Impossible de démarrer schtasks.exe.");
        process.WaitForExit();
        if (process.ExitCode != 0 && !(ignoreMissing && process.ExitCode == 1))
        {
            throw new InvalidOperationException($"schtasks.exe a échoué (code {process.ExitCode}) : {process.StandardError.ReadToEnd()}");
        }
    }
}
```

- [ ] **Step 3: Implement the installer, porting `INSTALL-GAMESAVEHUB-CLIENT.ps1`**

`src/GameSaveHub.Client.Setup/Installer.cs`:

```csharp
using System.Runtime.Versioning;
using System.ServiceProcess;
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.Setup;

[SupportedOSPlatform("windows")]
public static class Installer
{
    private const string ServiceName = "GameSaveHubClient";

    public static async Task<int> RunAsync(string serverBaseUrl, CancellationToken cancellationToken)
    {
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client");
        var serviceRoot = Path.Combine(installRoot, "Service");
        var appRoot = Path.Combine(installRoot, "App");
        var programDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameSaveHub");

        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Identité Windows introuvable.");
        if (ServiceAccountGuard.IsReservedAccount(sid))
            throw new InvalidOperationException("Le compte joueur ne peut pas être LocalSystem/LocalService/NetworkService.");

        using (var existing = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == ServiceName))
        {
            if (existing is not null)
            {
                Console.WriteLine("Arrêt de l'ancienne version du service...");
                if (existing.Status != ServiceControllerStatus.Stopped)
                {
                    existing.Stop();
                    existing.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
                }
            }
        }

        Directory.CreateDirectory(serviceRoot);
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(programDataRoot);

        var payloadRoot = Path.Combine(AppContext.BaseDirectory, "payload");
        CopyDirectory(Path.Combine(payloadRoot, "Service"), serviceRoot);
        CopyDirectory(Path.Combine(payloadRoot, "App"), appRoot);

        var serviceExe = Path.Combine(serviceRoot, "GameSaveHub.Client.Service.exe");
        var appExe = Path.Combine(appRoot, "GameSaveHub.Client.App.exe");
        if (!File.Exists(serviceExe)) throw new InvalidOperationException($"EXE service absent : {serviceExe}");
        if (!File.Exists(appExe)) throw new InvalidOperationException($"EXE application absent : {appExe}");

        var managedSlotAlreadyBound = File.Exists(Path.Combine(programDataRoot, "managed-slot.json"));

        InstallService(serviceExe);
        using (var service = new ServiceController(ServiceName))
        {
            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
        }

        CreateStartMenuShortcut(appExe, appRoot);
        ScheduledTaskManager.Register(Path.Combine(AppContext.BaseDirectory, "GameSaveHub-Setup.exe"));

        Console.WriteLine();
        Console.WriteLine("INSTALLATION RÉUSSIE");
        Console.WriteLine($"Service : {ServiceName} / Running");
        Console.WriteLine($"Application : {appExe}");
        Console.WriteLine(managedSlotAlreadyBound
            ? "Slot local permanent : déjà enregistré sur ce PC (conservé lors de cette installation)."
            : "Slot local permanent : pas encore configuré. L'application proposera la configuration initiale.");

        await Task.CompletedTask;
        return 0;
    }

    private static void InstallService(string serviceExePath)
    {
        RunSc($"create {ServiceName} binPath= \"{serviceExePath}\" DisplayName= \"GameSave Hub Client\" start= delayed-auto");
        RunSc($"failure {ServiceName} reset= 86400 actions= restart/5000/restart/15000//0");
    }

    private static void RunSc(string arguments)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Impossible de démarrer sc.exe.");
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"sc.exe a échoué (code {process.ExitCode}) : {process.StandardError.ReadToEnd()}");
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CreateStartMenuShortcut(string appExe, string appRoot)
    {
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs");
        Directory.CreateDirectory(startMenu);
        var shortcutPath = Path.Combine(startMenu, "GameSave Hub.lnk");
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = appExe;
        shortcut.WorkingDirectory = appRoot;
        shortcut.Description = "GameSave Hub";
        shortcut.Save();
    }
}
```

- [ ] **Step 4: Wire the entry point**

`src/GameSaveHub.Client.Setup/Program.cs`:

```csharp
using GameSaveHub.Client.Setup;

var mode = args.Length > 0 ? args[0] : "--install";
return mode switch
{
    "--install" or "-install" => await Installer.RunAsync("https://saves.stevenpwlk.fr:18443/", CancellationToken.None),
    "--auto-update" => Fail("Le mode --auto-update sera disponible après la tâche 10 de ce plan."),
    "--uninstall" => Fail("Le mode --uninstall sera disponible après la tâche 11 de ce plan."),
    _ => Fail($"Mode inconnu : {mode}")
};

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 3;
}
```

- [ ] **Step 5: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors. This confirms `GameSaveHub.Client.Setup` compiles against `Client.Service`, `Client.Orchestration`, and `Contracts` with no missing references.

Run: `dotnet test GameSaveHub.slnx --no-build --nologo`
Expected: same test count as before this task (this task adds no unit tests — the new project has none, matching `Client.Service`'s own status), all still passing.

- [ ] **Step 6: Commit**

```bash
git add src/GameSaveHub.Client.Setup/ GameSaveHub.slnx
git commit -m "feat: add GameSaveHub.Client.Setup with interactive install mode"
```

---

### Task 10: Mode silencieux --auto-update

**Files:**
- Create: `src/GameSaveHub.Client.Setup/Updater.cs`
- Modify: `src/GameSaveHub.Client.Setup/Program.cs`

**Interfaces:**
- Consumes: `FolderSwapReconciler.Resolve` (Task 7), `ClientReleaseSignature.Verify` (Task 1), `AuthenticatedTransferServerClient` (existing), pipe protocol from `PipeServerWorker`'s `"maintenance-status"` command (existing, response shape `MaintenanceSafetyStatus`).
- Produces: `Updater.RunAsync(CancellationToken) : Task<int>`.

No automated unit test for `Updater` itself (real network calls, real service control, real file swap — same category as `Installer`). The pure decision it delegates to (`FolderSwapReconciler.Resolve`, `ClientReleaseSignature.Verify`) is already tested in Tasks 1 and 7.

- [ ] **Step 1: Implement the pipe client for Setup.exe**

Add to `src/GameSaveHub.Client.Setup/Updater.cs` (new file), starting with a minimal pipe client matching the wire format already used by `PipeServerWorker` (`GameSaveHub.Client.App/PipeClient.cs` cannot be referenced without pulling in WPF, so this repeats the same ~20-line pattern the codebase already repeats between `Client.App` and `Client.Service` — see the file structure note in the spec):

```csharp
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using GameSaveHub.Client.Orchestration;
using GameSaveHub.Contracts;

namespace GameSaveHub.Client.Setup;

public sealed record SetupPipeRequest(string Command);
public sealed record SetupPipeResponse(bool Success, string Code, string Message, JsonElement? Data);

public static class Updater
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        // ... construction of AuthenticatedTransferServerClient happens once the manifest is verified, see Step 3.

        var manifestResponse = await http.GetAsync("https://saves.stevenpwlk.fr:18443/api/v1/client/latest", cancellationToken);
        if (!manifestResponse.IsSuccessStatusCode)
        {
            Console.WriteLine($"Vérification de mise à jour impossible ({(int)manifestResponse.StatusCode}), nouvelle tentative à la prochaine exécution planifiée.");
            return 0;
        }
        var manifest = await manifestResponse.Content.ReadFromJsonAsync<SignedClientReleaseManifest>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Manifeste de release illisible.");

        var installedVersion = ReadInstalledVersion();
        if (string.Equals(manifest.Version, installedVersion, StringComparison.Ordinal))
        {
            Console.WriteLine($"Déjà à jour ({installedVersion}).");
            return 0;
        }

        if (!ClientReleaseSignature.Verify(manifest, ClientReleasePublicKey.Pem))
        {
            Console.Error.WriteLine("Signature du manifeste invalide : mise à jour refusée.");
            return 1;
        }

        var programDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameSaveHub");
        var stagingRoot = Path.Combine(programDataRoot, "update-staging");
        Directory.CreateDirectory(stagingRoot);
        var packagePath = Path.Combine(stagingRoot, $"{manifest.Version}.zip");
        var packageBytes = await http.GetByteArrayAsync($"https://saves.stevenpwlk.fr:18443{manifest.DownloadUrl}", cancellationToken);
        await File.WriteAllBytesAsync(packagePath, packageBytes, cancellationToken);
        var downloadedHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(packageBytes));
        if (!downloadedHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Hash du paquet téléchargé invalide : mise à jour refusée.");
            File.Delete(packagePath);
            return 1;
        }

        var newFolder = Path.Combine(stagingRoot, "Client.new");
        if (Directory.Exists(newFolder)) Directory.Delete(newFolder, recursive: true);
        System.IO.Compression.ZipFile.ExtractToDirectory(packagePath, newFolder);

        var status = await QueryMaintenanceStatusAsync(cancellationToken);
        if (status is null || !status.Value.SafeToUpdate)
        {
            Console.WriteLine("Mise à jour reportée : condition de sûreté non réunie (jeu ouvert, session active ou transition en cours).");
            return 0;
        }

        ApplySwap(newFolder);
        Console.WriteLine($"Mise à jour vers {manifest.Version} appliquée.");
        return 0;
    }

    private static string? ReadInstalledVersion() =>
        File.Exists(InstalledVersionPath()) ? File.ReadAllText(InstalledVersionPath()).Trim() : null;

    private static string InstalledVersionPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client", "VERSION");

    private static void ApplySwap(string newFolder)
    {
        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client");
        var currentFolder = installRoot;
        var oldFolder = installRoot + ".old";

        var action = FolderSwapReconciler.Resolve(Directory.Exists(currentFolder), Directory.Exists(oldFolder));
        switch (action)
        {
            case FolderSwapReconciliationAction.RestoreFromOld:
                Directory.Move(oldFolder, currentFolder);
                break;
            case FolderSwapReconciliationAction.CleanupOldFolder:
                Directory.Delete(oldFolder, recursive: true);
                break;
            case FolderSwapReconciliationAction.ManualReviewRequired:
                throw new InvalidOperationException("Installation existante introuvable : intervention manuelle requise.");
            case FolderSwapReconciliationAction.NoActionNeeded:
                break;
        }

        StopService();
        Directory.Move(currentFolder, oldFolder);
        Directory.Move(newFolder, currentFolder);
        StartServiceAndWaitHealthy();
        Directory.Delete(oldFolder, recursive: true);
    }

    private static void StopService()
    {
        using var service = new System.ServiceProcess.ServiceController("GameSaveHubClient");
        if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
    }

    private static void StartServiceAndWaitHealthy()
    {
        using var service = new System.ServiceProcess.ServiceController("GameSaveHubClient");
        service.Start();
        service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
    }

    private static async Task<MaintenanceSafetyStatus?> QueryMaintenanceStatusAsync(CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", "GameSaveHub.Client", PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(3000, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new SetupPipeRequest("maintenance-status"), JsonOptions).AsMemory(), cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) return null;
        var response = JsonSerializer.Deserialize<SetupPipeResponse>(line, JsonOptions);
        if (response is null || !response.Success || response.Data is null) return null;
        return response.Data.Value.Deserialize<MaintenanceSafetyStatus>(JsonOptions);
    }
}
```

- [ ] **Step 2: Add the embedded test public key placeholder module**

`src/GameSaveHub.Client.Setup/ClientReleasePublicKey.cs`:

```csharp
namespace GameSaveHub.Client.Setup;

/// <summary>
/// Clé publique compilée servant à vérifier chaque manifeste de release avant application
/// (spec Lot 3 §4). Cette valeur est celle de la paire de test générée pour Task 1 de ce
/// plan — elle DOIT être remplacée par la clé publique réelle de Steven avant toute
/// publication réelle (voir Task 13, phase de validation externe). Ne jamais committer la
/// clé privée correspondante : seule la clé publique a sa place ici.
/// </summary>
public static class ClientReleasePublicKey
{
    public const string Pem = """
        -----BEGIN PUBLIC KEY-----
        MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBZL/gR7Ud5zqD2tLqGLGFv0B1MoX
        Noq6SqgSKbUfHB/ziUYl+bs3slIeHa/QwkwxvDi0lgMvzOQFoIih+JNBPQ==
        -----END PUBLIC KEY-----
        """;
}
```

- [ ] **Step 3: Wire the Program.cs dispatch**

In `src/GameSaveHub.Client.Setup/Program.cs`, replace the `"--auto-update"` arm:

```csharp
    "--auto-update" => await Updater.RunAsync(CancellationToken.None),
```

- [ ] **Step 4: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors.

Run: `dotnet test GameSaveHub.slnx --no-build --nologo`
Expected: unchanged test count, all passing (this task adds no new unit tests, consistent with Task 9's note).

- [ ] **Step 5: Commit**

```bash
git add src/GameSaveHub.Client.Setup/
git commit -m "feat: add silent auto-update mode to GameSaveHub.Client.Setup"
```

---

### Task 11: Mode --uninstall

**Files:**
- Create: `src/GameSaveHub.Client.Setup/Uninstaller.cs`
- Modify: `src/GameSaveHub.Client.Setup/Program.cs`

**Interfaces:**
- Consumes: `AuthenticatedTransferServerClient.RevokeSelfAsync` (Task 8), `ScheduledTaskManager.Remove` (Task 9), the same `QueryMaintenanceStatusAsync`-style pipe call pattern from Task 10 (duplicated here rather than shared, matching the existing `PipeRequest`/`PipeResponse` per-project duplication convention already present between `Client.App` and `Client.Service`).
- Produces: `Uninstaller.RunAsync(CancellationToken) : Task<int>`.

- [ ] **Step 1: Implement**

`src/GameSaveHub.Client.Setup/Uninstaller.cs`:

```csharp
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;
using GameSaveHub.Client.Service;
using Microsoft.Extensions.Options;

namespace GameSaveHub.Client.Setup;

public static class Uninstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var status = await QueryMaintenanceStatusAsync(cancellationToken);
        if (status is not null && !status.Value.SafeToUpdate)
        {
            Console.Error.WriteLine("Désinstallation refusée : une session locale est active ou une transition est en cours. Fermez le jeu et attendez la fin de l'opération en cours avant de réessayer.");
            return 1;
        }

        var revoked = await TryRevokeSelfAsync(cancellationToken);
        Console.WriteLine(revoked
            ? "Appareil révoqué côté serveur."
            : "Révocation côté serveur impossible (hors ligne ou serveur injoignable) : Steven devra révoquer cet appareil manuellement.");

        StopAndRemoveService();
        ScheduledTaskManager.Remove();

        var installRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GameSaveHub", "Client");
        if (Directory.Exists(installRoot)) Directory.Delete(installRoot, recursive: true);
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Microsoft", "Windows", "Start Menu", "Programs", "GameSave Hub.lnk");
        if (File.Exists(shortcutPath)) File.Delete(shortcutPath);

        var programDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GameSaveHub");
        if (Directory.Exists(programDataRoot)) Directory.Delete(programDataRoot, recursive: true);

        Console.WriteLine();
        Console.WriteLine("DÉSINSTALLATION TERMINÉE");
        Console.WriteLine("Service, application, tâche planifiée et identité locale supprimés.");
        if (!revoked)
            Console.WriteLine("RAPPEL : contactez Steven pour la révocation manuelle côté serveur.");
        return 0;
    }

    private static async Task<bool> TryRevokeSelfAsync(CancellationToken cancellationToken)
    {
        var options = Options.Create(new ClientServiceOptions());
        var identity = new DeviceIdentity(options);
        if (!identity.Exists) return true; // Rien à révoquer : jamais enrôlé ou déjà nettoyé.

        using var http = new HttpClient();
        var stateStore = new ClientStateStore(options);
        using var client = new AuthenticatedTransferServerClient(http, options, identity, stateStore);
        return await client.RevokeSelfAsync(cancellationToken);
    }

    private static void StopAndRemoveService()
    {
        var service = System.ServiceProcess.ServiceController.GetServices()
            .FirstOrDefault(s => s.ServiceName == "GameSaveHubClient");
        if (service is null) return;
        if (service.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
        }
        service.Dispose();
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sc.exe", "delete GameSaveHubClient")
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Impossible de démarrer sc.exe.");
        process.WaitForExit();
    }

    private static async Task<MaintenanceSafetyStatus?> QueryMaintenanceStatusAsync(CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(".", "GameSaveHub.Client", PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await pipe.ConnectAsync(3000, cancellationToken);
        }
        catch (TimeoutException)
        {
            return null;
        }
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(JsonSerializer.Serialize(new SetupPipeRequest("maintenance-status"), JsonOptions).AsMemory(), cancellationToken);
        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null) return null;
        var response = JsonSerializer.Deserialize<SetupPipeResponse>(line, JsonOptions);
        if (response is null || !response.Success || response.Data is null) return null;
        return response.Data.Value.Deserialize<GameSaveHub.Client.Orchestration.MaintenanceSafetyStatus>(JsonOptions);
    }
}
```

- [ ] **Step 2: Wire the Program.cs dispatch**

In `src/GameSaveHub.Client.Setup/Program.cs`, replace the `"--uninstall"` arm:

```csharp
    "--uninstall" => await Uninstaller.RunAsync(CancellationToken.None),
```

- [ ] **Step 3: Build**

Run: `dotnet build GameSaveHub.slnx --nologo`
Expected: 0 warnings, 0 errors.

Run: `dotnet test GameSaveHub.slnx --no-build --nologo`
Expected: unchanged test count, all passing.

- [ ] **Step 4: Commit**

```bash
git add src/GameSaveHub.Client.Setup/
git commit -m "feat: add complete uninstall mode to GameSaveHub.Client.Setup"
```

---

### Task 12: Script de publication du paquet single-file

**Files:**
- Create: `tools/build-lot3-setup.ps1`

**Interfaces:**
- Consumes: nothing new (invokes `dotnet publish`).
- Produces: `artifacts/GameSaveHub-Setup-<version>-win-x64.exe` plus a `payload/` subfolder (Service + App builds) copied next to it, matching what `Installer.RunAsync` expects at `AppContext.BaseDirectory/payload`.

- [ ] **Step 1: Write the script**

`tools/build-lot3-setup.ps1`:

```powershell
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot "artifacts"
$version = "0.5.0"
$publishRoot = Join-Path $artifactRoot "GameSaveHub-Setup-$version"

if (Test-Path -LiteralPath $publishRoot) { Remove-Item -LiteralPath $publishRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $publishRoot | Out-Null

Write-Host "Publication du service et de l'application (payload embarqué)..."
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.Service\GameSaveHub.Client.Service.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "payload\Service")
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.App\GameSaveHub.Client.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "payload\App")
Set-Content -LiteralPath (Join-Path $publishRoot "payload\App\..\VERSION") -Value $version -NoNewline
Set-Content -LiteralPath (Join-Path $publishRoot "VERSION") -Value $version -NoNewline

Write-Host "Publication de GameSaveHub-Setup.exe (single-file)..."
dotnet publish (Join-Path $repoRoot "src\GameSaveHub.Client.Setup\GameSaveHub.Client.Setup.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o (Join-Path $publishRoot "setup-exe")

Copy-Item -LiteralPath (Join-Path $publishRoot "setup-exe\GameSaveHub-Setup.exe") -Destination $publishRoot -Force

$exeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $publishRoot "GameSaveHub-Setup.exe")).Hash
Write-Host ""
Write-Host "Paquet prêt : $publishRoot"
Write-Host "SHA-256 de GameSaveHub-Setup.exe : $exeHash"
```

Note: the installer's `AppContext.BaseDirectory\payload` expectation from Task 9 means the final distributed layout is `GameSaveHub-Setup.exe` sitting next to a `payload\Service` and `payload\App` folder — this script produces exactly that under `$publishRoot`, but does not yet merge them into a true single self-extracting file (the spec's "single-file" refers to the *tool* being one `.exe`, not that the whole ~150 MB payload is embedded inside it — embedding the full self-contained service+app payload inside the single-file executable itself is a heavier packaging change explicitly deferred; ship the three-item folder for the first Lot 3 release, matching how `0.4.0-pilot` also shipped as a folder rather than a single archive).

- [ ] **Step 2: Verify the script runs end-to-end**

Run: `pwsh tools/build-lot3-setup.ps1` (or `powershell tools/build-lot3-setup.ps1` on Windows PowerShell 5.1)
Expected: completes without error, prints a SHA-256, and `artifacts/GameSaveHub-Setup-0.5.0/GameSaveHub-Setup.exe` exists alongside `payload/Service` and `payload/App`.

- [ ] **Step 3: Commit**

```bash
git add tools/build-lot3-setup.ps1
git commit -m "build: add packaging script for GameSaveHub-Setup.exe"
```

---

### Task 13: Documentation, checklist et vérification finale

**Files:**
- Create: `docs/operations/LOT3-SETUP-UPDATER.md`
- Create: `docs/operations/LOT3-VALIDATION-CHECKLIST.md`
- Modify: `README.md`

**Interfaces:** none (documentation only).

- [ ] **Step 1: Write the behavior runbook**

`docs/operations/LOT3-SETUP-UPDATER.md` — summarize, for future readers, the three modes, the two new endpoints, the two new admin commands, and explicitly restate the constraints from the spec (private key never on the NAS, folder-swap-only bascule, `maintenance-status` gates both auto-update and uninstall). Link to `docs/superpowers/specs/2026-08-11-lot3-updater-design.md` for the full design and to `docs/superpowers/plans/2026-08-11-lot3-updater-implementation.md` (this file) for what was actually built.

- [ ] **Step 2: Write the validation checklist**

`docs/operations/LOT3-VALIDATION-CHECKLIST.md`, mirroring the structure of `docs/operations/CLIENT-ORCHESTRATOR-VALIDATION-CHECKLIST.md`: a checklist of unchecked boxes for the **external validation phase**, since none of it can be ticked by this automated plan:

```markdown
# Checklist de validation — Lot 3

Toutes les cases ci-dessous nécessitent une exécution réelle sur un PC Windows physique,
avec accord explicite avant toute installation, tâche planifiée ou écriture WGS réelle.

## Préalable bloquant

- [ ] Steven a généré sa propre paire de clés ECDSA P-256 de production (hors dépôt,
      hors NAS) et remplacé la clé publique de test dans
      `src/GameSaveHub.Client.Setup/ClientReleasePublicKey.cs` par la vraie clé publique.
- [ ] La même clé publique de production est configurée dans la variable d'environnement
      `GSH_CLIENT_RELEASE_PUBLIC_KEY_PEM` du service `admin` sur le NAS
      (`deploy/compose.yml` ou l'équivalent Portainer déjà utilisé pour `gamesavehub-admin`).

## Installation

- [ ] `GameSaveHub-Setup.exe` installe avec succès sur un PC sans installation
      préexistante (service `Running`/`Automatic (Delayed Start)`, raccourci créé,
      tâche planifiée `GameSaveHubUpdater` visible dans le Planificateur de tâches).
- [ ] Installé par-dessus une installation `0.4.0-pilot` existante : identité CNG,
      pseudo enregistré et `managed-slot.json` préservés (pas de ré-enrôlement).

## Mise à jour silencieuse

- [ ] Une version factice plus récente publiée via `client-release sign` +
      `client-release publish` est détectée et appliquée par `--auto-update` lancé
      manuellement, jeu fermé et aucune session active.
- [ ] Lancée pendant une session `InGame` : `--auto-update` ne touche à rien et se
      termine proprement (vérifier via les journaux de diagnostic).
- [ ] Après application, le service redémarre et répond au tube nommé en moins de 30 s.

## Désinstallation

- [ ] `--uninstall` en ligne : révocation confirmée côté serveur (`device list` ne
      montre plus l'appareil comme actif), service/app/tâche/ProgramData supprimés.
- [ ] `--uninstall` hors ligne (Wi-Fi coupé) : suppression locale complète malgré
      l'échec de révocation, message de rappel affiché.

## Clôture

- [ ] `dotnet build GameSaveHub.slnx` : 0 avertissement, 0 erreur.
- [ ] `dotnet test GameSaveHub.slnx` : toutes les suites passent, total ≥ le plancher
      constaté au début de ce plan.
- [ ] Accord explicite de l'utilisateur obtenu avant toute fusion vers `main`.
```

- [ ] **Step 3: Update README**

In `README.md`, add a row to the "Par où commencer" table pointing to `docs/operations/LOT3-SETUP-UPDATER.md`, and update the status banner near the top to mention Lot 3 is in progress on its own branch (do not claim it is merged or validated — only Lot 1 and Lot 2 are, at the time this task runs).

- [ ] **Step 4: Final automated verification pass**

```bash
dotnet build GameSaveHub.slnx --nologo
dotnet test GameSaveHub.slnx --no-build --nologo
git ls-files | grep -v "^SOURCE-SHA256SUMS.txt$" | sort | while IFS= read -r f; do h=$(sha256sum "$f" | awk '{print $1}'); printf '%s *%s\n' "$h" "$f"; done > SOURCE-SHA256SUMS.txt
git diff --check
git status --short
```

Expected: 0 warnings/0 errors on build; test count at or above the floor recorded when this plan started, 0 failures; `SOURCE-SHA256SUMS.txt` regenerated; `git diff --check` silent; only expected files (`SOURCE-SHA256SUMS.txt`, docs, `README.md`) left to stage.

- [ ] **Step 5: Commit**

```bash
git add docs/operations/LOT3-SETUP-UPDATER.md docs/operations/LOT3-VALIDATION-CHECKLIST.md README.md SOURCE-SHA256SUMS.txt
git commit -m "docs: document Lot 3 setup/updater and add its validation checklist"
```

- [ ] **Step 6: Hand off to the external validation phase**

Do not proceed further automatically. Report to the user that the automated portion of Lot 3 (Tasks 1–13) is complete and green, and that `docs/operations/LOT3-VALIDATION-CHECKLIST.md` now gates everything else: real keypair generation, real NAS environment variable configuration, and a real install/update/uninstall cycle on a physical Windows PC, each requiring the user's explicit approval before it runs — exactly the same posture Lot 2 took for Tasks 12–14.

---

## Self-Review Notes

- **Spec coverage:** §2/§3.1 → Task 9; §3.2 → Task 10; §3.3 → Task 11; §4 → Task 1 (+ Task 13's key-rotation checklist item); §5 → Tasks 2–3, 5–6; §6 → Task 7 (logic) + Task 10 (application); §7 → Tasks 4, 8, 11; §8 → Task 9 (rollout is external, tracked in Task 13's checklist, not automated); §9 (hors périmètre) → nothing in this plan violates it; §10 (vérification) → Task 13.
- **Placeholder scan:** the only two intentionally-non-final values are the test ECDSA keypair (Task 1/10, explicitly documented as a test fixture with a checklist item in Task 13 to replace it) and the `Fail(...)` arms in Task 9's `Program.cs`, which are replaced by real code within the same plan (Tasks 10–11) rather than left in place — neither is a "TBD" left unresolved at the end of the plan.
- **Type consistency:** `MaintenanceSafetyStatus` (Task 10, 11) matches the existing record in `ManagedSlotCoordinator.cs` exactly (same property names/order), since it's deserialized from the same wire format the pipe already emits. `FolderSwapReconciliationAction`/`FolderSwapReconciler.Resolve` used identically in Task 10. `ClientReleaseManifest`/`SignedClientReleaseManifest` field names match between Task 1 (definition), Task 3 (API), Task 5/6 (admin CLI), and Task 10 (client) throughout.
