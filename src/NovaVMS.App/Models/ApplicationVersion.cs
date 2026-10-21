using System.Reflection;

namespace NovaVMS.App.Models;

public static class ApplicationVersion
{
    public static string Number { get; } = typeof(ApplicationVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

    public static string DisplayName => $"NovaVMS {Number}";
}
