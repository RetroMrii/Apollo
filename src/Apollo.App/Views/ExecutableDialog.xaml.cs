using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Apollo.Core.Configuration;
using Apollo.Services;
using Microsoft.Win32;

namespace Apollo.Views;

public partial class ExecutableDialog : Window
{
    private readonly bool _allowRunningApplications;
    private readonly RunningApplicationService _runningApplicationService = new();
    private CancellationTokenSource? _runningLoadCancellation;
    private bool _dialogReady;
    private bool _runningApplicationsLoaded;

    public ExecutableDialog(
        string title,
        string heading,
        ExecutableDetails? initialDetails = null,
        bool allowRunningApplications = false)
    {
        _allowRunningApplications = allowRunningApplications;
        InitializeComponent();
        Title = title;
        HeadingTextBlock.Text = heading;
        ModeSelector.Visibility = allowRunningApplications ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.Content = allowRunningApplications ? "Add" : "Save";
        Height = allowRunningApplications ? 540 : 410;

        if (initialDetails is not null)
        {
            PathTextBox.Text = initialDetails.ExecutablePath;
            DisplayNameTextBox.Text = initialDetails.DisplayName;
            ProcessNameTextBox.Text = initialDetails.ProcessName;
        }

        _dialogReady = true;
        ShowBrowseMode();
        Closed += ExecutableDialog_Closed;
    }

    public ExecutableDetails? SelectedDetails { get; private set; }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = HeadingTextBlock.Text,
            Filter = "Windows applications (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(PathTextBox.Text))
        {
            string? directory = Path.GetDirectoryName(PathTextBox.Text);
            if (Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ExecutableDetails details = ExecutableDetails.FromPath(dialog.FileName);
            PathTextBox.Text = details.ExecutablePath;
            DisplayNameTextBox.Text = details.DisplayName;
            ProcessNameTextBox.Text = details.ProcessName;
            HideValidation();
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            ShowValidation("Apollo could not read that executable. Choose a different file.");
        }
    }

    private void BrowseModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_dialogReady)
        {
            ShowBrowseMode();
        }
    }

    private async void RunningModeButton_Checked(object sender, RoutedEventArgs e)
    {
        if (!_dialogReady || !_allowRunningApplications)
        {
            return;
        }

        BrowsePanel.Visibility = Visibility.Collapsed;
        RunningPanel.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = RunningApplicationsListBox.SelectedItem is not null;
        HideValidation();

        if (!_runningApplicationsLoaded)
        {
            await LoadRunningApplicationsAsync();
        }
    }

    private async void RefreshRunningApplicationsButton_Click(object sender, RoutedEventArgs e)
        => await LoadRunningApplicationsAsync();

    private void RunningApplicationsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveButton.IsEnabled = RunningApplicationsListBox.SelectedItem is not null;
        HideValidation();
    }

    private void RunningApplicationsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RunningApplicationsListBox.SelectedItem is RunningApplicationInfo)
        {
            SaveRunningApplication();
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (RunningPanel.Visibility == Visibility.Visible)
        {
            SaveRunningApplication();
            return;
        }

        SaveBrowsedExecutable();
    }

    private void SaveBrowsedExecutable()
    {
        string path = PathTextBox.Text.Trim();
        string displayName = DisplayNameTextBox.Text.Trim();
        string processName = ConfigurationNormalizer.NormalizeProcessName(ProcessNameTextBox.Text, path);

        if (!File.Exists(path) || !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            ShowValidation("Choose an existing Windows executable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            ShowValidation("Enter a display name.");
            DisplayNameTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(processName))
        {
            ShowValidation("Enter a process name.");
            ProcessNameTextBox.Focus();
            return;
        }

        SelectedDetails = new ExecutableDetails(displayName, Path.GetFullPath(path), processName);
        DialogResult = true;
    }

    private void SaveRunningApplication()
    {
        if (RunningApplicationsListBox.SelectedItem is not RunningApplicationInfo application)
        {
            ShowValidation("Select a running application.");
            return;
        }

        SelectedDetails = new ExecutableDetails(
            application.DisplayName,
            application.ExecutablePath,
            application.ProcessName);
        DialogResult = true;
    }

    private async Task LoadRunningApplicationsAsync()
    {
        _runningLoadCancellation?.Cancel();
        _runningLoadCancellation?.Dispose();
        _runningLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _runningLoadCancellation.Token;

        RunningApplicationsListBox.ItemsSource = null;
        RunningApplicationsListBox.Visibility = Visibility.Collapsed;
        RunningStatusTextBlock.Text = "Loading running applications…";
        RunningStatusPanel.Visibility = Visibility.Visible;
        SaveButton.IsEnabled = false;
        HideValidation();

        try
        {
            IReadOnlyList<RunningApplicationInfo> applications =
                await _runningApplicationService.GetRunningApplicationsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            _runningApplicationsLoaded = true;
            RunningApplicationsListBox.ItemsSource = applications;
            RunningApplicationsListBox.Visibility = applications.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
            RunningStatusPanel.Visibility = applications.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            RunningStatusTextBlock.Text = applications.Count == 0
                ? "No open user applications were found. Open the game or browse to its executable."
                : string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                          or Win32Exception
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            RunningStatusTextBlock.Text = "Apollo could not enumerate running applications. You can still browse to the executable.";
            RunningStatusPanel.Visibility = Visibility.Visible;
        }
    }

    private void ShowBrowseMode()
    {
        BrowsePanel.Visibility = Visibility.Visible;
        RunningPanel.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = true;
        HideValidation();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ExecutableDialog_Closed(object? sender, EventArgs e)
    {
        _runningLoadCancellation?.Cancel();
        _runningLoadCancellation?.Dispose();
        _runningLoadCancellation = null;
        RunningApplicationsListBox.ItemsSource = null;
    }

    private void ShowValidation(string message)
    {
        ValidationTextBlock.Text = message;
        ValidationTextBlock.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationTextBlock.Text = string.Empty;
        ValidationTextBlock.Visibility = Visibility.Collapsed;
    }
}
