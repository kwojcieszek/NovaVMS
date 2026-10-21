using System.Diagnostics;

namespace NovaVMS.Updater;

public interface IUpdateProcessHost
{
    Task WaitForExitAsync(string executable, CancellationToken token);
    void Start(string executable, string[] arguments);
}

public sealed class UpdateProcessHost : IUpdateProcessHost
{
    public async Task WaitForExitAsync(string executable, CancellationToken token)
    {
        var expected = Path.GetFullPath(executable);
        while (true)
        {
            var running = false;
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == Environment.ProcessId || process.HasExited) continue;
                        // Fail closed if a same-name process cannot be inspected.
                        var path = process.MainModule?.FileName ?? throw new IOException("Cannot identify a running NovaVMS process.");
                        if (!string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase)) continue;
                        running = true;
                        process.CloseMainWindow();
                    }
                    catch (InvalidOperationException) when (process.HasExited) { }
                    catch (System.ComponentModel.Win32Exception) when (process.HasExited) { }
                }
            }
            if (!running) return; // The process has exited, including all its threads.
            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    public void Start(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("NovaVMS could not be restarted.");
    }
}

public sealed class UpdateInstaller(IUpdateProcessHost processes)
{
    public async Task ApplyAsync(UpdatePlan plan, CancellationToken token = default)
    {
        var install = Path.GetFullPath(plan.InstallDirectory);
        var job = Path.GetFullPath(plan.JobDirectory);
        if (plan.ExecutableName != "NovaVMS.exe" || install == Path.GetPathRoot(install)
            || job.StartsWith(install.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid update installation directory.");
        var executable = UpdatePackage.SafePath(install, plan.ExecutableName);
        var package = UpdatePackage.SafePath(job, "package.zip");
        if (!string.Equals(package, Path.GetFullPath(plan.PackagePath), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(UpdatePackage.Sha256(package), plan.PackageSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The downloaded update package has changed.");

        using var gate = new FileStream(Path.Combine(install, ".novavms-update.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        // The current application closes itself normally; other instances get the same close request.
        // Do not force-kill a process that may still be saving settings or exporting recordings.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        await processes.WaitForExitAsync(executable, timeout.Token).ConfigureAwait(false);

        var staging = UpdatePackage.SafePath(job, "payload");
        var backup = UpdatePackage.SafePath(job, "backup");
        var changed = new List<(string Target, string? Backup)>();
        var readyToRestart = false;
        var restored = false;
        try
        {
            // No extraction or application-file writes take place until every instance has exited.
            UpdatePackage.Extract(package, staging, plan.Version);
            foreach (var source in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(staging, source);
                var target = UpdatePackage.SafePath(install, relative);
                string? saved = null;
                if (File.Exists(target))
                {
                    saved = UpdatePackage.SafePath(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(target, saved, overwrite: false);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                changed.Add((target, saved));
                File.Copy(source, target, overwrite: true);
            }
            readyToRestart = true;
        }
        catch
        {
            // Restore every replaced file, including a partially written destination.
            foreach (var (target, saved) in changed.AsEnumerable().Reverse())
            {
                if (saved is not null) File.Copy(saved, target, overwrite: true);
                else if (File.Exists(target)) File.Delete(target);
            }
            restored = true;
            throw;
        }
        finally
        {
            // Release the startup gate before launching either the new or restored application.
            gate.Dispose();
            if (readyToRestart || restored)
                processes.Start(executable, plan.Arguments);
        }
    }
}
