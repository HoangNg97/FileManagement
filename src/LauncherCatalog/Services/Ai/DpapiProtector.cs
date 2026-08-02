using System;
using System.Security.Cryptography;
using System.Text;

namespace LauncherCatalog.Services.Ai;

/// <summary>
/// Encrypts small secrets (the AI Bridge bearer token) at rest using Windows DPAPI bound to the
/// current user account. Stored values are tagged with a <see cref="Prefix"/> so legacy Stage 1
/// plaintext tokens can be detected and transparently re-encrypted on next save.
/// </summary>
public static class DpapiProtector
{
    private const string Prefix = "DPAPI:";

    /// <summary>Encrypts <paramref name="plaintext"/> and returns a prefixed, base64-encoded blob.</summary>
    public static string Protect(string plaintext)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plaintext), optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(protectedBytes);
    }

    /// <summary>
    /// Recovers the plaintext from a stored value. Returns <c>true</c> on success. A value without
    /// the DPAPI prefix is treated as a legacy plaintext token (returned as-is); set
    /// <paramref name="wasEncrypted"/> to know whether a re-encryption save is warranted.
    /// </summary>
    public static bool TryUnprotect(string? stored, out string plaintext, out bool wasEncrypted)
    {
        plaintext = string.Empty;
        wasEncrypted = false;

        if (string.IsNullOrEmpty(stored))
        {
            return false;
        }

        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
        {
            // Legacy Stage 1 token stored in the clear.
            plaintext = stored;
            return true;
        }

        try
        {
            var cipher = Convert.FromBase64String(stored[Prefix.Length..]);
            var bytes = ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(bytes);
            wasEncrypted = true;
            return true;
        }
        catch (Exception)
        {
            // Corrupt or undecryptable (e.g. copied from another machine/user); caller regenerates.
            return false;
        }
    }
}
