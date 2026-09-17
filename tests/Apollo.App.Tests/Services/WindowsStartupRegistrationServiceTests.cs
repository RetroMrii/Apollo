using Apollo.Services;
using Microsoft.Win32;

namespace Apollo.App.Tests.Services;

[TestClass]
public sealed class WindowsStartupRegistrationServiceTests
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    [TestMethod]
    public void SetEnabled_WritesQuotedCommandAndRemovesOnlyTestValue()
    {
        string valueName = $"Apollo.Tests.{Guid.NewGuid():N}";
        string executablePath = Path.GetFullPath(@"C:\Program Files\Apollo Test\Apollo.exe");
        var service = new WindowsStartupRegistrationService(executablePath, valueName);

        try
        {
            service.SetEnabled(false);
            Assert.IsFalse(service.IsEnabled());

            service.SetEnabled(true);

            Assert.IsTrue(service.IsEnabled());
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            Assert.AreEqual($"\"{executablePath}\"", key?.GetValue(valueName));
        }
        finally
        {
            service.SetEnabled(false);
        }

        using RegistryKey? verificationKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        Assert.IsNull(verificationKey?.GetValue(valueName));
    }
}
