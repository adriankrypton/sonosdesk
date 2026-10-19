using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Core.Settings;

namespace SonosDesk.Infrastructure.Receivers;

// One command of the Pioneer IP control protocol (ASCII, terminated by CR). ExpectPrefix = start of the answer line.
public sealed record PioneerCommand(string Text, string? ExpectPrefix);

public static class PioneerResponse
{
    public static bool IsError(string line) => line is "E02" or "E03" or "E04" or "E06" or "B00" || line.StartsWith('E') && line.Length == 3 && char.IsAsciiDigit(line[1]);

    public static bool? ParsePower(string? line) => line switch
    {
        "PWR0" => true,
        "PWR1" or "PWR2" => false,
        _ => null,
    };

    public static int? ParseVolume(string? line) =>
        line is { Length: 6 } && line.StartsWith("VOL", StringComparison.Ordinal)
        && int.TryParse(line.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var step)
            ? Math.Clamp(step, 0, PioneerProtocol.MaxVolumeStep)
            : null;

    public static bool? ParseMute(string? line) => line switch
    {
        "MUT0" => true,
        "MUT1" => false,
        _ => null,
    };

    public static string? ParseInput(string? line) =>
        line is { Length: 4 } && line.StartsWith("FN", StringComparison.Ordinal) && char.IsAsciiDigit(line[2]) && char.IsAsciiDigit(line[3])
            ? line[2..]
            : null;

    // "RGB" + code + flag (0 = default name, 1 = renamed) + name.
    public static ReceiverInput? ParseInputName(string? line) =>
        line is { Length: > 6 } && line.StartsWith("RGB", StringComparison.Ordinal) && char.IsAsciiDigit(line[3]) && char.IsAsciiDigit(line[4])
            ? new ReceiverInput(line[3..5], line[6..].Trim())
            : null;

