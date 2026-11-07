using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Petty.Online.Client;

namespace Petty.App.Online;

[SupportedOSPlatform("windows")]
internal sealed class ProtectedTokenStore : ITokenStore
{
    private readonly string _path;
    private readonly byte[] _entropy;

    public ProtectedTokenStore(string directory, Uri server)
    {
        _entropy = SHA256.HashData(Encoding.UTF8.GetBytes("Petty.Online.v1:" + server.AbsoluteUri));
        _path = Path.Combine(directory, "online-session-" + Convert.ToHexString(_entropy.AsSpan(0, 8)) + ".bin");
    }

    public async Task<SessionTokens?> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return null;
        }
        byte[] encrypted = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        byte[]? plaintext = null;
        try
        {
            plaintext = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
            SessionTokens? tokens = JsonSerializer.Deserialize<SessionTokens>(plaintext);
            if (tokens is null || tokens.SessionId == Guid.Empty || string.IsNullOrEmpty(tokens.AccessToken) || string.IsNullOrEmpty(tokens.RefreshToken))
            {
                throw new JsonException("Invalid stored session.");
            }
            return tokens;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            File.Delete(_path);
            return null;
        }
        finally
        {
            if (plaintext is not null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public async Task SaveAsync(SessionTokens tokens, CancellationToken cancellationToken)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(tokens);
        byte[] encrypted;
        try
        {
            encrypted = ProtectedData.Protect(plaintext, _entropy, DataProtectionScope.CurrentUser);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temporary = _path + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        File.Delete(_path);
        return Task.CompletedTask;
    }
}
