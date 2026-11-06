using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class TestEnvironmentTests
{
    [Fact]
    public void ApplicationTestsUseAnIsolatedDataDirectory()
    {
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "NovaVMS-app-tests") + Path.DirectorySeparatorChar, AppDataPaths.DataDirectory);
    }
}
