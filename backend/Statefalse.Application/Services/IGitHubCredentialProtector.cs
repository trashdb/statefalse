namespace Statefalse.Application;

/// <summary>
/// Protects credentials that must be recoverable for outbound GitHub calls.
/// </summary>
public interface IGitHubCredentialProtector
{
    string Protect(string plaintext);

    string? Unprotect(string? value);

    bool IsProtected(string? value);

    bool NeedsReEncryption(string? value);

    string ReEncrypt(string value);
}
