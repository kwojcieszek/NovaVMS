using System.Runtime.InteropServices;
using System.Text.Json;

namespace NovaVMS.Updater;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        var planFile = Path.GetFullPath(args[0]);
        try
        {
            var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planFile)) ?? throw new InvalidDataException("Missing update plan.");
            new UpdateInstaller(new UpdateProcessHost()).ApplyAsync(plan).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(plan.JobDirectory, "result.txt"), "OK");
            return 0;
        }
        catch (Exception exception)
        {
            var log = Path.Combine(Path.GetDirectoryName(planFile)!, "update-error.log");
            try { File.WriteAllText(log, exception.ToString()); } catch { }
            if (OperatingSystem.IsWindows())
                MessageBox(IntPtr.Zero, $"Aktualizacja NovaVMS nie została ukończona. / NovaVMS update failed.\n\n{exception.Message}\n\n{log}", "NovaVMS", 0x10);
            return 1;
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
}
