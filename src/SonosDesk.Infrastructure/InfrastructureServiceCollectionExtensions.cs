using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Models;
using SonosDesk.Core.Services;
using SonosDesk.Infrastructure.ControlApi;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Networking;
using SonosDesk.Infrastructure.Persistence;
using SonosDesk.Infrastructure.Radio;
using SonosDesk.Infrastructure.Updates;
using SonosDesk.Infrastructure.Upnp;

namespace SonosDesk.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSonosDesk(this IServiceCollection services, IAppPaths paths, ISonosConfigurationService configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(paths);
        services.AddSingleton(configuration);

        // Networking
        services.AddSingleton<INetworkInterfaceProvider, SystemNetworkInterfaceProvider>();
        services.AddSingleton<INetworkChangeNotifier, SystemNetworkChangeNotifier>();
        services.AddSingleton<IGuardedHttpClient>(sp => GuardedHttpClient.CreateDeviceClient(sp.GetRequiredService<ILogger<GuardedHttpClient>>()));
        services.AddSingleton<IAlbumArtProvider>(sp => new AlbumArtService(
            GuardedHttpClient.CreateArtworkClient(sp.GetRequiredService<ILogger<GuardedHttpClient>>()),
            sp.GetRequiredService<ILogger<AlbumArtService>>()));

        // Discovery
        services.AddSingleton<ISsdpTransport, UdpSsdpTransport>();
        services.AddSingleton<IDeviceDescriptionClient, DeviceDescriptionClient>();
        services.AddSingleton<IKnownDeviceStore, KnownDeviceStore>();
        services.AddSingleton(new DiscoveryOptions());
        services.AddSingleton<ISonosDiscoveryService, SonosDiscoveryService>();

        // Local UPnP backend
        services.AddSingleton<IUpnpSoapClient, UpnpSoapClient>();
        services.AddSingleton<LocalSonosControlService>();

        // Official Control API backend (cloud). Uses the system proxy, normal TLS validation.
        services.AddSingleton(_ => new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
        });
        services.AddSingleton<ControlApiAuthorizationService>();
        services.AddSingleton<SonosControlApiClient>();
        services.AddSingleton<CloudSonosControlService>();

        services.AddSingleton<ISonosControlService>(sp => configuration.Current.Backend switch
        {
            SonosBackendKind.ControlApi => sp.GetRequiredService<CloudSonosControlService>(),
            _ => sp.GetRequiredService<LocalSonosControlService>(),
        });

        // Internet radio: station directory and ICY "now playing" metadata
        services.AddSingleton<IRadioDirectory>(sp => new RadioBrowserClient(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }),
            sp.GetRequiredService<ILogger<RadioBrowserClient>>()));
        services.AddSingleton<IStreamMetadataProvider, IcyMetadataClient>();
        services.AddSingleton<IRadioLogoResolver, RadioLogoResolver>();
        services.AddSingleton<IStreamUrlResolver, StreamUrlResolver>();

        services.AddSingleton<IHostNameResolver, DnsHostNameResolver>();

        // Update hint (GitHub releases of the repository)
        services.AddSingleton<IUpdateChecker>(sp => new GitHubUpdateChecker(
            new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }),
            sp.GetRequiredService<ILogger<GitHubUpdateChecker>>()));

        // Core orchestration
        services.AddSingleton<ISonosStateService, SonosStateService>();
        services.AddSingleton<IQuickActionExecutor, QuickActionExecutor>();
        services.AddSingleton<AutomationScheduler>();
        return services;
    }
}
