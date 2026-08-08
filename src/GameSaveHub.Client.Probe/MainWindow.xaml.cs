using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using GameSaveHub.Adapters.PlanetCrafter.GamePass;
using GameSaveHub.Contracts;
using Microsoft.Win32;

namespace GameSaveHub.Client.Probe;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly PlanetCrafterGamePassAdapter _adapter = new();
    private LocalStorageInspection? _inspection;
    private InstallationDetection? _installation;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Analyse en cours…");
        try
        {
            _installation = await _adapter.DetectInstallationAsync();
            _inspection = await _adapter.InspectLocalStorageAsync();
            WorldCombo.ItemsSource = _inspection.Worlds;
            WorldCombo.SelectedIndex = _inspection.Worlds.Count > 0 ? 0 : -1;
            WorldCombo.IsEnabled = _inspection.Worlds.Count > 0;
            IncludeArtifactCheck.IsEnabled = _inspection.Worlds.Count > 0 && !_inspection.GameRunning && _inspection.Stable;
            ExportButton.IsEnabled = true;
            ReportText.Text = BuildSummary(_installation, _inspection);
            StateText.Text = _inspection.GameRunning
                ? "Jeu actif — export de sauvegarde désactivé"
                : _inspection.Stable ? "Analyse cohérente" : "Fichiers instables";
        }
        catch (Exception exception)
        {
            ReportText.Text = "Échec du diagnostic : " + exception.Message;
            StateText.Text = "Échec";
        }
        finally
        {
            SetBusy(false, StateText.Text);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_inspection is null || _installation is null) return;
        var includeArtifact = IncludeArtifactCheck.IsChecked == true;
        if (includeArtifact && (_inspection.GameRunning || !_inspection.Stable || WorldCombo.SelectedItem is not DiscoveredWorld))
        {
            MessageBox.Show(this, "Fermez le jeu et relancez une analyse cohérente avant d’inclure une sauvegarde.", "Export refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Créer le rapport GameSave Hub",
            Filter = "Rapport GameSave Hub (*.gshdiag)|*.gshdiag",
            FileName = $"gamesavehub-probe-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.gshdiag",
            AddExtension = true,
            DefaultExt = ".gshdiag"
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, "Création du rapport…");
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "GameSaveHub-Probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var report = new
            {
                schemaVersion = 1,
                capturedAtUtc = DateTimeOffset.UtcNow,
                computer = new { os = Environment.OSVersion.VersionString, architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString() },
                installation = new { _installation.IsInstalled, _installation.PackageFamilyName, _installation.PackageFullName, _installation.InstalledVersion },
                inspection = _inspection,
                includesDisposableWorldArtifact = includeArtifact,
                probeVersion = typeof(MainWindow).Assembly.GetName().Version?.ToString()
            };
            await File.WriteAllTextAsync(Path.Combine(temporaryDirectory, "diagnostic.json"), JsonSerializer.Serialize(report, JsonOptions));

            if (includeArtifact && WorldCombo.SelectedItem is DiscoveredWorld world)
            {
                var artifact = await _adapter.ExportPortableArtifactAsync(world.LogicalName, temporaryDirectory);
                File.Move(artifact.Path, Path.Combine(temporaryDirectory, "disposable-world.gshsave"));
            }

            var target = Path.GetFullPath(dialog.FileName);
            var temporaryArchive = target + ".tmp";
            if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
            ZipFile.CreateFromDirectory(temporaryDirectory, temporaryArchive, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(temporaryArchive, target, overwrite: true);
            StateText.Text = "Rapport créé";
            MessageBox.Show(this, $"Rapport créé :\n{target}\n\nTransmettez uniquement ce fichier à Steven.", "Terminé", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            StateText.Text = "Échec de l’export";
            MessageBox.Show(this, exception.Message, "Échec", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try { Directory.Delete(temporaryDirectory, recursive: true); } catch (IOException) { }
            SetBusy(false, StateText.Text);
        }
    }

    private void SetBusy(bool busy, string state)
    {
        AnalyzeButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && _inspection is not null;
        StateText.Text = state;
    }

    private static string BuildSummary(InstallationDetection installation, LocalStorageInspection inspection)
    {
        var lines = new List<string>
        {
            $"Installation : {(installation.IsInstalled ? "détectée" : "absente")}",
            $"Version : {installation.InstalledVersion ?? "inconnue"}",
            $"Jeu actif : {(inspection.GameRunning ? "oui" : "non")}",
            $"Capture stable : {(inspection.Stable ? "oui" : "non")}",
            $"Fichiers WGS : {inspection.Files.Count} ({inspection.Files.Sum(x => x.Length):N0} octets)",
            ""
        };
        foreach (var world in inspection.Worlds)
        {
            lines.Add($"Monde : {world.DisplayName} [{world.LogicalName}]");
            foreach (var player in world.Players)
                lines.Add($"  • {player.Name} — id {player.Id}, hôte {(player.IsHost ? "oui" : "non")}, inventaire {player.InventoryId}, équipement {player.EquipmentId}");
        }
        if (inspection.Warnings.Count > 0)
        {
            lines.Add("");
            lines.AddRange(inspection.Warnings.Select(x => "Attention : " + x));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
