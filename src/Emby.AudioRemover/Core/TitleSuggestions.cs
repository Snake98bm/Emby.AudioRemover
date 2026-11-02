using System;
using System.Collections.Generic;
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

        /// <summary>One track name it's in, with its film or show, for the settings page.</summary>
        public string Example { get; set; }
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

        // Words that describe a track rather than advertise. Language names come from the language list on top of these.
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
            "українська", "日本語", "한국어", "中文", "हिन्दी"
        };

        private static readonly Regex Bracketed = new Regex(@"\[[^\[\]]{2,40}\]", RegexOptions.CultureInvariant);
        private static readonly Regex Separators = new Regex(@"[\s|/\\,;:~•·–—_+=*#()\[\]{}""]+", RegexOptions.CultureInvariant);

        /// <param name="names">Each track name, with the film or show it belongs to.</param>
        /// <param name="current">The list as it is now: what it already removes isn't suggested again.</param>
        public static List<TitleSuggestion> Find(IEnumerable<(string Video, string Title)> names, TitleCleaner current)
        {
            var found = new Dictionary<string, Candidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var (video, title) in names)
            {
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

            return found.Values
                .Where(c => c.Videos.Count >= MinVideos)
                // The same safety checks as anything typed into the box: nothing that would empty ordinary names.
                .Where(c => new TitleCleaner(c.Entry).Problems.Count == 0)
                .OrderByDescending(c => c.Videos.Count).ThenBy(c => c.Entry, StringComparer.OrdinalIgnoreCase)
                .Take(MostShown)
                .Select(c => new TitleSuggestion { Entry = c.Entry, Videos = c.Videos.Count, Example = c.Example })
                .ToList();
        }

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

        private static bool IsCandidateWord(string word) =>
            word.Length >= 2
            && word.Any(char.IsLetter)
            && !word.Any(char.IsDigit)
            && !Describing.Contains(word)
            && !Languages.All.Any(l => string.Equals(l.Name, word, StringComparison.OrdinalIgnoreCase))
            && !Languages.IsKnown(word);
    }
}
