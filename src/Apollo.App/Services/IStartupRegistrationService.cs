namespace Apollo.Services;

internal interface IStartupRegistrationService
{
    bool IsSupported { get; }

    bool IsEnabled();

    void SetEnabled(bool enabled);
}
