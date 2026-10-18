using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;

namespace SonosDesk.Windows.Security;

public sealed class DpapiSecretStore(IAppPaths paths, ILogger<DpapiSecretStore> logger) : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SonosDesk.SecretStore.v1");
    private const long MaxSecretBytes = 64 * 1024;

    public async Task<string?> GetAsync(string name, CancellationToken cancellationToken = default)
    {
        var path = PathFor(name);
        if (!File.Exists(path) || new FileInfo(path).Length > MaxSecretBytes)
        {
            return null;
        }

        try
        {
            var encrypted = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var plain = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            logger.LogWarning("Secret {Name} could not be decrypted and is ignored", name);
            return null;
        }
    }

    public async Task SetAsync(string name, string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        var path = PathFor(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), Entropy, DataProtectionScope.CurrentUser);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, encrypted, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
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
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
        {
            throw new ArgumentException("Invalid secret name.", nameof(name));
        }

        return Path.Combine(paths.SecretsDirectory, name + ".bin");
    }
}
