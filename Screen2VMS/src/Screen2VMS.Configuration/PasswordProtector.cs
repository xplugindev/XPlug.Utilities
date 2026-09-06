using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Screen2VMS.Configuration;

/// <summary>
/// Protects the ONVIF password at rest (spec 56).
/// </summary>
/// <remarks>
/// <para>
/// DPAPI with the local-machine scope, so the streaming engine can read the
/// password when it eventually runs as a service under a different account
/// (spec 58). That means anyone who can run code on this machine can recover
/// it - acceptable for a LAN camera credential, and the honest alternative to
/// pretending a plaintext file is secure.
/// </para>
/// <para>
/// The point is narrow: keep the password out of a config file that gets
/// copied around or committed by accident.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class PasswordProtector
{
    /// <summary>Ties the ciphertext to this application, so an unrelated blob will not decrypt.</summary>
    private static readonly byte[] Entropy = "Screen2VMS.OnvifCredential.v1"u8.ToArray();

    /// <summary>Encrypts a password for storage, returning base64.</summary>
    public static string Protect(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password),
            Entropy,
            DataProtectionScope.LocalMachine);

        return Convert.ToBase64String(encrypted);
    }

    /// <summary>
    /// Decrypts a stored password, or returns null if the blob is missing or
    /// cannot be read on this machine.
    /// </summary>
    /// <remarks>
    /// A config file copied from another machine will fail to decrypt. That is
    /// treated as "no password set" rather than an error, so the user is asked
    /// for a new one instead of being locked out.
    /// </remarks>
    public static string? Unprotect(string? protectedPassword)
    {
        if (string.IsNullOrWhiteSpace(protectedPassword))
        {
            return null;
        }

        try
        {
            var decrypted = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedPassword),
                Entropy,
                DataProtectionScope.LocalMachine);

            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a strong password for first run.
    /// </summary>
    /// <remarks>
    /// Spec 24 forbids shipping a universal default. A per-install random
    /// password means every deployment differs, and the user is shown it so
    /// they can enter it into the VMS.
    /// </remarks>
    public static string GeneratePassword()
    {
        // Ambiguous characters are left out: this gets typed into a VMS dialog
        // by a person reading it off a screen.
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        const int length = 16;

        var builder = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            builder.Append(alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]);
        }

        return builder.ToString();
    }
}
