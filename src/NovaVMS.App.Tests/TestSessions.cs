using NovaVMS.App.Services;

namespace NovaVMS.App.Tests;

internal static class TestSessions
{
    public static UserSessionService Create(ConfigurationService configuration)
    {
        return new UserSessionService(configuration, new LocalSettingsService(new MemoryLocalSettingsStore()));
    }

    /// <summary>
    /// Logs in the default administrator that loading a configuration without users creates.
    /// </summary>
    public static void LogInAsDefaultAdmin(UserSessionService session)
    {
        if (!session.TryLogin(ConfigurationService.DefaultUserName, ConfigurationService.DefaultUserName))
        {
            throw new InvalidOperationException("The default administrator could not log in.");
        }
    }
}
