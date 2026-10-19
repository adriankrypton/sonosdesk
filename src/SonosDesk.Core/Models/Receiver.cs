using System.Globalization;
using System.Text;

namespace SonosDesk.Core.Models;

public sealed record ReceiverIdentity
{
    public required string Host { get; init; }

    public required string Name { get; init; }

    public string? Model { get; init; }

    public string? Manufacturer { get; init; }

    public string? Udn { get; init; }

    public int ControlPort { get; init; } = PioneerProtocol.DefaultControlPort;

    public Uri? DescriptionUrl { get; init; }
}

public sealed record ReceiverInput(string Code, string Name);

// Listening mode ("Klangmodus") as set with xxxxSR; names as Pioneer shows them.
// Alternatives: codes other model years use for the same mode, or that the receiver reports for it
// (e.g. the cyclic "0005" while Auto Surround is active).
public sealed record ReceiverListeningMode(string Code, string Name, params string[] Alternatives)
{
    public IEnumerable<string> AllCodes => [Code, .. Alternatives];

    public bool Matches(string? code) => code is not null && AllCodes.Contains(code);
}

// Tone control: bass and treble in dB (−6…+6); only effective while the tone control is on.
public sealed record ReceiverTone(int Bass, int Treble, bool Enabled)
{
    // False when the current listening mode does not allow tone control (e.g. Direct, Extended Stereo).
    public bool Available { get; init; } = true;

    public const int MinLevel = -6;
    public const int MaxLevel = 6;

    public static int Clamp(int level) => Math.Clamp(level, MinLevel, MaxLevel);

    // Pioneer codes: 00 = +6 dB, 06 = 0 dB, 12 = −6 dB.
    public static int FromCode(int code) => Clamp(6 - code);

    public static int ToCode(int level) => 6 - Clamp(level);
}

// Device details for the info dialog (IP control ?RGD, ?SSI, ?SSO and the UPnP description).
public sealed record ReceiverDetails
{
    public string? Model { get; init; }

    public string? Manufacturer { get; init; }

    public string? SoftwareVersion { get; init; }

    public string? FriendlyName { get; init; }

    public string? Udn { get; init; }
}

public sealed record ReceiverStatus
{
    public static ReceiverStatus Unreachable { get; } = new() { IsReachable = false };

    public bool IsReachable { get; init; } = true;

    public bool? IsOn { get; init; }

    // Pioneer volume step 0–185 (0.5 dB each); null while in standby or unknown.
    public int? VolumeStep { get; init; }

    public bool? Muted { get; init; }

    public string? InputCode { get; init; }

    public string? DisplayText { get; init; }

    public TransportState Transport { get; init; } = TransportState.Unknown;

    // URI the network renderer currently plays (may be a SonosDesk relay URL).
    public string? CurrentUri { get; init; }

    public double? VolumeDb => VolumeStep is { } step ? PioneerProtocol.StepToDb(step) : null;
}

public static class PioneerProtocol
{
    public const int DefaultControlPort = 8102;
    public const int MaxVolumeStep = 185;
    public const double MinVolumeDb = -80.5;
    public const double MaxVolumeDb = 12.0;
    public const string MediaRendererInput = "47";

    // Step 161 is the 0.0 dB reference level; the 0–100 scale of the app ends there like a Sonos volume.
    public const int ReferenceStep = 161;

    public static int StepToPercent(int step) => (int)Math.Clamp(Math.Round(step * 100.0 / ReferenceStep, MidpointRounding.AwayFromZero), 0, 100);

