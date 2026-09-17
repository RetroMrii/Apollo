using Apollo.Core.Configuration;

namespace Apollo.Services;

internal interface IConfigurationDialogService
{
    RecorderConfiguration? ShowRecorderDialog(RecorderConfiguration? currentRecorder);

    GameConfiguration? ShowGameDialog();

    bool ConfirmGameRemoval(string displayName);

    void ShowError(string title, string message);
}
