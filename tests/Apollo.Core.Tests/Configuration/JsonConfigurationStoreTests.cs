using Apollo.Core.Configuration;

namespace Apollo.Core.Tests.Configuration;

[TestClass]
public sealed class JsonConfigurationStoreTests
{
    private string _testDirectory = null!;
    private string _configurationPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "Apollo.Tests", Guid.NewGuid().ToString("N"));
        _configurationPath = Path.Combine(_testDirectory, "settings.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task LoadAsync_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var store = new JsonConfigurationStore(_configurationPath);

        var configuration = await store.LoadAsync();

        Assert.IsNull(configuration.Recorder);
        Assert.IsEmpty(configuration.Games);
        Assert.IsTrue(configuration.Settings.LaunchRecorderWithGames);
        Assert.IsTrue(configuration.Settings.CloseRecorderWhenGamesExit);
    }

    [TestMethod]
    public async Task SaveAndLoadAsync_RoundTripsConfiguration()
    {
        var gameId = Guid.NewGuid();
        var configuration = new ApolloConfiguration
        {
            Recorder = new RecorderConfiguration
            {
                DisplayName = "Medal",
                ExecutablePath = @"C:\Apps\Medal.exe",
                ProcessName = "Medal.exe",
            },
            Games =
            [
                new GameConfiguration
                {
                    Id = gameId,
                    DisplayName = "Test Game",
                    ExecutablePath = @"C:\Games\TestGame.exe",
                    ProcessName = "TestGame.exe",
                    IsEnabled = false,
                },
            ],
            Settings = new BehaviorSettings
            {
                LaunchRecorderWithGames = false,
                CloseRecorderWhenGamesExit = true,
            },
        };

        var store = new JsonConfigurationStore(_configurationPath);
        await store.SaveAsync(configuration);
        var loaded = await store.LoadAsync();

        Assert.IsNotNull(loaded.Recorder);
        Assert.AreEqual("Medal", loaded.Recorder.DisplayName);
        Assert.AreEqual("Medal", loaded.Recorder.ProcessName);
        Assert.HasCount(1, loaded.Games);
        Assert.AreEqual(gameId, loaded.Games[0].Id);
        Assert.AreEqual("TestGame", loaded.Games[0].ProcessName);
        Assert.IsFalse(loaded.Games[0].IsEnabled);
        Assert.IsFalse(loaded.Settings.LaunchRecorderWithGames);
    }

    [TestMethod]
    public async Task LoadAsync_WhenJsonIsMalformed_ReturnsDefaults()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(_configurationPath, "{ this is not json");
        var store = new JsonConfigurationStore(_configurationPath);

        var configuration = await store.LoadAsync();

        Assert.IsNull(configuration.Recorder);
        Assert.IsEmpty(configuration.Games);
    }

    [TestMethod]
    public async Task LoadAsync_NormalizesPartialAndDuplicateEntries()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """
            {
              "recorder": { "executablePath": "C:\\Apps\\Medal.exe", "processName": "Medal.exe" },
              "games": [
                { "id": "00000000-0000-0000-0000-000000000000", "executablePath": "C:\\Games\\First.exe", "processName": "First.exe", "isEnabled": true },
                { "displayName": "Duplicate", "processName": "first", "isEnabled": true },
                { "displayName": "Invalid", "processName": "" }
              ]
            }
            """);

        var store = new JsonConfigurationStore(_configurationPath);
        var configuration = await store.LoadAsync();

        Assert.IsNotNull(configuration.Recorder);
        Assert.AreEqual("Medal", configuration.Recorder.ProcessName);
        Assert.HasCount(1, configuration.Games);
        Assert.AreNotEqual(Guid.Empty, configuration.Games[0].Id);
        Assert.AreEqual("First", configuration.Games[0].DisplayName);
        Assert.AreEqual(ApolloConfiguration.CurrentSchemaVersion, configuration.SchemaVersion);
    }

    [TestMethod]
    public async Task SaveAsync_WhenConfigurationExists_ReplacesItAndRemovesTemporaryFile()
    {
        var store = new JsonConfigurationStore(_configurationPath);
        await store.SaveAsync(new ApolloConfiguration
        {
            Games =
            [
                new GameConfiguration { DisplayName = "Old", ProcessName = "Old.exe" },
            ],
        });

        await store.SaveAsync(new ApolloConfiguration
        {
            Games =
            [
                new GameConfiguration { DisplayName = "New", ProcessName = "New.exe" },
            ],
        });

        var loaded = await store.LoadAsync();
        Assert.HasCount(1, loaded.Games);
        Assert.AreEqual("New", loaded.Games[0].DisplayName);
        Assert.IsEmpty(Directory.EnumerateFiles(_testDirectory, "*.tmp"));
    }

    [TestMethod]
    public async Task LoadAsync_AllowsCommentsAndTrailingCommas()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(
            _configurationPath,
            """
            {
              // Older hand-edited configurations should remain readable.
              "settings": {
                "launchRecorderWithGames": false,
              },
            }
            """);

        var store = new JsonConfigurationStore(_configurationPath);
        var configuration = await store.LoadAsync();

        Assert.IsFalse(configuration.Settings.LaunchRecorderWithGames);
        Assert.IsTrue(configuration.Settings.CloseRecorderWhenGamesExit);
    }
}
