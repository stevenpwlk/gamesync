using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GameSaveHub.Adapters.PlanetCrafter.GamePass;
using GameSaveHub.Client.Orchestration;

namespace GameSaveHub.Client.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ReportJsonOptions = new(JsonOptions) { WriteIndented = true };
    private static readonly Brush ActiveStep = new SolidColorBrush(Color.FromRgb(75, 135, 60));
    private static readonly Brush InactiveStep = new SolidColorBrush(Color.FromRgb(234, 229, 220));
    private static readonly Brush Online = new SolidColorBrush(Color.FromRgb(75, 135, 60));
    private static readonly Brush Offline = new SolidColorBrush(Color.FromRgb(179, 59, 50));

    private readonly PipeClient _pipeClient = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PlanetCrafterGamePassAdapter _interactiveAdapter = new();
    private HomeContextSnapshot? _context;
    private HomeViewState? _view;
    private bool _refreshInFlight;
    private bool _showingSettings;
    private bool _openXboxFallback;
    private string? _launchError;
    private HomeVisualState? _actionErrorState;
    private string? _actionError;

    public MainWindow()
    {
        InitializeComponent();
        DeviceNameText.Text = Environment.MachineName;
        _refreshTimer.Tick += RefreshTimer_Tick;
        Loaded += LoadedAsync;
        Closed += (_, _) => Dispose();
    }

    private async void LoadedAsync(object sender, RoutedEventArgs e)
    {
#if DEBUG
        if (Environment.GetEnvironmentVariable("GSH_VISUAL_PREVIEW") is "ready")
        {
            RenderReadyPreview();
            return;
        }
#endif
        await RefreshAsync();
        _refreshTimer.Start();
    }

#if DEBUG
    private void RenderReadyPreview()
    {
        var worldId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        _context = new HomeContextSnapshot(
            true,
            Guid.NewGuid(),
            "Stevenpwlk",
            true,
            new(worldId, "Monde principal", "Available", Guid.NewGuid(), true),
            new(
                worldId,
                "Monde principal",
                "Available",
                Guid.NewGuid(),
                null,
                null,
                new(Guid.NewGuid(), "Stevenpwlk", now.AddHours(-2))),
            null,
            null,
            null,
            false,
            true);
        _view = HomeStatePresenter.Present(_context);
        Render(_context, _view);
    }
