using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Import;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class IvmsBackupTests
{
    private const string Segment = "0123456789ABCDEF0123456789ABCDEF";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MergesDuplicateRecordersAndRemapsAllGroupReferences(bool validCopyLast)
    {
        var extra = """
            INSERT INTO HikDevice SELECT 'old', 'Old name', LoginMode, LoginAddress, LoginPort,
                'invalid!', 'invalid!', SN, AChanStartNo, AChanCount, DChanStartNo FROM HikDevice;
            INSERT INTO HikDevice SELECT 'empty', 'Empty name', LoginMode, LoginAddress, LoginPort,
                '', '', SN, AChanStartNo, AChanCount, DChanStartNo FROM HikDevice WHERE GUID = 'device';
            INSERT INTO Channel VALUES ('old-analog', 'old', 'Old channel', 0, 1),
                ('extra', 'old', 'Extra channel', 1, 34), ('empty-digital', 'empty', 'Empty channel', 1, 33);
            """;

        if (validCopyLast)
        {
            extra += "UPDATE HikDevice SET rowid = 100 WHERE GUID = 'device';";
        }

        var database = CreateDevices(EncodeCredential("operator"), EncodeCredential("secret"), extra);
        var groups = CreateDatabase("""
            CREATE TABLE Grouping (ID INTEGER, Name TEXT, Sequence INTEGER);
            CREATE TABLE Camera (ID INTEGER, GroupID INTEGER, ChannelGUID TEXT, Sequence INTEGER);
            INSERT INTO Grouping VALUES (1, 'Mixed copies', 0);
            INSERT INTO Camera VALUES (1, 1, 'old-analog', 0), (2, 1, 'analog', 1),
                (3, 1, 'empty-digital', 2), (4, 1, 'extra', 3);
            """);

        using var source = CreateBackup(database, groups);

        var result = await new IvmsDeviceReader().ReadAsync(source);
        var device = Assert.Single(result.Devices);

        Assert.Equal("Łódź", device.Name);
        Assert.Equal("operator", device.Username);
        Assert.Equal("secret", device.Password);
        Assert.Equal(new[] { "Wejście", "Podwórze", "Extra channel" }, device.Channels.Select(c => c.Name));
        Assert.Equal(device.Channels.Select(c => CameraReference.For(device, c)), Assert.Single(result.Groups).Cameras);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task KeepsRecordersOnDifferentSdkPortsSeparate()
    {
        var database = CreateDevices(EncodeCredential("operator"), EncodeCredential("secret"), """
            INSERT INTO HikDevice SELECT 'other', Name, LoginMode, LoginAddress, 8002,
                UserName, UserPwd, SN, AChanStartNo, AChanCount, DChanStartNo FROM HikDevice;
            """);

        using var source = CreateBackup(database);

        var result = await new IvmsDeviceReader().ReadAsync(source);

        Assert.Equal(2, result.Devices.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportsMacSqliteWithCredentialsAndGroups(bool standalone)
    {
        var database = CreateDevices(EncodeCredential("operator"), EncodeCredential("Zażółć!123"));

        using var source = standalone ? new MemoryStream(database) : CreateBackup(database, CreateGroups());
        using var setup = standalone ? new MemoryStream(CreateSetup()) : null;

        var result = await new IvmsDeviceReader().ReadAsync(source, setup);
        var device = Assert.Single(result.Devices);

        Assert.Equal("Łódź", device.Name);
        Assert.Equal("192.0.2.10", device.Host);
        Assert.Equal(8001, device.SdkPort);
        Assert.Equal(DeviceProtocol.HikvisionSdk, device.Protocol);
        Assert.Equal("operator", device.Username);
        Assert.Equal("Zażółć!123", device.Password);
        Assert.Equal(new[] { 1, 2 }, device.Channels.Select(c => c.Number));
        Assert.Equal(new[] { "Wejście", "Podwórze" }, device.Channels.Select(c => c.Name));
        Assert.Empty(result.Warnings);

        if (standalone)
        {
            Assert.Empty(result.Groups);
        }
        else
        {
            var group = Assert.Single(result.Groups);

            Assert.Equal("Żółta grupa", group.Name);
            Assert.Equal(new[] { device.Channels[1].Id, device.Channels[0].Id }, group.Cameras.Select(c => c.ChannelId));
            Assert.All(group.Cameras, c => Assert.Equal(device.Id, c.DeviceId));
        }
    }

    [Fact]
    public async Task MacCredentialsFromAnotherSetupKeepDeviceAndWarnWithoutGuessing()
    {
        using var source = CreateBackup(CreateDevices(EncodeCredential("operator"), "invalid!"), CreateGroups());

        var result = await new IvmsDeviceReader().ReadAsync(source);
        var device = Assert.Single(result.Devices);

        Assert.Empty(device.Username);
        Assert.Empty(device.Password);
        Assert.Equal(2, device.Channels.Count);
        Assert.Single(result.Groups);
        Assert.Contains(device.Name, Assert.Single(result.Warnings));
    }

    [Fact]
    public async Task EncryptedDatabaseStillRequiresValidAuthentication()
    {
        using var source = CreateBackup(new byte[1024]);

        await Assert.ThrowsAsync<FormatException>(() => new IvmsDeviceReader().ReadAsync(source));
    }

    [Fact]
    public async Task DamagedSqliteIsRejected()
    {
        using var source = CreateBackup("SQLite format 3\0damaged"u8.ToArray());

        await Assert.ThrowsAsync<FormatException>(() => new IvmsDeviceReader().ReadAsync(source));
    }

    [Fact]
    public async Task CancelledImportDoesNotReturnDevices()
    {
        using var source = CreateBackup(CreateDevices("", ""));
        using var cancellation = new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new IvmsDeviceReader().ReadAsync(source, cancellationToken: cancellation.Token));
    }

    private static byte[] CreateDevices(string username, string password, string extraSql = "")
    {
        var sql = """
            CREATE TABLE HikDevice (GUID TEXT, Name TEXT, LoginMode INTEGER, LoginAddress TEXT,
                LoginPort INTEGER, UserName TEXT, UserPwd TEXT, SN TEXT,
                AChanStartNo INTEGER, AChanCount INTEGER, DChanStartNo INTEGER);
            CREATE TABLE Channel (GUID TEXT, DeviceGUID TEXT, Name TEXT, ChannelType INTEGER, ChannelNo INTEGER);
            INSERT INTO HikDevice VALUES ('device', 'Łódź', 0, '192.0.2.10', 8001, $user, $password, 'TEST', 1, 1, 33);
            INSERT INTO Channel VALUES ('analog', 'device', 'Wejście', 0, 1),
                ('digital', 'device', 'Podwórze', 1, 33), ('resource', 'device', 'Resource', 2, 1);
            """;

        return CreateDatabase(sql + extraSql, username, password);
    }

    private static byte[] CreateGroups()
    {
        return CreateDatabase("""
            CREATE TABLE Grouping (ID INTEGER, Name TEXT, Sequence INTEGER);
            CREATE TABLE Camera (ID INTEGER, GroupID INTEGER, ChannelGUID TEXT, Sequence INTEGER);
            INSERT INTO Grouping VALUES (1, 'Żółta grupa', 0);
            INSERT INTO Camera VALUES (1, 1, 'analog', 2), (2, 1, 'digital', 1);
            """);
    }

    private static byte[] CreateDatabase(string sql, string username = "", string password = "")
    {
        var path = Path.Combine(Path.GetTempPath(), $"novavms-test-{Guid.NewGuid():N}.db");

        try
        {
            using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                db.Open();

                using var command = db.CreateCommand();

                command.CommandText = sql;
                command.Parameters.AddWithValue("$user", username);
                command.Parameters.AddWithValue("$password", password);
                command.ExecuteNonQuery();
            }

            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MemoryStream CreateBackup(byte[] database, byte[]? groups = null)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write("Device", database);
            Write("DESetup.xml", CreateSetup());

            if (groups is not null)
            {
                Write("Entity", groups);
            }

            void Write(string name, byte[] data)
            {
                using var entry = archive.CreateEntry($"MacExport.zip.123/{name}").Open();

                entry.Write(data);
            }
        }

        stream.Position = 0;

        return stream;
    }

    private static byte[] CreateSetup()
    {
        var xml = "<DESetup><Segments>" + string.Concat(Enumerable.Range(1, 4)
            .Select(i => $"<Segment{i}>{Segment}</Segment{i}>")) + "</Segments></DESetup>";

        return Encoding.ASCII.GetBytes(Encode(xml, "RlD$Ly9pU2eTS!XR"u8.ToArray(), "Bh#GztSoMEFc%a9w"u8.ToArray()));
    }

    private static string EncodeCredential(string value)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(Segment + Segment));

        for (var i = 1; i < 100; i++)
        {
            hash = SHA256.HashData(Encoding.ASCII.GetBytes(Convert.ToHexString(hash)));
        }

        return Encode(value, Encoding.ASCII.GetBytes(Convert.ToHexString(hash[..8])),
            Encoding.ASCII.GetBytes(Convert.ToHexString(hash[24..])));
    }

    private static string Encode(string value, byte[] key, byte[] iv)
    {
        var input = Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));

        // Pad only for the provider's block requirement, then retain the original CFB length.
        var padded = new byte[(input.Length + 15) / 16 * 16];

        input.CopyTo(padded, 0);

        using var aes = Aes.Create();

        aes.Key = key;

        return Convert.ToBase64String(aes.EncryptCfb(padded, iv, PaddingMode.None, 128)[..input.Length]);
    }
}
