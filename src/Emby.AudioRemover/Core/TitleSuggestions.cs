using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emby.AudioRemover.Core
{
    /// <summary>One line the "Text to remove" list could get, with how widely it turns up.</summary>
    public sealed class TitleSuggestion
    {
        public string Entry { get; set; }

        /// <summary>How many different films and shows have it in a track name.</summary>
        public int Videos { get; set; }

        /// <summary>One track name it's in, with its film or show.</summary>
        public string Example { get; set; }

        /// <summary>The films and shows it's in, by name, A to Z.</summary>
        public List<string> VideoNames { get; set; } = new List<string>();
    }

    /// <summary>
    /// Looks through track names for what the "Text to remove" list misses: after the list has done its work, words and
    /// bracketed tags that keep turning up across different films and shows, but describe nothing - a release group's or a
    /// site's tag, like "ETRG" or "[SomeSite Torrents]".
    /// </summary>
    public static class TitleSuggestions
    {
        /// <summary>Different films or shows a word has to turn up in: one show's own name ("My Show Season 4") never counts.</summary>
        public const int MinVideos = 2;

        public const int MostShown = 30;

        /// <summary>Films and shows kept per suggestion across runs: enough to show, without the file growing with every episode.</summary>
        public const int MostNamesKept = 50;

        // Words that describe a track rather than advertise. Language names come from the language list on top of these, and
        // countries and regions ("English (United States)") from Regions.
        private static readonly HashSet<string> Describing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audio", "track", "tracks", "stereo", "mono", "surround", "dolby", "digital", "atmos", "dts", "hd", "ma", "truehd", "true",
            "aac", "ac", "eac", "flac", "pcm", "mp", "opus", "vorbis", "lpcm", "lc", "he", "dd", "ddp", "plus", "dts-hd", "dts-x", "e-ac3",
            "dub", "dubbed", "dubbing", "original", "version", "commentary", "commentaries", "director", "directors", "director's",
            "producer", "producers", "writer", "writers", "cast", "crew", "actor", "actors", "with", "and", "the", "of", "by", "for",
            "an", "on", "in", "at", "to", "from", "description", "descriptive", "described", "narration", "narrated", "visually",
            "impaired", "ad", "forced", "sdh", "cc", "hi", "full", "signs", "songs", "sign", "song", "main", "feature", "default",
            "music", "score", "isolated", "effects", "sound", "kbps", "kbs", "chnls", "channels", "channel", "ch", "bit", "khz",
            "hz", "mbps", "lossless", "lossy", "mixed", "mix", "remix", "remastered", "restored", "theatrical", "extended", "edition",
            "cut", "uncut", "unrated", "international", "latino", "latin", "european", "castilian", "canadian", "brazilian",
            "simplified", "traditional", "mandarin", "cantonese", "flemish", "american", "british", "uk", "us", "usa", "season",
            "episode", "part", "chapter", "special", "bonus", "trailer", "unknown", "undefined", "track1", "vo", "vf", "vff", "vfq",
            "own", "cinema", "imax", "ita", "eng", "spa", "fre", "ger", "por", "rus", "jpn", "kor", "chi", "hin", "italiano",
            "español", "espanol", "castellano", "français", "francais", "deutsch", "português", "portugues", "nederlands",
            "svenska", "norsk", "dansk", "suomi", "polski", "magyar", "čeština", "cesky", "türkçe", "turkce", "ελληνικά", "русский",
            "українська", "日本語", "한국어", "中文", "हिन्दी",
            // Subtitle kinds and labels
            "srt", "ass", "ssa", "pgs", "sup", "idx", "vobsub", "sub", "subs", "subbed", "subtitle", "subtitles", "closed",
            "caption", "captions", "hearing", "deaf", "regular", "normal", "normaal", "stripped", "hq", "lq", "white", "yellow",
            "wit", "geel", "weiß", "weiss", "gelb", "blanc", "jaune", "blanco", "amarillo", "file", "captured",
            // Format, encoding and source
            "video", "stream", "streams", "bitrate", "kb", "kbit", "kbits", "master", "remaster", "core", "hdma", "dtshd", "shd",
            "compatibility", "encoded", "encode", "encoder", "encoding", "using", "dvd", "dvdrip", "bluray", "blu-ray", "bdrip",
            "brrip", "bdremux", "remux", "webrip", "web-dl", "webdl", "web", "hdtv", "hdrip", "hevc", "avc", "uhd", "hdr", "sdr",
            "dv", "multi", "multisub", "multisubs", "dual", "proper", "repack", "ntsc", "pal",
            // Scripts and varieties of a language
            "modern", "cyrillic", "latinica", "cirilica", "ćirilica", "latn", "简体", "繁體", "繁体", "普通话", "國語", "国语",
            "粵語", "粤语", "廣東話", "广东话"
        };

        // Languages named in their own language ("Bahasa Melayu", "Lietuvių", "Български"), which the language list doesn't
        // spell; the server's own culture names add the rest where it has them.
        private static readonly HashSet<string> NativeNames = BuildNativeNames(
            "עברית ไทย Bahasa Melayu Lietuvių Slovenčina Slovenščina Slovenski Български Eesti Latviešu العربية عربي Bokmål Nynorsk " +
            "தமிழ் తెలుగు Română Tiếng Việt Íslenska Català Hrvatski Srpski Српски Македонски Bosanski Shqip Gaeilge Cymraeg Euskara " +
            "Galego Malti Lëtzebuergesch Azərbaycan Қазақ Oʻzbek ქართული Հայերեն नेपाली සිංහල ລາວ ខ្មែរ မြန်မာ বাংলা मराठी ಕನ್ನಡ മലയാളം " +
            "ગુજરાતી ਪੰਜਾਬੀ اردو فارسی Kiswahili Filipino Tagalog Afrikaans Føroyskt Kalaallisut " +
            // ISO 639-3 codes some tools write instead: standard Estonian, Latvian, Malay, Mongolian, Chinese, Cantonese...
            "ekk lvs zsm khk cmn yue nob nno arb pes prs swh uzn azj ydd npi ory pbt ckb kmr");

        private static HashSet<string> BuildNativeNames(string names)
        {
            var words = new HashSet<string>(names.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
            // Its own split: static fields are set in order, and Separators comes later.
            var split = new[] { ' ', '(', ')', ',', '（', '）' };
            try
            {
                foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
                    foreach (var word in (culture.NativeName + " " + culture.EnglishName).Split(split, StringSplitOptions.RemoveEmptyEntries))
                        if (word.Length >= 2) words.Add(word);
            }
            catch (Exception)
            {
                // A server without culture data (invariant mode) has the list above.
            }
            return words;
        }

        // Commentary names people ("Commentary by director Jane Doe"): its words are never ads.
        private static readonly Regex Commentary = new Regex(@"comment(ary|aries|aire|aires|ario|ari|o)\b|kommentar",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Bracketed = new Regex(@"\[[^\[\]]{2,40}\]", RegexOptions.CultureInvariant);

        // Full-width brackets and the ideographic comma too, as in "中文（简体）".
        private static readonly Regex Separators = new Regex(@"[\s|/\\,;:~•·–—_+=*#()\[\]{}""（）【】「」、]+", RegexOptions.CultureInvariant);
        private static readonly StringComparer Text = StringComparer.OrdinalIgnoreCase;

        /// <param name="names">Each track name, with the film or show it belongs to.</param>
        /// <param name="current">The list as it is now: what it already removes isn't suggested again.</param>
        public static List<TitleSuggestion> Find(IEnumerable<(string Video, string Title)> names, TitleCleaner current) =>
            Collect(names, current).Values
                .Where(c => c.Videos.Count >= MinVideos)
                // The same safety checks as anything typed into the box: nothing that would empty ordinary names.
                .Where(c => new TitleCleaner(c.Entry).Problems.Count == 0)
                .OrderByDescending(c => c.Videos.Count).ThenBy(c => c.Entry, Text)
                .Take(MostShown)
                .Select(c => new TitleSuggestion
                {
                    Entry = c.Entry, Videos = c.Videos.Count, Example = c.Example,
                    VideoNames = c.Videos.OrderBy(v => v, Text).ToList()
                })
                .ToList();

        // Every candidate in these names, however few films or shows it's in.
        private static Dictionary<string, Candidate> Collect(IEnumerable<(string Video, string Title)> names, TitleCleaner current)
        {
            var found = new Dictionary<string, Candidate>(Text);
            foreach (var (video, title) in names)
            {
                if (Commentary.IsMatch(title)) continue;
                var left = current.Clean(title);
                if (string.IsNullOrWhiteSpace(left)) continue;
                // A word from the film's or show's own name ("My Movie - English") is its title, not an ad.
                foreach (var entry in CandidatesIn(left).Where(e => video.IndexOf(e, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    if (!found.TryGetValue(entry, out var candidate))
                        found[entry] = candidate = new Candidate { Entry = entry, Example = $"{video}: \"{title}\"" };
                    candidate.Videos.Add(video);
                }
            }
            return found;
        }

        /// <summary>
        /// For the run log: each suggestion on its own line with the films and shows it turned up in, by name (no folders or
        /// files). Long lists are cut short; the settings page's "Suggested text" has the rest.
        /// </summary>
        public static string Describe(IList<TitleSuggestion> found, int most = 10, int namesEach = 10) =>
            "text in track names the 'Text to remove' list misses (in 2+ films or shows); add it with 'Suggested text' on the settings page:" +
            string.Concat(found.Take(most).Select(s =>
                $"\n    {s.Entry} ({s.Videos}): {string.Join(", ", s.VideoNames.Take(namesEach))}" +
                (s.VideoNames.Count > namesEach ? $" and {s.VideoNames.Count - namesEach} more" : ""))) +
            (found.Count > most ? $"\n    and {found.Count - most} more" : "");

        // ---- "Suggested text": built up across runs

        /// <summary>Adds what these track names have to the suggestions kept across runs.</summary>
        public static void Merge(SuggestionData data, IEnumerable<(string Video, string Title)> names, TitleCleaner current)
        {
            foreach (var c in Collect(names, current).Values)
            {
                if (!data.Seen.TryGetValue(c.Entry, out var videos)) data.Seen[c.Entry] = videos = new List<string>();
                foreach (var video in c.Videos)
                    if (videos.Count < MostNamesKept && !videos.Contains(video, Text)) videos.Add(video);
            }
        }

        /// <summary>
        /// What "Suggested text" offers: what's been seen in at least two films or shows, passes the box's checks, isn't removed
        /// by the list as it is now, and wasn't taken out of the box before. The most widespread, A to Z.
        /// </summary>
        public static List<TitleSuggestion> Waiting(SuggestionData data, TitleCleaner current)
        {
            var dismissed = new HashSet<string>(data.Dismissed, Text);
            return data.Seen
                .Where(s => s.Value.Count >= MinVideos && !dismissed.Contains(s.Key))
                // Kept from before the rules changed: a region, a describing word, or something the list now takes care of.
                .Where(s => CandidatesIn(s.Key).Any(e => Text.Equals(e, s.Key)) && current.Clean(s.Key) == s.Key)
                .Where(s => new TitleCleaner(s.Key).Problems.Count == 0)
                .OrderByDescending(s => s.Value.Count).ThenBy(s => s.Key, Text)
                .Take(MostShown)
                .Select(s => new TitleSuggestion { Entry = s.Key, Videos = s.Value.Count, VideoNames = s.Value.OrderBy(v => v, Text).ToList() })
                .OrderBy(s => s.Entry, Text)
                .ToList();
        }

        /// <summary>The box: what was typed there and what's waiting, one per line, A to Z.</summary>
        public static string BoxText(SuggestionData data, IEnumerable<TitleSuggestion> waiting) =>
            string.Join("\n", data.Typed.Concat(waiting.Select(w => w.Entry)).Distinct(Text).OrderBy(l => l, Text));

        /// <summary>Keeps what was done in the box: a line taken out isn't suggested again; a line typed or changed stays.</summary>
        public static void KeepEdits(SuggestionData data, string shown, string now)
        {
            var before = Lines(shown);
            var after = Lines(now);
            foreach (var gone in before.Where(l => !after.Contains(l, Text)))
            {
                data.Typed.RemoveAll(t => Text.Equals(t, gone));
                if (!data.Dismissed.Contains(gone, Text)) data.Dismissed.Add(gone);
            }
            foreach (var typed in after.Where(l => !before.Contains(l, Text)))
            {
                data.Dismissed.RemoveAll(d => Text.Equals(d, typed));
                if (!data.Typed.Contains(typed, Text)) data.Typed.Add(typed);
            }
        }

        /// <summary>Lines added to the list leave the box, and aren't suggested again even if they're taken off the list later.</summary>
        public static void Added(SuggestionData data, IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                data.Typed.RemoveAll(t => Text.Equals(t, line));
                if (!data.Dismissed.Contains(line, Text)) data.Dismissed.Add(line);
            }
        }

        /// <summary>A box's or list's lines, trimmed, each once.</summary>
        public static List<string> Lines(string text) =>
            (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(Text).ToList();

        private sealed class Candidate
        {
            public string Entry;
            public string Example;
            public readonly HashSet<string> Videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Bracketed tags ("[SomeSite Torrents]") whole, then single words; nothing with digits, which is format detail.</summary>
        internal static IEnumerable<string> CandidatesIn(string name)
        {
            foreach (Match m in Bracketed.Matches(name))
            {
                var inner = m.Value.Substring(1, m.Length - 2);
                if (Words(inner).Any(IsCandidateWord)) yield return m.Value;
            }
            foreach (var word in Words(Bracketed.Replace(name, " ")).Where(IsCandidateWord)) yield return word;
        }

        private static IEnumerable<string> Words(string text) =>
            Separators.Split(text).Select(w => w.Trim('.', '-', '\'', '!', '?')).Where(w => w.Length > 0);

        // A hyphenated word counts only for a part that would: "pt-BR" and "English-SRT" describe the track. A whole word the
        // lists know ("WEB-DL", "DTS-HD") is decided as it is.
        private static bool IsCandidateWord(string word) =>
            word.IndexOf('-') > 0 && IsCandidatePart(word)
                ? word.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries).Any(IsCandidatePart)
                : IsCandidatePart(word);

        private static bool IsCandidatePart(string word) =>
            word.Length >= 2
            && word.Any(char.IsLetter)
            && !word.Any(char.IsDigit)
            && !Describing.Contains(word)
            && !NativeNames.Contains(word)
            && !Regions.Contains(word)
            && !Languages.All.Any(l => string.Equals(l.Name, word, StringComparison.OrdinalIgnoreCase))
            && !Languages.IsKnown(word);
    }
}