#endif

    private async void RefreshTimer_Tick(object? sender, EventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_refreshInFlight || _lifetime.IsCancellationRequested) return;
        _refreshInFlight = true;
        try
        {
            var response = await _pipeClient.SendAsync(new PipeRequest("home-context"), _lifetime.Token);
            if (!response.Success || response.Data is not JsonElement data)
            {
                RenderUnavailable(response.Message);
                return;
            }

            _context = data.Deserialize<HomeContextSnapshot>(JsonOptions);
            if (_context is null)
            {
                RenderUnavailable("Le service local a renvoyé un état incomplet.");
                return;
            }

            _view = HomeStatePresenter.Present(_context);
            Render(_context, _view);
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException or JsonException)
        {
            Debug.WriteLine(exception);
            RenderUnavailable("Les détails sont disponibles dans le diagnostic.");
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    private void Render(HomeContextSnapshot context, HomeViewState view)
    {
        HealthDot.Fill = context.ServerHealthy ? Online : Offline;
        HealthText.Text = context.ServerHealthy ? "Service en ligne" : "Service indisponible";

        if (!context.IsEnrolled)
        {
            _showingSettings = false;
            ShowSetup(onboarding: true);
            return;
        }

        if (!_showingSettings)
        {
            HomePanel.Visibility = Visibility.Visible;
            SetupPanel.Visibility = Visibility.Collapsed;
        }

        StateTitle.Text = view.Title;
        StateInstruction.Text = view.Instruction;
        if (_actionErrorState != view.State)
        {
            _actionErrorState = null;
            _actionError = null;
        }
        else if (!string.IsNullOrWhiteSpace(_actionError))
        {
            StateInstruction.Text = _actionError;
        }
        PrimaryButton.Visibility = view.PrimaryAction == HomePrimaryAction.None ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = _openXboxFallback && view.PrimaryAction == HomePrimaryAction.LaunchGame
            ? "Ouvrir l'application Xbox"
            : view.PrimaryActionLabel;
        if (_openXboxFallback && view.PrimaryAction == HomePrimaryAction.LaunchGame && !string.IsNullOrWhiteSpace(_launchError))
            StateInstruction.Text = _launchError;
        BusyBar.Visibility = view.IsProgressIndeterminate ? Visibility.Visible : Visibility.Collapsed;
        SafetyText.Visibility = view.State is HomeVisualState.Ready or HomeVisualState.ReadyToPlay
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyProgress(view.ProgressStep);

        var activity = context.WorldStatus?.LastActivity;
        ActivityText.Text = activity is null
            ? "Aucune partie publiée récemment."
            : string.IsNullOrWhiteSpace(activity.PlayerName)
                ? $"La dernière partie a été sécurisée le {activity.PublishedAtUtc.ToLocalTime():dd/MM à HH:mm}."
                : $"{activity.PlayerName} a terminé sa partie le {activity.PublishedAtUtc.ToLocalTime():dd/MM à HH:mm}.";

        var remote = context.WorldStatus?.ActiveSession;
        WorldSummaryText.Text = remote is null
            ? $"{context.PrimaryWorld?.Name ?? "Monde principal"} · disponible pour {context.PlayerName}"
            : string.IsNullOrWhiteSpace(remote.PlayerName)
                ? "Un autre joueur possède actuellement le monde."
                : $"{remote.PlayerName} possède actuellement le monde.";
    }

    private void RenderUnavailable(string message)
    {
        HealthDot.Fill = Offline;
        HealthText.Text = "Service indisponible";
        StateTitle.Text = "Le monde est momentanément inaccessible";
        StateInstruction.Text = "La connexion sera réessayée automatiquement.";
        PrimaryButton.Visibility = Visibility.Collapsed;
        BusyBar.Visibility = Visibility.Visible;
        SafetyText.Visibility = Visibility.Collapsed;
        ActivityText.Text = message;
    }

    private void ApplyProgress(int step)
    {
        StepOneCircle.Background = ActiveStep;
        StepOneText.Foreground = ActiveStep;
        StepTwoCircle.Background = step >= 2 ? ActiveStep : InactiveStep;
        StepTwoText.Foreground = step >= 2 ? ActiveStep : (Brush)FindResource("TextPrimary");
        StepThreeCircle.Background = step >= 3 ? ActiveStep : InactiveStep;
        StepThreeText.Foreground = step >= 3 ? ActiveStep : (Brush)FindResource("TextPrimary");
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        if (_view is null || _context is null) return;
        PrimaryButton.IsEnabled = false;
        FeedbackText.Text = string.Empty;
        var refreshAfterAction = true;
        try
        {
            switch (_view.PrimaryAction)
            {
                case HomePrimaryAction.StartTransfer when _context.PrimaryWorld is { } world:
                    var started = await _pipeClient.SendAsync(new PipeRequest(
                        "transfer-start",
                        WorldId: world.WorldId,
                        PlayerName: _context.PlayerName), _lifetime.Token);
                    refreshAfterAction = HandleActionResult(started);
                    break;
                case HomePrimaryAction.LaunchGame:
                    await LaunchXboxGameAsync();
                    break;
                case HomePrimaryAction.ResumeTransfer when _context.LocalSession is { } session:
                    var resumed = await _pipeClient.SendAsync(new PipeRequest(
                        "transfer-resume",
                        TransferSessionId: session.LocalSessionId), _lifetime.Token);
                    refreshAfterAction = HandleActionResult(resumed);
                    break;
                case HomePrimaryAction.OpenDiagnostics:
                    await SaveDiagnosticAsync();
                    refreshAfterAction = false;
                    break;
            }
        }
        catch (Exception exception) when (exception is IOException or TimeoutException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Debug.WriteLine(exception);
            _actionErrorState = _view.State;
            _actionError = "L'action n'a pas pu aboutir. Vous pouvez réessayer ou ouvrir le diagnostic.";
            StateInstruction.Text = _actionError;
            refreshAfterAction = false;
        }
        finally
        {
            PrimaryButton.IsEnabled = true;
            if (refreshAfterAction) await RefreshAsync();
        }
    }

    private bool HandleActionResult(PipeResponse response)
    {
        if (response.Success)
        {
            _actionErrorState = null;
            _actionError = null;
            return true;
        }

        _actionErrorState = _view?.State;
        _actionError = HomeActionErrorPresenter.Present(response.Code);
        StateInstruction.Text = _actionError;
        return false;
    }

    private async Task LaunchXboxGameAsync()
    {
        if (_openXboxFallback)
        {
            Process.Start(new ProcessStartInfo { FileName = "xbox:", UseShellExecute = true });
            return;
        }

        var launch = await _interactiveAdapter.LaunchGameAsync(_lifetime.Token);
        if (launch.Success)
        {
            _openXboxFallback = false;
            _launchError = null;
            return;
        }

        _openXboxFallback = true;
        _launchError = "Le jeu n'a pas pu être lancé automatiquement. Ouvrez l'application Xbox pour réessayer.";
        StateInstruction.Text = _launchError;
        PrimaryButton.Content = "Ouvrir l'application Xbox";
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_context?.IsEnrolled != true) return;
        _showingSettings = !_showingSettings;
        if (_showingSettings)
        {
            PlayerNameText.Text = _context.PlayerName ?? string.Empty;
            ShowSetup(onboarding: false);
        }
        else
        {
            SetupPanel.Visibility = Visibility.Collapsed;
            HomePanel.Visibility = Visibility.Visible;
        }
    }

    private void ShowSetup(bool onboarding)
    {
        SetupPanel.Visibility = Visibility.Visible;
        HomePanel.Visibility = Visibility.Collapsed;
        SetupTitle.Text = onboarding ? "Associons ce PC" : "Réglages et diagnostics";
        SetupIntro.Text = onboarding
            ? "Cette étape ne sera demandée qu'une fois."
            : "Le parcours quotidien reste sur l'accueil.";
        DeviceFields.Visibility = onboarding ? Visibility.Visible : Visibility.Collapsed;
        EnrollmentFields.Visibility = onboarding ? Visibility.Visible : Visibility.Collapsed;
        EnrollButton.Content = onboarding ? "Continuer" : "Enregistrer le pseudo";
    }

    private async void Enroll_Click(object sender, RoutedEventArgs e)
    {
        FeedbackText.Text = string.Empty;
        if (_context?.IsEnrolled == true)
        {
            var profile = await _pipeClient.SendAsync(new PipeRequest(
                "profile-player-set",
                PlayerName: PlayerNameText.Text.Trim()), _lifetime.Token);
            FeedbackText.Text = profile.Message;
            if (profile.Success)
            {
                _showingSettings = false;
                await RefreshAsync();
            }
            return;
        }

        if (string.IsNullOrWhiteSpace(DeviceNameText.Text) ||
            string.IsNullOrWhiteSpace(PlayerNameText.Text) ||
            string.IsNullOrWhiteSpace(EnrollmentCodeText.Text))
        {
            FeedbackText.Text = "Le nom du PC, le pseudo et le code d'invitation sont requis.";
            return;
        }

        var result = await _pipeClient.SendAsync(new PipeRequest(
            "enroll",
            EnrollmentCodeText.Text.Trim(),
            DeviceNameText.Text.Trim(),
            PlayerName: PlayerNameText.Text.Trim()), _lifetime.Token);
        FeedbackText.Text = result.Message;
        if (result.Success) await RefreshAsync();
    }

    private async void Diagnostic_Click(object sender, RoutedEventArgs e) => await SaveDiagnosticAsync();

    private async Task SaveDiagnosticAsync()
    {
        var result = await _pipeClient.SendAsync(new PipeRequest("diagnostic-report"), _lifetime.Token);
        if (!result.Success || result.Data is not JsonElement data)
        {
            FeedbackText.Text = result.Message;
            return;
        }

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"GameSaveHub-diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data, ReportJsonOptions), _lifetime.Token);
        if (SetupPanel.Visibility != Visibility.Visible)
        {
            _showingSettings = true;
            ShowSetup(onboarding: false);
        }
        FeedbackText.Text = $"Rapport enregistré sur le Bureau : {Path.GetFileName(path)}";
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
