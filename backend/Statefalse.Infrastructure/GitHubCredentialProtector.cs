using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Statefalse.Application;

namespace Statefalse.Infrastructure;

/// <summary>
/// AES-256-GCM protection for recoverable GitHub credentials.
/// The key is supplied externally as base64 or 64-character hexadecimal text.
/// </summary>
public sealed class GitHubCredentialProtector : IGitHubCredentialProtector
{
    private const string Prefix = "v1.";
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public GitHubCredentialProtector(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["GitHubCredentials:EncryptionKey"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (environment.IsProduction())
                throw new InvalidOperationException("GitHubCredentials:EncryptionKey must be configured in production.");

            _key = RandomNumberGenerator.GetBytes(KeySize);
            return;
        }

        _key = ParseKey(configured);
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return string.Join('.', Prefix.TrimEnd('.'), Encode(nonce), Encode(tag), Encode(ciphertext));
    }

    public string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (!IsProtected(value))
            return value; // Compatibility window for legacy plaintext rows.

        var parts = value.Split('.');
        if (parts.Length != 4 || parts[0] != "v1")
            throw new CryptographicException("Invalid GitHub credential envelope.");

        try
        {
            var nonce = Decode(parts[1]);
            var tag = Decode(parts[2]);
            var ciphertext = Decode(parts[3]);
            if (nonce.Length != NonceSize || tag.Length != TagSize)
                throw new CryptographicException("Invalid GitHub credential envelope.");

            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Invalid GitHub credential envelope.", ex);
        }
    }

    public bool IsProtected(string? value)
        => value?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    private static byte[] ParseKey(string value)
    {
        try
        {
            var key = value.Length == KeySize * 2
                ? Convert.FromHexString(value)
                : Convert.FromBase64String(value);
            if (key.Length != KeySize)
                throw new InvalidOperationException("GitHubCredentials:EncryptionKey must be a 256-bit key.");
            return key;
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("GitHubCredentials:EncryptionKey must be base64 or hexadecimal.", ex);
        }
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes)
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }
}