    // "BA06", "TR03": two-digit value after the prefix.
    public static int? ParseTwoDigits(string? line, string prefix) =>
        line is not null && line.Length == prefix.Length + 2 && line.StartsWith(prefix, StringComparison.Ordinal)
        && int.TryParse(line.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    // "RGD<005><VSX-830/SYXEV8><E1>": the second field is the model.
    public static string? ParseModel(string? line)
    {
        if (line is null || !line.StartsWith("RGD", StringComparison.Ordinal))
        {
            return null;
        }

        var fields = line[3..].Split(['<', '>'], StringSplitOptions.RemoveEmptyEntries);
        return fields.Length >= 2 && fields[1].Trim().Length > 0 ? fields[1].Trim() : null;
    }

    // 'SSI"1-282-009-008-193"' or 'SSO22"Name"': text between the first and last double quote.
    public static string? ParseQuoted(string? line, string prefix)
    {
        if (line is null || !line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var start = line.IndexOf('"', StringComparison.Ordinal);
        var end = line.LastIndexOf('"');
        return start >= 0 && end > start + 1 ? line[(start + 1)..end].Trim() : null;
    }

    // "FL" + 2 flag digits + text as hex pairs. Control characters (icons) are dropped.
    public static string? ParseDisplay(string? line)
    {
        if (line is not { Length: >= 4 } || !line.StartsWith("FL", StringComparison.Ordinal))
        {
            return null;
        }

        var hex = line.AsSpan(4);
        var text = new StringBuilder(hex.Length / 2);
        for (var i = 0; i + 1 < hex.Length; i += 2)
        {
            if (!byte.TryParse(hex.Slice(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            text.Append(value is >= 0x20 and < 0x7F ? (char)value : ' ');
        }

        return text.ToString();
    }
}

public sealed class PioneerIpControl(ILogger<PioneerIpControl> logger) : IDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(20);
    private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(1.5);
    private const int MaxLineLength = 1024;

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    // Replaceable for tests (loopback fake receiver).
    internal Func<string, int, CancellationToken, Task<Stream>> Connect { get; init; } = ConnectTcpAsync;

    // Sends the commands; returns the matching answer per command (null = no answer). The connection is kept
    // open and reused: the receiver accepts few connections and reacts slowly to frequent reconnects.
    public async Task<IReadOnlyList<string?>> SendAsync(ReceiverConfig receiver, IReadOnlyList<PioneerCommand> commands, CancellationToken cancellationToken = default)
    {
        var key = receiver.Host + ":" + receiver.ControlPort.ToString(CultureInfo.InvariantCulture);
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var connection = await GetConnectionAsync(key, receiver, cancellationToken).ConfigureAwait(false);
                try
                {
                    var answers = await ExchangeAsync(connection, commands, cancellationToken).ConfigureAwait(false);
                    connection.LastUsed = DateTimeOffset.UtcNow;
                    return answers;
                }
                catch (Exception ex) when (attempt == 0 && ex is SocketException or IOException or ObjectDisposedException)
                {
                    // The receiver closed the idle connection; reconnect once.
                    await DropAsync(key).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            await DropAsync(key).ConfigureAwait(false);
            logger.LogInformation("IP control of {Host}:{Port} failed: {Message}", receiver.Host, receiver.ControlPort, ex.Message);
            throw new SonosException(SonosErrorKind.Unreachable, L.F("receiver.unreachable", receiver.Name), ex);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            connection.Stream.Dispose();
        }

        _connections.Clear();
    }

    private async Task<Connection> GetConnectionAsync(string key, ReceiverConfig receiver, CancellationToken cancellationToken)
    {
        if (_connections.TryGetValue(key, out var existing))
        {
            if (DateTimeOffset.UtcNow - existing.LastUsed < IdleTimeout)
            {
                existing.DiscardPending();
                return existing;
            }

            await DropAsync(key).ConfigureAwait(false);
        }

        var stream = await Connect(receiver.Host, receiver.ControlPort, cancellationToken).ConfigureAwait(false);
        var connection = new Connection(stream);
        _connections[key] = connection;
        return connection;
    }

    private async Task DropAsync(string key)
    {
        if (_connections.TryRemove(key, out var connection))
        {
            await connection.Stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<string?>> ExchangeAsync(Connection connection, IReadOnlyList<PioneerCommand> commands, CancellationToken cancellationToken)
    {
        var answers = new List<string?>(commands.Count);
        foreach (var command in commands)
        {
            var bytes = Encoding.ASCII.GetBytes(command.Text + "\r");
            await connection.Stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (command.ExpectPrefix is null)
            {
                answers.Add(null);
                await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken).ConfigureAwait(false);
                continue;
            }

            answers.Add(await ReadAnswerAsync(connection.Reader, command.ExpectPrefix, cancellationToken).ConfigureAwait(false));
            if (connection.Reader.IsClosed)
            {
                throw new IOException("The receiver closed the connection.");
            }
        }

        return answers;
    }

    private sealed class Connection(Stream stream)
    {
        public Stream Stream { get; } = stream;

        public LineReader Reader { get; } = new(stream);

        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;

        // Status pushes that arrived while idle are old news; drop them so they are not taken as answers.
        public void DiscardPending()
        {
            Reader.Clear();
            if (Stream is NetworkStream network)
            {
                var buffer = new byte[4096];
                while (network.DataAvailable && network.Read(buffer, 0, buffer.Length) > 0)
                {
                }
            }
        }
    }

    private static async Task<string?> ReadAnswerAsync(LineReader reader, string prefix, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AnswerTimeout);
        try
        {
            while (true)
            {
                var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                if (line is null)
                {
                    return null;
                }

                // Status pushes (display text etc.) arrive in between; only the expected answer or an error counts.
                if (line.StartsWith(prefix, StringComparison.Ordinal) || PioneerResponse.IsError(line))
                {
                    return line;
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async Task<Stream> ConnectTcpAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (!SettingsNormalizer.IsLocalIPv4(host) || !IPAddress.TryParse(host, out var address))
        {
            throw new SonosException(SonosErrorKind.Rejected, L.T("receiver.local_only"));
        }

        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectTimeout);
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, port), timeout.Token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private sealed class LineReader(Stream stream)
    {
        public bool IsClosed { get; private set; }

        public void Clear()
        {
            _offset = _count;
            _line.Clear();
        }

        private readonly byte[] _buffer = new byte[2048];
        private readonly StringBuilder _line = new();
        private int _offset;
        private int _count;

        public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                while (_offset < _count)
                {
                    var b = _buffer[_offset++];
                    if (b is (byte)'\r' or (byte)'\n')
                    {
                        if (_line.Length > 0)
                        {
                            var line = _line.ToString();
                            _line.Clear();
                            return line;
                        }

                        continue;
                    }

                    if (_line.Length < MaxLineLength)
                    {
                        _line.Append(b is >= 0x20 and < 0x7F ? (char)b : '?');
                    }
                }

                _count = await stream.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
                _offset = 0;
                if (_count == 0)
                {
                    IsClosed = true;
                    return null;
                }
            }
        }
    }
}
