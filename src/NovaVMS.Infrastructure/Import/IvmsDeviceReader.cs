using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Resources;

namespace NovaVMS.Infrastructure.Import;

/// <summary>
/// Imports iVMS-4200 SQLCipher 3 backups using their accompanying DESetup.xml.
/// No vendor DLLs or application login password are required.
/// </summary>
public sealed class IvmsDeviceReader
{
    private const int MaximumDatabaseSize = 64 * 1024 * 1024;
    private const int PageSize = 1024;
    private const int ReserveSize = 48;
    private static readonly Encoding LegacyNameEncoding = CodePagesEncodingProvider.Instance.GetEncoding(1250)!;

    public async Task<DeviceCsvResult> ReadAsync(Stream source, Stream? encryptionSetup = null,
        CancellationToken cancellationToken = default)
    {
        byte[] database;
        byte[] setup;
        byte[]? entities = null;
        if (encryptionSetup is not null)
        {
            database = await ReadLimitedAsync(source, MaximumDatabaseSize, cancellationToken);
            setup = await ReadLimitedAsync(encryptionSetup, 64 * 1024, cancellationToken);
        }
        else
        {
            using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);
            database = await ReadEntryAsync(archive, "Device", MaximumDatabaseSize, cancellationToken);
            setup = await ReadEntryAsync(archive, "DESetup.xml", 64 * 1024, cancellationToken);
            if (archive.Entries.Any(entry => string.Equals(entry.Name, "Entity", StringComparison.OrdinalIgnoreCase)))
                entities = await ReadEntryAsync(archive, "Entity", MaximumDatabaseSize, cancellationToken);
        }

