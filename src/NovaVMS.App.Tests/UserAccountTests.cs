using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Core.Security;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class UserAccountTests
{
    [Fact]
    public void PasswordIsHashedAndVerified()
    {
        var user = new UserAccount();
        PasswordHasher.SetPassword(user, "correct horse");

        Assert.DoesNotContain("correct horse", user.PasswordHash);
        Assert.True(PasswordHasher.Verify(user, "correct horse"));
        Assert.False(PasswordHasher.Verify(user, "Correct horse"));
        Assert.False(PasswordHasher.Verify(new UserAccount(), string.Empty));
    }

    [Fact]
    public async Task EmptyConfigurationGetsSavedDefaultAdministrator()
    {
        var store = new MemoryConfigurationStore(new AppConfiguration());
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        await configuration.FlushAsync();

        var admin = Assert.Single(store.Stored.Users);
        Assert.Equal("admin", admin.UserName);
        Assert.Equal(UserPermissions.All, admin.Permissions);
        Assert.True(PasswordHasher.Verify(admin, "admin"));
    }

    [Fact]
    public async Task SessionFollowsPermissionsAndEndsWhenTheAccountIsRemoved()
    {
        var (configuration, session) = await CreateAsync();
        TestSessions.LogInAsDefaultAdmin(session);
        Assert.True(session.CanEditUsers);
        Assert.False(session.TryLogin("admin", "wrong"));

        var admin = configuration.FindUser("admin")!;
        admin.Permissions = UserPermissions.Views;
        await configuration.SaveAsync();
        Assert.True(session.IsLoggedIn);
        Assert.True(session.CanEditViews);
        Assert.False(session.CanOpenSettings);
        Assert.False(session.CanEditFavorites);

        configuration.Current.Users.Clear();
        await configuration.SaveAsync();
        Assert.False(session.IsLoggedIn);
    }

    [Fact]
    public async Task AutomaticLoginIsStoredOnlyOnThisComputer()
    {
        var configurationStore = new MemoryConfigurationStore(new AppConfiguration());
        var configuration = new ConfigurationService(configurationStore, new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var localStore = new MemoryLocalSettingsStore();
        var session = new UserSessionService(configuration, new LocalSettingsService(localStore));
        TestSessions.LogInAsDefaultAdmin(session);

        await session.SetAutoLoginAsync(true);
        Assert.Equal("admin", localStore.Saved!.AutoLoginUserName);

        var nextStart = new UserSessionService(configuration, new LocalSettingsService(localStore));
        Assert.True(nextStart.TryAutoLogin());
        Assert.Equal("admin", nextStart.UserName);
    }

    [Fact]
    public async Task ExportWithoutPasswordsOmitsHashesAndImportKeepsThem()
    {
        var (configuration, _) = await CreateAsync();
        using var stream = new MemoryStream();
        await configuration.ExportAsync(stream, includePasswords: false);
        stream.Position = 0;
        var exported = await configuration.ReadImportAsync(stream);
        Assert.False(Assert.Single(exported.Users).HasPassword);

        await configuration.ImportAsync(exported);
        Assert.True(PasswordHasher.Verify(configuration.FindUser("admin")!, "admin"));
    }

    [Fact]
    public async Task VersionWithoutUsersFromTheDatabaseKeepsTheCurrentUsers()
    {
        var (configuration, _) = await CreateAsync();
        var id = configuration.FindUser("admin")!.Id;

        await configuration.ReplaceAsync(new AppConfiguration());

        Assert.Equal(id, Assert.Single(configuration.Current.Users).Id);
    }

    [Fact]
    public async Task LastUserManagerCannotLoseThePermissionOrBeDeleted()
    {
        var (configuration, session) = await CreateAsync();
        TestSessions.LogInAsDefaultAdmin(session);
        using var page = new UsersSettingsViewModel(configuration, session, new LocalSettingsService(new MemoryLocalSettingsStore()), null!);

        page.SelectedUser = page.Users.Single();
        page.Permissions.Single(option => option.Permission == UserPermissions.Users).IsChecked = false;
        await page.SaveUserCommand.ExecuteAsync(null);
        Assert.True(configuration.FindUser("admin")!.Has(UserPermissions.Users));

        await page.DeleteUserCommand.ExecuteAsync(null);
        Assert.Single(configuration.Current.Users);
    }

    [Fact]
    public async Task NewUserNeedsUniqueNameAndMatchingPassword()
    {
        var (configuration, session) = await CreateAsync();
        TestSessions.LogInAsDefaultAdmin(session);
        using var page = new UsersSettingsViewModel(configuration, session, new LocalSettingsService(new MemoryLocalSettingsStore()), null!);

        page.AddUserCommand.Execute(null);
        page.UserName = "ADMIN";
        page.Password = page.ConfirmPassword = "x";
        await page.SaveUserCommand.ExecuteAsync(null);
        Assert.Single(configuration.Current.Users);

        page.UserName = "operator";
        page.ConfirmPassword = "y";
        await page.SaveUserCommand.ExecuteAsync(null);
        Assert.Single(configuration.Current.Users);

        page.ConfirmPassword = "x";
        await page.SaveUserCommand.ExecuteAsync(null);
        var created = configuration.FindUser("operator")!;
        Assert.True(PasswordHasher.Verify(created, "x"));
        Assert.False(created.Has(UserPermissions.Users));
        Assert.True(created.Has(UserPermissions.Playback));
    }

    private static async Task<(ConfigurationService, UserSessionService)> CreateAsync()
    {
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new AppConfiguration()), new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        return (configuration, TestSessions.Create(configuration));
    }
}
