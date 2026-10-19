using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Core.Interfaces;

// Control of a network AV receiver (Pioneer VSX: IP control on TCP 8102, UPnP media renderer for streams).
public interface IReceiverClient
{
    Task<ReceiverIdentity> IdentifyAsync(string host, CancellationToken cancellationToken = default);

    Task<ReceiverStatus> GetStatusAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default);

    Task<ReceiverDetails> GetDetailsAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReceiverInput>> GetInputsAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default);

    Task SetPowerAsync(ReceiverConfig receiver, bool on, CancellationToken cancellationToken = default);

    Task SetVolumeStepAsync(ReceiverConfig receiver, int step, CancellationToken cancellationToken = default);

    Task SetMuteAsync(ReceiverConfig receiver, bool muted, CancellationToken cancellationToken = default);

    // Current listening mode code ("0006" = Auto Surround), null if unknown.
    Task<string?> GetListeningModeAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default);

    Task SetListeningModeAsync(ReceiverConfig receiver, string modeCode, CancellationToken cancellationToken = default);

    Task<ReceiverTone> GetToneAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default);

    // Switches the tone control on and sets bass and treble.
    Task SetToneAsync(ReceiverConfig receiver, int bass, int treble, CancellationToken cancellationToken = default);

    Task SelectInputAsync(ReceiverConfig receiver, string inputCode, CancellationToken cancellationToken = default);

    // Plays an internet radio stream through the SonosDesk relay, so the front display shows "Station - via SonosDesk".
    Task PlayStreamAsync(ReceiverConfig receiver, Uri streamUrl, string stationName, CancellationToken cancellationToken = default);

    Task StopAsync(ReceiverConfig receiver, CancellationToken cancellationToken = default);

    // True while the receiver plays a stream that SonosDesk started.
    bool IsPlayingRelayStream(ReceiverConfig receiver, ReceiverStatus status);

    // Station name of the stream SonosDesk last started on the receiver, if any.
    string? CurrentStationName(ReceiverConfig receiver);

    // Current song of that stream (from its ICY title), if the station provides one.
    string? CurrentSongTitle(ReceiverConfig receiver);
}

// Result of one UPnP search: Pioneer receivers plus the number of devices per kind (counted per IP address).
public sealed record RendererSearchResult(IReadOnlyList<ReceiverIdentity> Receivers, int SonosDevices, int MediaRenderers, int OtherUpnpDevices);

public interface IReceiverDiscovery
{
    Task<RendererSearchResult> SearchAsync(TimeSpan duration, CancellationToken cancellationToken = default);
}

// Relays an internet radio stream to a device in the local network and replaces its ICY title.
public sealed record RelayRegistration(Uri Url, string ContentType);

public interface IStreamRelay
{
    // Checks the upstream stream and returns the local URL the client (receiver) may open.
    Task<RelayRegistration> RegisterAsync(Uri upstream, string displayTitle, string clientHost, CancellationToken cancellationToken = default);

    void Release(Uri relayUrl);

    bool IsRelayUrl(string? url);

    // Completes when the client opened the relay URL; false after the timeout.
    Task<bool> WaitForClientAsync(Uri relayUrl, TimeSpan timeout, CancellationToken cancellationToken = default);

    // Current song of the upstream stream (its ICY title), if it provides one.
    string? GetUpstreamTitle(Uri relayUrl);
}
