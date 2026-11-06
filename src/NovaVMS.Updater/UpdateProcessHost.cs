using System.Diagnostics;

namespace NovaVMS.Updater;

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
                        if (process.Id == Environment.ProcessId || process.HasExited)
                        {
                            continue;
                        }

                        // Fail closed if a same-name process cannot be inspected.
                        var path = process.MainModule?.FileName ?? throw new IOException("Cannot identify a running NovaVMS process.");

                        if (!string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        running = true;

                        process.CloseMainWindow();
                    }
                    catch (InvalidOperationException) when (process.HasExited)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception) when (process.HasExited)
                    {
                    }
                }
            }

            if (!running)
            {
                return; // The process has exited, including all its threads.
            }

            await Task.Delay(250, token).ConfigureAwait(false);
        }
    }

    public void Start(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("NovaVMS could not be restarted.");
    }
}
