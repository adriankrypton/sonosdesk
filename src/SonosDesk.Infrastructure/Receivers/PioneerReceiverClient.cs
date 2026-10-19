using System.Collections.Concurrent;
using System.Net;
using System.Security;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Interfaces;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;
using SonosDesk.Infrastructure.Networking;
using SonosDesk.Infrastructure.Upnp;

namespace SonosDesk.Infrastructure.Receivers;

// Pioneer VSX receivers: power, volume, mute, input and display via IP control (TCP 8102),
// internet radio via the UPnP media renderer (AVTransport) fed by the SonosDesk stream relay.
public sealed class PioneerReceiverClient : IReceiverClient, IDisposable
{
    private const string AvTransport = "urn:schemas-upnp-org:service:AVTransport:1";
    private const int DescriptionMaxBytes = 256 * 1024;
    private static readonly TimeSpan ClientWait = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan PowerOnWait = TimeSpan.FromSeconds(10);

    private readonly PioneerIpControl _ipControl;
    private readonly IGuardedHttpClient _http;
    private readonly IUpnpSoapClient _soap;
    private readonly IStreamRelay _relay;
    private readonly IStreamUrlResolver _resolver;
    private readonly ILogger<PioneerReceiverClient> _logger;
    private readonly bool _ownsHttp;
    private readonly ConcurrentDictionary<string, Uri> _controlUrls = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IReadOnlyList<ReceiverInput>> _inputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (Uri Url, string Station)> _streams = new(StringComparer.OrdinalIgnoreCase);

    public PioneerReceiverClient(PioneerIpControl ipControl, IStreamRelay relay, IStreamUrlResolver resolver, ILoggerFactory loggers)
    {
        var http = new GuardedHttpClient(
            GuardedHttpClient.CreateHandler(NetworkAddressPolicy.IsLocalNetworkAddress, useProxy: false),
            ValidateReceiverUrl,
            TimeSpan.FromSeconds(5),
            loggers.CreateLogger<GuardedHttpClient>());
        _ipControl = ipControl;
        _http = http;
        _soap = new UpnpSoapClient(http, loggers.CreateLogger<UpnpSoapClient>());
        _relay = relay;
        _resolver = resolver;
        _logger = loggers.CreateLogger<PioneerReceiverClient>();
        _ownsHttp = true;
    }

    internal PioneerReceiverClient(
        PioneerIpControl ipControl,
        IGuardedHttpClient http,
        IUpnpSoapClient soap,
        IStreamRelay relay,
        IStreamUrlResolver resolver,
        ILogger<PioneerReceiverClient> logger)
    {
        _ipControl = ipControl;
        _http = http;
        _soap = soap;
        _relay = relay;
        _resolver = resolver;
        _logger = logger;
    }

    // Plain HTTP to an IP address in the local network; any port (Pioneer uses 8080 and 49152+).
    public static string? ValidateReceiverUrl(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttp)
        {
            return "only http is allowed";
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return "user info is not allowed";
        }

