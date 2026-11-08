using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NovaVMS.Mobile.Services;

namespace NovaVMS.Mobile.Views;

/// <summary>Settings files through the platform document picker (Android Storage Access Framework, iOS Files).</summary>
public sealed class StorageFileService(TopLevel topLevel) : IFileService
{
    private static readonly FilePickerFileType JsonFiles = new("JSON")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"],
        AppleUniformTypeIdentifiers = ["public.json"]
    };

    public async Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName)
    {
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [JsonFiles]
        });

        return file is null ? null : await file.OpenWriteAsync();
    }

    public async Task<Stream?> OpenJsonFileAsync(string title)
    {
        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [JsonFiles]
        });

        return files.Count == 0 ? null : await files[0].OpenReadAsync();
    }
}
