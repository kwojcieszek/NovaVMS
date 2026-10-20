using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using NovaVMS.Infrastructure.Import;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class IvmsNameEncodingTests
{
    [Theory]
    [InlineData("5769656C6F67F37261", "Wielogóra")]
    [InlineData("426961B36F776965BF61", "Białowieża")]
    [InlineData("476461F1736B", "Gdańsk")]
    [InlineData("4B616E61B3207A65726F31", "Kanał zero1")]
    public void ReadsLegacyBytesFromBackupWithoutReplacingPolishLetters(string hex, string expected)
    {
        // SQLite's TEXT conversion is lossy for these iVMS values. CAST AS BLOB preserves them.
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE HikDevice(Name TEXT); INSERT INTO HikDevice VALUES(CAST($name AS TEXT)); SELECT CAST(Name AS BLOB) FROM HikDevice;";
        command.Parameters.AddWithValue("$name", Convert.FromHexString(hex));
        var bytes = (byte[])command.ExecuteScalar()!;
        Assert.Equal(expected, IvmsDeviceReader.DecodeName(bytes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Camera 01")]
    [InlineData("Zażółć gęślą jaźń ĄĆĘŁŃÓŚŹŻ")]
    [InlineData("攝影機 — Київ — Łódź")]
    public void PreservesExistingUtf8Names(string name)
    {
        Assert.Equal(name, IvmsDeviceReader.DecodeName(Encoding.UTF8.GetBytes(name)));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl-PL")]
    public void LegacyDecodingDoesNotDependOnSystemCulture(string culture)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(1250)!;
            const string name = "Zażółć gęślą jaźń ĄĆĘŁŃÓŚŹŻ";
            Assert.Equal(name, IvmsDeviceReader.DecodeName(encoding.GetBytes(name)));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
