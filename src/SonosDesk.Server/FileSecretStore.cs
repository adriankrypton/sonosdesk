using System.Security.Cryptography;
using System.Text;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Persistence;

namespace SonosDesk.Server;

// Secrets (Control API tokens) encrypted with AES-GCM. The key comes from the Electron app, which keeps it
// with the OS keychain (safeStorage); without a key, secrets cannot be stored on this platform.
public sealed class FileSecretStore : ISecretStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly string _directory;
    private readonly byte[]? _key;

    public FileSecretStore(string directory, string? base64Key)
    {
        _directory = directory;
        try
        {
            var key = string.IsNullOrWhiteSpace(base64Key) ? null : Convert.FromBase64String(base64Key);
            _key = key is { Length: 32 } ? key : null;
        }
        catch (FormatException)
        {
            _key = null;
        }
    }

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name);
        if (_key is null || !File.Exists(path))
        {
            return null;
        }

        var data = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (data.Length < NonceSize + TagSize)
        {
            return null;
        }

        try
        {
            var plain = new byte[data.Length - NonceSize - TagSize];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(data.AsSpan(0, NonceSize), data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), plain, Encoding.UTF8.GetBytes(name));
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Encrypted with another key (e.g. keychain reset): treat as missing.
            return null;
        }
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        if (_key is null)
        {
            throw new SonosException(SonosErrorKind.NotSupported, L.T("server.secrets_unavailable"));
        }

        var plain = Encoding.UTF8.GetBytes(value);
        var data = new byte[NonceSize + TagSize + plain.Length];
        RandomNumberGenerator.Fill(data.AsSpan(0, NonceSize));
        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(data.AsSpan(0, NonceSize), plain, data.AsSpan(NonceSize + TagSize), data.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(name));
        }

        await AtomicFile.WriteAllBytesAsync(PathFor(name), data, cancellationToken).ConfigureAwait(false);
    }

    public Task DeleteAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
        {
            throw new ArgumentException("Invalid secret name.", nameof(name));
        }

        return Path.Combine(_directory, name + ".secret");
    }
}
