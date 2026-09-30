using System.Security.Cryptography;
using System.Text;

namespace GuardWatch.Mcp;

public sealed class TokenStore(string path)
{
    public static TokenStore CreateDefault() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GuardWatch",
        "Mcp",
        "token.bin"));

    public string? TryRead()
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string token)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, protectedBytes);
    }

    public void Clear()
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
