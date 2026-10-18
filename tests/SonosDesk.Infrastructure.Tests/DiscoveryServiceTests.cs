using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SonosDesk.Core.Events;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.Discovery;
using SonosDesk.Infrastructure.Persistence;

namespace SonosDesk.Infrastructure.Tests;

public class DiscoveryServiceTests
{
    private const string LivingId = "RINCON_AAAAAAAAAAAA01400";
    private const string KitchenId = "RINCON_BBBBBBBBBBBB01400";
    private const string LivingHost = "192.168.1.10";
    private const string KitchenHost = "192.168.1.11";

    private readonly FakeSsdpTransport _transport = new();
    private readonly FakeDescriptionClient _descriptions = new();
    private readonly InMemoryKnownDevices _known = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-01-05T10:00:00Z"));
    private readonly List<DeviceChangedEventArgs> _changes = [];
    private FakeConfiguration _configuration = new();

    public DiscoveryServiceTests()
    {
        _descriptions.Devices[LivingHost] = (LivingId, "Wohnzimmer");
        _descriptions.Devices[KitchenHost] = (KitchenId, "Küche");
    }

    private static string Location(string host) => $"http://{host}:1400/xml/device_description.xml";

    private SonosDiscoveryService Create()
    {
        var service = new SonosDiscoveryService(
            _transport,
            _descriptions,
            _configuration,
            _known,
            new NoNetworkChanges(),
            _time,
            NullLogger<SonosDiscoveryService>.Instance,
            new DiscoveryOptions { ListenForNotifications = false });
        service.DeviceChanged += (_, e) => _changes.Add(e);
        return service;
    }

    [Fact]
    public async Task Finds_devices_from_ssdp_responses()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        _transport.AddResponse(Samples.SsdpResponse(KitchenId, Location(KitchenHost)), KitchenHost);
        var discovery = Create();

        var result = await discovery.RefreshAsync();

