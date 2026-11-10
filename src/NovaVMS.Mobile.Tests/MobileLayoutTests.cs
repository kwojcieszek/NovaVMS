using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Services;
using NovaVMS.Mobile.ViewModels;
using NovaVMS.Mobile.Views;
using NovaVMS.Presentation.Localization;
using Xunit;

namespace NovaVMS.Mobile.Tests;

public sealed class MobileLayoutTests
{
    [AvaloniaFact]
    public async Task GroupPreviewOpensCameraAndBackReturnsToGroup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaVMS-Mobile-Tests", Guid.NewGuid().ToString("N"));
        var protection = new TestProtection();
        var device = new Device
        {
            Name = "Recorder",
            Host = "camera.invalid",
            Channels = [new Channel { Number = 1, Name = "Test camera" }]
        };

        // Devices of earlier versions are migrated and get a device group.
        await new MobileDeviceStore(directory, protection).SaveAsync([device], TestContext.Current.CancellationToken);

        var players = new List<FakePlayer>();
        var display = new FakeDisplay();
        var shell = new MobileShell(new MobilePlatform(directory, protection, () =>
        {
            var player = new FakePlayer();

            players.Add(player);

            return player;
        }, display));
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = shell
        };

        try
        {
            window.Show();

            var main = players[0];

            await WaitForAsync(() => FindExecutable(shell, name => name == "Recorder") is not null);
            Click(FindExecutable(shell, name => name == "Recorder")!);
            await WaitForAsync(() => Shell(shell).CurrentPage is CameraGroupViewModel);
            await WaitForAsync(() => FindTile(shell) is not null);

            // A single tap on a preview does nothing; a double tap opens the camera.
            FindTile(shell)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.IsType<CameraGroupViewModel>(Shell(shell).CurrentPage);

            FindTile(shell)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => main.Starts == 1);

            // The preview stopped while its video surface was still shown, then it was released.
            Assert.True(players[1].StoppedWhileShown);
            Assert.True(players[1].IsDisposed);

            // Previews skip audio decoding; the opened camera keeps its audio.
            Assert.False(players[1].Options!.WithAudio);
            Assert.True(main.Options!.WithAudio);

            // A camera opened from the previews fills the landscape screen.
            Assert.True(Shell(shell).IsFullScreen);
            Assert.True(display.IsFullScreen);

            Assert.True(await shell.GoBackAsync());

            await WaitForAsync(() => Shell(shell).CurrentPage is CameraGroupViewModel);

            Assert.False(display.IsFullScreen);
            await WaitForAsync(() => FindTile(shell) is not null);
            FindTile(shell)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindTile(shell)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitForAsync(() => main.Starts == 2);
            await shell.SuspendAsync();

            Assert.False(main.IsPlaying);
            Assert.IsType<CamerasViewModel>(Shell(shell).CurrentPage);

            await WaitForAsync(() => players.Skip(1).All(player => player.IsDisposed));
        }
        finally
        {
            await shell.DisposeAsync();
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(320, 568)]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    [InlineData(768, 1024)]
    public async Task NavigationAndDeviceFormFitPhoneAndTablet(double width, double height)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var shell = new MobileShell(new MobilePlatform(directory, new TestProtection(), () => new FakePlayer()));
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = shell
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var navigation = FindButton(shell, Localizer.Text("Mobile_Settings"));

            Assert.InRange(navigation.Bounds.Width, 48, width);
            Assert.True(navigation.Bounds.Height >= 48);

            await OpenAsync(shell, Localizer.Text("Mobile_Settings"), typeof(SettingsViewModel));
            await OpenAsync(shell, Localizer.Text("Settings_Devices"), typeof(DevicesViewModel));
            await OpenAsync(shell, Localizer.Text("Mobile_AddDevice"), typeof(DeviceEditViewModel));
            Dispatcher.UIThread.RunJobs();

            foreach (var input in shell.GetLogicalDescendants().OfType<TextBox>())
            {
                Assert.InRange(input.Bounds.Width, 48, width);
            }

            Assert.Contains(shell.GetLogicalDescendants().OfType<TextBox>(), input => input.PasswordChar != '\0');
            Assert.Contains(shell.GetLogicalDescendants(), control => control is ScrollViewer);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EverySettingsPageOpensAndReturnsToTheMenu()
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovaVMS-Mobile-Tests", Guid.NewGuid().ToString("N"));
        var shell = new MobileShell(new MobilePlatform(directory, new TestProtection(), () => new FakePlayer()));
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = shell
        };

        try
        {
            window.Show();

            await WaitForAsync(() => Shell(shell).StorageReady);

            var pages = new (string Key, Type Page)[]
            {
                ("Settings_General", typeof(GeneralSettingsViewModel)),
                ("Settings_Groups", typeof(GroupsSettingsViewModel)),
                ("Settings_Devices", typeof(DevicesViewModel)),
                ("Settings_Sync", typeof(SyncSettingsViewModel))
            };

            foreach (var (key, page) in pages)
            {
                await OpenAsync(shell, Localizer.Text("Mobile_Settings"), typeof(SettingsViewModel));
                await OpenAsync(shell, Localizer.Text(key), page);
            }

            await OpenAsync(shell, Localizer.Text("Mobile_Settings"), typeof(SettingsViewModel));
            await OpenAsync(shell, Localizer.Text("Settings_Groups"), typeof(GroupsSettingsViewModel));
            await OpenAsync(shell, Localizer.Text("Groups_New"), typeof(GroupEditViewModel));

            Assert.True(await shell.GoBackAsync());
            Assert.IsType<GroupsSettingsViewModel>(Shell(shell).CurrentPage);
            Assert.True(await shell.GoBackAsync());
            Assert.IsType<SettingsViewModel>(Shell(shell).CurrentPage);
            Assert.Empty(Shell(shell).Current.Users);

            // Opening the pages must not change any setting, otherwise the change would be synchronized to desktops.
            var settings = Shell(shell).Current.Settings;
            var defaults = new AppSettings();

            Assert.Equal(defaults.Theme, settings.Theme);
            Assert.Equal(defaults.Language, settings.Language);
            Assert.Equal(defaults.GridStreamMode, settings.GridStreamMode);
            Assert.Equal(defaults.VideoAspectMode, settings.VideoAspectMode);
            Assert.Equal(defaults.DefaultLayoutCount, settings.DefaultLayoutCount);
            Assert.Equal(defaults.NetworkCachingMs, settings.NetworkCachingMs);
        }
        finally
        {
            await shell.DisposeAsync();
            window.Close();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ShellViewModel Shell(MobileShell shell)
    {
        return (ShellViewModel)((Control)shell.Content!).DataContext!;
    }

    private static bool IsTestCamera(string? name)
    {
        return name?.Contains("Test camera") == true;
    }

    private static async Task OpenAsync(MobileShell shell, string buttonName, Type page)
    {
        await WaitForAsync(() => FindExecutable(shell, name => name == buttonName) is not null);
        Click(FindExecutable(shell, name => name == buttonName)!);
        await WaitForAsync(() => Shell(shell).CurrentPage?.GetType() == page);
    }

    // Only a button whose command can run; the previous page may still be shown until the next layout pass.
    private static Button? FindExecutable(MobileShell shell, Func<string?, bool> name)
    {
        return shell.GetLogicalDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => name(AutomationProperties.GetName(button)) && button.Command?.CanExecute(button.CommandParameter) == true);
    }

    private static Button? FindTile(MobileShell shell)
    {
        return shell.GetLogicalDescendants().OfType<Button>().FirstOrDefault(button => IsTestCamera(AutomationProperties.GetName(button)) && button.Classes.Contains("tile"));
    }

    // Buttons run bound commands; raising the routed Click event alone does not execute them.
    private static void Click(Button button)
    {
        Assert.True(button.Command?.CanExecute(button.CommandParameter) == true);

        button.Command.Execute(button.CommandParameter);
    }

    private static Button FindButton(MobileShell shell, string content)
    {
        return shell.GetLogicalDescendants().OfType<Button>().First(button => AutomationProperties.GetName(button) == content);
    }

    private static async Task WaitForAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 200 && !predicate(); i++)
        {
            Dispatcher.UIThread.RunJobs();

            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        Assert.True(predicate());
    }

    private sealed class FakeDisplay : IDeviceDisplay
    {
        public bool IsFullScreen { get; private set; }

        public void EnterFullScreen()
        {
            IsFullScreen = true;
        }

        public void ExitFullScreen()
        {
            IsFullScreen = false;
        }

        public void SetRotationLocked(bool locked)
        {
        }
    }

    private sealed class FakePlayer : IMobilePlayer
    {
        public int Starts { get; private set; }

        public bool IsPlaying { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool StoppedWhileShown { get; private set; }

        public Control View { get; } = new Border();

        public event EventHandler<string>? StatusChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public PlaybackOptions? Options { get; private set; }

        public Task PlayAsync(VideoSource source, Device device, PlaybackOptions options, CancellationToken cancellationToken = default)
        {
            Options = options;
            Starts++;
            IsPlaying = true;

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StoppedWhileShown |= IsPlaying && View.Parent is not null;
            IsPlaying = false;

            return Task.CompletedTask;
        }

        public void SetPaused(bool paused)
        {
        }

        public void SetMuted(bool muted)
        {
        }

        public void SetFillArea(bool fill)
        {
        }

        public ValueTask DisposeAsync()
        {
            IsPlaying = false;
            IsDisposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