    public static int PercentToStep(int percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) * ReferenceStep / 100.0, MidpointRounding.AwayFromZero);

    // Input codes known from Pioneer's IP control documentation; names are read from the receiver (?RGBxx) when available.
    public static IReadOnlyList<string> CandidateInputCodes { get; } =
    [
        "25", "04", "06", "15", "05", "10", "14", "19", "20", "21", "22", "23", "24", "34", "49",
        "01", "03", "02", "17", "13", "33", "38", "44", "45", "46", "47", "53", "41", "40", "26", "12",
    ];

    // Listening modes of Pioneer VSX receivers (IP control "Listening Mode Set"). Which ones are available
    // depends on the model, the speakers and the input signal; the receiver rejects the others.
    public static IReadOnlyList<ReceiverListeningMode> ListeningModes { get; } =
    [
        new("0006", "Auto Surround", "0005"),
        new("0151", "Auto Level Control (A.L.C.)"),
        new("0007", "Direct"),
        new("0008", "Pure Direct"),
        new("0009", "Stereo", "0001"),
        // VSX models from 2014 on use 0041 (verified on a VSX-529); older ones 0112.
        new("0041", "Extended Stereo", "0112"),
        new("0010", "Standard (Dolby/DTS)"),
        new("0003", "Front Stage Surround Advance"),
        new("0101", "Action", "0100"),
        new("0103", "Drama"),
        new("0118", "Advanced Game"),
        new("0117", "Sports"),
        new("0107", "Classical"),
        new("0110", "Rock/Pop"),
        new("0109", "Unplugged"),
        new("0152", "Optimum Surround"),
    ];

    public static ReceiverListeningMode? FindListeningMode(string? code) => ListeningModes.FirstOrDefault(m => m.Matches(code));

    public static double StepToDb(int step) => (Math.Clamp(step, 0, MaxVolumeStep) / 2.0) - 80.5;

    public static int DbToStep(double db) => (int)Math.Clamp(Math.Round((db + 80.5) * 2, MidpointRounding.AwayFromZero), 0, MaxVolumeStep);

    public static string FormatDb(double db) => db <= MinVolumeDb
        ? "−∞ dB"
        : string.Create(CultureInfo.CurrentCulture, $"{db:+0.0;−0.0;0.0} dB");

    public static string DefaultInputName(string code) => code switch
    {
        "01" => "CD",
        "02" => "TUNER",
        "03" => "CD-R/TAPE",
        "04" => "DVD",
        "05" => "TV",
        "06" => "SAT/CBL",
        "10" => "VIDEO",
        "12" => "MULTI CH IN",
        "13" => "USB-DAC",
        "14" => "VIDEO 2",
        "15" => "DVR/BDR",
        "17" => "iPod/USB",
        "19" => "HDMI 1",
        "20" => "HDMI 2",
        "21" => "HDMI 3",
        "22" => "HDMI 4",
        "23" => "HDMI 5",
        "24" => "HDMI 6",
        "25" => "BD",
        "26" => "NETWORK",
        "33" => "BT AUDIO",
        "34" => "HDMI 7",
        "38" => "INTERNET RADIO",
        "40" => "SiriusXM",
        "41" => "PANDORA",
        "44" => "MEDIA SERVER",
        "45" => "FAVORITES",
        "46" => "AirPlay",
        "47" => "DMR (Media Renderer)",
        "49" => "GAME",
        "53" => "Spotify",
        _ => code,
    };

    // The front display shows ASCII; umlauts and accents are transliterated, other characters dropped.
    public static string ToDisplayText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            switch (c)
            {
                case 'ä': builder.Append("ae"); continue;
                case 'ö': builder.Append("oe"); continue;
                case 'ü': builder.Append("ue"); continue;
                case 'Ä': builder.Append("Ae"); continue;
                case 'Ö': builder.Append("Oe"); continue;
                case 'Ü': builder.Append("Ue"); continue;
                case 'ß': builder.Append("ss"); continue;
                case '·' or '–' or '—' or '•': builder.Append('-'); continue;
                case '’' or '‘' or '´' or '`': builder.Append('\''); continue;
                case '“' or '”' or '„': builder.Append('"'); continue;
            }

            if (c is >= ' ' and <= '~')
            {
                builder.Append(c);
                continue;
            }

            foreach (var d in c.ToString().Normalize(NormalizationForm.FormD))
            {
                if (d is >= ' ' and <= '~')
                {
                    builder.Append(d);
                }
            }
        }

        var result = string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return result.Replace("'", "`", StringComparison.Ordinal);
    }

    public static string DisplayTitle(string? stationName)
    {
        var name = string.IsNullOrWhiteSpace(stationName) ? string.Empty : ToDisplayText(Branding.StripVia(stationName.Trim()));
        if (name.Length > 60)
        {
            name = name[..60].TrimEnd();
        }

        return name.Length == 0 ? "via SonosDesk" : name + " - via SonosDesk";
    }
}
