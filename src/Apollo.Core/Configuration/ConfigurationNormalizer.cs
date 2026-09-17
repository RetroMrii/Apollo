namespace Apollo.Core.Configuration;

public static class ConfigurationNormalizer
{
    public static ApolloConfiguration Normalize(ApolloConfiguration? configuration)
    {
        configuration ??= new ApolloConfiguration();
        configuration.SchemaVersion = ApolloConfiguration.CurrentSchemaVersion;
        configuration.Settings ??= new BehaviorSettings();
        configuration.Games ??= [];

        if (configuration.Recorder is not null)
        {
            NormalizeRecorder(configuration.Recorder);
            if (string.IsNullOrWhiteSpace(configuration.Recorder.ProcessName))
            {
                configuration.Recorder = null;
            }
        }

        var normalizedGames = new List<GameConfiguration>(configuration.Games.Count);
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var game in configuration.Games)
        {
            if (game is null)
            {
                continue;
            }

            NormalizeGame(game);
            if (string.IsNullOrWhiteSpace(game.ProcessName) || !processNames.Add(game.ProcessName))
            {
                continue;
            }

            normalizedGames.Add(game);
        }

        configuration.Games = normalizedGames;
        return configuration;
    }

    public static string NormalizeProcessName(string? processName, string? executablePath = null)
    {
        var candidate = string.IsNullOrWhiteSpace(processName) ? executablePath : processName;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var trimmed = candidate.Trim().Trim('"');
        return Path.GetFileNameWithoutExtension(trimmed).Trim();
    }

    private static void NormalizeRecorder(RecorderConfiguration recorder)
    {
        recorder.ExecutablePath = NormalizePath(recorder.ExecutablePath);
        recorder.ProcessName = NormalizeProcessName(recorder.ProcessName, recorder.ExecutablePath);
        recorder.DisplayName = NormalizeDisplayName(recorder.DisplayName, recorder.ProcessName, "Medal");
    }

    private static void NormalizeGame(GameConfiguration game)
    {
        game.Id = game.Id == Guid.Empty ? Guid.NewGuid() : game.Id;
        game.ExecutablePath = NormalizePath(game.ExecutablePath);
        game.ProcessName = NormalizeProcessName(game.ProcessName, game.ExecutablePath);
        game.DisplayName = NormalizeDisplayName(game.DisplayName, game.ProcessName, "Game");
    }

    private static string NormalizePath(string? path) => path?.Trim().Trim('"') ?? string.Empty;

    private static string NormalizeDisplayName(string? displayName, string processName, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName.Trim();
        }

        return string.IsNullOrWhiteSpace(processName) ? fallback : processName;
    }
}
