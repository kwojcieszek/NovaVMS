using Avalonia;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Configuration;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Infrastructure.Sync;
using NovaVMS.Mobile.Localization;
using NovaVMS.Mobile.Models;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>
/// Single-view navigation: no desktop windows, pointer-only actions or video overlays.
/// One device operation runs at a time; leaving a camera cancels it and stops playback.
/// </summary>
public sealed partial class ShellViewModel : ViewModelBase, IAsyncDisposable
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(45);

    private readonly LocalSettingsStore _localSettingsStore;
    private readonly Func<IMobilePlayer> _createPlayer;
    private readonly IDeviceDisplay? _display;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private CancellationTokenSource? _pending;
    private IDeviceClient? _client;
    private bool _loaded;
    private bool _suspended;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCamerasTab))]
    [NotifyPropertyChangedFor(nameof(IsSettingsTab))]
    [NotifyPropertyChangedFor(nameof(ShowBackButton))]
    private PageViewModel? _currentPage;

    /// <summary>True until the configuration is read; the start screen with the logo is shown meanwhile.</summary>
    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConfirmation))]
    private ConfirmationViewModel? _confirmation;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWindowed))]
    [NotifyPropertyChangedFor(nameof(ContentInsets))]
    [NotifyPropertyChangedFor(nameof(ShowBackButton))]
    private bool _isFullScreen;

    /// <summary>
    /// Screen edges covered by the system: the notch or Dynamic Island, rounded corners and the home indicator.
    /// The backgrounds reach the screen edges; only the content keeps out of these areas.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContentInsets))]
    [NotifyPropertyChangedFor(nameof(StatusInsets))]
    [NotifyPropertyChangedFor(nameof(NavigationInsets))]
    private Thickness _safeArea;

    public IMobilePlayer Player { get; }

    public MobileConfigurationService Configuration { get; }

    public SettingsSyncService Sync { get; }

    /// <summary>Settings of this phone only, in the desktop local.json format; never synchronized.</summary>
    public LocalSettings LocalSettings { get; }

    /// <summary>System file picker; set by the view once it is shown.</summary>
    public IFileService? Files { get; set; }

    public AppConfiguration Current => Configuration.Current;

    public IReadOnlyList<Device> Devices => Configuration.Current.Devices;

    public bool StorageReady { get; private set; }

    /// <summary>Session preference kept between cameras; starts from the synchronized stream mode.</summary>
    public bool MainStream { get; set; }

    /// <summary>Session preference kept between cameras; sound starts muted.</summary>
    public bool Muted { get; set; } = true;

    public bool HasConfirmation => Confirmation is not null;

    /// <summary>Status bar and navigation are hidden while a camera fills the screen.</summary>
    public bool IsWindowed => !IsFullScreen;

    /// <summary>Pages keep below the top system area; a full screen camera uses the whole screen.</summary>
    public Thickness ContentInsets => IsFullScreen
        ? default
        : new Thickness(SafeArea.Left, SafeArea.Top, SafeArea.Right, 0);

    public Thickness StatusInsets => new(SafeArea.Left, 0, SafeArea.Right, 0);

    /// <summary>The navigation bar extends under the home indicator; its buttons stay above it.</summary>
    public Thickness NavigationInsets => new(SafeArea.Left, 0, SafeArea.Right, SafeArea.Bottom);

    /// <summary>True when the back button closes a question or returns to a previous page instead of leaving the application.</summary>
    public bool CanGoBack => Confirmation is not null || CurrentPage is { IsRoot: false };

    /// <summary>Back button above the page; a full screen camera is left by tapping the picture twice.</summary>
    public bool ShowBackButton => !IsFullScreen && CurrentPage is { HasBackButton: true };

    public bool IsCamerasTab => CurrentPage?.Tab == MobileTab.Cameras;

    public bool IsSettingsTab => CurrentPage?.Tab == MobileTab.Settings;

    public ShellViewModel(MobilePlatform platform)
    {
        var files = new MobileDataFiles(platform.DataDirectory);

        Directory.CreateDirectory(platform.DataDirectory);
        files.MigrateLegacyFiles();

        var protection = new ConfigurationProtectionAdapter(platform.Protection);
        var serializer = new JsonConfigurationSerializer();
        var configurationStore = new JsonConfigurationStore(serializer, files.ConfigurationFile, protection);
        var optionsStore = new SettingsSyncOptionsStore(files.SyncOptionsFile, protection);

        _localSettingsStore = new LocalSettingsStore(files.LocalSettingsFile);

        Configuration = new MobileConfigurationService(configurationStore, serializer, new MobileDeviceStore(platform.DataDirectory, platform.Protection));
        Configuration.Changed += OnConfigurationChanged;
        Configuration.Replaced += OnConfigurationReplaced;

        Sync = new SettingsSyncService(Configuration, optionsStore, new BackgroundRemoteSettingsStoreFactory(new RemoteSettingsStoreFactory(serializer)));
        Sync.Notified += OnSyncNotified;

        LocalSettings = _localSettingsStore.Load();
        _display?.SetRotationLocked(LocalSettings.LockRotation);

        _createPlayer = platform.CreatePlayer;
        _display = platform.Display;
        Player = platform.CreatePlayer();
        Player.StatusChanged += PlayerStatusChanged;

        ShowCameras();
    }

    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        await RunAsync(async token =>
        {
            await Configuration.LoadAsync(token);

            StorageReady = true;
            MainStream = Current.Settings.GridStreamMode == GridStreamMode.Main;
            ShowCameras();
        });

        IsLoading = false;

        if (!StorageReady)
        {
            Status = Localizer.Text("Mobile_StorageReadFailed");

            return;
        }

        await Sync.StartAsync();
    }

    public async Task SuspendAsync()
    {
        _suspended = true;
        Confirmation?.Cancel();

        await CloseCameraAsync();

        ShowCameras();
        Status = Localizer.Text("Mobile_StreamSuspended");

        await Sync.FlushAsync();
    }

    public void Resume()
    {
        _suspended = false;
    }

    public async Task<bool> GoBackAsync()
    {
        if (Confirmation is { } confirmation)
        {
            confirmation.Cancel();

            return true;
        }

        if (CurrentPage is null or { IsRoot: true })
        {
            return false;
        }

        var previous = CurrentPage;

        await CloseCameraAsync();

        previous.NavigateBack(this);

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _suspended = true;
        Confirmation?.Cancel();

        await CloseCameraAsync();

        CurrentPage = null;
        Player.StatusChanged -= PlayerStatusChanged;
        Sync.Notified -= OnSyncNotified;
        Sync.Dispose();
        Configuration.Changed -= OnConfigurationChanged;
        Configuration.Replaced -= OnConfigurationReplaced;

        await Player.DisposeAsync();
    }

    internal void ShowCameras()
    {
        CurrentPage = new CamerasViewModel(this);
    }

    internal void ShowCameraCollection(CameraCollection collection)
    {
        CurrentPage = new CameraGroupViewModel(this, collection);
    }

    /// <summary>Creates an extra player for a live preview tile; the caller disposes it.</summary>
    internal IMobilePlayer CreatePreviewPlayer()
    {
        return _createPlayer();
    }

    internal void ShowDevices()
    {
        CurrentPage = new DevicesViewModel(this);
    }

    internal void ShowSettings()
    {
        CurrentPage = new SettingsViewModel(this);
    }

    internal void ShowGeneralSettings()
    {
        CurrentPage = new GeneralSettingsViewModel(this);
    }

    internal void ShowGroups()
    {
        CurrentPage = new GroupsSettingsViewModel(this);
    }

    internal void EditGroup(CameraGroup group)
    {
        CurrentPage = new GroupEditViewModel(this, group);
    }

    internal void ShowSync()
    {
        CurrentPage = new SyncSettingsViewModel(this);
    }

    internal void EditDevice(Device? existing)
    {
        CurrentPage = new DeviceEditViewModel(this, existing);
    }

    internal void ConfirmDeviceRemoval(Device device)
    {
        CurrentPage = new DeviceRemovalViewModel(this, device);
    }

    /// <summary>Asks a question over the current page; the answer is false when the page is left.</summary>
    internal async Task<bool> ConfirmAsync(string title, string message)
    {
        Confirmation?.Cancel();

        var confirmation = new ConfirmationViewModel(title, message);

        Confirmation = confirmation;

        try
        {
            return await confirmation.Result;
        }
        finally
        {
            if (Confirmation == confirmation)
            {
                Confirmation = null;
            }
        }
    }

    internal bool IsCurrentClient(IDeviceClient client)
    {
        return ReferenceEquals(_client, client);
    }

    internal void CancelPending()
    {
        _pending?.Cancel();
    }

    /// <summary>Runs one device operation with a timeout; a second operation is ignored while one is running.</summary>
    internal async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (_suspended || !await _operations.WaitAsync(0))
        {
            return;
        }

        using var cancellation = new CancellationTokenSource(OperationTimeout);

        _pending = cancellation;
        IsBusy = true;

        try
        {
            await operation(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Status = Localizer.Text("Mobile_OperationCancelled");
        }
        catch
        {
            Status = Localizer.Text("Mobile_OperationFailed");
        }
        finally
        {
            _pending = null;
            IsBusy = false;
            _operations.Release();
        }
    }

    /// <summary>Cancels the running operation and runs <paramref name="action"/> once it has finished.</summary>
    internal async Task RunExclusiveAsync(Func<Task> action)
    {
        _pending?.Cancel();

        await _operations.WaitAsync();

        try
        {
            await action();
        }
        finally
        {
            _operations.Release();
        }
    }

    /// <summary>Reports a failed user action in the status bar instead of crashing the application.</summary>
    internal async Task GuardAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            Status = Localizer.Text("Mobile_ActionFailed");
        }
    }

    /// <summary>Saves a local change; it is sent to the settings database when automatic sending is on.</summary>
    internal async Task SaveConfigurationAsync()
    {
        try
        {
            await Configuration.SaveAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = Localizer.Format("General_SaveFailed", exception.Message);
        }
    }

    /// <summary>Keeps the interface in portrait or lets it turn with the phone; saved for this phone only.</summary>
    internal async Task SetRotationLockedAsync(bool locked)
    {
        LocalSettings.LockRotation = locked;
        _display?.SetRotationLocked(locked);

        await SaveLocalSettingsAsync();
    }

    internal async Task SaveLocalSettingsAsync()
    {
        try
        {
            await _localSettingsStore.SaveAsync(LocalSettings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = Localizer.Format("General_SaveFailed", exception.Message);
        }
    }

    /// <summary>
    /// Stops the open camera and the live previews. Called before every page change away from them:
    /// the native video surfaces must still exist while the players stop.
    /// </summary>
    internal async Task CloseCameraAsync()
    {
        if (CurrentPage is CameraGroupViewModel group)
        {
            await group.ReleaseAsync();
        }

        await RunExclusiveAsync(async () =>
        {
            await Player.StopAsync();

            if (_client is not null)
            {
                // Playback messages describe the closed camera.
                Status = string.Empty;
            }

            _client?.Dispose();
            _client = null;
        });
    }

    internal async Task OpenCameraAsync(Device device, Channel channel, CameraCollection? returnTo = null)
    {
        await CloseCameraAsync();
        await RunAsync(async token =>
        {
            var client = await MobileDevices.ConnectAsync(device, LocalSettings.UseOptionalProtocol, token);

            if (token.IsCancellationRequested)
            {
                client.Dispose();
                token.ThrowIfCancellationRequested();
            }

            _client = client;

            var camera = new CameraViewModel(this, client, device, channel, returnTo);

            CurrentPage = camera;

            await camera.PlayLiveAsync(token);
        });
    }

    /// <summary>Adds, replaces (keeping its position) or removes a device and saves the configuration.</summary>
    internal async Task ReplaceDeviceAsync(Device? previous, Device? updated, CancellationToken token)
    {
        var devices = Current.Devices;
        var index = previous is null ? -1 : devices.FindIndex(device => device.Id == previous.Id);

        if (updated is null)
        {
            if (previous is not null)
            {
                Current.RemoveDevice(previous.Id);
            }
        }
        else if (index >= 0)
        {
            devices[index] = updated;
        }
        else
        {
            devices.Add(updated);
        }

        token.ThrowIfCancellationRequested();

        await Configuration.SaveAsync(token);
    }

    private static void ApplyTheme(ThemeMode theme)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = theme switch
        {
            ThemeMode.Light => ThemeVariant.Light,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };
    }

    [RelayCommand]
    private Task NavigateBackAsync()
    {
        return GuardAsync(GoBackAsync);
    }

    [RelayCommand]
    private Task ShowCamerasTabAsync()
    {
        return GuardAsync(async () =>
        {
            await CloseCameraAsync();
            ShowCameras();
        });
    }

    [RelayCommand]
    private Task ShowSettingsTabAsync()
    {
        return GuardAsync(async () =>
        {
            await CloseCameraAsync();
            ShowSettings();
        });
    }

    partial void OnIsFullScreenChanged(bool value)
    {
        if (value)
        {
            _display?.EnterFullScreen();
        }
        else
        {
            _display?.ExitFullScreen();
        }
    }

    private void PlayerStatusChanged(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_disposed && !_suspended && _client is not null)
            {
                Status = message;
            }
        });
    }

    private void OnSyncNotified(object? sender, string message)
    {
        Status = message;
    }

    // Language and theme are synchronized settings, as in the desktop application.
    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        Localizer.Instance.SetLanguage(Current.Settings.Language);
        ApplyTheme(Current.Settings.Theme);
    }

    // A version from the settings database replaces every object; list pages are rebuilt, an open camera stays.
    private void OnConfigurationReplaced(object? sender, EventArgs e)
    {
        switch (CurrentPage)
        {
            case CamerasViewModel:
                ShowCameras();
                break;
            case CameraGroupViewModel group:
                ShowCameraCollection(group.Collection);
                break;
            case DevicesViewModel or DeviceEditViewModel or DeviceRemovalViewModel:
                ShowDevices();
                break;
            case GroupsSettingsViewModel or GroupEditViewModel:
                ShowGroups();
                break;
            case GeneralSettingsViewModel:
                ShowGeneralSettings();
                break;
        }
    }

    // The native video view can have only one parent, so the previous camera page releases it first;
    // a left camera group stops and disposes its preview players and other pages stop following services.
    partial void OnCurrentPageChanged(PageViewModel? oldValue, PageViewModel? newValue)
    {
        if (oldValue is CameraViewModel camera)
        {
            camera.Detach();
        }

        if (oldValue is CameraGroupViewModel group)
        {
            _ = group.ReleaseAsync();
        }

        if (oldValue is SyncSettingsViewModel sync)
        {
            sync.Detach();
        }

        IsFullScreen = newValue is CameraViewModel { IsFullScreen: true };
    }
}
