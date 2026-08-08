using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace GameSaveHub.Client.App;

public partial class MainWindow : Window
{
    private readonly PipeClient _pipeClient = new();
    private bool _enrolled;
    private bool _wgsTransferEnabled;
    private bool _preflightCompatible;
    private string? _registeredPlayerName;

    public MainWindow()
    {
        InitializeComponent();
        DeviceNameText.Text = Environment.MachineName;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Enroll_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DeviceNameText.Text) ||
            string.IsNullOrWhiteSpace(PlayerProfileText.Text) ||
            string.IsNullOrWhiteSpace(EnrollmentCodeText.Text))
        {
            FeedbackText.Text = "Nom du PC, pseudo Planet Crafter et code d’invitation sont requis.";
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _pipeClient.SendAsync(new PipeRequest(
                "enroll",
                EnrollmentCodeText.Text.Trim(),
                DeviceNameText.Text.Trim(),
                PlayerName: PlayerProfileText.Text.Trim()));
            FeedbackText.Text = result.Message;
            if (result.Success)
            {
                EnrollmentCodeText.Clear();
                await RefreshAsync();
                await LoadWorldsAsync();
            }
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or HttpRequestException)
        {
            FeedbackText.Text = "Service local indisponible : " + exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void SavePlayer_Click(object sender, RoutedEventArgs e)
    {
        if (!_enrolled)
        {
            FeedbackText.Text = "Associez d’abord ce PC au serveur.";
            return;
        }
        if (string.IsNullOrWhiteSpace(PlayerProfileText.Text))
        {
            FeedbackText.Text = "Le pseudo Planet Crafter ne peut pas être vide.";
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _pipeClient.SendAsync(new PipeRequest(
                "profile-player-set",
                PlayerName: PlayerProfileText.Text.Trim()));
            FeedbackText.Text = result.Message;
            if (result.Success) await RefreshAsync();
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or HttpRequestException)
        {
            FeedbackText.Text = "Impossible d’enregistrer le pseudo : " + exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void LoadWorlds_Click(object sender, RoutedEventArgs e) => await LoadWorldsAsync();

    private async Task LoadWorldsAsync()
    {
        if (!_enrolled)
        {
            FeedbackText.Text = "Associez d’abord ce PC au serveur.";
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _pipeClient.SendAsync(new PipeRequest("world-list"));
            if (!result.Success)
            {
                FeedbackText.Text = result.Message;
                return;
            }

            var worlds = new List<WorldSelectionItem>();
            if (result.Data is JsonElement data && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                {
                    if (!item.TryGetProperty("worldId", out var idElement) ||
                        !Guid.TryParse(idElement.GetString(), out var worldId))
                    {
                        continue;
                    }

                    var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? worldId.ToString("D") : worldId.ToString("D");
                    var status = item.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "Unknown" : "Unknown";
                    var hasArtifact = item.TryGetProperty("hasArtifact", out var artifactElement) && artifactElement.GetBoolean();
                    worlds.Add(new WorldSelectionItem(
                        worldId,
                        name,
                        status,
                        hasArtifact,
                        $"{name} — {status}{(hasArtifact ? string.Empty : " — aucune sauvegarde")}"));
                }
            }

            WorldComboBox.ItemsSource = worlds;
            if (worlds.Count > 0) WorldComboBox.SelectedIndex = 0;
            FeedbackText.Text = worlds.Count == 0
                ? "Aucun monde n’est encore enregistré sur le NAS."
                : $"{worlds.Count} monde(s) chargé(s) depuis le NAS.";
            ResetPreview();
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or HttpRequestException)
        {
            FeedbackText.Text = "Impossible de charger les mondes : " + exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (WorldComboBox.SelectedItem is not WorldSelectionItem selected)
        {
            FeedbackText.Text = "Sélectionnez un monde serveur.";
            return;
        }

        SetBusy(true);
        try
        {
            var previewResult = await _pipeClient.SendAsync(new PipeRequest("world-preview", WorldId: selected.WorldId));
            if (previewResult.Success && previewResult.Data is JsonElement preview)
            {
                ApplyPreview(preview);
            }

            var preflight = await _pipeClient.SendAsync(new PipeRequest("preflight", WorldId: selected.WorldId));
            _preflightCompatible = preflight.Success;
            CompatibilityText.Text = preflight.Message;
            CompatibilityText.Foreground = preflight.Success
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.Orange;
            TransferStartButton.IsEnabled = _preflightCompatible && _wgsTransferEnabled;
            FeedbackText.Text = preflight.Success
                ? "Préflight réussi. Cette sauvegarde contient bien votre pseudo."
                : preflight.Message;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or HttpRequestException)
        {
            _preflightCompatible = false;
            TransferStartButton.IsEnabled = false;
            CompatibilityText.Text = "Échec du préflight : " + exception.Message;
            FeedbackText.Text = CompatibilityText.Text;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void TransferStart_Click(object sender, RoutedEventArgs e)
    {
        if (WorldComboBox.SelectedItem is not WorldSelectionItem selected)
        {
            FeedbackText.Text = "Sélectionnez un monde serveur.";
            return;
        }

        var result = await _pipeClient.SendAsync(new PipeRequest(
            "transfer-start",
            WorldId: selected.WorldId,
            PlayerName: _registeredPlayerName));
        FeedbackText.Text = result.Message;
    }

    private async Task RefreshAsync()
    {
        SetBusy(true);
        try
        {
            var status = await _pipeClient.SendAsync(new PipeRequest("status"));
            ServiceState.Text = status.Success ? "Connecté" : "Erreur";

            _enrolled = false;
            _wgsTransferEnabled = false;
            _registeredPlayerName = null;

            if (status.Data is JsonElement data)
            {
                if (data.TryGetProperty("state", out var state))
                {
                    _enrolled = state.TryGetProperty("deviceId", out var deviceId) &&
                                deviceId.ValueKind == JsonValueKind.String;
                    if (state.TryGetProperty("registeredPlayerName", out var playerElement) &&
                        playerElement.ValueKind == JsonValueKind.String)
                    {
                        _registeredPlayerName = playerElement.GetString();
                    }
                }

                _wgsTransferEnabled = data.TryGetProperty("wgsTransferEnabled", out var transferEnabledElement) &&
                                      transferEnabledElement.GetBoolean();
            }

            EnrollButton.IsEnabled = !_enrolled;
            DeviceNameText.IsEnabled = !_enrolled;
            EnrollmentCodeText.IsEnabled = !_enrolled;
            SavePlayerButton.IsEnabled = _enrolled;

            if (!string.IsNullOrWhiteSpace(_registeredPlayerName) &&
                !PlayerProfileText.IsKeyboardFocusWithin)
            {
                PlayerProfileText.Text = _registeredPlayerName;
            }

            ProfileState.Text = _enrolled
                ? string.IsNullOrWhiteSpace(_registeredPlayerName)
                    ? "Associé — pseudo à configurer"
                    : $"Associé — {_registeredPlayerName}"
                : "PC non associé";

            LocalGateText.Text = _wgsTransferEnabled
                ? "Écriture WGS : ACTIVÉE"
                : "Écriture WGS : désactivée (Phase 3)";
            LocalGateText.Foreground = _wgsTransferEnabled
                ? System.Windows.Media.Brushes.OrangeRed
                : System.Windows.Media.Brushes.LightGreen;

            var server = await _pipeClient.SendAsync(new PipeRequest("server-health"));
            var healthy = server.Data is JsonElement serverData &&
                          serverData.TryGetProperty("healthy", out var health) &&
                          health.GetBoolean();
            ServerState.Text = healthy ? "Healthy" : "Non joignable";

            var transfer = await _pipeClient.SendAsync(new PipeRequest("transfer-active"));
            TransferStateText.Text = transfer.Message;
            TransferStartButton.IsEnabled = _preflightCompatible && _wgsTransferEnabled;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            ServiceState.Text = "Non installé ou arrêté";
            ServerState.Text = "Non vérifié";
            ProfileState.Text = "Indisponible";
            FeedbackText.Text = exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyPreview(JsonElement preview)
    {
        var displayName = preview.TryGetProperty("saveDisplayName", out var displayElement) &&
                          displayElement.ValueKind == JsonValueKind.String
            ? displayElement.GetString()
            : null;
        var seed = preview.TryGetProperty("worldSeed", out var seedElement) &&
                   seedElement.ValueKind == JsonValueKind.Number
            ? seedElement.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "—";
        var status = preview.TryGetProperty("status", out var statusElement)
            ? statusElement.GetString() ?? "Unknown"
            : "Unknown";
        var artifact = preview.TryGetProperty("artifactSha256", out var hashElement) &&
                       hashElement.ValueKind == JsonValueKind.String
            ? hashElement.GetString()
            : null;

        WorldDetailsText.Text =
            $"Nom sauvegarde : {displayName ?? "—"}\n" +
            $"Seed : {seed}\n" +
            $"État serveur : {status}\n" +
            $"Artefact : {(artifact is null ? "aucun" : artifact[..Math.Min(12, artifact.Length)] + "…")}";

        if (preview.TryGetProperty("players", out var players) && players.ValueKind == JsonValueKind.Array)
        {
            var lines = new List<string>();
            foreach (var player in players.EnumerateArray())
            {
                var id = player.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : -1;
                var name = player.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "?" : "?";
                var host = player.TryGetProperty("isHost", out var hostElement) && hostElement.GetBoolean();
                lines.Add($"• {name} — ID {id}{(host ? " — hôte actuel" : string.Empty)}");
            }
            PlayersText.Text = lines.Count == 0 ? "Aucun joueur dans le manifeste." : string.Join(Environment.NewLine, lines);
        }
        else
        {
            PlayersText.Text = "Aucun joueur dans le manifeste.";
        }
    }

    private void ResetPreview()
    {
        _preflightCompatible = false;
        TransferStartButton.IsEnabled = false;
        CompatibilityText.Text = "Sélectionnez un monde puis lancez la vérification.";
        CompatibilityText.Foreground = System.Windows.Media.Brushes.White;
        PlayersText.Text = "—";
        WorldDetailsText.Text = "—";
    }

    private void SetBusy(bool busy) => IsEnabled = !busy;

    private sealed record WorldSelectionItem(
        Guid WorldId,
        string Name,
        string Status,
        bool HasArtifact,
        string Label);
}
