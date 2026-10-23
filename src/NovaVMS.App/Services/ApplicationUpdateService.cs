using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Localization;
using NovaVMS.App.Models;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Updater;

namespace NovaVMS.App.Services;

public sealed partial class ApplicationUpdateService : ObservableObject, IDisposable
{
    private readonly ConfigurationService _configuration;
    private readonly IDialogService _dialogs;
    private readonly HttpClient _http;
    private readonly UpdateDownloadCache _downloads;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromHours(6) };
    private bool _started;
    private string _lastUrl = "";
    private bool _lastAutomatic;
    private bool _lastAutoDownload;
    private ApplicationUpdate? _promptedUpdate;
    private CancellationTokenSource? _operation;

    [ObservableProperty]
    private bool _isDownloaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckCommand))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    [NotifyPropertyChangedFor(nameof(AvailableText))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private ApplicationUpdate? _available;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private double _progress;

    public bool IsIndeterminate => IsBusy && Progress == 0;

    public bool HasUpdate => Available is not null;
    public string AvailableText => Available is null ? "" : Localizer.Format("Updates_Available", Available.VersionText);
    public string CurrentVersion => ApplicationVersion.Number;

    public ApplicationUpdateService(ConfigurationService configuration, IDialogService dialogs)
        : this(configuration, dialogs, new HttpClient { Timeout = TimeSpan.FromMinutes(15) },
            Path.Combine(Path.GetTempPath(), "NovaVMS-updates", "cache"))
    {
    }

    internal ApplicationUpdateService(ConfigurationService configuration, IDialogService dialogs, HttpClient http, string cacheDirectory)
    {
        _configuration = configuration;
        _dialogs = dialogs;
        _http = http;
        _downloads = new UpdateDownloadCache(http, cacheDirectory);
        _configuration.Changed += OnConfigurationChanged;
        _timer.Tick += OnTimer;
    }

    public void Start()
    {
        _started = true;
        OnConfigurationChanged(this, EventArgs.Empty);
    }

    private void OnConfigurationChanged(object? sender, EventArgs e)
    {
        if (!_started) return;
        var settings = _configuration.Current.Settings;
        var url = (settings.UpdateServerUrl ?? "").Trim();
        var automatic = settings.AutoCheckForUpdates || settings.AutoDownloadUpdates;
        if (url == _lastUrl && automatic == _lastAutomatic && settings.AutoDownloadUpdates == _lastAutoDownload) return;
        if (url != _lastUrl)
        {
            _operation?.Cancel();
            Available = null;
            IsDownloaded = false;
            _promptedUpdate = null;
        }
        _lastUrl = url;
        _lastAutomatic = automatic;
        _lastAutoDownload = settings.AutoDownloadUpdates;
        _timer.Stop();
        if (_lastAutomatic && url.Length > 0)
        {
            _timer.Start();
            _ = CheckAfterDelayAsync(url);
        }
    }

    private async Task CheckAfterDelayAsync(string url)
    {
        try
        {
            await Task.Delay(2000, _lifetime.Token);
            if (url == _lastUrl && _lastAutomatic && !IsBusy) await CheckAsync();
        }
        catch (OperationCanceledException) { }
    }

    private void OnTimer(object? sender, EventArgs e)
    {
        if (!IsBusy && (_configuration.Current.Settings.AutoCheckForUpdates || _configuration.Current.Settings.AutoDownloadUpdates)) _ = CheckAsync();
    }

    private bool CanCheck() => !IsBusy && !_lifetime.IsCancellationRequested;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckAsync()
    {
        if (!CanCheck()) return;
        var url = _configuration.Current.Settings.UpdateServerUrl;
        IsBusy = true;
        Progress = 0;
        StatusText = Localizer.Text("Updates_Checking");
        try
        {
            if (!OperatingSystem.IsWindows()) throw new NotSupportedException(Localizer.Text("Updates_WindowsOnly"));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _operation = timeout;
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            var found = await UpdateCatalog.CheckAsync(_http, url, CurrentVersion, timeout.Token);
            if (url != _configuration.Current.Settings.UpdateServerUrl) return;
            Available = found;
            IsDownloaded = false;
            StatusText = found is null ? Localizer.Text("Updates_Current") : AvailableText;
            if (found is not null)
            {
                var cached = await _downloads.TryGetAsync(found, timeout.Token);
                timeout.Token.ThrowIfCancellationRequested();
                if (url != _configuration.Current.Settings.UpdateServerUrl) return;
                if (cached is not null)
                {
                    IsDownloaded = true;
                    StatusText = Localizer.Format("Updates_Downloaded", found.VersionText);
                }
                else if (_configuration.Current.Settings.AutoDownloadUpdates)
                    await DownloadAsync(found);
                if (IsDownloaded && _configuration.Current.Settings.AutoDownloadUpdates)
                    await PromptForDownloadedUpdateAsync(found);
            }
        }
        catch (OperationCanceledException) { StatusText = Localizer.Text("Updates_Cancelled"); }
        catch (Exception exception) { StatusText = Localizer.Format("Updates_Failed", exception.Message); }
        finally
        {
            _operation = null;
            IsBusy = false;
            if (url != _configuration.Current.Settings.UpdateServerUrl && _lastAutomatic) _ = CheckAfterDelayAsync(_lastUrl);
        }
    }

    private bool CanInstall() => HasUpdate && !IsBusy && !_lifetime.IsCancellationRequested;

    private async Task PromptForDownloadedUpdateAsync(ApplicationUpdate update)
    {
        if (_lifetime.IsCancellationRequested || Available != update || _promptedUpdate == update) return;
        // A declined package stays available; do not repeat the prompt on every periodic check.
        _promptedUpdate = update;
        var accepted = await _dialogs.ConfirmAsync(Localizer.Text("Updates_Title"),
            Localizer.Format("Updates_InstallConfirm", update.VersionText));
        if (accepted && !_lifetime.IsCancellationRequested && Available == update)
            await InstallCoreAsync(update, confirmed: true);
    }

    private async Task<DownloadedUpdate> DownloadAsync(ApplicationUpdate update)
    {
        Progress = 0;
        StatusText = Localizer.Text("Updates_Downloading");
        using var download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        download.CancelAfter(TimeSpan.FromMinutes(15));
        _operation = download;
        try
        {
            var progress = new Progress<double>(value =>
            {
                if (ReferenceEquals(_operation, download) && !download.IsCancellationRequested)
                    Progress = Math.Min(99, value);
            });
            var package = await _downloads.GetAsync(update, progress, download.Token);
            download.Token.ThrowIfCancellationRequested();
            IsDownloaded = true;
            Progress = 100;
            StatusText = Localizer.Format("Updates_Downloaded", update.VersionText);
            return package;
        }
        finally { if (ReferenceEquals(_operation, download)) _operation = null; }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (Available is not { } update || !CanInstall()) return;
        await InstallCoreAsync(update, confirmed: false);
    }

    private async Task InstallCoreAsync(ApplicationUpdate update, bool confirmed)
    {
        IsBusy = true;
        Progress = 0;
        StatusText = Localizer.Text("Updates_Downloading");
        var job = Path.Combine(Path.GetTempPath(), "NovaVMS-updates", Guid.NewGuid().ToString("N"));
        var package = Path.Combine(job, "package.zip");
        var handedOff = false;
        using var download = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        download.CancelAfter(TimeSpan.FromMinutes(15));
        _operation = download;
        try
        {
            Directory.CreateDirectory(job);
            var executable = Environment.ProcessPath;
            if (executable is null || !Path.GetFileName(executable).Equals("NovaVMS.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException(Localizer.Text("Updates_RunExecutable"));
            var install = Path.GetDirectoryName(executable)!;
            // Check permissions before closing the running application.
            var writeTest = Path.Combine(install, ".novavms-write-test-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(writeTest, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }

            var cached = await DownloadAsync(update);
            _operation = download;
            var hash = await Task.Run(() =>
            {
                File.Copy(cached.PackagePath, package);
                var actual = UpdatePackage.Sha256(package);
                if (!actual.Equals(cached.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(Localizer.Text("Updates_ChecksumFailed"));
                return actual;
            });
            Progress = 100;
            download.Token.ThrowIfCancellationRequested();
            if (!confirmed && !await _dialogs.ConfirmAsync(Localizer.Text("Updates_Title"), Localizer.Format("Updates_InstallConfirm", update.VersionText)))
            {
                StatusText = Localizer.Format("Updates_Downloaded", update.VersionText);
                return;
            }
            // Copy the installer out of the application directory so it can replace its own files.
            var helperSource = Path.Combine(AppContext.BaseDirectory, "updater");
            var helper = Path.Combine(job, "helper");
            if (!File.Exists(Path.Combine(helperSource, "NovaVMS.Updater.exe"))) throw new IOException(Localizer.Text("Updates_HelperMissing"));
            foreach (var source in Directory.EnumerateFiles(helperSource, "*", SearchOption.AllDirectories))
            {
                var target = UpdatePackage.SafePath(helper, Path.GetRelativePath(helperSource, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            var plan = new UpdatePlan { InstallDirectory = install, JobDirectory = job, PackagePath = package,
                PackageSha256 = hash, Version = update.VersionText, Arguments = Environment.GetCommandLineArgs().Skip(1).ToArray() };
            var planPath = Path.Combine(job, "plan.json");
            await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), download.Token);
            await _configuration.FlushAsync();
            var start = new ProcessStartInfo(Path.Combine(helper, "NovaVMS.Updater.exe"))
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = helper };
            start.ArgumentList.Add(planPath);
            using var process = Process.Start(start) ?? throw new IOException(Localizer.Text("Updates_HelperMissing"));
            handedOff = true;
            StatusText = Localizer.Text("Updates_Restarting");
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows.Where(window => window != desktop.MainWindow).ToArray()) window.Close();
                desktop.MainWindow?.Close(); // Normal shutdown flushes settings and stops native SDK/VLC workers.
            }
        }
        catch (OperationCanceledException) { StatusText = Localizer.Text("Updates_Cancelled"); }
        catch (Exception exception) { StatusText = Localizer.Format("Updates_Failed", exception.Message); }
        finally
        {
            if (!handedOff && File.Exists(package))
                try { File.Delete(package); } catch (IOException exception) { UnhandledExceptionLogger.Log("Cleaning cancelled update download", exception); }
            _operation = null;
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel() => _operation?.Cancel();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimer;
        _configuration.Changed -= OnConfigurationChanged;
        _lifetime.Cancel();
        _http.Dispose();
    }
}
