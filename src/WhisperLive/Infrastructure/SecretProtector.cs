using System;
using System.Security.Cryptography;
using System.Text;

namespace WhisperLive.Infrastructure;

/// <summary>
/// Encrypts/decrypts small secrets (API keys) with Windows DPAPI, scoped to the current user.
/// Ciphertext is stored base64 with a <c>dpapi:</c> marker in settings.json, so a leaked settings
/// file — or a prompt-injection read of it — yields no usable key. Values without the marker are
/// treated as legacy cleartext and returned as-is on read (they get re-protected on the next save).
/// </summary>
internal static class SecretProtector
{
    private const string Prefix = "dpapi:";

    public static string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext)) return string.Empty;
        if (plaintext.StartsWith(Prefix, StringComparison.Ordinal)) return plaintext; // already protected
        try
        {
            var bytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(plaintext), null, DataProtectionScope.CurrentUser);
            return Prefix + Convert.ToBase64String(bytes);
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Could not DPAPI-protect secret — dropping it to avoid cleartext leak");
            return string.Empty;
        }
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal)) return stored; // legacy cleartext
        try
        {
            var bytes = Convert.FromBase64String(stored[Prefix.Length..]);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex)
        {
            AppLogger.Warning(ex, "Could not DPAPI-unprotect secret");
            return string.Empty;
        }
    }
}
