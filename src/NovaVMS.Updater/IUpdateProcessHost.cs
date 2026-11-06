namespace NovaVMS.Updater;

public interface IUpdateProcessHost
{
    Task WaitForExitAsync(string executable, CancellationToken token);
    void Start(string executable, string[] arguments);
}
