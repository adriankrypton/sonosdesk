using System.Buffers;
using System.Text;

namespace SonosDesk.Infrastructure.Receivers;

// Removes the ICY metadata of the upstream stream and inserts SonosDesk's own title for the client.
// Upstream: audio blocks of upstreamMetaInt bytes, each followed by a length byte and length*16 bytes metadata.
// Downstream: the same layout with downstreamMetaInt (0 = the client did not ask for metadata).
public sealed class IcyRewriter
{
    public const int MaxBlockLength = 255 * 16;

    private readonly int _upstreamMetaInt;
    private readonly int _downstreamMetaInt;
    private readonly Func<string> _title;
    private readonly Action<string?>? _onUpstreamTitle;

    private int _audioLeftUpstream;
    private int _metaLeftUpstream = -1;
    private byte[] _meta = [];
    private int _metaFill;

    private int _audioLeftDownstream;
    private string? _lastSentTitle;

    public IcyRewriter(int upstreamMetaInt, int downstreamMetaInt, Func<string> title, Action<string?>? onUpstreamTitle = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(upstreamMetaInt);
        ArgumentOutOfRangeException.ThrowIfNegative(downstreamMetaInt);
        _upstreamMetaInt = upstreamMetaInt;
        _downstreamMetaInt = downstreamMetaInt;
        _title = title;
        _onUpstreamTitle = onUpstreamTitle;
        _audioLeftUpstream = upstreamMetaInt;
        _audioLeftDownstream = downstreamMetaInt;
    }

    public void Process(ReadOnlySpan<byte> input, IBufferWriter<byte> output)
    {
        while (!input.IsEmpty)
        {
            if (_upstreamMetaInt == 0)
            {
                WriteAudio(input, output);
                return;
            }

            if (_metaLeftUpstream < 0)
            {
                var take = Math.Min(_audioLeftUpstream, input.Length);
                WriteAudio(input[..take], output);
                input = input[take..];
                _audioLeftUpstream -= take;
                if (_audioLeftUpstream == 0)
                {
                    _metaLeftUpstream = 0;
                    _metaFill = -1;
                }

                continue;
            }

            if (_metaFill < 0)
            {
                // Length byte of the upstream metadata block.
                var length = input[0] * 16;
                input = input[1..];
                if (length == 0)
                {
                    EndUpstreamMeta();
                    continue;
                }

                _meta = new byte[length];
                _metaFill = 0;
                _metaLeftUpstream = length;
                continue;
            }

            var chunk = Math.Min(_metaLeftUpstream, input.Length);
            input[..chunk].CopyTo(_meta.AsSpan(_metaFill));
            _metaFill += chunk;
            _metaLeftUpstream -= chunk;
            input = input[chunk..];
            if (_metaLeftUpstream == 0)
            {
                _onUpstreamTitle?.Invoke(ParseStreamTitle(_meta));
                EndUpstreamMeta();
            }
        }
    }

    public static byte[] BuildBlock(string title)
    {
        var safe = title.Replace("'", "`", StringComparison.Ordinal);
        var text = Encoding.Latin1.GetBytes($"StreamTitle='{safe}';");
        var length = Math.Min((text.Length + 15) / 16 * 16, MaxBlockLength);
        var block = new byte[length + 1];
        block[0] = (byte)(length / 16);
        text.AsSpan(0, Math.Min(text.Length, length)).CopyTo(block.AsSpan(1));
        return block;
    }

    public static string? ParseStreamTitle(ReadOnlySpan<byte> block)
    {
        var end = block.IndexOf((byte)0);
        var text = Encoding.UTF8.GetString(end >= 0 ? block[..end] : block);
        const string marker = "StreamTitle='";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var stop = text.IndexOf("';", start, StringComparison.Ordinal);
        var title = (stop >= 0 ? text[start..stop] : text[start..].TrimEnd('\'', ';')).Trim();
        return title.Length == 0 ? null : title;
    }

    private void EndUpstreamMeta()
    {
        _metaLeftUpstream = -1;
        _metaFill = 0;
        _audioLeftUpstream = _upstreamMetaInt;
    }

    private void WriteAudio(ReadOnlySpan<byte> audio, IBufferWriter<byte> output)
    {
        if (_downstreamMetaInt == 0)
        {
            output.Write(audio);
            return;
        }

        while (!audio.IsEmpty)
        {
            var take = Math.Min(_audioLeftDownstream, audio.Length);
            output.Write(audio[..take]);
            audio = audio[take..];
            _audioLeftDownstream -= take;
            if (_audioLeftDownstream == 0)
            {
                // The title is repeated only when it changed; otherwise an empty block (length byte 0).
                var title = _title();
                output.Write(title == _lastSentTitle ? [0] : BuildBlock(title));
                _lastSentTitle = title;
                _audioLeftDownstream = _downstreamMetaInt;
            }
        }
    }
}
