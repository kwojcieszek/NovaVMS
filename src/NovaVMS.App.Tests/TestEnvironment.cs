using System.Runtime.CompilerServices;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Fault-injection tests must never write to the user's real configuration or error log.
        var directory = Path.Combine(Path.GetTempPath(), "NovaVMS-app-tests", $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        Environment.SetEnvironmentVariable("NOVAVMS_DATA_DIR", directory);
    }
}

public sealed class TestEnvironmentTests
{
    [Fact]
    public void ApplicationTestsUseAnIsolatedDataDirectory()
    {
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "NovaVMS-app-tests") + Path.DirectorySeparatorChar, AppDataPaths.DataDirectory);
    }
}
