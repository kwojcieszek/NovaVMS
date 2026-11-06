using System.Runtime.CompilerServices;

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
