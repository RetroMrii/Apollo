using System.Text.Json;
using System.Text.Json.Serialization;

namespace Apollo.Core.Configuration;

public sealed class JsonConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _configurationPath;

    public JsonConfigurationStore(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        _configurationPath = Path.GetFullPath(configurationPath);
    }

    public async Task<ApolloConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_configurationPath))
            {
                return new ApolloConfiguration();
            }

            try
            {
                await using var stream = new FileStream(
                    _configurationPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    useAsync: true);

                var configuration = await JsonSerializer.DeserializeAsync<ApolloConfiguration>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);

                return ConfigurationNormalizer.Normalize(configuration);
            }
            catch (Exception exception) when (exception is JsonException
                                              or NotSupportedException
                                              or IOException
                                              or UnauthorizedAccessException)
            {
                return new ApolloConfiguration();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(ApolloConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        string? temporaryPath = null;
        try
        {
            var normalized = ConfigurationNormalizer.Normalize(configuration);
            var directory = Path.GetDirectoryName(_configurationPath)
                ?? throw new InvalidOperationException("The configuration path must include a directory.");

            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(_configurationPath)}.{Guid.NewGuid():N}.tmp");

            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_configurationPath))
            {
                File.Replace(
                    temporaryPath,
                    _configurationPath,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, _configurationPath);
            }

            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            _gate.Release();
        }
    }
}
