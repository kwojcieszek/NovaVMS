using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using NovaVMS.App.Views;
using NovaVMS.App.Localization;

namespace NovaVMS.App.Services;

/// <summary>
/// Avalonia implementation of <see cref="IDialogService"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    private static FilePickerFileType JsonFileType => new(Localizer.Text("FileType_Settings"))
    {
        Patterns = new[] { "*.json" },
        MimeTypes = new[] { "application/json" },
        AppleUniformTypeIdentifiers = new[] { "public.json" }
    };

    public async Task<string?> PromptTextAsync(string title, string message, string initialValue)
    {
        var owner = GetOwner();
        if (owner is null)
        {
            return null;
        }

        var dialog = new TextInputDialog(title, message, initialValue);
        return await dialog.ShowDialog<string?>(owner);
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        var owner = GetOwner();
        if (owner is null)
        {
            return false;
        }

        var dialog = new ConfirmDialog(title, message);
        return await dialog.ShowDialog<bool>(owner);
    }

    private static FilePickerFileType CsvFileType => new(Localizer.Text("FileType_Csv"))
    {
        Patterns = new[] { "*.csv", "*.txt" },
        MimeTypes = new[] { "text/csv", "text/plain" },
        AppleUniformTypeIdentifiers = new[] { "public.comma-separated-values-text", "public.plain-text" }
    };

    public Task<Stream?> OpenJsonFileAsync(string title)
    {
        return OpenFileAsync(title, JsonFileType);
    }

    public Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName)
    {
        return SaveFileAsync(title, suggestedFileName, "json", JsonFileType);
    }

    public Task<Stream?> OpenCsvFileAsync(string title)
    {
        return OpenFileAsync(title, CsvFileType);
    }

    public Task<Stream?> SaveCsvFileAsync(string title, string suggestedFileName)
    {
        return SaveFileAsync(title, suggestedFileName, "csv", CsvFileType);
    }

    public async Task<(Stream Content, Stream? Setup)?> OpenIvmsFileAsync(string title)
    {
        var owner = GetOwner();
        if (owner is null) return null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType(Localizer.Text("FileType_Ivms"))
                { Patterns = new[] { "*.zip", "Device", "Device.*" } }, FilePickerFileTypes.All }
        });
        if (files.Count == 0) return null;
        var file = files[0];
        if (file.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return (await file.OpenReadAsync(), null);

        // A raw database cannot be decrypted without its original setup file.
        Stream? setup = null;
        var localPath = file.TryGetLocalPath();
        var siblingPath = localPath is null ? null : Path.Combine(Path.GetDirectoryName(localPath)!, "DESetup.xml");
        if (siblingPath is not null && File.Exists(siblingPath))
            setup = File.OpenRead(siblingPath);
        else
        {
            var companions = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = Localizer.Text("Devices_IvmsSetupTitle"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("DESetup.xml") { Patterns = new[] { "*.xml" } } }
            });
            if (companions.Count == 0) return null;
            setup = await companions[0].OpenReadAsync();
        }
        try { return (await file.OpenReadAsync(), setup); }
        catch { await setup.DisposeAsync(); throw; }
    }

    public async Task<string?> PickFolderAsync(string title)
    {
        var owner = GetOwner();
        if (owner is null)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false
        });

        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    private static async Task<Stream?> OpenFileAsync(string title, FilePickerFileType fileType)
    {
        var owner = GetOwner();
        if (owner is null)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[] { fileType }
        });

        if (files.Count == 0)
        {
            return null;
        }

        return await files[0].OpenReadAsync();
    }

    private static async Task<Stream?> SaveFileAsync(string title, string suggestedFileName, string extension, FilePickerFileType fileType)
    {
        var owner = GetOwner();
        if (owner is null)
        {
            return null;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = new[] { fileType },
            ShowOverwritePrompt = true
        });

        if (file is null)
        {
            return null;
        }

        return await file.OpenWriteAsync();
    }

    private static Window? GetOwner()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.Windows.FirstOrDefault(window => window.IsActive) ?? desktop.MainWindow;
    }
}