        Assert.Equal(2, result.OnlineDevices);
        Assert.Equal(2, result.NewDevices);
        Assert.Equal(["Küche", "Wohnzimmer"], discovery.Devices.Select(d => d.DisplayName));
        Assert.All(_changes, c => Assert.Equal(DeviceChangeKind.Added, c.Kind));
        Assert.Equal(DiscoveryPhase.Completed, discovery.Status.Phase);
    }

    [Fact]
    public async Task Deduplicates_by_device_id_across_search_targets_and_embedded_devices()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost), "upnp:rootdevice"), LivingHost);
        _transport.AddResponse(Samples.SsdpResponse(LivingId + "_MR", Location(LivingHost), "urn:schemas-upnp-org:device:MediaRenderer:1"), LivingHost);
        var discovery = Create();

        await discovery.RefreshAsync();

        Assert.Single(discovery.Devices);
        Assert.Single(_descriptions.Requests);
    }

    [Fact]
    public async Task Ignores_locations_that_do_not_belong_to_the_sender()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), "192.168.1.99");
        _transport.AddResponse(Samples.SsdpResponse(KitchenId, "http://203.0.113.7:1400/xml/device_description.xml"), "203.0.113.7");
        var discovery = Create();

        var result = await discovery.RefreshAsync();

        Assert.Equal(0, result.OnlineDevices);
        Assert.Empty(_descriptions.Requests);
        Assert.NotNull(discovery.Status.LastError);
    }

    [Fact]
    public async Task Ignores_non_sonos_responses()
    {
        _transport.AddResponse(
            "HTTP/1.1 200 OK\r\nUSN: uuid:abc::upnp:rootdevice\r\nST: upnp:rootdevice\r\nSERVER: Router\r\nLOCATION: http://192.168.1.1:1400/x.xml\r\n\r\n",
            "192.168.1.1");
        var discovery = Create();

        await discovery.RefreshAsync();

        Assert.Empty(discovery.Devices);
        Assert.Empty(_descriptions.Requests);
    }

    [Fact]
    public async Task Repeated_rounds_reuse_fresh_descriptions()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        var discovery = Create();

        await discovery.RefreshAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await discovery.RefreshAsync();
        Assert.Single(_descriptions.Requests);

        _time.Advance(new DiscoveryOptions().DescriptionMaxAge);
        await discovery.RefreshAsync();
        Assert.Equal(2, _descriptions.Requests.Count);
        Assert.Single(_changes);
    }

    [Fact]
    public async Task Device_is_lost_after_repeated_failures_and_recovers_later()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        var discovery = Create();
        await discovery.RefreshAsync();

        _transport.Responses.Clear();
        _descriptions.Offline.Add(LivingHost);
        await discovery.RefreshAsync();
        Assert.True(discovery.FindDevice(LivingId)!.IsOnline);

        var result = await discovery.RefreshAsync();
        Assert.Equal(1, result.LostDevices);
        Assert.False(discovery.FindDevice(LivingId)!.IsOnline);
        Assert.Equal(DeviceChangeKind.Lost, _changes[^1].Kind);

        _descriptions.Offline.Clear();
        await discovery.RefreshAsync();
        Assert.True(discovery.FindDevice(LivingId)!.IsOnline);
        Assert.Equal(DeviceChangeKind.Recovered, _changes[^1].Kind);
    }

    [Fact]
    public async Task Address_change_is_reported_as_update()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        var discovery = Create();
        await discovery.RefreshAsync();

        _transport.Responses.Clear();
        _descriptions.Devices["192.168.1.40"] = (LivingId, "Wohnzimmer");
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location("192.168.1.40")), "192.168.1.40");
        await discovery.RefreshAsync();

        Assert.Single(discovery.Devices);
        Assert.Equal("192.168.1.40", discovery.FindDevice(LivingId)!.Host);
        Assert.Equal(DeviceChangeKind.Updated, _changes[^1].Kind);
    }

    [Fact]
    public async Task Speaker_group_document_does_not_replace_player_description()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        var discovery = Create();
        await discovery.RefreshAsync();

        // Sonos also announces /xml/group_description.xml for the same UDN (no control services).
        discovery.AddCandidateLocations([new Uri($"http://{LivingHost}:1400/xml/group_description.xml")], DiscoverySource.SsdpNotification);
        await discovery.ProcessPendingAsync(CancellationToken.None);

        var device = discovery.FindDevice(LivingId)!;
        Assert.Equal(Location(LivingHost), device.Location.ToString());
        Assert.NotNull(device.Description.FindService(SonosServiceTypes.AVTransport));
        Assert.True(device.IsOnline);
        Assert.Single(_changes);
    }

    [Fact]
    public async Task Speaker_group_announcements_are_ignored()
    {
        _transport.AddResponse(
            Samples.SsdpResponse(LivingId, $"http://{LivingHost}:1400/xml/group_description.xml", "urn:smartspeaker-audio:device:SpeakerGroup:1"),
            LivingHost);
        var discovery = Create();

        await discovery.RefreshAsync();

        Assert.Empty(_descriptions.Requests);
        Assert.Empty(discovery.Devices);
    }

    [Fact]
    public async Task Probes_manual_addresses_when_multicast_finds_nothing()
    {
        _configuration = new FakeConfiguration(new AppSettings { ManualPlayerAddresses = [KitchenHost, "8.8.8.8"] });
        var discovery = Create();

        var result = await discovery.RefreshAsync();

        Assert.Equal(1, result.OnlineDevices);
        Assert.Equal(DiscoverySource.Manual, discovery.FindDevice(KitchenId)!.Source);
        Assert.Contains(result.Errors, e => e.Contains("8.8.8.8"));
    }

    [Fact]
    public async Task Topology_candidates_are_processed_without_a_search()
    {
        var discovery = Create();

        discovery.AddCandidateLocations(
            [new Uri(Location(KitchenHost)), new Uri("http://169.254.169.254:1400/latest")],
            DiscoverySource.Topology);
        await discovery.ProcessPendingAsync(CancellationToken.None);

        Assert.Equal(0, _transport.Searches);
        Assert.Equal(DiscoverySource.Topology, Assert.Single(discovery.Devices).Source);
        Assert.Single(_descriptions.Requests);
    }

    [Fact]
    public async Task Known_devices_are_persisted_and_used_on_next_start()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        await Create().RefreshAsync();
        Assert.Single(_known.Entries);

        _transport.Responses.Clear();
        var next = Create();
        await next.StartAsync();
        await next.StopAsync();
        await next.RefreshAsync();

        Assert.Equal(LivingId, Assert.Single(next.Devices).Id);
    }

    [Fact]
    public async Task Description_from_unrelated_sonos_device_does_not_hijack_identity()
    {
        // The address now answers with a different player id (DHCP reassignment).
        _descriptions.Devices[LivingHost] = (KitchenId, "Küche");
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        var discovery = Create();

        await discovery.RefreshAsync();

        Assert.Null(discovery.FindDevice(LivingId));
        Assert.NotNull(discovery.FindDevice(KitchenId));
    }

    [Fact]
    public async Task Cancellation_stops_a_round()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        var discovery = Create();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => discovery.RefreshAsync(cts.Token));
        Assert.Empty(discovery.Devices);
    }

    [Fact]
    public async Task Background_loop_runs_first_round_and_stops_cleanly()
    {
        _transport.AddResponse(Samples.SsdpResponse(LivingId, Location(LivingHost)), LivingHost);
        await using var discovery = Create();
        var completed = new TaskCompletionSource();
        discovery.StatusChanged += (_, s) =>
        {
            if (s.Phase == DiscoveryPhase.Completed)
            {
                completed.TrySetResult();
            }
        };

        await discovery.StartAsync();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await discovery.StopAsync();

        Assert.Single(discovery.Devices);
        Assert.Equal(DiscoveryPhase.Stopped, discovery.Status.Phase);
    }
}