        // Cryptography and database parsing run off the UI thread.
        return await Task.Run(() => Parse(database, setup, entities, cancellationToken), cancellationToken);
    }

    private static DeviceCsvResult Parse(byte[] encrypted, byte[] setup, byte[]? entities, CancellationToken cancellationToken)
    {
        var parameters = ReadParameters(setup);
        byte[]? plaintext = null;
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"novavms-import-{Guid.NewGuid():N}.db");
        try
        {
            plaintext = DecryptDatabase(encrypted, parameters.Secret, cancellationToken);
            File.WriteAllBytes(temporaryPath, plaintext);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = temporaryPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            using (var integrity = connection.CreateCommand())
            {
                integrity.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(integrity.ExecuteScalar() as string, "ok", StringComparison.Ordinal))
                    throw new FormatException(ImportText.Get("IvmsDamaged"));
            }

            var devices = new Dictionary<string, (Device Device, int AnalogStart, int AnalogCount, int DigitalStart)>();
            var warnings = new List<string>();
            var channels = new Dictionary<string, CameraReference>(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                // Some iVMS backups store ANSI name bytes in a database labelled UTF-8.
                // Read raw bytes before SQLite's UTF-8 conversion can replace Polish letters.
                command.CommandText = "SELECT GUID, CAST(Name AS BLOB), LoginMode, LoginAddress, LoginPort, UserName, UserPwd, SN, AChanStartNo, AChanCount, DChanStartNo FROM HikDevice";
                using var rows = command.ExecuteReader();
                while (rows.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var name = DecodeName((byte[])rows.GetValue(1));
                    var host = rows.GetString(3).Trim();
                    if (rows.GetInt32(2) != 0 || string.IsNullOrWhiteSpace(host))
                    {
                        warnings.Add(ImportText.Format("IvmsUnsupportedDevice", name));
                        continue;
                    }

                    var port = rows.GetInt32(4);
                    if (port is < 1 or > 65535)
                        throw new FormatException(ImportText.Get("IvmsDamaged"));
                    var device = new Device
                    {
                        Name = name,
                        Host = host,
                        Protocol = DeviceProtocol.HikvisionSdk,
                        SdkPort = port,
                        Username = ReadCredential(rows.GetString(5), parameters.Key, parameters.Iv),
                        Password = ReadCredential(rows.GetString(6), parameters.Key, parameters.Iv),
                        SerialNumber = rows.GetString(7)
                    };
                    devices.Add(rows.GetString(0), (device, rows.GetInt32(8), rows.GetInt32(9), rows.GetInt32(10)));
                }
            }

            using (var command = connection.CreateCommand())
            {
                // Channel type 2 is a device resource, not an additional camera.
                command.CommandText = "SELECT DeviceGUID, CAST(Name AS BLOB), ChannelType, ChannelNo, GUID FROM Channel WHERE ChannelType IN (0, 1) ORDER BY ChannelType, ChannelNo";
                using var rows = command.ExecuteReader();
                while (rows.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!devices.TryGetValue(rows.GetString(0), out var item))
                        continue;
                    var sdkNumber = rows.GetInt32(3);
                    var number = rows.GetInt32(2) == 0
                        ? sdkNumber - item.AnalogStart + 1
                        : sdkNumber - item.DigitalStart + item.AnalogCount + 1;
                    if (number <= 0 || item.Device.Channels.Any(channel => channel.Number == number))
                        throw new FormatException(ImportText.Get("IvmsDamaged"));
                    var channel = new Channel { Number = number, Name = DecodeName((byte[])rows.GetValue(1)) };
                    item.Device.Channels.Add(channel);
                    channels.Add(rows.GetString(4), CameraReference.For(item.Device, channel));
                }
            }

            var groups = entities is null ? new List<CameraGroup>()
                : ReadGroups(entities, parameters.Secret, channels, warnings, cancellationToken);
            return new DeviceCsvResult(devices.Values.Select(item => item.Device).ToList(), warnings) { Groups = groups };
        }
        catch (Exception exception) when (exception is SqliteException or InvalidCastException or ArgumentException)
        {
            throw new FormatException(ImportText.Get("IvmsDamaged"), exception);
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(parameters.Secret);
            CryptographicOperations.ZeroMemory(parameters.Key);
            CryptographicOperations.ZeroMemory(parameters.Iv);
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    internal static string DecodeName(byte[] bytes)
    {
        return System.Text.Unicode.Utf8.IsValid(bytes)
            ? Encoding.UTF8.GetString(bytes) : LegacyNameEncoding.GetString(bytes);
    }

    private static List<CameraGroup> ReadGroups(byte[] encrypted, byte[] secret,
        IReadOnlyDictionary<string, CameraReference> channels, List<string> warnings, CancellationToken token)
    {
        var plaintext = DecryptDatabase(encrypted, secret, token);
        var path = Path.Combine(Path.GetTempPath(), $"novavms-groups-{Guid.NewGuid():N}.db");
        try
        {
            File.WriteAllBytes(path, plaintext);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            return ReadGroupRows(connection, channels, warnings, token);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    internal static List<CameraGroup> ReadGroupRows(SqliteConnection connection,
        IReadOnlyDictionary<string, CameraReference> channels, List<string> warnings, CancellationToken token)
    {
        var groups = new Dictionary<long, CameraGroup>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT ID, CAST(Name AS BLOB) FROM Grouping ORDER BY Sequence, ID";
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                token.ThrowIfCancellationRequested();
                groups.Add(rows.GetInt64(0), new CameraGroup { Name = DecodeName((byte[])rows.GetValue(1)) });
            }
        }
        var skipped = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT GroupID, ChannelGUID FROM Camera ORDER BY GroupID, Sequence, ID";
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                token.ThrowIfCancellationRequested();
                if (!groups.TryGetValue(rows.GetInt64(0), out var group) || rows.IsDBNull(1)
                    || !channels.TryGetValue(rows.GetString(1), out var camera)) { skipped++; continue; }
                if (!group.Cameras.Contains(camera)) group.Cameras.Add(camera);
            }
        }
        if (skipped > 0) warnings.Add(ImportText.Format("IvmsSkippedGroupCameras", skipped));
        return groups.Values.ToList();
    }

    private static (byte[] Secret, byte[] Key, byte[] Iv) ReadParameters(byte[] setup)
    {
        try
        {
            // Fixed transport parameters of iVMS DESetup files, independent of device passwords.
            var encoded = DecryptCfb(Convert.FromBase64String(Encoding.UTF8.GetString(setup).Trim('\uFEFF', ' ', '\r', '\n')),
                Encoding.ASCII.GetBytes("RlD$Ly9pU2eTS!XR"), Encoding.ASCII.GetBytes("Bh#GztSoMEFc%a9w"));
            var xml = XDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(encoded))));
            var segments = Enumerable.Range(1, 4).Select(index =>
                xml.Root?.Element("Segments")?.Element($"Segment{index}")?.Value
                ?? throw new FormatException()).ToArray();
            if (segments.Any(segment => segment.Length != 32 || !segment.All(Uri.IsHexDigit)))
                throw new FormatException();
            var first = HashSegments(segments[0] + segments[1]);
            var second = HashSegments(segments[2] + segments[3]);
            var secret = Encoding.ASCII.GetBytes(Convert.ToBase64String(first[16..].Concat(second[..16]).ToArray()));
            return (secret, Encoding.ASCII.GetBytes(Convert.ToHexString(first[..8])),
                Encoding.ASCII.GetBytes(Convert.ToHexString(second[24..])));
        }
        catch (Exception exception) when (exception is FormatException or System.Xml.XmlException or CryptographicException)
        {
            throw new FormatException(ImportText.Get("IvmsSetupInvalid"), exception);
        }
    }

    private static byte[] HashSegments(string segments)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(segments));
        for (var iteration = 1; iteration < 100; iteration++)
            hash = SHA256.HashData(Encoding.ASCII.GetBytes(Convert.ToHexString(hash)));
        return hash;
    }

    private static byte[] DecryptDatabase(byte[] encrypted, byte[] secret, CancellationToken cancellationToken)
    {
        if (encrypted.Length == 0 || encrypted.Length % PageSize != 0)
            throw new FormatException(ImportText.Get("IvmsDamaged"));
        var key = Rfc2898DeriveBytes.Pbkdf2(secret, encrypted.AsSpan(0, 16), 64000, HashAlgorithmName.SHA1, 32);
        var salt = encrypted.AsSpan(0, 16).ToArray();
        for (var index = 0; index < salt.Length; index++) salt[index] ^= 0x3a;
        var authKey = Rfc2898DeriveBytes.Pbkdf2(key, salt, 2, HashAlgorithmName.SHA1, 32);
        var output = new byte[encrypted.Length];
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            for (var offset = 0; offset < encrypted.Length; offset += PageSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = encrypted.AsSpan(offset, PageSize);
                var start = offset == 0 ? 16 : 0;
                var authenticated = new byte[PageSize - start - ReserveSize + 16 + 4];
                page[start..(PageSize - ReserveSize + 16)].CopyTo(authenticated);
                BinaryPrimitives.WriteInt32LittleEndian(authenticated.AsSpan(authenticated.Length - 4), offset / PageSize + 1);
                var mac = HMACSHA1.HashData(authKey, authenticated);
                if (!CryptographicOperations.FixedTimeEquals(mac, page.Slice(992, 20)))
                    throw new FormatException(ImportText.Get("IvmsAuthenticationFailed"));
                var decrypted = aes.DecryptCbc(page[start..976], page[976..992], PaddingMode.None);
                decrypted.CopyTo(output.AsSpan(offset + start));
                CryptographicOperations.ZeroMemory(decrypted);
            }
            "SQLite format 3\0"u8.CopyTo(output);
            if (!output.AsSpan(16, 8).SequenceEqual(new byte[] { 4, 0, 1, 1, 48, 64, 32, 32 }))
                throw new FormatException(ImportText.Get("IvmsDamaged"));
            return output;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(output);
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(authKey);
        }
    }

    private static string ReadCredential(string value, byte[] key, byte[] iv)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        try
        {
            var encoded = DecryptCfb(Convert.FromBase64String(value), key, iv);
            var plaintext = Convert.FromBase64String(Encoding.UTF8.GetString(encoded));
            try { return new UTF8Encoding(false, true).GetString(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); CryptographicOperations.ZeroMemory(encoded); }
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or DecoderFallbackException)
        {
            throw new FormatException(ImportText.Get("IvmsCredentialsInvalid"), exception);
        }
    }

    private static byte[] DecryptCfb(byte[] data, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        // AES CFB128 accepts partial final blocks in iVMS. Implement the feedback
        // explicitly because platform crypto providers differ in partial-block support.
        var output = new byte[data.Length];
        var feedback = iv.ToArray();
        for (var offset = 0; offset < data.Length; offset += 16)
        {
            var mask = aes.EncryptEcb(feedback, PaddingMode.None);
            var count = Math.Min(16, data.Length - offset);
            for (var index = 0; index < count; index++) output[offset + index] = (byte)(data[offset + index] ^ mask[index]);
            if (count == 16) data.AsSpan(offset, 16).CopyTo(feedback);
        }
        return output;
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchive archive, string name, int limit, CancellationToken cancellationToken)
    {
        var entries = archive.Entries.Where(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (entries.Count != 1) throw new FormatException(ImportText.Format("IvmsMissingEntry", name));
        await using var stream = entries[0].Open();
        return await ReadLimitedAsync(stream, limit, cancellationToken);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + count > limit) throw new FormatException(ImportText.Get("IvmsFileTooLarge"));
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken);
        }
        return buffer.ToArray();
    }
}
