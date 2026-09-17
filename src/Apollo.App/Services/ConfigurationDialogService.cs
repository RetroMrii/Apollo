using System.Windows;
using Apollo.Core.Configuration;
using Apollo.Views;

namespace Apollo.Services;

internal sealed class ConfigurationDialogService : IConfigurationDialogService
{
    public RecorderConfiguration? ShowRecorderDialog(RecorderConfiguration? currentRecorder)
    {
        var dialog = new ExecutableDialog("Configure recorder", "Choose the recorder executable", currentRecorder is null
            ? null
            : new ExecutableDetails(currentRecorder.DisplayName, currentRecorder.ExecutablePath, currentRecorder.ProcessName));

        if (!ShowDialog(dialog, out var details))
        {
            return null;
        }

        return new RecorderConfiguration
        {
            DisplayName = details.DisplayName,
            ExecutablePath = details.ExecutablePath,
            ProcessName = details.ProcessName,
        };
    }

    public GameConfiguration? ShowGameDialog()
    {
        var dialog = new ExecutableDialog(
            "Add game",
            "Add a monitored game",
            allowRunningApplications: true);
        if (!ShowDialog(dialog, out var details))
        {
            return null;
        }

        return new GameConfiguration
        {
            DisplayName = details.DisplayName,
            ExecutablePath = details.ExecutablePath,
            ProcessName = details.ProcessName,
            IsEnabled = true,
        };
    }

    public bool ConfirmGameRemoval(string displayName)
    {
        return System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            $"Remove {displayName} from Apollo?",
            "Remove game",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    public void ShowError(string title, string message)
    {
        System.Windows.MessageBox.Show(
            System.Windows.Application.Current.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static bool ShowDialog(ExecutableDialog dialog, out ExecutableDetails details)
    {
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        if (dialog.ShowDialog() == true && dialog.SelectedDetails is not null)
        {
            details = dialog.SelectedDetails;
            return true;
        }

        details = null!;
        return false;
    }
}
