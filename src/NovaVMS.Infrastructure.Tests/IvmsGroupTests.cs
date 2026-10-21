using Microsoft.Data.Sqlite;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Import;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class IvmsGroupTests
{
    [Fact]
    public void GroupsKeepOrderPolishNamesEmptyGroupsAndChannelReferences()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE Grouping (ID INTEGER, Name TEXT, Sequence INTEGER);
            CREATE TABLE Camera (ID INTEGER, GroupID INTEGER, ChannelGUID TEXT, Sequence INTEGER);
            INSERT INTO Grouping VALUES (1, 'Empty', 2), (2, CAST(X'AFf3b3e6' AS TEXT), 1);
            INSERT INTO Camera VALUES (1, 2, 'channel-b', 2), (2, 2, 'channel-a', 1),
                (3, 2, 'channel-a', 3), (4, 2, 'unsupported', 4), (5, 99, 'channel-a', 0);
            """;
        command.ExecuteNonQuery();
        var deviceId = Guid.NewGuid();
        var a = new CameraReference { DeviceId = deviceId, ChannelNumber = 1 };
        var b = new CameraReference { DeviceId = deviceId, ChannelNumber = 5 };
        var warnings = new List<string>();
        var groups = IvmsDeviceReader.ReadGroupRows(db,
            new Dictionary<string, CameraReference> { ["channel-a"] = a, ["channel-b"] = b }, warnings, CancellationToken.None);
        Assert.Equal(2, groups.Count);
        Assert.Equal("Żółć", groups[0].Name);
        Assert.Equal(new[] { a, b }, groups[0].Cameras);
        Assert.Empty(groups[1].Cameras);
        Assert.All(groups, group => Assert.Null(group.SourceDeviceId));
        Assert.Single(warnings);
    }

    [Fact]
    public void CancelledGroupImportDoesNotContinueReading()
    {
        using var db = new SqliteConnection("Data Source=:memory:");
        db.Open();
        using var command = db.CreateCommand();
        command.CommandText = "CREATE TABLE Grouping (ID INTEGER, Name TEXT, Sequence INTEGER); INSERT INTO Grouping VALUES (1, 'group', 0);";
        command.ExecuteNonQuery();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => IvmsDeviceReader.ReadGroupRows(db,
            new Dictionary<string, CameraReference>(), [], cancellation.Token));
    }
}
