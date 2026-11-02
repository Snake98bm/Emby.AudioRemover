using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emby.AudioRemover.Core
{
    // Property names deliberately match ffprobe's JSON keys so any JSON serializer can bind them without attributes.
    public class ProbeResult
    {
        public List<ProbeStream> streams { get; set; } = new List<ProbeStream>();
        public ProbeFormat format { get; set; }

        public int Count(string codecType) =>
            streams.Count(s => string.Equals(s.codec_type, codecType, StringComparison.OrdinalIgnoreCase));

        public double? DurationSeconds =>
            double.TryParse(format?.duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : (double?)null;
    }

    public class ProbeFormat
    {
        public string duration { get; set; }
        public string format_name { get; set; }
        public Dictionary<string, string> tags { get; set; }

        public string Tag(string name) =>
            tags?.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
    }

    public class ProbeStream
    {
        public int index { get; set; }
        public string codec_type { get; set; }
        public string codec_name { get; set; }
        public int channels { get; set; }
        public string channel_layout { get; set; }
        public string profile { get; set; }
        public string bit_rate { get; set; }
        public Dictionary<string, string> tags { get; set; }
        public Dictionary<string, int> disposition { get; set; }

        public bool IsAudio => string.Equals(codec_type, "audio", StringComparison.OrdinalIgnoreCase);

        /// <summary>What Whisper heard on this track, when it listened; null when it didn't.</summary>
        public HeardLanguage Heard { get; set; }

        public string Tag(string name)
        {
            if (tags == null) return null;
            foreach (var kv in tags)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value;
            }
            return null;
        }

        public bool Disposition(string name)
        {
            if (disposition == null) return false;
            foreach (var kv in disposition)
            {
                if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)) return kv.Value != 0;
            }
            return false;
        }

        public string Language => Languages.Normalize(Tag("language"));

        public string Title => Tag("title") ?? string.Empty;

        public bool IsDefault => Disposition("default");

        public bool IsCommentary =>
            Disposition("comment") || Title.IndexOf("commentary", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>A narrator describes what's on screen, for blind and partially sighted viewers.</summary>
        public bool IsAudioDescription =>
            Disposition("visual_impaired") || Disposition("descriptions")
            || Title.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0
            || Title.IndexOf("descriptive", StringComparison.OrdinalIgnoreCase) >= 0
            || Title.IndexOf("visually impaired", StringComparison.OrdinalIgnoreCase) >= 0
            || AudioDescribed.IsMatch(Title);

        // "English (AD)", "English [AD]": capitals and a separate word only, so "Headphones" or "Advanced" don't count.
        private static readonly Regex AudioDescribed = new Regex(@"\bAD\b", RegexOptions.CultureInvariant);

        /// <summary>"5.1", "stereo", or the channel count when ffprobe gives no layout.</summary>
        public string Channels =>
            !string.IsNullOrEmpty(channel_layout) ? channel_layout.Replace("(side)", "") : channels > 0 ? channels + "ch" : "";

        /// <summary>Bits per second: ffprobe's own figure, or the statistics tag mkvmerge writes. 0 when neither says.</summary>
        public long BitRate =>
            long.TryParse(bit_rate ?? Tag("BPS") ?? Tag("BPS-eng"), NumberStyles.None, CultureInfo.InvariantCulture, out var bps) ? bps : 0;

        /// <summary>Lossless sound: TrueHD, FLAC, ALAC, PCM or DTS-HD Master Audio.</summary>
        public bool IsLossless =>
            LosslessCodecs.Contains(codec_name ?? string.Empty)
            || (codec_name ?? string.Empty).StartsWith("pcm_", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(codec_name, "dts", StringComparison.OrdinalIgnoreCase) && (profile ?? string.Empty).Contains("DTS-HD MA"));

        private static readonly HashSet<string> LosslessCodecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "truehd", "mlp", "flac", "alac" };

        public string Describe() =>
            $"#{index} {Language} {codec_name}{(Channels.Length > 0 ? " " + Channels : "")}{(IsDefault ? " default" : "")}" +
            $"{(Disposition("comment") && Title.IndexOf("comment", StringComparison.OrdinalIgnoreCase) < 0 ? " commentary" : "")}" +
            $"{(Disposition("visual_impaired") ? " audio-description" : "")}" +
            $"{(Title.Length > 0 ? " \"" + Title + "\"" : "")}" +
            $"{(Heard != null ? " (" + Heard.Describe() + ")" : "")}";
    }
}
