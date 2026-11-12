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

        /// <summary>
        /// SubRemover's settings file is there but couldn't be read (SubRemover may have been saving it): whether a video
        /// needs a look-up first isn't known, so videos are left unchanged for the next run.
        /// </summary>
        public bool Unreadable { get; private set; }

        /// <param name="subRemoverLoaded">Whether SubRemover is installed in this Emby; null: look it up.</param>
        /// <returns>
        /// Null when SubRemover isn't installed. SubRemover's defaults (look-up on) when it is but hasn't saved its settings
        /// yet. <see cref="Unreadable"/> when the file can't be read.
        /// </returns>
        public static SubRemoverSettings Read(string configurationsFolder, IJsonSerializer json, bool? subRemoverLoaded = null)
        {
            var path = Path.Combine(configurationsFolder, FileName);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                        return (subRemoverLoaded ?? IsSubRemoverLoaded()) ? new SubRemoverSettings { OptionsVersion = 2 } : null;
                    var settings = json.DeserializeFromFile<SubRemoverSettings>(path);
                    if (settings != null) return settings;
                }
                catch (Exception)
                {
                }
                System.Threading.Thread.Sleep(300);
            }
            return new SubRemoverSettings { Unreadable = true };
        }

        private static bool IsSubRemoverLoaded() =>
            AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.GetName().Name, "Emby.Subremover", StringComparison.OrdinalIgnoreCase));
    }
}
