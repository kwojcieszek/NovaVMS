using System.Data.Common;
using System.Globalization;
using System.IO.Compression;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Exceptions;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Sync;

/// <summary>
/// Settings versions in a PostgreSQL or MySQL database. Tables are created on first use; every version
/// is compressed and encrypted with the shared password before it leaves this computer.
/// </summary>
internal sealed class DatabaseRemoteSettingsStore : IRemoteSettingsStore
{
    public const int ConnectTimeoutSeconds = 10;
    public const int CommandTimeoutSeconds = 60;

    private const string SchemaVersion = "1";
    private const int KdfIterations = 600_000;
    private const int SaltLength = 32;
    private const string SchemaVersionKey = "schema_version";
    private const string SaltKey = "kdf_salt";
    private const string IterationsKey = "kdf_iterations";
    private const string KeyCheckKey = "key_check";
    private static ReadOnlySpan<byte> KeyCheckPlaintext => "NovaVMS.Settings.KeyCheck.v1"u8;

    private readonly ISqlDialect _dialect;
    private readonly SettingsSyncOptions _options;
    private readonly IConfigurationSerializer _serializer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SettingsPayloadCipher? _cipher;
    private bool _isDisposed;

    public DatabaseRemoteSettingsStore(ISqlDialect dialect, SettingsSyncOptions options, IConfigurationSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(serializer);

        _dialect = dialect;
        _options = options.Copy();
        _serializer = serializer;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(async connection =>
        {
            await EnsureCipherAsync(connection, cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }

    public Task<SettingsVersionInfo?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync(async connection =>
        {
            await EnsureCipherAsync(connection, cancellationToken).ConfigureAwait(false);
            return await ReadLatestAsync(connection, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<SettingsVersionInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync<IReadOnlyList<SettingsVersionInfo>>(async connection =>
        {
            await EnsureCipherAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection,
                "SELECT " + SettingsTables.VersionColumns + " FROM " + SettingsTables.Versions + " ORDER BY id DESC");
            return await ReadVersionsAsync(command, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<SettingsUploadResult> UploadAsync(AppConfiguration configuration, Guid instanceId, int retainedVersions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedVersions, 1);

        return RunAsync(async connection =>
        {
            var cipher = await EnsureCipherAsync(connection, cancellationToken).ConfigureAwait(false);
            using var json = new MemoryStream();
            try
            {
                await _serializer.SerializeAsync(json, configuration, cancellationToken).ConfigureAwait(false);
                var plaintext = json.GetBuffer().AsMemory(0, checked((int)json.Length));
                var hash = cipher.ComputeHash(plaintext.Span);

                var latest = await ReadLatestAsync(connection, cancellationToken).ConfigureAwait(false);
                if (latest is not null && string.Equals(latest.ContentHash, hash, StringComparison.Ordinal))
                {
                    return new SettingsUploadResult(latest, false);
                }

                var payload = cipher.Encrypt(Compress(plaintext.Span));
                var version = new SettingsVersionInfo(
                    0,
                    DateTimeOffset.UtcNow,
                    instanceId,
                    Limit(Environment.MachineName, 255),
                    Limit(await LocalAddressResolver.ResolveAsync(_options.Host, _options.Port, cancellationToken).ConfigureAwait(false), 64),
                    Limit(Environment.UserName, 255),
                    Limit(GetApplicationVersion(), 64),
                    hash);

                await using var insert = CreateCommand(connection, _dialect.InsertVersion);
                AddParameter(insert, "@createdAt", version.CreatedAt.UtcDateTime);
                AddParameter(insert, "@instanceId", version.InstanceId.ToString("D"));
                AddParameter(insert, "@machineName", version.MachineName);
                AddParameter(insert, "@ipAddress", version.IpAddress);
                AddParameter(insert, "@userName", version.UserName);
                AddParameter(insert, "@appVersion", version.AppVersion);
                AddParameter(insert, "@contentHash", version.ContentHash);
                AddParameter(insert, "@payload", payload);
                var id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);

                await DeleteOldVersionsAsync(connection, retainedVersions, cancellationToken).ConfigureAwait(false);
                return new SettingsUploadResult(version with { Id = id }, true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(json.GetBuffer());
            }
        }, cancellationToken);
    }

    public Task<AppConfiguration> DownloadAsync(long versionId, CancellationToken cancellationToken = default)
    {
        return RunAsync(async connection =>
        {
            var cipher = await EnsureCipherAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var command = CreateCommand(connection, "SELECT payload FROM " + SettingsTables.Versions + " WHERE id = @id");
            AddParameter(command, "@id", versionId);
            if (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not byte[] payload)
            {
                throw new SettingsSyncException(SettingsSyncFailure.VersionNotFound, $"Settings version {versionId} does not exist.");
            }

            var compressed = cipher.Decrypt(payload)
                ?? throw new SettingsSyncException(SettingsSyncFailure.CorruptedData, $"Settings version {versionId} cannot be decrypted.");
            byte[]? plaintext = null;
            try
            {
                plaintext = Decompress(compressed);
                using var stream = new MemoryStream(plaintext, writable: false);
                return await _serializer.DeserializeAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
            {
                throw new SettingsSyncException(SettingsSyncFailure.CorruptedData, $"Settings version {versionId} is not a valid configuration.", exception);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(compressed);
                if (plaintext is not null)
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
        }, cancellationToken);
    }

    public void Dispose()
    {
        _isDisposed = true;
        _cipher?.Dispose();
        _cipher = null;
    }

    /// <summary>
    /// Opens a connection for one operation and turns provider errors into <see cref="SettingsSyncException"/>.
    /// </summary>
    private async Task<T> RunAsync<T>(Func<DbConnection, Task<T>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var connection = _dialect.CreateConnection(_options);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return await operation(connection).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is DbException or SocketException or TimeoutException or IOException or ArgumentException
            && !cancellationToken.IsCancellationRequested)
        {
            throw new SettingsSyncException(SettingsSyncFailure.ConnectionFailed, exception.Message, exception);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Creates missing tables, reads the key derivation parameters and verifies the password against the stored check value.
    /// </summary>
    private async Task<SettingsPayloadCipher> EnsureCipherAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (_cipher is not null)
        {
            return _cipher;
        }

        await ExecuteAsync(connection, _dialect.CreateMetaTable, cancellationToken).ConfigureAwait(false);
        await ExecuteAsync(connection, _dialect.CreateVersionsTable, cancellationToken).ConfigureAwait(false);

        // Concurrent first connections: only the first value of each key is kept and everyone reads it back.
        await InsertMetaIfMissingAsync(connection, SchemaVersionKey, SchemaVersion, cancellationToken).ConfigureAwait(false);
        await InsertMetaIfMissingAsync(connection, SaltKey, Convert.ToBase64String(RandomNumberGenerator.GetBytes(SaltLength)), cancellationToken).ConfigureAwait(false);
        await InsertMetaIfMissingAsync(connection, IterationsKey, KdfIterations.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);

        var meta = await ReadMetaAsync(connection, cancellationToken).ConfigureAwait(false);
        if (!meta.TryGetValue(SchemaVersionKey, out var schemaVersion) || schemaVersion != SchemaVersion)
        {
            throw new SettingsSyncException(SettingsSyncFailure.UnsupportedSchema, $"Unsupported settings database schema version '{schemaVersion}'.");
        }

        byte[] salt;
        int iterations;
        try
        {
            salt = Convert.FromBase64String(meta[SaltKey]);
            iterations = int.Parse(meta[IterationsKey], NumberStyles.None, CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or KeyNotFoundException)
        {
            throw new SettingsSyncException(SettingsSyncFailure.CorruptedData, "The settings database contains invalid key derivation parameters.", exception);
        }

        if (iterations is < SettingsPayloadCipher.MinimumIterations or > SettingsPayloadCipher.MaximumIterations)
        {
            throw new SettingsSyncException(SettingsSyncFailure.CorruptedData, "The settings database contains an invalid key derivation iteration count.");
        }

        // Key derivation is deliberately slow; keep it off the UI thread.
        var cipher = await Task.Run(() => new SettingsPayloadCipher(_options.EncryptionPassword, salt, iterations), cancellationToken).ConfigureAwait(false);
        try
        {
            await InsertMetaIfMissingAsync(connection, KeyCheckKey, Convert.ToBase64String(cipher.Encrypt(KeyCheckPlaintext)), cancellationToken).ConfigureAwait(false);
            meta = await ReadMetaAsync(connection, cancellationToken).ConfigureAwait(false);
            byte[]? check = null;
            try
            {
                check = cipher.Decrypt(Convert.FromBase64String(meta[KeyCheckKey]));
            }
            catch (FormatException)
            {
                check = null;
            }

            if (check is null || !check.AsSpan().SequenceEqual(KeyCheckPlaintext))
            {
                throw new SettingsSyncException(SettingsSyncFailure.WrongEncryptionPassword, "The encryption password does not match the settings database.");
            }

            _cipher = cipher;
            return cipher;
        }
        catch
        {
            cipher.Dispose();
            throw;
        }
    }

    private async Task<SettingsVersionInfo?> ReadLatestAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection,
            "SELECT " + SettingsTables.VersionColumns + " FROM " + SettingsTables.Versions + " ORDER BY id DESC LIMIT 1");
        var versions = await ReadVersionsAsync(command, cancellationToken).ConfigureAwait(false);
        return versions.Count > 0 ? versions[0] : null;
    }

    private static async Task DeleteOldVersionsAsync(DbConnection connection, int retainedVersions, CancellationToken cancellationToken)
    {
        // The derived table lets MySQL read the table it deletes from; the offset is an integer, not user text.
        var sql = "DELETE FROM " + SettingsTables.Versions + " WHERE id < ("
            + "SELECT boundary.id FROM (SELECT id FROM " + SettingsTables.Versions
            + " ORDER BY id DESC LIMIT 1 OFFSET " + (retainedVersions - 1).ToString(CultureInfo.InvariantCulture) + ") AS boundary)";
        await ExecuteAsync(connection, sql, cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertMetaIfMissingAsync(DbConnection connection, string key, string value, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, _dialect.InsertMetaIfMissing);
        AddParameter(command, "@key", key);
        AddParameter(command, "@value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, string>> ReadMetaAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, "SELECT meta_key, meta_value FROM " + SettingsTables.Meta);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var meta = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            meta[reader.GetString(0)] = reader.GetString(1);
        }

        return meta;
    }

    private static async Task<List<SettingsVersionInfo>> ReadVersionsAsync(DbCommand command, CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var versions = new List<SettingsVersionInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // MySQL DATETIME has no time zone; values are always written in UTC.
            var createdAt = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
            versions.Add(new SettingsVersionInfo(
                reader.GetInt64(0),
                new DateTimeOffset(createdAt),
                Guid.TryParse(reader.GetString(2), out var instanceId) ? instanceId : Guid.Empty,
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return versions;
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static DbCommand CreateCommand(DbConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        return command;
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static byte[] Compress(ReadOnlySpan<byte> data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data, writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static string GetApplicationVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(DatabaseRemoteSettingsStore).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(informational))
        {
            // Drop the source revision suffix added by the SDK ("0.1.0+abcdef").
            var plus = informational.IndexOf('+');
            return plus < 0 ? informational : informational[..plus];
        }

        return assembly.GetName().Version?.ToString() ?? string.Empty;
    }

    private static string Limit(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
