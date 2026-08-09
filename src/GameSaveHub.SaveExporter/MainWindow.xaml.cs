using System.Windows;
using System.Windows.Controls;
using GameSaveHub.Adapters.PlanetCrafter.GamePass;
using GameSaveHub.SaveExporter.Core;
using Microsoft.Win32;

namespace GameSaveHub.SaveExporter;

public partial class MainWindow : Window
{
    private readonly SaveExporterService _service = new(new PlanetCrafterGamePassAdapter());

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void WorldList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ExportButton.IsEnabled = WorldList.SelectedItem is SaveExportWorld;

    private async Task RefreshAsync()
    {
        SetBusy(true, "Recherche des sauvegardes…");
        try
        {
            var worlds = await _service.DiscoverAsync();
            WorldList.ItemsSource = worlds;
            WorldList.SelectedIndex = worlds.Count == 1 ? 0 : -1;
            StatusText.Text = worlds.Count switch
            {
                0 => "Aucune sauvegarde n'a été trouvée.",
                1 => "Une sauvegarde trouvée. Vérifiez les joueurs et la date avant l'export.",
                _ => $"{worlds.Count} sauvegardes trouvées. Les joueurs et la date permettent de distinguer les homonymes."
            };
        }
        catch (Exception exception)
        {
            WorldList.ItemsSource = null;
            StatusText.Text = "Impossible de lire les sauvegardes : " + exception.Message;
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (WorldList.SelectedItem is not SaveExportWorld world) return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choisir où enregistrer la sauvegarde",
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, "Export et vérification en cours… Ne lancez pas le jeu.");
        try
        {
            var artifact = await _service.ExportAsync(world.LogicalName, dialog.FolderName);
            StatusText.Text = $"Export terminé : {artifact.Path}";
            MessageBox.Show(
                this,
                $"Le fichier a été vérifié :\n\n{artifact.Path}\n\nEnvoyez ce fichier à Steven.",
                "Export terminé",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            StatusText.Text = "Export refusé ou interrompu : " + exception.Message;
            MessageBox.Show(this, exception.Message, "Export impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            SetBusy(false, StatusText.Text);
        }
    }

    private void SetBusy(bool busy, string status)
    {
        RefreshButton.IsEnabled = !busy;
        WorldList.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && WorldList.SelectedItem is SaveExportWorld;
        StatusText.Text = status;
    }
}
