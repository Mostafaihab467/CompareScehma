using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SchemaCompare.Models;

namespace SchemaCompare.Services;

/// <summary>
/// Persists saved DB credential profiles to %AppData%/SchemaCompare/saved_connections.json.
/// Passwords are encrypted with Windows DPAPI (CurrentUser scope) so they can only be
/// decrypted by the same Windows user on this machine. When a secret cannot be protected
/// it is not saved at all — never written as plaintext; see <see cref="LastWarning"/>.
/// </summary>
public sealed class SavedConnectionsService
{
    private static readonly string StoreDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SchemaCompare");

    private static readonly string StorePath = Path.Combine(StoreDir, "saved_connections.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath => StorePath;

    /// <summary>
    /// Set by the last Load/Save when secrets could not be protected (or were stored
    /// unencrypted by an older build). The UI surfaces this instead of failing quietly.
    /// </summary>
    public string? LastWarning { get; private set; }

    public List<SavedConnection> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
                return [];
            var json = File.ReadAllText(StorePath);
            var dtos = JsonSerializer.Deserialize<List<SavedConnectionDto>>(json, JsonOptions) ?? [];
            LastWarning = null;
            var list = dtos.Select(d => new SavedConnection
            {
                Id = string.IsNullOrWhiteSpace(d.Id) ? Guid.NewGuid().ToString("N") : d.Id,
                Name = d.Name ?? string.Empty,
                Server = d.Server ?? string.Empty,
                Database = d.Database ?? string.Empty,
                UseWindowsAuth = d.UseWindowsAuth,
                Username = d.Username ?? string.Empty,
                Password = Decrypt(d.EncryptedPassword ?? string.Empty),
                EncryptConnection = d.EncryptConnection,
                TrustServerCertificate = d.TrustServerCertificate ?? true,
            }).ToList();
            if (_sawLegacyPlaintext)
                LastWarning = "saved_connections.json holds password(s) written unencrypted by an older version. " +
                              "Re-enter and re-save them so they are protected.";
            return list;
        }
        catch
        {
            // A corrupt store must never crash the app — start with an empty list.
            return [];
        }
    }

    public void Save(IEnumerable<SavedConnection> connections)
    {
        Directory.CreateDirectory(StoreDir);
        var unprotectable = 0;
        var dtos = connections.Select(c =>
        {
            var secret = Protect(c.Password ?? string.Empty);
            if (secret == null)
            {
                // Fail closed: never write a password this machine cannot protect.
                unprotectable++;
                secret = string.Empty;
            }
            return new SavedConnectionDto
            {
                Id = c.Id,
                Name = string.IsNullOrWhiteSpace(c.Name) ? c.BuildDefaultName() : c.Name,
                Server = c.Server,
                Database = c.Database,
                UseWindowsAuth = c.UseWindowsAuth,
                Username = c.Username,
                EncryptedPassword = secret,
                EncryptConnection = c.EncryptConnection,
                TrustServerCertificate = c.TrustServerCertificate,
            };
        }).ToList();
        var json = JsonSerializer.Serialize(dtos, JsonOptions);
        File.WriteAllText(StorePath, json, Encoding.UTF8);
        LastWarning = unprotectable > 0
            ? $"{unprotectable} password(s) could not be encrypted on this machine (Windows DPAPI unavailable), " +
              "so they were NOT saved — re-enter them when connecting."
            : null;
    }

    /// <summary>DPAPI-protected Base64, or null when the secret cannot be protected.</summary>
    private static string? Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return string.Empty;
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return Convert.ToBase64String(DpapiHelper.Protect(Encoding.UTF8.GetBytes(plainText)));
        }
        catch
        {
            return null;
        }
    }

    private bool _sawLegacyPlaintext;

    private string Decrypt(string stored)
    {
        if (string.IsNullOrEmpty(stored))
            return string.Empty;
        try
        {
            var bytes = Convert.FromBase64String(stored);
            if (OperatingSystem.IsWindows())
            {
                try { return Encoding.UTF8.GetString(DpapiHelper.Unprotect(bytes)); }
                catch { /* may be plain Base64 written by an older version — decode below */ }
            }
            _sawLegacyPlaintext = true;
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private sealed class SavedConnectionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Server { get; set; } = string.Empty;
        public string Database { get; set; } = string.Empty;
        public bool UseWindowsAuth { get; set; } = true;
        public string Username { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
        public bool EncryptConnection { get; set; }
        public bool? TrustServerCertificate { get; set; }
    }

    /// <summary>Minimal DPAPI wrapper (Crypt32) — no NuGet dependency required.</summary>
    [SupportedOSPlatform("windows")]
    private static class DpapiHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptProtectData(
            ref DATA_BLOB pDataIn, string? szDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved,
            IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn, IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy, IntPtr pvReserved,
            IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);

        public static byte[] Protect(byte[] plain)
        {
            var inBlob = ToBlob(plain);
            try
            {
                if (!CryptProtectData(ref inBlob, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outBlob))
                    throw new InvalidOperationException("DPAPI protect failed.");
                try { return FromBlob(outBlob); }
                finally { LocalFree(outBlob.pbData); }
            }
            finally { Marshal.FreeHGlobal(inBlob.pbData); }
        }

        public static byte[] Unprotect(byte[] cipher)
        {
            var inBlob = ToBlob(cipher);
            try
            {
                if (!CryptUnprotectData(ref inBlob, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, out var outBlob))
                    throw new InvalidOperationException("DPAPI unprotect failed.");
                try { return FromBlob(outBlob); }
                finally { LocalFree(outBlob.pbData); }
            }
            finally { Marshal.FreeHGlobal(inBlob.pbData); }
        }

        private static DATA_BLOB ToBlob(byte[] data)
        {
            var blob = new DATA_BLOB { cbData = data.Length, pbData = Marshal.AllocHGlobal(data.Length) };
            Marshal.Copy(data, 0, blob.pbData, data.Length);
            return blob;
        }

        private static byte[] FromBlob(DATA_BLOB blob)
        {
            var result = new byte[blob.cbData];
            Marshal.Copy(blob.pbData, result, 0, blob.cbData);
            return result;
        }
    }
}