public class PersistenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sonosdesk-tests-" + Guid.NewGuid().ToString("N"));

    private JsonConfigurationService Create() =>
        new(new AppPaths(_root), new FakeTimeProvider(), NullLogger<JsonConfigurationService>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_file_yields_defaults_without_writing()
    {
        var settings = await Create().LoadAsync();

        Assert.Equal(AppSettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.NotEmpty(settings.VolumeProfiles);
        Assert.False(File.Exists(Path.Combine(_root, "settings.json")));
    }

    [Fact]
    public async Task Settings_round_trip()
    {
        var service = Create();
        await service.SaveAsync(new AppSettings
        {
            Theme = AppTheme.Dark,
            DiscoveryIntervalSeconds = 120,
            DefaultRoom = "Küche",
            Automations = [],
            QuickActions = [new QuickAction { Id = "q", Name = "Morgens", Steps = [new QuickActionStep { Kind = QuickActionStepKind.PlayFavorite, Argument = "Radio" }] }],
            StartupQuickActionId = "q",
        });

        var loaded = await Create().LoadAsync();

        Assert.Equal(AppTheme.Dark, loaded.Theme);
        Assert.Equal(120, loaded.DiscoveryIntervalSeconds);
        Assert.Equal("Küche", loaded.DefaultRoom);
        Assert.Equal("q", loaded.StartupQuickActionId);
        Assert.Equal(QuickActionStepKind.PlayFavorite, loaded.QuickActions.Single().Steps.Single().Kind);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Json_uses_readable_enum_names_and_contains_no_secrets_section()
    {
        await Create().SaveAsync(new AppSettings { Theme = AppTheme.Light });

        var json = await File.ReadAllTextAsync(Path.Combine(_root, "settings.json"));

        Assert.Contains("\"theme\": \"light\"", json);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Corrupt_file_is_backed_up_and_defaults_are_used()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), "{ \"theme\": ");

        var settings = await Create().LoadAsync();

        Assert.Equal(AppTheme.System, settings.Theme);
        Assert.Single(Directory.GetFiles(_root, "settings.corrupt-*.json"));
    }

    [Fact]
    public async Task Incomplete_entries_are_dropped_instead_of_failing()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), """
            {
              // comments and trailing commas are tolerated
              "schemaVersion": 1,
              "theme": "dark",
              "volumeProfiles": [ { "activeRoomVolume": 10 }, { "name": "Ok", "activeRoomVolume": 20 }, ],
              "quickActions": null,
              "unknownSetting": 42
            }
            """);

        var settings = await Create().LoadAsync();

        Assert.Equal(AppTheme.Dark, settings.Theme);
        Assert.Equal("Ok", Assert.Single(settings.VolumeProfiles).Name);
        Assert.Empty(settings.QuickActions);
    }

    [Fact]
    public async Task Newer_schema_is_backed_up_before_overwriting()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), """{ "schemaVersion": 99, "theme": "dark" }""");
        var service = Create();

        var loaded = await service.LoadAsync();
        await service.SaveAsync(loaded with { DiscoveryIntervalSeconds = 30 });

        Assert.Single(Directory.GetFiles(_root, "settings.v99-*.json"));
        Assert.Contains("\"schemaVersion\": 1", await File.ReadAllTextAsync(Path.Combine(_root, "settings.json")));
    }

    [Fact]
    public async Task Documented_quick_action_example_loads()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "settings.json"), """
            {
              "quickActions": [
                {
                  "id": "party-unten",
                  "name": "Party unten",
                  "showInTray": true,
                  "steps": [
                    { "kind": "createGroup", "room": "Wohnzimmer", "argument": "Wohnzimmer, Küche, Esszimmer" },
                    { "kind": "setVolume", "room": "Küche", "value": 30 },
                    { "kind": "playFavorite", "room": "Wohnzimmer", "argument": "1LIVE DIGGI" }
                  ]
                }
              ],
              "automations": [
                { "id": "morning", "name": "Morgens", "enabled": true, "time": "06:45:00",
                  "days": ["monday", "tuesday", "wednesday", "thursday", "friday"], "quickActionId": "party-unten" }
              ]
            }
            """);

        var settings = await Create().LoadAsync();

        var action = Assert.Single(settings.QuickActions);
        Assert.Equal([QuickActionStepKind.CreateGroup, QuickActionStepKind.SetVolume, QuickActionStepKind.PlayFavorite], action.Steps.Select(s => s.Kind));
        var rule = Assert.Single(settings.Automations);
        Assert.Equal(new TimeOnly(6, 45), rule.Time);
        Assert.Equal(5, rule.Days.Count);
    }

    [Fact]
    public async Task Update_raises_change_event()
    {
        var service = Create();
        AppSettings? changed = null;
        service.SettingsChanged += (_, s) => changed = s;

        await service.UpdateAsync(s => s with { VolumeStep = 7 });

        Assert.Equal(7, changed!.VolumeStep);
        Assert.Equal(7, service.Current.VolumeStep);
    }

    [Fact]
    public async Task Known_device_store_round_trips_and_survives_corruption()
    {
        var paths = new AppPaths(_root);
        var store = new KnownDeviceStore(paths, NullLogger<KnownDeviceStore>.Instance);

        await store.SaveAsync([new KnownDeviceEntry("RINCON_A", "http://192.168.1.10:1400/xml/device_description.xml", "Bad", DateTimeOffset.UnixEpoch)]);
        Assert.Equal("RINCON_A", Assert.Single(await store.LoadAsync()).Id);

        await File.WriteAllTextAsync(paths.KnownDevicesFile, "[{ broken");
        Assert.Empty(await store.LoadAsync());
    }
}
