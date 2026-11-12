using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Serialization;

namespace Emby.AudioRemover.Core
{
    /// <summary>
    /// The two SubRemover settings AudioRemover follows, read from SubRemover's own settings file ("Subremover.json"):
    /// with "Download hash-matched subtitles before changing a video" on, AudioRemover does that step itself for a video
    /// SubRemover hasn't changed yet, so the order the two plugins run in doesn't matter.
    /// </summary>
    public class SubRemoverSettings
    {
        public const string FileName = "Subremover.json";

        public bool DownloadHashMatchedSubtitles { get; set; } = true;

        /// <summary>SubRemover's settings version: before 2 the switch is on, whatever was saved (SubRemover turns it on as it reads them).</summary>
        public int OptionsVersion { get; set; }

        /// <summary>The switch as SubRemover itself sees it.</summary>
        public bool DownloadsHashMatched => DownloadHashMatchedSubtitles || OptionsVersion < 2;
        public string KeepLanguages { get; set; } = "eng";

        /// <summary>SubRemover's "...at most this many downloads a day"; 0 = no limit.</summary>
        public int HashDownloadsPerDay { get; set; } = 10;

        /// <summary>Text subtitle codecs: a language with one of these embedded already has an exact subtitle.</summary>
        private static readonly HashSet<string> TextCodecs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text", "microdvd", "subviewer", "subviewer1",
            "realtext", "sami", "stl", "jacosub", "mpl2", "pjs", "vplayer", "ttml"
        };

        public static bool IsTextSubtitle(string codecType, string codec) =>
            string.Equals(codecType, "subtitle", StringComparison.OrdinalIgnoreCase) && codec != null && TextCodecs.Contains(codec);

        public IEnumerable<string> KeptLanguages() =>
            (KeepLanguages ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => Core.Languages.Normalize(l.Trim()));

        /// <returns>Null when SubRemover isn't installed or its settings can't be read: AudioRemover then leaves it alone.</returns>
        public static SubRemoverSettings Read(string configurationsFolder, IJsonSerializer json)
        {
            try
            {
                var path = Path.Combine(configurationsFolder, FileName);
                return File.Exists(path) ? json.DeserializeFromFile<SubRemoverSettings>(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
