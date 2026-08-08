using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace GameSaveHub.Client.App;

public partial class App : Application
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "GameSaveHub",
        "app.log");

    /// <summary>
    /// Dernier filet de sécurité : une exception non gérée dans un gestionnaire
    /// <c>async void</c> fermait l'application sans le moindre message, y compris
    /// en pleine session de transfert. Elle est désormais journalisée et affichée,
    /// et l'application reste ouverte pour que l'utilisateur puisse rendre compte.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        TryLog(e.Exception);

        MessageBox.Show(
            $"Une erreur inattendue s'est produite :\n\n{e.Exception.Message}\n\n" +
            $"Le détail a été enregistré dans :\n{LogPath}\n\n" +
            "Aucune sauvegarde n'a été modifiée par cette erreur. " +
            "Vous pouvez actualiser l'écran ; si le problème persiste, transmettez ce fichier.",
            "GameSave Hub",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    internal static void TryLog(Exception exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.AppendAllText(
                LogPath,
                $"{DateTimeOffset.UtcNow:O} {exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // La journalisation ne doit jamais provoquer une seconde erreur.
        }
    }
}
