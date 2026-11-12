using System.Runtime.InteropServices;
using System.Text;

namespace TradeMonitor.ServiceInterface;

public interface ICoinbaseCredentialSource
{
    bool IsSupported { get; }

    CoinbaseApiCredential? Read();
}

public interface IWindowsCredentialStore
{
    bool IsSupported { get; }

    WindowsCredentialRecord? Read(string target);
}

/// <summary>
/// Key material. ToString is redacted so a log of the object does not print the secret.
/// </summary>
public sealed class CoinbaseApiCredential
{
    public CoinbaseApiCredential(string keyName, string privateKey)
    {
        KeyName = keyName;
        PrivateKey = privateKey;
    }

    public string KeyName { get; }

    public string PrivateKey { get; }

    public override string ToString() => "CoinbaseApiCredential";
}

public sealed class WindowsCredentialRecord
{
    public WindowsCredentialRecord(string userName, string password)
    {
        UserName = userName;
        Password = password;
    }

    public string UserName { get; }

    public string Password { get; }

    public override string ToString() => "WindowsCredentialRecord";
}

/// <summary>
/// keyring WinVaultKeyring stores the first secret on the service target and, once a second
/// username is written for the same service, moves the previous one to username@service.
/// Both forms are read. The service target is used only when its UserName matches the entry.
/// </summary>
public sealed class WinVaultCoinbaseCredentialSource(IWindowsCredentialStore store) : ICoinbaseCredentialSource
{
    public const string Service = "WalletSync-Coinbase";
    public const string NameEntry = "api_key_name";
    public const string SecretEntry = "api_private_key";

    public bool IsSupported => store.IsSupported;

    public CoinbaseApiCredential? Read()
    {
        var secret = ReadEntry(SecretEntry);
        var name = ReadEntry(NameEntry);
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(name))
            return null;
        return new CoinbaseApiCredential(name.Trim(), secret.Trim());
    }

    string? ReadEntry(string entry)
    {
        var direct = store.Read(Service);
        if (direct != null && string.Equals(direct.UserName, entry, StringComparison.Ordinal))
            return direct.Password;
        return store.Read(entry + "@" + Service)?.Password;
    }
}

public static class CoinbaseSyncGate
{
    public static bool ShouldRun(bool enabled, bool testingEnvironment, ICoinbaseCredentialSource source)
    {
        if (!enabled || !source.IsSupported)
            return false;
        if (testingEnvironment && source is WinVaultCoinbaseCredentialSource)
            return false;
        return true;
    }
}

public static class CredentialBlobText
{
    public static string Decode(byte[] blob)
    {
        if (blob.Length == 0)
            return "";
        if (blob.Length >= 2 && blob[0] == 0xFF && blob[1] == 0xFE)
            return Encoding.Unicode.GetString(blob, 2, blob.Length - 2).TrimEnd('\0');
        if (blob.Length % 2 == 0 && LooksLikeUtf16LeAscii(blob))
            return Encoding.Unicode.GetString(blob).TrimEnd('\0');
        return Encoding.UTF8.GetString(blob).TrimEnd('\0');
    }

    static bool LooksLikeUtf16LeAscii(byte[] blob)
    {
        for (var i = 1; i < blob.Length; i += 2)
        {
            if (blob[i] != 0)
                return false;
        }
        return true;
    }
}

public sealed class WindowsCredentialManagerStore : IWindowsCredentialStore
{
    const int Generic = 1;
    const int NotFound = 1168;

    public bool IsSupported => OperatingSystem.IsWindows();

    public WindowsCredentialRecord? Read(string target)
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("Windows Credential Manager is only available on Windows.");
        if (!CredRead(target, Generic, 0, out var pointer))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == NotFound)
                return null;
            throw new InvalidOperationException("CredRead failed with Win32 error " + error + ".");
        }

        try
        {
            var native = Marshal.PtrToStructure<NativeCredential>(pointer);
            var size = native.CredentialBlobSize;
            var blob = size > 0 ? new byte[size] : [];
            if (size > 0 && native.CredentialBlob != IntPtr.Zero)
                Marshal.Copy(native.CredentialBlob, blob, 0, size);
            var user = Marshal.PtrToStringUni(native.UserName) ?? "";
            return new WindowsCredentialRecord(user, CredentialBlobText.Decode(blob));
        }
        finally
        {
            CredFree(pointer);
        }
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern void CredFree(IntPtr credential);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NativeCredential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }
}
