namespace SonosDesk.Core.Interfaces;

public interface IHostNameResolver
{
    // Reverse lookup of a speaker's IP address in the local network; null when no name is known.
    Task<string?> GetHostNameAsync(string ipAddress, CancellationToken cancellationToken = default);
}
