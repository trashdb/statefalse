using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Statefalse.Application;

namespace Statefalse.Infrastructure;

public sealed class GitHubCredentialProtector : IGitHubCredentialProtector
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _activeKeyId;
    private readonly byte[] _activeKey;
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    public GitHubCredentialProtector(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var legacyKey = configuration["GitHubCredentials:EncryptionKey"];
        var activeKey = configuration["GitHubCredentials:ActiveKey"] ?? legacyKey;
        if (string.IsNullOrWhiteSpace(activeKey))
        {
            if (environment.IsProduction())
                throw new InvalidOperationException("GitHubCredentials:ActiveKey must be configured in production.");
            activeKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySize));
        }

        _activeKeyId = configuration["GitHubCredentials:ActiveKeyId"] ?? "v1";
        _activeKey = ParseKey(activeKey, "GitHubCredentials:ActiveKey");
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal) { [_activeKeyId] = _activeKey };
        var previousId = configuration["GitHubCredentials:PreviousKeyId"];
        var previous = configuration["GitHubCredentials:PreviousKey"];
        if (!string.IsNullOrWhiteSpace(previousId) || !string.IsNullOrWhiteSpace(previous))
        {
            if (string.IsNullOrWhiteSpace(previousId) || string.IsNullOrWhiteSpace(previous))
                throw new InvalidOperationException("PreviousKeyId and PreviousKey must be configured together.");
            if (keys.ContainsKey(previousId))
                throw new InvalidOperationException("ActiveKeyId and PreviousKeyId must differ.");
            keys.Add(previousId, ParseKey(previous, "GitHubCredentials:PreviousKey"));
        }
        if (!string.IsNullOrWhiteSpace(legacyKey))
            keys.TryAdd("v1", ParseKey(legacyKey, "GitHubCredentials:EncryptionKey"));
        _keys = keys;
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrEmpty(plaintext);
        return Encrypt(plaintext, _activeKeyId, _activeKey);
    }

    public string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!IsProtected(value)) return value;
        var parts = value.Split('.');
        if (parts[0] == "v1")
        {
            if (parts.Length != 4 || !_keys.TryGetValue("v1", out var key))
                throw new CryptographicException("Invalid GitHub credential envelope.");
            return Decrypt(parts[1], parts[2], parts[3], key);
        }
        if (parts.Length != 5 || !_keys.TryGetValue(parts[1], out var v2Key))
            throw new CryptographicException("Invalid GitHub credential envelope.");
        return Decrypt(parts[2], parts[3], parts[4], v2Key);
    }

    public bool IsProtected(string? value) => value?.StartsWith("v1.", StringComparison.Ordinal) == true || value?.StartsWith("v2.", StringComparison.Ordinal) == true;

    public bool NeedsReEncryption(string? value) => !string.IsNullOrEmpty(value) && !value.StartsWith($"v2.{_activeKeyId}.", StringComparison.Ordinal);

    public string ReEncrypt(string value) => Protect(Unprotect(value) ?? throw new CryptographicException("Credential cannot be decrypted."));

    private static string Encrypt(string plaintext, string keyId, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var input = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[input.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, input, ciphertext, tag);
        return string.Join('.', "v2", keyId, Encode(nonce), Encode(tag), Encode(ciphertext));
    }

    private static string Decrypt(string noncePart, string tagPart, string ciphertextPart, byte[] key)
    {
        try
        {
            var nonce = Decode(noncePart); var tag = Decode(tagPart); var ciphertext = Decode(ciphertextPart);
            if (nonce.Length != NonceSize || tag.Length != TagSize) throw new CryptographicException("Invalid GitHub credential envelope.");
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (FormatException ex) { throw new CryptographicException("Invalid GitHub credential envelope.", ex); }
    }

    private static byte[] ParseKey(string value, string setting)
    {
        try
        {
            var key = value.Length == KeySize * 2 ? Convert.FromHexString(value) : Convert.FromBase64String(value);
            if (key.Length != KeySize) throw new InvalidOperationException($"{setting} must be a 256-bit key.");
            return key;
        }
        catch (FormatException ex) { throw new InvalidOperationException($"{setting} must be base64 or hexadecimal.", ex); }
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
}
