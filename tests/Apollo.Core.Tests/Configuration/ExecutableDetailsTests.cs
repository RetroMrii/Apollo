using Apollo.Core.Configuration;

namespace Apollo.Core.Tests.Configuration;

[TestClass]
public sealed class ExecutableDetailsTests
{
    [TestMethod]
    public void FromPath_WithExecutable_InfersNormalizedPathAndProcessName()
    {
        string relativePath = Path.Combine("Games", "Example.Game.exe");

        ExecutableDetails details = ExecutableDetails.FromPath(relativePath);

        Assert.AreEqual(Path.GetFullPath(relativePath), details.ExecutablePath);
        Assert.AreEqual("Example.Game", details.ProcessName);
        Assert.IsFalse(string.IsNullOrWhiteSpace(details.DisplayName));
    }

    [TestMethod]
    public void FromPath_WithNonExecutable_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ExecutableDetails.FromPath("game.txt"));
    }

    [TestMethod]
    [DataRow("Medal.exe", "Medal")]
    [DataRow("  Medal  ", "Medal")]
    [DataRow(@"C:\Apps\Medal.exe", "Medal")]
    public void NormalizeProcessName_RemovesPathQuotesAndExtension(string input, string expected)
    {
        Assert.AreEqual(expected, ConfigurationNormalizer.NormalizeProcessName(input));
    }
}
