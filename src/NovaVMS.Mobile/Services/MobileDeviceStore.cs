using System.Security.Cryptography;
using System.Text.Json;
using NovaVMS.Core.Models;

namespace NovaVMS.Mobile.Services;

/// <summary>
/// Encrypted device list of earlier mobile versions; a failed write never replaces the previous file.
/// Read once and moved into the shared configuration format.
/// </summary>
public sealed class MobileDeviceStore(string directory, IDataProtection protection)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path = Path.Combine(directory, "devices.enc");

    public bool Exists => File.Exists(_path);

    public async Task<List<Device>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            var encrypted = await File.ReadAllBytesAsync(_path, cancellationToken);
            var plain = protection.Unprotect(encrypted);

            try
            {
                return JsonSerializer.Deserialize(plain, MobileJsonContext.Default.ListDevice) ?? [];
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Delete()
    {
        File.Delete(_path);
    }

    public async Task SaveAsync(List<Device> devices, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        var temporary = _path + ".tmp";

        try
        {
            Directory.CreateDirectory(directory);

            var plain = JsonSerializer.SerializeToUtf8Bytes(devices, MobileJsonContext.Default.ListDevice);
            byte[] encrypted;

            try
            {
                encrypted = protection.Protect(plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plain);
            }

            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
    }
}
