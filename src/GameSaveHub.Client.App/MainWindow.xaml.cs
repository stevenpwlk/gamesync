using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.App;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly SolidColorBrush NeutralBrush = new((Color)ColorConverter.ConvertFromString("#AEB8C5"));
    private static readonly SolidColorBrush ProgressBrush = new((Color)ColorConverter.ConvertFromString("#7FB3D5"));
    private static readonly SolidColorBrush ActionBrush = new((Color)ColorConverter.ConvertFromString("#9FD3A8"));
    private static readonly SolidColorBrush WarningBrush = new((Color)ColorConverter.ConvertFromString("#FFD28C"));
    private static readonly SolidColorBrush DangerBrush = new((Color)ColorConverter.ConvertFromString("#FF9B8C"));

    private readonly PipeClient _pipeClient = new();
    private readonly DispatcherTimer _sessionTimer;

    private bool _enrolled;
    private bool _wgsTransferEnabled;
    private bool _preflightCompatible;
    private bool _needsManualReview;
    private string? _registeredPlayerName;
    private TransferSession? _activeSession;
    private WizardView _wizard = TransferWizardPresenter.Describe(null, false, false);
    private int _busyDepth;

    public MainWindow()
    {
        InitializeComponent();
        DeviceNameText.Text = Environment.MachineName;

        // Les étapes automatiques (import, envoi) avancent sans intervention : sans ce
        // rafraîchissement l'utilisateur croirait l'application figée et cliquerait au hasard.
        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _sessionTimer.Tick += SessionTimer_Tick;

        Loaded += async (_, _) => await GuardAsync(RefreshAsync);
    }

    // ---------------------------------------------------------------- gestionnaires

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await GuardAsync(RefreshAsync);

    private async void LoadWorlds_Click(object sender, RoutedEventArgs e) => await GuardAsync(LoadWorldsAsync);

    private async void SessionTimer_Tick(object? sender, EventArgs e)
    {
        if (_busyDepth > 0) return;
        await GuardAsync(RefreshTransferAsync);
    }

    private async void Enroll_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(DeviceNameText.Text) ||
            string.IsNullOrWhiteSpace(PlayerProfileText.Text) ||
            string.IsNullOrWhiteSpace(EnrollmentCodeText.Text))
        {
            FeedbackText.Text = "Nom du PC, pseudo Planet Crafter et code d’invitation sont requis.";
            return;
        }

        var result = await _pipeClient.SendAsync(new PipeRequest(
            "enroll",
            EnrollmentCodeText.Text.Trim(),
            DeviceNameText.Text.Trim(),
            PlayerName: PlayerProfileText.Text.Trim()));

        FeedbackText.Text = result.Message;
        if (!result.Success) return;

        EnrollmentCodeText.Clear();
        await RefreshAsync();
        await LoadWorldsAsync();
    });

    private async void SavePlayer_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
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

        var result = await _pipeClient.SendAsync(new PipeRequest(
            "profile-player-set",
            PlayerName: PlayerProfileText.Text.Trim()));

        FeedbackText.Text = result.Message;

        // Le pseudo conditionne le préflight : un changement invalide la vérification précédente.
        ResetPreview();
        if (result.Success) await RefreshAsync();
    });

    private void WorldComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ResetPreview();

    private async void Preview_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (WorldComboBox.SelectedItem is not WorldSelectionItem selected)
        {
            FeedbackText.Text = "Sélectionnez un monde serveur.";
            return;
        }

        var previewResult = await _pipeClient.SendAsync(new PipeRequest("world-preview", WorldId: selected.WorldId));
        if (previewResult.Success && previewResult.Data is JsonElement preview)
        {
            ApplyPreview(preview);
        }

        var preflight = await _pipeClient.SendAsync(new PipeRequest("preflight", WorldId: selected.WorldId));
        _preflightCompatible = preflight.Success;
        CompatibilityText.Text = preflight.Message;
        CompatibilityText.Foreground = preflight.Success ? ActionBrush : WarningBrush;
        FeedbackText.Text = preflight.Success
            ? "Préflight réussi : cette sauvegarde contient bien votre pseudo."
            : preflight.Message;

        RenderWizard();
    });

    private async void WizardPrimary_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_wizard.PrimaryAction is not { } action) return;

        // Commande nulle : action purement locale (fermer un récapitulatif terminal).
        if (action.Command is null)
        {
            _activeSession = null;
            _sessionTimer.Stop();
            FeedbackText.Text = string.Empty;
            await RefreshAsync();
            return;
        }

        if (action.Command == TransferWizardPresenter.StartCommand)
        {
            await StartTransferAsync();
            return;
        }

        await SendSessionCommandAsync(action.Command);
    });

    private async void WizardAbort_Click(object sender, RoutedEventArgs e) => await GuardAsync(async () =>
    {
        if (_activeSession is null) return;

        var confirmation = MessageBox.Show(
            "Abandonner ce transfert ?\n\n" +
            "Aucune de vos sauvegardes n’a encore été modifiée, et le monde sera rendu aux autres joueurs.",
            "GameSave Hub",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes) return;

        await SendSessionCommandAsync(TransferWizardPresenter.AbortCommand);
    });

    private void CopyPlaceholder_Click(object sender, RoutedEventArgs e)
    {
        var name = _wizard.PlaceholderName;
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            Clipboard.SetText(name);
            FeedbackText.Text = $"« {name} » copié. Collez-le comme nom de la nouvelle partie dans Planet Crafter.";
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            FeedbackText.Text = "Copie impossible — recopiez le nom manuellement, exactement tel qu’affiché.";
        }
    }

    // ---------------------------------------------------------------- opérations

    private async Task StartTransferAsync()
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
        await RefreshTransferAsync();
    }

    private async Task SendSessionCommandAsync(string command)
    {
        if (_activeSession is null)
        {
            FeedbackText.Text = "Aucune session de transfert active.";
            return;
        }

        var result = await _pipeClient.SendAsync(new PipeRequest(
            command,
            TransferSessionId: _activeSession.LocalSessionId));

        FeedbackText.Text = result.Message;
        await RefreshTransferAsync();
    }

    private async Task RefreshAsync()
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

            _wgsTransferEnabled = data.TryGetProperty("wgsTransferEnabled", out var gate) &&
                                  gate.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                  gate.GetBoolean();
        }

        if (!string.IsNullOrWhiteSpace(_registeredPlayerName) && !PlayerProfileText.IsKeyboardFocusWithin)
        {
            PlayerProfileText.Text = _registeredPlayerName;
        }

        ProfileState.Text = _enrolled
            ? string.IsNullOrWhiteSpace(_registeredPlayerName)
                ? "Associé — pseudo à configurer"
                : $"Associé — {_registeredPlayerName}"
            : "PC non associé";

        LocalGateText.Text = _wgsTransferEnabled ? "ACTIVÉE" : "Désactivée";
        LocalGateText.Foreground = _wgsTransferEnabled ? WarningBrush : ActionBrush;
        LocalGateHelpText.Text = _wgsTransferEnabled
            ? "Ce PC peut écrire dans vos sauvegardes Planet Crafter. Suivez l’assistant sans sauter d’étape."
            : "Cette version peut lister et vérifier les mondes du NAS mais ne peut pas écrire dans vos sauvegardes.";

        GateBadgeText.Text = _wgsTransferEnabled
            ? "TRANSFERT ACTIVÉ SUR CE PC"
            : "ÉCRITURE DES SAUVEGARDES DÉSACTIVÉE";

        var server = await _pipeClient.SendAsync(new PipeRequest("server-health"));
        var healthy = server.Data is JsonElement serverData &&
                      serverData.TryGetProperty("healthy", out var health) &&
                      health.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                      health.GetBoolean();
        ServerState.Text = healthy ? "Healthy" : "Non joignable";

        await RefreshTransferAsync();
    }

    private async Task RefreshTransferAsync()
    {
        var result = await _pipeClient.SendAsync(new PipeRequest("transfer-active"));

        _needsManualReview = result.Code == "multiple_active_transfers";
        _activeSession = ParseSession(result);

        if (_needsManualReview)
        {
            FeedbackText.Text = result.Message;
        }

        RenderWizard();

        var shouldPoll = _activeSession is not null &&
                         !TransferStageRules.IsTerminal(_activeSession.Stage) &&
                         !_needsManualReview;

        if (shouldPoll) _sessionTimer.Start(); else _sessionTimer.Stop();
    }

    private static TransferSession? ParseSession(PipeResponse response)
    {
        if (response.Data is not JsonElement data || data.ValueKind != JsonValueKind.Object) return null;
        if (!data.TryGetProperty("session", out var session) || session.ValueKind != JsonValueKind.Object) return null;

        try
        {
            return session.Deserialize<TransferSession>(JsonOptions);
        }
        catch (JsonException)
        {
            // Service d'une version différente : mieux vaut un écran neutre qu'un état inventé.
            return null;
        }
    }

    // ---------------------------------------------------------------- rendu

    private void RenderWizard()
    {
        _wizard = TransferWizardPresenter.Describe(_activeSession, _preflightCompatible, _wgsTransferEnabled);

        if (_needsManualReview)
        {
            _wizard = _wizard with
            {
                Title = "Plusieurs transferts actifs",
                Instruction = "GameSave Hub a détecté plus d’une session locale et refuse de choisir à votre place. " +
                              "Aucune écriture automatique ne sera faite. Signalez-le avant toute nouvelle tentative.",
                Steps = [],
                PrimaryAction = null,
                ShowAbort = false,
                IsWaitingOnService = false,
                Tone = WizardTone.Danger
            };
        }

        WizardTitle.Text = _wizard.Title;
        WizardInstruction.Text = _wizard.Instruction;
        WizardTitle.Foreground = ToneBrush(_wizard.Tone);

        PlaceholderText.Text = _wizard.PlaceholderName ?? "—";
        PlaceholderCard.Visibility = string.IsNullOrWhiteSpace(_wizard.PlaceholderName)
            ? Visibility.Collapsed
            : Visibility.Visible;

        WizardSteps.ItemsSource = _wizard.Steps;
        WizardSteps.Visibility = _wizard.Steps.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        WizardProgress.Visibility = _wizard.IsWaitingOnService ? Visibility.Visible : Visibility.Collapsed;

        WizardDetail.Text = _wizard.Detail ?? string.Empty;
        WizardDetail.Visibility = string.IsNullOrWhiteSpace(_wizard.Detail) ? Visibility.Collapsed : Visibility.Visible;
        WizardDetail.Foreground = _wizard.Tone == WizardTone.Success ? NeutralBrush : WarningBrush;

        if (_wizard.PrimaryAction is { } action)
        {
            WizardPrimaryButton.Content = action.Label;
            WizardPrimaryButton.Visibility = Visibility.Visible;
        }
        else
        {
            WizardPrimaryButton.Visibility = Visibility.Collapsed;
        }

        WizardAbortButton.Visibility = _wizard.ShowAbort ? Visibility.Visible : Visibility.Collapsed;

        // Pendant une session, le catalogue n'a plus de sens : l'utilisateur ne doit pas
        // pouvoir en changer et croire que l'assistant a suivi.
        CatalogPanel.IsEnabled = _activeSession is null || TransferStageRules.IsTerminal(_activeSession.Stage);

        ApplyEnabledStates();
    }

    private void ApplyPreview(JsonElement preview)
    {
        var displayName = preview.TryGetProperty("saveDisplayName", out var displayElement) &&
                          displayElement.ValueKind == JsonValueKind.String
            ? displayElement.GetString()
            : null;

        var seed = preview.TryGetProperty("worldSeed", out var seedElement) &&
                   seedElement.ValueKind == JsonValueKind.Number &&
                   seedElement.TryGetInt64(out var seedValue)
            ? seedValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "—";

        var status = preview.TryGetProperty("status", out var statusElement) &&
                     statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString() ?? "Inconnu"
            : "Inconnu";

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
                var id = player.TryGetProperty("id", out var idElement) &&
                         idElement.ValueKind == JsonValueKind.Number &&
                         idElement.TryGetInt32(out var idValue)
                    ? idValue.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "?";

                var name = player.TryGetProperty("name", out var nameElement) &&
                           nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? "?"
                    : "?";

                var host = player.TryGetProperty("isHost", out var hostElement) &&
                           hostElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                           hostElement.GetBoolean();

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
        CompatibilityText.Text = "Sélectionnez un monde puis lancez la vérification.";
        CompatibilityText.Foreground = NeutralBrush;
        PlayersText.Text = "—";
        WorldDetailsText.Text = "—";
        RenderWizard();
    }

    private async Task LoadWorldsAsync()
    {
        if (!_enrolled)
        {
            FeedbackText.Text = "Associez d’abord ce PC au serveur.";
            return;
        }

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
                    idElement.ValueKind != JsonValueKind.String ||
                    !Guid.TryParse(idElement.GetString(), out var worldId))
                {
                    continue;
                }

                var name = item.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                    ? nameElement.GetString() ?? worldId.ToString("D")
                    : worldId.ToString("D");
                var status = item.TryGetProperty("status", out var statusElement) && statusElement.ValueKind == JsonValueKind.String
                    ? statusElement.GetString() ?? "Inconnu"
                    : "Inconnu";
                var hasArtifact = item.TryGetProperty("hasArtifact", out var artifactElement) &&
                                  artifactElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                                  artifactElement.GetBoolean();

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

    // ---------------------------------------------------------------- plomberie

    /// <summary>
    /// Enveloppe commune à tous les gestionnaires <c>async void</c>. Sans elle, une
    /// exception non filtrée ferme l'application sans message — y compris en pleine
    /// session de transfert.
    /// </summary>
    private async Task GuardAsync(Func<Task> operation)
    {
        SetBusy(true);
        try
        {
            await operation();
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or HttpRequestException)
        {
            ServiceState.Text = "Non installé ou arrêté";
            FeedbackText.Text = "Service local indisponible : " + exception.Message;
            _sessionTimer.Stop();
        }
        catch (Exception exception)
        {
            App.TryLog(exception);
            FeedbackText.Text = "Erreur inattendue : " + exception.Message +
                                " — le détail est dans %ProgramData%\\GameSaveHub\\app.log.";
            _sessionTimer.Stop();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busyDepth = Math.Max(0, busy ? _busyDepth + 1 : _busyDepth - 1);
        var active = _busyDepth > 0;

        BusyBar.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        Cursor = active ? System.Windows.Input.Cursors.Wait : null;

        if (active)
        {
            foreach (var control in ActionControls()) control.IsEnabled = false;
        }
        else
        {
            ApplyEnabledStates();
        }
    }

    private IEnumerable<Control> ActionControls()
    {
        yield return RefreshButton;
        yield return EnrollButton;
        yield return SavePlayerButton;
        yield return LoadWorldsButton;
        yield return PreviewButton;
        yield return WizardPrimaryButton;
        yield return WizardAbortButton;
        yield return CopyPlaceholderButton;
        yield return WorldComboBox;
    }

    private void ApplyEnabledStates()
    {
        if (_busyDepth > 0) return;

        var sessionActive = _activeSession is not null && !TransferStageRules.IsTerminal(_activeSession.Stage);

        RefreshButton.IsEnabled = true;
        CopyPlaceholderButton.IsEnabled = true;
        WizardPrimaryButton.IsEnabled = true;
        WizardAbortButton.IsEnabled = true;

        EnrollButton.IsEnabled = !_enrolled;
        DeviceNameText.IsEnabled = !_enrolled;
        EnrollmentCodeText.IsEnabled = !_enrolled;

        // Changer de pseudo pendant une session est refusé par le service : ne pas le proposer.
        SavePlayerButton.IsEnabled = _enrolled && !sessionActive;
        PlayerProfileText.IsEnabled = !sessionActive;

        LoadWorldsButton.IsEnabled = _enrolled && !sessionActive;
        PreviewButton.IsEnabled = _enrolled && !sessionActive;
        WorldComboBox.IsEnabled = !sessionActive;
    }

    private static SolidColorBrush ToneBrush(WizardTone tone) => tone switch
    {
        WizardTone.Progress => ProgressBrush,
        WizardTone.Action => ActionBrush,
        WizardTone.Success => ActionBrush,
        WizardTone.Warning => WarningBrush,
        WizardTone.Danger => DangerBrush,
        _ => NeutralBrush
    };

    private sealed record WorldSelectionItem(
        Guid WorldId,
        string Name,
        string Status,
        bool HasArtifact,
        string Label);
}
