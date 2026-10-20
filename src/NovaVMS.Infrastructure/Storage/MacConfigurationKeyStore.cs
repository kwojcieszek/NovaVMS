using System.Runtime.InteropServices;

namespace NovaVMS.Infrastructure.Storage;

/// <summary>Uses the user's login Keychain without passing secrets to a command-line process.</summary>
internal sealed class MacConfigurationKeyStore : IConfigurationKeyStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private static readonly Lazy<nint> SecurityLibrary = new(() => NativeLibrary.Load(Security));
    private static readonly Lazy<nint> CoreLibrary = new(() => NativeLibrary.Load(CoreFoundation));

    public byte[]? ReadKey(string id)
    {
        using var query = new Query(id);
        query.SetConstant("kSecReturnData", Marshal.ReadIntPtr(NativeLibrary.GetExport(CoreLibrary.Value, "kCFBooleanTrue")));
        var status = SecItemCopyMatching(query.Handle, out var result);
        try
        {
            if (status == -25300) return null; // errSecItemNotFound
            Check(status);
            if (result == 0 || CFDataGetLength(result) != 32)
                throw new IOException("Keychain contains an invalid configuration key.");
            var key = new byte[32];
            Marshal.Copy(CFDataGetBytePtr(result), key, 0, key.Length);
            return key;
        }
        finally { if (result != 0) CFRelease(result); }
    }

    public void CreateKey(string id, byte[] key)
    {
        using var query = new Query(id);
        var data = CFDataCreate(0, key, key.Length);
        if (data == 0) throw new IOException("Unable to allocate the Keychain credential.");
        try
        {
            query.SetConstant("kSecValueData", data);
            // Add only: an existing key must never be replaced.
            Check(SecItemAdd(query.Handle, 0));
        }
        finally { CFRelease(data); }
    }

    private static void Check(int status)
    {
        if (status != 0) throw new IOException($"Unable to access the macOS Keychain (status {status}). Unlock the login keychain and allow NovaVMS access.");
    }

    private sealed class Query : IDisposable
    {
        public nint Handle { get; }
        public Query(string id)
        {
            Handle = CFDictionaryCreateMutable(0, 0,
                NativeLibrary.GetExport(CoreLibrary.Value, "kCFTypeDictionaryKeyCallBacks"),
                NativeLibrary.GetExport(CoreLibrary.Value, "kCFTypeDictionaryValueCallBacks"));
            if (Handle == 0) throw new IOException("Unable to allocate a Keychain query.");
            try
            {
                SetConstant("kSecClass", Constant("kSecClassGenericPassword"));
                SetString("kSecAttrService", "org.novavms.Configuration");
                SetString("kSecAttrAccount", id);
            }
            catch { Dispose(); throw; }
        }

        public void SetConstant(string name, nint value) => CFDictionarySetValue(Handle, Constant(name), value);
        private static nint Constant(string name) => Marshal.ReadIntPtr(NativeLibrary.GetExport(SecurityLibrary.Value, name));
        private void SetString(string name, string value)
        {
            var text = CFStringCreateWithCString(0, value, 0x08000100); // kCFStringEncodingUTF8
            if (text == 0) throw new IOException("Unable to allocate a Keychain attribute.");
            try { SetConstant(name, text); }
            finally { CFRelease(text); }
        }
        public void Dispose() => CFRelease(Handle);
    }

    [DllImport(Security)] private static extern int SecItemCopyMatching(nint query, out nint result);
    [DllImport(Security)] private static extern int SecItemAdd(nint attributes, nint result);
    [DllImport(CoreFoundation)] private static extern nint CFDictionaryCreateMutable(nint allocator, nint capacity, nint keyCallbacks, nint valueCallbacks);
    [DllImport(CoreFoundation)] private static extern void CFDictionarySetValue(nint dictionary, nint key, nint value);
    [DllImport(CoreFoundation)] private static extern nint CFStringCreateWithCString(nint allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, uint encoding);
    [DllImport(CoreFoundation)] private static extern nint CFDataCreate(nint allocator, byte[] bytes, nint length);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetLength(nint data);
    [DllImport(CoreFoundation)] private static extern nint CFDataGetBytePtr(nint data);
    [DllImport(CoreFoundation)] private static extern void CFRelease(nint value);
}
