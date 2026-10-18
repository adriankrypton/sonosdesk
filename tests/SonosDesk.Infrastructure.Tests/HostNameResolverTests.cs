using Microsoft.Extensions.Logging.Abstractions;
using SonosDesk.Infrastructure.Networking;

namespace SonosDesk.Infrastructure.Tests;

public class HostNameResolverTests
{
    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("169.254.169.254")]
    [InlineData("not-an-ip")]
    [InlineData("")]
    public async Task Only_local_network_addresses_are_looked_up(string address)
    {
        var resolver = new DnsHostNameResolver(NullLogger<DnsHostNameResolver>.Instance);

        Assert.Null(await resolver.GetHostNameAsync(address));
    }
}
