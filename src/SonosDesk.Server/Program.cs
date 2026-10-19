using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Infrastructure;
using SonosDesk.Infrastructure.ControlApi;
using SonosDesk.Infrastructure.Logging;
using SonosDesk.Infrastructure.Persistence;
using SonosDesk.Server;

// Started by the Electron app; see docs/desktop-api.md for the handshake.
var token = Environment.GetEnvironmentVariable("SONOSDESK_TOKEN");
if (token is not { Length: >= 32 })
{
    await Console.Error.WriteLineAsync("SONOSDESK_TOKEN (at least 32 characters) is required.");
    return 2;
}

var dataDirectory = Environment.GetEnvironmentVariable("SONOSDESK_DATA_DIR");
var paths = string.IsNullOrWhiteSpace(dataDirectory) ? new AppPaths() : new AppPaths(dataDirectory);
var fileLogger = new FileLoggerProvider(paths.LogDirectory);
using var bootstrapLoggers = LoggerFactory.Create(b => b.AddProvider(fileLogger));
var configuration = new JsonConfigurationService(paths, TimeProvider.System, bootstrapLoggers.CreateLogger<JsonConfigurationService>());

L.SetLanguage(null);
var settings = await configuration.LoadAsync();
L.SetLanguage(Environment.GetEnvironmentVariable("SONOSDESK_LANGUAGE") is { Length: > 0 } language ? language : settings.Language);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddProvider(fileLogger);

builder.Services.AddSonosDesk(paths, configuration);
builder.Services.AddSingleton<ISecretStore>(new FileSecretStore(paths.SecretsDirectory, Environment.GetEnvironmentVariable("SONOSDESK_SECRET_KEY")));
builder.Services.AddSingleton(new ApiToken(token));
builder.Services.AddSingleton<EventHub>();
builder.Services.AddSingleton<CensusStore>();
builder.Services.AddSingleton<ArtResolver>();
builder.Services.AddSingleton<ReceiverMonitor>();
builder.Services.AddSingleton<SnapshotBuilder>();
builder.Services.AddSingleton<Sonos>();
builder.Services.AddSingleton<Receivers>();
builder.Services.AddSingleton<SonosHost>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SonosHost>());
builder.Services.ConfigureHttpJsonOptions(options => Json.Configure(options.SerializerOptions));

var app = builder.Build();
app.UseMiddleware<ApiGuard>();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.MapSonosEndpoints();
app.MapRadioEndpoints();
app.MapReceiverEndpoints();
app.MapAppEndpoints();

await app.Services.GetRequiredService<ControlApiAuthorizationService>().InitializeAsync();
await app.StartAsync();

var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
Console.Out.WriteLine($"SONOSDESK_READY {address}");
Console.Out.Flush();
app.Logger.LogInformation("SonosDesk server {Version} listening on {Address}", ApiVersion.Current, address);

// Electron closes stdin when it quits; the server then stops (also if Electron crashed).
_ = Task.Run(async () =>
{
    try
    {
        while (await Console.In.ReadLineAsync() is not null)
        {
        }
    }
    catch (IOException)
    {
    }

    await app.StopAsync();
});

await app.WaitForShutdownAsync();
return 0;
