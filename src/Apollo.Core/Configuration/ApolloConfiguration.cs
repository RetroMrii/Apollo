namespace Apollo.Core.Configuration;

public sealed class ApolloConfiguration
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    public RecorderConfiguration? Recorder { get; set; }

    public List<GameConfiguration> Games { get; set; } = [];

    public BehaviorSettings Settings { get; set; } = new();
}