        return uri.HostNameType == UriHostNameType.IPv4
            && IPAddress.TryParse(uri.DnsSafeHost, out var address)
            && NetworkAddressPolicy.IsLocalNetworkAddress(address)
                ? null
                : "host is not a local IPv4 address";
    }

    public static Uri DefaultDescriptionUrl(string host) => new($"http://{host}:8080/description.xml");

    public async Task<ReceiverIdentity> IdentifyAsync(string host, CancellationToken cancellationToken = default)
    {
        host = host.Trim();
        if (!SettingsNormalizer.IsLocalIPv4(host))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.local_only"));
        }

        var description = await TryReadDescriptionAsync(DefaultDescriptionUrl(host), cancellationToken).ConfigureAwait(false);
        var probe = new ReceiverConfig
        {
            Id = "probe",
            Name = description?.Identity.Name ?? host,
            Host = host,
            ControlPort = description?.Identity.ControlPort ?? PioneerProtocol.DefaultControlPort,
        };

        // The IP control port must answer; the media renderer is optional (only needed for streams).
        var power = await _ipControl.SendAsync(probe, [new PioneerCommand("?P", "PWR")], cancellationToken).ConfigureAwait(false);
        if (PioneerResponse.ParsePower(power[0]) is null)
        {
            throw new SonosException(SonosErrorKind.Protocol, L.F("receiver.not_pioneer", host));
        }

        return description?.Identity ?? new ReceiverIdentity { Host = host, Name = "Pioneer " + host, Manufacturer = "PIONEER" };
    }

    public async Task<ReceiverStatus> GetStatusAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string?> answers;
        try
        {
            answers = await _ipControl.SendAsync(
                receiver,
                [
                    new PioneerCommand("?P", "PWR"),
                    new PioneerCommand("?V", "VOL"),
                    new PioneerCommand("?M", "MUT"),
                    new PioneerCommand("?F", "FN"),
                    new PioneerCommand("?FL", "FL"),
                ],
                cancellationToken).ConfigureAwait(false);
        }
        catch (SonosException ex) when (ex.Kind == SonosErrorKind.Unreachable)
        {
            return ReceiverStatus.Unreachable;
        }

        var isOn = PioneerResponse.ParsePower(answers[0]);
        var status = new ReceiverStatus
        {
            IsReachable = isOn is not null,
            IsOn = isOn,
            VolumeStep = isOn == true ? PioneerResponse.ParseVolume(answers[1]) : null,
            Muted = isOn == true ? PioneerResponse.ParseMute(answers[2]) : null,
            InputCode = isOn == true ? PioneerResponse.ParseInput(answers[3]) : null,
            DisplayText = isOn == true ? PioneerResponse.ParseDisplay(answers[4])?.Trim() : null,
        };

        if (isOn != true)
        {
            return status;
        }

        try
        {
            var control = await GetAvTransportAsync(receiver, cancellationToken).ConfigureAwait(false);
            var transport = await _soap.InvokeAsync(control, AvTransport, "GetTransportInfo", [new("InstanceID", "0")], cancellationToken).ConfigureAwait(false);
            var media = await _soap.InvokeAsync(control, AvTransport, "GetMediaInfo", [new("InstanceID", "0")], cancellationToken).ConfigureAwait(false);
            return status with
            {
                Transport = UpnpValueParsers.ParseTransportState(transport.GetValueOrDefault("CurrentTransportState")),
                CurrentUri = media.GetValueOrDefault("CurrentURI"),
            };
        }
        catch (SonosException ex)
        {
            // The renderer is optional (e.g. network standby off); control via IP control still works.
            _logger.LogDebug("Renderer state of {Host} unavailable: {Message}", receiver.Host, ex.Message);
            return status;
        }
    }

    public async Task<ReceiverDetails> GetDetailsAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default)
    {
        var answers = await _ipControl.SendAsync(
            receiver,
            [new PioneerCommand("?RGD", "RGD"), new PioneerCommand("?SSI", "SSI"), new PioneerCommand("?SSO", "SSO")],
            cancellationToken).ConfigureAwait(false);
        var location = Uri.TryCreate(receiver.DescriptionUrl, UriKind.Absolute, out var configured) ? configured : DefaultDescriptionUrl(receiver.Host);
        var description = await TryReadDescriptionAsync(location, cancellationToken).ConfigureAwait(false);
        return new ReceiverDetails
        {
            Model = PioneerResponse.ParseModel(answers[0]) ?? description?.Identity.Model ?? receiver.Model,
            Manufacturer = description?.Identity.Manufacturer,
            SoftwareVersion = PioneerResponse.ParseQuoted(answers[1], "SSI"),
            FriendlyName = PioneerResponse.ParseQuoted(answers[2], "SSO") ?? description?.Identity.Name,
            Udn = description?.Identity.Udn,
        };
    }

    public async Task<IReadOnlyList<ReceiverInput>> GetInputsAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default)
    {
        if (_inputs.TryGetValue(receiver.Host, out var cached))
        {
            return cached;
        }

        var commands = PioneerProtocol.CandidateInputCodes
            .Where(c => c != PioneerProtocol.MediaRendererInput)
            .Select(c => new PioneerCommand($"?RGB{c}", "RGB"))
            .ToList();
        var answers = await _ipControl.SendAsync(receiver, commands, cancellationToken).ConfigureAwait(false);
        var inputs = answers
            .Select(PioneerResponse.ParseInputName)
            .OfType<ReceiverInput>()
            .Select(i => i with { Name = string.IsNullOrWhiteSpace(i.Name) ? PioneerProtocol.DefaultInputName(i.Code) : i.Name })
            .DistinctBy(i => i.Code)
            .ToList();
        if (inputs.Count > 0)
        {
            _inputs[receiver.Host] = inputs;
        }

        return inputs;
    }

    public async Task SetPowerAsync(ReceiverConfig receiver, bool on, CancellationToken cancellationToken = default)
    {
        // In standby the receiver needs a first CR to wake up its command parser.
        IReadOnlyList<PioneerCommand> commands = on
            ? [new PioneerCommand(string.Empty, null), new PioneerCommand("PO", "PWR")]
            : [new PioneerCommand("PF", "PWR")];
        var answers = await _ipControl.SendAsync(receiver, commands, cancellationToken).ConfigureAwait(false);
        ThrowOnError(answers[^1], receiver);
    }

    public async Task SetVolumeStepAsync(ReceiverConfig receiver, int step, CancellationToken cancellationToken = default)
    {
        var value = Math.Clamp(step, 0, PioneerProtocol.MaxVolumeStep);
        var answers = await _ipControl.SendAsync(receiver, [new PioneerCommand($"{value:000}VL", "VOL")], cancellationToken).ConfigureAwait(false);
        ThrowOnError(answers[0], receiver);
    }

    public async Task SetMuteAsync(ReceiverConfig receiver, bool muted, CancellationToken cancellationToken = default)
    {
        var answers = await _ipControl.SendAsync(receiver, [new PioneerCommand(muted ? "MO" : "MF", "MUT")], cancellationToken).ConfigureAwait(false);
        ThrowOnError(answers[0], receiver);
    }

    public async Task<string?> GetListeningModeAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default)
    {
        var answers = await _ipControl.SendAsync(receiver, [new PioneerCommand("?S", "SR")], cancellationToken).ConfigureAwait(false);
        return answers[0] is { Length: 6 } answer && answer.StartsWith("SR", StringComparison.Ordinal) && answer[2..].All(char.IsAsciiDigit)
            ? answer[2..]
            : null;
    }

    public async Task SetListeningModeAsync(ReceiverConfig receiver, string modeCode, CancellationToken cancellationToken = default)
    {
        if (modeCode is not { Length: 4 } || !modeCode.All(char.IsAsciiDigit))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.mode_unavailable"));
        }

        // Other model years use other codes for some modes; try them in order.
        var codes = PioneerProtocol.FindListeningMode(modeCode)?.AllCodes.Where(c => !IsCyclic(c)).ToList() ?? [modeCode];
        if (!codes.Contains(modeCode))
        {
            codes.Insert(0, modeCode);
        }

        foreach (var code in codes)
        {
            var answers = await _ipControl.SendAsync(receiver, [new PioneerCommand(code + "SR", "SR")], cancellationToken).ConfigureAwait(false);
            if (answers[0] is { } answer && !PioneerResponse.IsError(answer))
            {
                return;
            }
        }

        // Modes that do not fit the speakers or the current signal are refused by the receiver.
        throw new SonosException(SonosErrorKind.NotSupported, L.T("receiver.mode_unavailable"));
    }

    // Cyclic codes step through a group of modes; they are only recognized, never sent.
    private static bool IsCyclic(string code) => code is "0001" or "0005" or "0100";

    public async Task<ReceiverTone> GetToneAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default)
    {
        var answers = await _ipControl.SendAsync(
            receiver,
            [new PioneerCommand("?TO", "TO"), new PioneerCommand("?BA", "BA"), new PioneerCommand("?TR", "TR")],
            cancellationToken).ConfigureAwait(false);
        var bass = PioneerResponse.ParseTwoDigits(answers[1], "BA");
        var treble = PioneerResponse.ParseTwoDigits(answers[2], "TR");
        if (bass is null || treble is null)
        {
            throw new SonosException(SonosErrorKind.NotSupported, L.F("receiver.tone_unsupported", receiver.Name));
        }

        var enabled = answers[0] == "TO1";
        var available = true;
        if (enabled)
        {
            // Re-sending "on" changes nothing, but the receiver answers E02 if the listening mode blocks tone control.
            var probe = await _ipControl.SendAsync(receiver, [new PioneerCommand("1TO", "TO")], cancellationToken).ConfigureAwait(false);
            available = probe[0] != "E02";
        }

        return new ReceiverTone(ReceiverTone.FromCode(bass.Value), ReceiverTone.FromCode(treble.Value), enabled) { Available = available };
    }

    public async Task SetToneAsync(ReceiverConfig receiver, int bass, int treble, CancellationToken cancellationToken = default)
    {
        var answers = await _ipControl.SendAsync(
            receiver,
            [
                // Parameter first, like all Pioneer set commands: "1TO" = tone control on (answer "TO1").
                new PioneerCommand("1TO", "TO"),
                new PioneerCommand($"{ReceiverTone.ToCode(bass):00}BA", "BA"),
                new PioneerCommand($"{ReceiverTone.ToCode(treble):00}TR", "TR"),
            ],
            cancellationToken).ConfigureAwait(false);
        // E02: the tone control is not available in the current listening mode (e.g. Direct, Extended Stereo).
        if (answers.Any(a => a == "E02"))
        {
            throw new SonosException(SonosErrorKind.NotSupported, L.T("receiver.tone_mode_unavailable"));
        }

        foreach (var answer in answers)
        {
            ThrowOnError(answer, receiver);
        }
    }

    public async Task SelectInputAsync(ReceiverConfig receiver, string inputCode, CancellationToken cancellationToken = default)
    {
        if (inputCode is not { Length: 2 } || !inputCode.All(char.IsAsciiDigit))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.input_invalid"));
        }

        var answers = await _ipControl.SendAsync(receiver, [new PioneerCommand(inputCode + "FN", "FN")], cancellationToken).ConfigureAwait(false);
        if (answers[0] is { } answer && PioneerResponse.IsError(answer))
        {
            var name = _inputs.TryGetValue(receiver.Host, out var inputs)
                ? inputs.FirstOrDefault(i => i.Code == inputCode)?.Name ?? PioneerProtocol.DefaultInputName(inputCode)
                : PioneerProtocol.DefaultInputName(inputCode);
            throw new SonosException(SonosErrorKind.NotSupported, L.F("receiver.input_disabled", name));
        }
    }

    public async Task PlayStreamAsync(ReceiverConfig receiver, Uri streamUrl, string stationName, CancellationToken cancellationToken = default)
    {
        var stream = await _resolver.ResolveAsync(streamUrl, cancellationToken).ConfigureAwait(false);
        await EnsurePoweredOnAsync(receiver, cancellationToken).ConfigureAwait(false);

        var title = PioneerProtocol.DisplayTitle(stationName);
        var registration = await _relay.RegisterAsync(stream, title, receiver.Host, cancellationToken).ConfigureAwait(false);
        var control = await GetAvTransportAsync(receiver, cancellationToken).ConfigureAwait(false);
        try
        {
            await _soap.InvokeAsync(control, AvTransport, "Stop", [new("InstanceID", "0")], cancellationToken).ConfigureAwait(false);
        }
        catch (SonosException)
        {
            // Nothing was playing.
        }

        await _soap.InvokeAsync(
            control,
            AvTransport,
            "SetAVTransportURI",
            [new("InstanceID", "0"), new("CurrentURI", registration.Url.AbsoluteUri), new("CurrentURIMetaData", BuildMetadata(title, stationName, registration))],
            cancellationToken).ConfigureAwait(false);
        await _soap.InvokeAsync(control, AvTransport, "Play", [new("InstanceID", "0"), new("Speed", "1")], cancellationToken).ConfigureAwait(false);

        if (_streams.TryGetValue(receiver.Host, out var previous) && previous.Url != registration.Url)
        {
            _relay.Release(previous.Url);
        }

        _streams[receiver.Host] = (registration.Url, Branding.StripVia(stationName));
        if (!await _relay.WaitForClientAsync(registration.Url, ClientWait, cancellationToken).ConfigureAwait(false))
        {
            // Usually the firewall (or macOS local network privacy) blocks the receiver's connection to SonosDesk.
            throw new SonosException(
                SonosErrorKind.Unreachable,
                L.F(OperatingSystem.IsMacOS() ? "receiver.relay_not_reached_mac" : "receiver.relay_not_reached", receiver.Name));
        }
    }

    public async Task StopAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default)
    {
        var control = await GetAvTransportAsync(receiver, cancellationToken).ConfigureAwait(false);
        await _soap.InvokeAsync(control, AvTransport, "Stop", [new("InstanceID", "0")], cancellationToken).ConfigureAwait(false);
        if (_streams.TryRemove(receiver.Host, out var stream))
        {
            _relay.Release(stream.Url);
        }
    }

    public bool IsPlayingRelayStream(ReceiverConfig receiver, ReceiverStatus status) =>
        _streams.TryGetValue(receiver.Host, out var stream)
        && string.Equals(status.CurrentUri, stream.Url.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
        && status.Transport is TransportState.Playing or TransportState.Transitioning;

    public string? CurrentStationName(ReceiverConfig receiver) =>
        _streams.TryGetValue(receiver.Host, out var stream) ? stream.Station : null;

    public string? CurrentSongTitle(ReceiverConfig receiver) =>
        _streams.TryGetValue(receiver.Host, out var stream) ? _relay.GetUpstreamTitle(stream.Url) : null;

    public void Dispose()
    {
        if (_ownsHttp && _http is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    // No artist in the metadata: the receiver then takes artist and title from the stream ("Artist - Title"),
    // so its info pages show the current song. The station is shown as album.
    internal static string BuildMetadata(string title, string station, RelayRegistration registration)
    {
        var escapedTitle = SecurityElement.Escape(title);
        var escapedStation = SecurityElement.Escape(PioneerProtocol.ToDisplayText(Branding.StripVia(station)));
        var escapedUrl = SecurityElement.Escape(registration.Url.AbsoluteUri);
        return "<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" "
            + "xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\"><item id=\"0\" parentID=\"-1\" restricted=\"1\">"
            + $"<dc:title>{escapedTitle}</dc:title><upnp:album>{escapedStation}</upnp:album>"
            + "<upnp:class>object.item.audioItem.audioBroadcast</upnp:class>"
            + $"<res protocolInfo=\"http-get:*:{registration.ContentType}:*\">{escapedUrl}</res></item></DIDL-Lite>";
    }

    private async Task EnsurePoweredOnAsync(ReceiverConfig receiver, CancellationToken cancellationToken)
    {
        var answers = await _ipControl.SendAsync(receiver, [new PioneerCommand("?P", "PWR")], cancellationToken).ConfigureAwait(false);
        if (PioneerResponse.ParsePower(answers[0]) == true)
        {
            return;
        }

        await SetPowerAsync(receiver, on: true, cancellationToken).ConfigureAwait(false);
        var until = DateTimeOffset.UtcNow + PowerOnWait;
        while (DateTimeOffset.UtcNow < until)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700), cancellationToken).ConfigureAwait(false);
            answers = await _ipControl.SendAsync(receiver, [new PioneerCommand("?P", "PWR")], cancellationToken).ConfigureAwait(false);
            if (PioneerResponse.ParsePower(answers[0]) == true)
            {
                // The network renderer needs a moment after power-on.
                await Task.Delay(TimeSpan.FromSeconds(1.5), cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }

    private async Task<Uri> GetAvTransportAsync(ReceiverConfig receiver, CancellationToken cancellationToken)
    {
        if (_controlUrls.TryGetValue(receiver.Host, out var cached))
        {
            return cached;
        }

        var location = Uri.TryCreate(receiver.DescriptionUrl, UriKind.Absolute, out var configured)
            && string.Equals(configured.Host, receiver.Host, StringComparison.OrdinalIgnoreCase)
                ? configured
                : DefaultDescriptionUrl(receiver.Host);
        var description = await TryReadDescriptionAsync(location, cancellationToken).ConfigureAwait(false);
        if (description?.AvTransportControl is not { } control)
        {
            throw new SonosException(SonosErrorKind.NotSupported, L.F("receiver.no_renderer", receiver.Name));
        }

        _controlUrls[receiver.Host] = control;
        return control;
    }

    private async Task<RendererDescription?> TryReadDescriptionAsync(Uri location, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _http.GetAsync(location, DescriptionMaxBytes, cancellationToken).ConfigureAwait(false);
            return response.IsSuccess ? PioneerDescriptionParser.Parse(response.BodyText, location) : null;
        }
        catch (SonosException ex)
        {
            _logger.LogDebug("No renderer description at {Location}: {Message}", location, ex.Message);
            return null;
        }
    }

    private static void ThrowOnError(string? answer, ReceiverConfig receiver)
    {
        if (answer is null)
        {
            throw new SonosException(SonosErrorKind.Timeout, L.F("receiver.no_answer", receiver.Name));
        }

        if (PioneerResponse.IsError(answer))
        {
            throw new SonosException(SonosErrorKind.DeviceRejected, L.F("receiver.rejected", receiver.Name, answer));
        }
    }
}
