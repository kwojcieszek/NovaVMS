using System.IO.Compression;
using System.Diagnostics;
using System.Text.Json;
using NovaVMS.App.Models;
using NovaVMS.Updater;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class UpdateInstallerTests
{
    [Fact]
    public async Task ExternalHelperWaitsForRealProcessReplacesFilesAndRestarts()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = new Fixture(nativeExecutable: true);
        var marker = Path.Combine(fixture.Install, "restarted.txt");
        var plan = fixture.Plan with { Arguments = ["/c", "echo restarted>restarted.txt"] };
        var helper = Path.Combine(fixture.Job, "helper");
        Directory.CreateDirectory(helper);
        foreach (var extension in new[] { ".exe", ".dll", ".deps.json", ".runtimeconfig.json" })
            File.Copy(Path.Combine(AppContext.BaseDirectory, "NovaVMS.Updater" + extension), Path.Combine(helper, "NovaVMS.Updater" + extension));
        var planFile = Path.Combine(fixture.Job, "plan.json");
        File.WriteAllText(planFile, JsonSerializer.Serialize(plan));
        var runningInfo = new ProcessStartInfo(Path.Combine(fixture.Install, "NovaVMS.exe")) { UseShellExecute = false, CreateNoWindow = true };
        runningInfo.ArgumentList.Add("/c");
        runningInfo.ArgumentList.Add("ping -n 4 127.0.0.1 >nul");
        using var running = Process.Start(runningInfo)!;
        var updateInfo = new ProcessStartInfo(Path.Combine(helper, "NovaVMS.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true };
        updateInfo.ArgumentList.Add(planFile);
        using var updater = Process.Start(updateInfo)!;
        try
        {
            await Task.Delay(300);
            Assert.False(running.HasExited);
            Assert.False(Directory.Exists(Path.Combine(fixture.Job, "payload")));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await updater.WaitForExitAsync(timeout.Token);
            Assert.Equal(0, updater.ExitCode);
            Assert.True(running.HasExited);
            for (var i = 0; i < 30 && !File.Exists(marker); i++) await Task.Delay(100);
            Assert.True(File.Exists(marker));
            Assert.Equal("OK", File.ReadAllText(Path.Combine(fixture.Job, "result.txt")));
            Assert.True(File.Exists(Path.Combine(fixture.Install, "new-file.txt")));
        }
        finally
        {
            if (!running.HasExited) { running.Kill(entireProcessTree: true); await running.WaitForExitAsync(); }
            if (!updater.HasExited) { updater.Kill(); await updater.WaitForExitAsync(); }
        }
    }

    [Fact]
    public async Task WaitsForAllProcessesBeforeExtractionThenReplacesAndRestarts()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Install, "config.json"), "user settings");
        File.WriteAllText(Path.Combine(fixture.Install, "retained.dll"), "SDK dependency");
        var host = new Host(() =>
        {
            Assert.False(Directory.Exists(Path.Combine(fixture.Job, "payload")));
            Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "NovaVMS.exe")));
        });
        await new UpdateInstaller(host).ApplyAsync(fixture.Plan);
        Assert.Equal("new", File.ReadAllText(Path.Combine(fixture.Install, "NovaVMS.exe")));
        Assert.Equal("user settings", File.ReadAllText(Path.Combine(fixture.Install, "config.json")));
        Assert.Equal("SDK dependency", File.ReadAllText(Path.Combine(fixture.Install, "retained.dll")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Job, "backup", "NovaVMS.exe")));
        Assert.Equal(1, host.Starts);
        // The installer must release its exclusive startup lock before launching NovaVMS.
        using var gate = File.Open(Path.Combine(fixture.Install, ".novavms-update.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task FileReplacementFailureRollsBackAndRestartsOriginal()
    {
        using var fixture = new Fixture("zz-blocked");
        Directory.CreateDirectory(Path.Combine(fixture.Install, "zz-blocked"));
        var host = new Host();
        await Assert.ThrowsAnyAsync<IOException>(() => new UpdateInstaller(host).ApplyAsync(fixture.Plan));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "NovaVMS.exe")));
        Assert.False(File.Exists(Path.Combine(fixture.Install, "new-file.txt")));
        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public async Task ChangedDownloadCannotCloseProcessesOrModifyApplication()
    {
        using var fixture = new Fixture();
        File.AppendAllText(fixture.Plan.PackagePath, "tampered");
        var host = new Host(() => throw new Exception("Should not wait on processes"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateInstaller(host).ApplyAsync(fixture.Plan));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "NovaVMS.exe")));
        Assert.Equal(0, host.Starts);
    }

    [Fact]
    public async Task ProcessThatDoesNotExitPreventsExtractionAndReplacement()
    {
        using var fixture = new Fixture();
        var host = new Host(() => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new UpdateInstaller(host).ApplyAsync(fixture.Plan));
        Assert.False(Directory.Exists(Path.Combine(fixture.Job, "payload")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "NovaVMS.exe")));
        Assert.Equal(0, host.Starts);
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("C:/escape.exe")]
    [InlineData("nested/../../escape.exe")]
    [InlineData("config.json")]
    [InlineData("data/sync.json")]
    [InlineData("logs/update.log")]
    public void UnsafeZipPathsAndUserSettingsAreRejected(string entryName)
    {
        using var fixture = new Fixture(entryName);
        using var archive = ZipFile.OpenRead(fixture.Plan.PackagePath);
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Validate(archive));
    }

    [Fact]
    public async Task WrongBinaryVersionLeavesOldApplicationIntact()
    {
        using var fixture = new Fixture();
        var host = new Host();
        var wrongVersion = DateOnly.ParseExact(ApplicationVersion.Number, "yyyy.MM.dd").AddDays(1).ToString("yyyy.MM.dd");
        await Assert.ThrowsAsync<InvalidDataException>(() => new UpdateInstaller(host).ApplyAsync(fixture.Plan with { Version = wrongVersion }));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "NovaVMS.exe")));
        Assert.Equal(1, host.Starts);
    }

    [Fact]
    public void SingleWrappingDirectoryIsAccepted()
    {
        using var fixture = new Fixture(prefix: "NovaVMS-2026.10.21/");
        using var archive = ZipFile.OpenRead(fixture.Plan.PackagePath);
        Assert.Contains(UpdatePackage.Validate(archive), item => item.Name == "NovaVMS.exe");
    }

    private sealed class Host(Action? wait = null) : IUpdateProcessHost
    {
        public int Starts { get; private set; }
        public Task WaitForExitAsync(string executable, CancellationToken token) { wait?.Invoke(); return Task.CompletedTask; }
        public void Start(string executable, string[] arguments)
        {
            using var gate = File.Open(Path.Combine(Path.GetDirectoryName(executable)!, ".novavms-update.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Starts++;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "NovaVMS-update-test-" + Guid.NewGuid().ToString("N"));
        public string Install => Path.Combine(_root, "install");
        public string Job => Path.Combine(_root, "job");
        public UpdatePlan Plan { get; }
        public Fixture(string? extra = null, string prefix = "", bool nativeExecutable = false)
        {
            Directory.CreateDirectory(Install);
            Directory.CreateDirectory(Job);
            if (nativeExecutable) File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), Path.Combine(Install, "NovaVMS.exe"));
            else File.WriteAllText(Path.Combine(Install, "NovaVMS.exe"), "old");
            var package = Path.Combine(Job, "package.zip");
            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                foreach (var name in new[] { "NovaVMS.exe", "NovaVMS.deps.json", "NovaVMS.runtimeconfig.json", "new-file.txt",
                    "updater/NovaVMS.Updater.exe", "updater/NovaVMS.Updater.dll", "updater/NovaVMS.Updater.deps.json", "updater/NovaVMS.Updater.runtimeconfig.json" })
                {
                    if (nativeExecutable && name == "NovaVMS.exe")
                    {
                        archive.CreateEntryFromFile(Path.Combine(Environment.SystemDirectory, "cmd.exe"), prefix + name);
                        continue;
                    }
                    using var writer = new StreamWriter(archive.CreateEntry(prefix + name).Open());
                    writer.Write("new");
                }
                archive.CreateEntryFromFile(typeof(ApplicationVersion).Assembly.Location, prefix + "NovaVMS.dll");
                if (extra is not null) { using var writer = new StreamWriter(archive.CreateEntry(prefix + extra).Open()); writer.Write("test"); }
            }
            Plan = new UpdatePlan { InstallDirectory = Install, JobDirectory = Job, PackagePath = package,
                PackageSha256 = UpdatePackage.Sha256(package), Version = ApplicationVersion.Number };
        }
        public void Dispose() { foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.Delete(path); Directory.Delete(_root, true); }
    }
}
