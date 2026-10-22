using Avalonia.Headless.XUnit;
using LibVLCSharp.Shared;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class GroupLayoutCapacityTests
{
    [AvaloniaFact]
    public async Task OpeningGroupAsksOnlyWhenNeededAndRespectsAnswerAndSetting()
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        foreach (var (count, enabled, answer, expectedCount, prompts) in new[]
        {
            (6, true, true, 6, 1), (6, true, false, 4, 1),
            (6, false, true, 4, 0), (4, true, true, 4, 0), (3, true, true, 4, 0)
        })
        {
            var (configuration, session) = await CreateAsync(count);
            configuration.Current.Settings.WarnWhenGroupExceedsLayout = enabled;
            var dialogs = new Dialogs { Answer = Task.FromResult(answer) };
            var live = new LiveViewViewModel(vlc, configuration, new FakeDeviceClientFactory(), new StatusBarService(), dialogs, session);
            try
            {
                var cameras = configuration.Current.Groups[0].Cameras;
                await live.ShowGroupAsync("test-group", cameras);
                Assert.Equal(prompts, dialogs.Confirmations);
                Assert.Equal(expectedCount, live.SelectedLayout.Count);
                Assert.Equal(expectedCount, live.Tiles.Count);
                Assert.Equal("test-group", live.ActiveGroup);
            }
            finally { live.Dispose(); await live.Disposal; }
        }
    }

    [AvaloniaFact]
    public async Task SavedGroupSplitIsCheckedAndManualShrinkAsksAgain()
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var (configuration, session) = await CreateAsync(6);
        configuration.Current.GroupLiveViews["test-group"] = new() { LayoutId = "standard-4", LayoutCount = 4 };
        var dialogs = new Dialogs();
        var live = new LiveViewViewModel(vlc, configuration, new FakeDeviceClientFactory(), new StatusBarService(), dialogs, session);
        try
        {
            live.SelectedLayout = NovaVMS.App.Models.GridLayoutOption.Find("standard-9");
            await live.ShowGroupAsync("test-group", configuration.Current.Groups[0].Cameras);
            Assert.Equal(1, dialogs.Confirmations);
            Assert.Equal(6, live.SelectedLayout.Count);
            live.SelectedLayout = NovaVMS.App.Models.GridLayoutOption.Find("standard-4");
            Assert.Equal(2, dialogs.Confirmations);
            Assert.Equal(6, live.SelectedLayout.Count);
        }
        finally { live.Dispose(); await live.Disposal; }
    }

    [AvaloniaFact]
    public async Task AnswerToPreviousGroupCannotChangeNewlyOpenedView()
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var (configuration, session) = await CreateAsync(6);
        var answer = new TaskCompletionSource<bool>();
        var dialogs = new Dialogs { Answer = answer.Task };
        var live = new LiveViewViewModel(vlc, configuration, new FakeDeviceClientFactory(), new StatusBarService(), dialogs, session);
        try
        {
            var opening = live.ShowGroupAsync("test-group", configuration.Current.Groups[0].Cameras);
            Assert.False(opening.IsCompleted);
            var view = new SavedCameraView { Layout = new() { LayoutId = "standard-1", LayoutCount = 1 } };
            configuration.Current.Views.Add(view);
            await live.ShowSavedViewAsync(view.Id);
            answer.SetResult(true);
            await opening;
            Assert.Null(live.ActiveGroup);
            Assert.Equal(view.Id, live.ActiveSavedViewId);
            Assert.Equal(1, live.SelectedLayout.Count);
        }
        finally { answer.TrySetResult(false); live.Dispose(); await live.Disposal; }
    }

    private static async Task<(ConfigurationService, UserSessionService)> CreateAsync(int count)
    {
        var device = new Device { Name = "Recorder", Channels = Enumerable.Range(1, count)
            .Select(number => new Channel { Number = number, Name = $"Camera {number}" }).ToList() };
        var group = new CameraGroup { Name = "Group", Cameras = device.Channels.Select(channel =>
            new CameraReference { DeviceId = device.Id, ChannelNumber = channel.Number }).ToList() };
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new() { Devices = [device], Groups = [group] }),
            new JsonConfigurationSerializer());
        var session = TestSessions.Create(configuration);
        await configuration.LoadAsync();
        TestSessions.LogInAsDefaultAdmin(session);
        return (configuration, session);
    }

    private sealed class Dialogs : IDialogService
    {
        public Task<bool> Answer { get; init; } = Task.FromResult(true);
        public int Confirmations { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message) { Confirmations++; return Answer; }
        public Task<string?> PromptTextAsync(string title, string message, string initialValue) => throw new NotSupportedException();
        public Task<bool> LoginAsync(UserSessionService session, string userName) => throw new NotSupportedException();
        public Task<Stream?> OpenJsonFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
        public Task<Stream?> OpenCsvFileAsync(string title) => throw new NotSupportedException();
        public Task<(Stream Content, Stream? Setup)?> OpenIvmsFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveCsvFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
        public Task<string?> PickFolderAsync(string title) => throw new NotSupportedException();
    }
}
