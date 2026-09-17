using Apollo.Services;

namespace Apollo.App.Tests.Services;

[TestClass]
public sealed class SystemProcessSnapshotProviderTests
{
    [TestMethod]
    public async Task GetRunningProcessNamesAsync_ToleratesInaccessibleAndExitingProcesses()
    {
        var provider = new SystemProcessSnapshotProvider();

        IReadOnlySet<string> processNames = await provider.GetRunningProcessNamesAsync(CancellationToken.None);

        Assert.IsNotNull(processNames);
        Assert.Contains(
            Environment.ProcessPath is null
                ? "testhost"
                : Path.GetFileNameWithoutExtension(Environment.ProcessPath),
            processNames);
    }
}
