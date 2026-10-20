using System.Reflection;
using System.Runtime.InteropServices;
using NovaVMS.Core.Exceptions;
using NovaVMS.Protocols.Resources;

namespace NovaVMS.Protocols.HcNetSdk;

/// <summary>
/// Loads and initializes the Hikvision Device Network SDK (HCNetSDK) from a user-selected folder.
/// The library is proprietary and is not distributed with this application; Hikvision provides it for
/// Windows and Linux at hikvision.com (Support → Download → SDK → Device Network SDK).
/// </summary>
public static class HcNetSdkRuntime
{
    private const string DefaultFolderName = "hcnetsdk";

    // NET_DVR_LOCAL_SDK_PATH: char sPath[256] followed by 128 reserved bytes.
    private const int SdkPathLength = 256;
    private const int SdkPathStructSize = 384;

    private static readonly object SyncRoot = new();
    private static IntPtr _libraryHandle;
    private static bool _isInitialized;

    /// <summary>
    /// Folder with the SDK libraries; empty selects the "hcnetsdk" folder next to the application.
    /// Takes effect when the SDK is first used.
    /// </summary>
    public static string LibraryDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Folder the SDK was loaded from, or null when it has not been loaded in this session.
    /// </summary>
    public static string? LoadedDirectory { get; private set; }

    public static bool IsPlatformSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static string ResolveDirectory(string configuredDirectory)
    {
        return string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(AppContext.BaseDirectory, DefaultFolderName)
            : configuredDirectory.Trim();
    }

    /// <summary>
    /// Path of the main SDK library in the folder, or null when it is missing or the platform is not supported.
    /// </summary>
    public static string? FindLibrary(string configuredDirectory)
    {
        if (!IsPlatformSupported)
        {
            return null;
        }

        var path = Path.Combine(ResolveDirectory(configuredDirectory), LibraryFileName);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// SDK build version ("6.1.9.48"), or null when the SDK has not been loaded.
    /// </summary>
    public static string? LoadedVersion
    {
        get
        {
            if (!_isInitialized)
            {
                return null;
            }

            var version = HcNetSdkNative.GetSdkBuildVersion();
            return $"{version >> 24}.{(version >> 16) & 0xFF}.{(version >> 8) & 0xFF}.{version & 0xFF}";
        }
    }

    private static string LibraryFileName => OperatingSystem.IsWindows() ? "HCNetSDK.dll" : "libhcnetsdk.so";

    /// <summary>
    /// Stops all streams, logs out of all devices and releases the SDK.
    /// </summary>
    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            if (!_isInitialized)
            {
                return;
            }

            HcNetSdkVideoStream.StopAll();
            HikvisionRtspVideoStream.StopAll();
            HcNetSdkSessionPool.LogoutAll();
            HcNetSdkNative.Cleanup();
            _isInitialized = false;
        }
    }

    internal static void EnsureInitialized()
    {
        lock (SyncRoot)
        {
            if (_isInitialized)
            {
                return;
            }

            if (!IsPlatformSupported)
            {
                throw new NotSupportedException(ProtocolText.Get("SdkUnsupportedPlatform"));
            }

            var directory = ResolveDirectory(LibraryDirectory);
            var libraryPath = Path.Combine(directory, LibraryFileName);
            if (!File.Exists(libraryPath))
            {
                throw new DeviceCommunicationException(ProtocolText.Format("SdkNotFound", directory));
            }

            LoadLibrary(libraryPath);
            ConfigureComponentPaths(directory);

            if (!HcNetSdkNative.Init())
            {
                throw new DeviceCommunicationException(ProtocolText.Format("SdkInitFailed", HcNetSdkNative.GetLastError()));
            }

            HcNetSdkNative.SetConnectTime(3000, 1);
            HcNetSdkNative.SetReconnect(10000, true);
            LoadedDirectory = directory;
            _isInitialized = true;
        }
    }

    private static void LoadLibrary(string libraryPath)
    {
        if (_libraryHandle != IntPtr.Zero)
        {
            return;
        }

        try
        {
            // An absolute path makes the loader find the dependent libraries (HCCore, ...) in the same folder.
            _libraryHandle = NativeLibrary.Load(libraryPath);
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
        {
            throw new DeviceCommunicationException(ProtocolText.Format("SdkLoadFailed", exception.Message), exception);
        }

        NativeLibrary.SetDllImportResolver(typeof(HcNetSdkRuntime).Assembly, ResolveImport);
    }

    private static IntPtr ResolveImport(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        return libraryName == HcNetSdkConstants.LibraryName ? _libraryHandle : IntPtr.Zero;
    }

    /// <summary>
    /// The SDK loads its components (HCNetSDKCom folder) and, on Linux, OpenSSL from paths set before initialization.
    /// </summary>
    private static void ConfigureComponentPaths(string directory)
    {
        SetPath(HcNetSdkConstants.InitConfigSdkPath, directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);

        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var crypto = Path.Combine(directory, "libcrypto.so.1.1");
        if (File.Exists(crypto))
        {
            SetPath(HcNetSdkConstants.InitConfigCryptoLibraryPath, crypto);
        }

        var ssl = Path.Combine(directory, "libssl.so.1.1");
        if (File.Exists(ssl))
        {
            SetPath(HcNetSdkConstants.InitConfigSslLibraryPath, ssl);
        }
    }

    private static void SetPath(int configType, string path)
    {
        var buffer = Marshal.AllocHGlobal(SdkPathStructSize);
        var text = Marshal.StringToHGlobalAnsi(path);
        try
        {
            var zeros = new byte[SdkPathStructSize];
            Marshal.Copy(zeros, 0, buffer, zeros.Length);

            var length = 0;
            while (length < SdkPathLength - 1 && Marshal.ReadByte(text, length) != 0)
            {
                length++;
            }

            var bytes = new byte[length];
            Marshal.Copy(text, bytes, 0, length);
            Marshal.Copy(bytes, 0, buffer, length);

            HcNetSdkNative.SetSdkInitConfig(configType, buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
            Marshal.FreeHGlobal(buffer);
        }
    }
}
