using Apollo.Core.Configuration;
using Apollo.Services;
using System.Windows.Media;

namespace Apollo.ViewModels;

internal sealed class GameItemViewModel(GameConfiguration configuration) : ObservableObject
{
    private bool _isRunning;

    public GameConfiguration Configuration { get; } = configuration;

    public Guid Id => Configuration.Id;

    public string DisplayName => Configuration.DisplayName;

    public string DisplayProcessName => $"{Configuration.ProcessName}.exe";

    public string ExecutablePath => Configuration.ExecutablePath;

    public ImageSource? Icon { get; } = ApplicationIconService.TryGetIcon(configuration.ExecutablePath);

    public bool IsRunning
    {
        get => _isRunning;
        set => SetProperty(ref _isRunning, value);
    }

    public bool IsEnabled
    {
        get => Configuration.IsEnabled;
        set
        {
            if (Configuration.IsEnabled == value)
            {
                return;
            }

            Configuration.IsEnabled = value;
            OnPropertyChanged();
        }
    }
}
