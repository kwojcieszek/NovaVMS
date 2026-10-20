using System.Runtime.InteropServices;

namespace NovaVMS.Infrastructure.Storage;

/// <summary>Stores the configuration key in the desktop Secret Service using libsecret.</summary>
internal sealed class LinuxConfigurationKeyStore : IConfigurationKeyStore
{
    private const string Secret = "libsecret-1.so.0";
    private const string Glib = "libglib-2.0.so.0";
    private static readonly Lazy<nint> GlibLibrary = new(() => NativeLibrary.Load(Glib));

    public byte[]? ReadKey(string id) => WithService(() =>
    {
        using var query = new Query(id);
        var password = secret_password_lookupv_sync(query.Schema, query.Attributes, 0, out var error);
        try
        {
            Check(error);
            if (password == 0) return null;
            try { return Convert.FromBase64String(Marshal.PtrToStringUTF8(password)!); }
            catch (FormatException) { throw new IOException("Secret Service contains an invalid configuration key."); }
        }
        finally { if (password != 0) secret_password_free(password); }
    });

    public void CreateKey(string id, byte[] key) => WithService(() =>
    {
        using var query = new Query(id);
        // The identifier is random and unique to this configuration; other files keep their own keys.
        var success = secret_password_storev_sync(query.Schema, query.Attributes, "default", "NovaVMS settings",
            Convert.ToBase64String(key), 0, out var error);
        Check(error);
        if (success == 0) throw new IOException("Secret Service did not save the configuration key.");
        return true;
    });

    private static T WithService<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            throw new IOException("Configuration encryption requires libsecret-1 and an unlocked Secret Service (GNOME Keyring or compatible service) in the desktop D-Bus session.", exception);
        }
    }

    private static void Check(nint error)
    {
        if (error == 0) return;
        // Native error text is deliberately excluded: credentials must not enter exception logs.
        g_error_free(error);
        throw new IOException("Unable to access Secret Service. Unlock the desktop keyring and allow NovaVMS access.");
    }

    private sealed class Query : IDisposable
    {
        public nint Schema { get; private set; }
        public nint Attributes { get; private set; }
        private readonly nint _attribute = Marshal.StringToCoTaskMemUTF8("key-id");
        private nint _id;

        public Query(string id)
        {
            nint types = 0;
            try
            {
                _id = Marshal.StringToCoTaskMemUTF8(id);
                types = NewTable();
                g_hash_table_insert(types, _attribute, 0); // SECRET_SCHEMA_ATTRIBUTE_STRING
                Schema = secret_schema_newv("org.novavms.Configuration", 0, types);
                if (Schema == 0) throw new IOException("Unable to allocate the Secret Service schema.");
                Attributes = NewTable();
                g_hash_table_insert(Attributes, _attribute, _id);
            }
            catch { Dispose(); throw; }
            finally { if (types != 0) g_hash_table_unref(types); }
        }

        private static nint NewTable()
        {
            var table = g_hash_table_new(NativeLibrary.GetExport(GlibLibrary.Value, "g_str_hash"),
                NativeLibrary.GetExport(GlibLibrary.Value, "g_str_equal"));
            return table != 0 ? table : throw new IOException("Unable to allocate Secret Service attributes.");
        }

        public void Dispose()
        {
            if (Attributes != 0) g_hash_table_unref(Attributes);
            if (Schema != 0) secret_schema_unref(Schema);
            Marshal.FreeCoTaskMem(_id);
            Marshal.FreeCoTaskMem(_attribute);
        }
    }

    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl)] private static extern nint secret_schema_newv([MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags, nint types);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl)] private static extern void secret_schema_unref(nint schema);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl)] private static extern nint secret_password_lookupv_sync(nint schema, nint attributes, nint cancellable, out nint error);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl)] private static extern int secret_password_storev_sync(nint schema, nint attributes,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string collection, [MarshalAs(UnmanagedType.LPUTF8Str)] string label,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string password, nint cancellable, out nint error);
    [DllImport(Secret, CallingConvention = CallingConvention.Cdecl)] private static extern void secret_password_free(nint password);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] private static extern nint g_hash_table_new(nint hash, nint equal);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] private static extern int g_hash_table_insert(nint table, nint key, nint value);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] private static extern void g_hash_table_unref(nint table);
    [DllImport(Glib, CallingConvention = CallingConvention.Cdecl)] private static extern void g_error_free(nint error);
}
