using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Emby.AudioRemover.Core
{
    /// <summary>
    /// Takes ads out of the names in a video file, following the "Text to remove" list: website names like
    /// "- SomeSite.com" that download sites put on the file and every track, words such as a release group's tag, and
    /// regular expressions. A title that's only the download's file or release name goes too.
    /// </summary>
    public sealed class TitleCleaner
    {
        private static readonly string[] DefaultEndings =
        {
            "com", "net", "org", "info", "biz", "co", "io", "me", "to", "tv", "cc", "in", "ws", "xyz", "top", "site", "club",
            "live", "pro", "eu", "uk", "ru", "se", "nl", "la", "ag", "pw", "mx", "lol", "vip", "win"
        };

        /// <summary>The list the settings page starts with and "Reset to default" puts back: "*.com" is any website ending in .com.</summary>
        public static readonly string DefaultList = string.Join("\n", DefaultEndings.Select(e => "*." + e));

        private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        // Per title, so a badly written regex from the list can't hang a run.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        private static readonly Regex EmptyBrackets = new Regex(@"\(\s*\)|\[\s*\]|\{\s*\}|<\s*>", RegexOptions.CultureInvariant);

        // "English -  - Forced" after the ad in the middle went.
        private static readonly Regex DoubleSeparator = new Regex(@"\s+[-|~•·–—/](?:\s+[-|~•·–—/])+\s+", RegexOptions.CultureInvariant);

        private static readonly Regex Spaces = new Regex(@"\s{2,}", RegexOptions.CultureInvariant);

        private static readonly char[] LeftoverSeparators = { ' ', '-', '_', '|', '~', ':', '@', '•', '·', '–', '—', '/', '\\', ';', '+', '*', '#', '=' };

        // "My Movie (2020).mkv": the downloaded file's name.
        private static readonly Regex FileName = new Regex(@"\.(?:mkv|mk3d|mp4|m4v|avi|ts|m2ts|mts|mov|webm|wmv|mpg|mpeg)$", Options);

        // What a release name has besides the title and year: resolution, source or codec ("My.Movie.2020.1080p.WEBRip.x265-GROUP").
        private static readonly Regex ReleaseTag = new Regex(
            @"(?<![a-z0-9])(?:480p|576p|720p|1080p|2160p|4320p|web-?dl|webrip|bluray|blu-ray|bdrip|brrip|dvdrip|hdtv|hdrip|remux|x264|x265|h264|h265|hevc|xvid|10bit)(?![a-z0-9])",
            Options);

        // The list is typed in by hand; these keep a mistake, or a list pasted from somewhere, from doing harm.
        private const int MaxEntries = 300;
        private const int MaxEntryLength = 200;
        private const int MaxEndingLength = 30;

        private static readonly Regex EndingText = new Regex(@"^[a-z0-9-]+(?:\.[a-z0-9-]+)*$", Options);

        // A repeat inside a repeated group, like (a+)+ or (\w*)*: the classic way to make a regex take forever.
        private static readonly Regex NestedRepeat = new Regex(
            @"\((?:[^()\\]|\\.)*(?:[+*]|\{\d+,\d*\})(?:[^()\\]|\\.)*\)(?:[+*]|\{\d+,\d*\})", RegexOptions.CultureInvariant, RegexTimeout);

        // Titles found everywhere: an entry that takes the whole of one would empty nearly every title in the library.
        private static readonly string[] OrdinaryTitles = { "English", "Forced", "SDH", "Commentary", "Dolby Digital 5.1", "European (Forced)" };

        // Long runs of one kind of character that end wrong: what makes a badly written regex backtrack for ages.
        private static readonly string[] StressTitles =
        {
            new string('a', 40) + "!", new string('1', 40) + "!", "a" + new string(' ', 40) + "!", string.Concat(Enumerable.Repeat("a.", 20)) + "!"
        };

        // Last: static fields are set in order, and building it needs the ones above.
        public static readonly TitleCleaner Default = new TitleCleaner(DefaultList);

        private sealed class Rule
        {
            public string Entry;
            public Regex Regex;
            public bool SwitchedOff;
        }

        private readonly List<Rule> _rules = new List<Rule>();
        private readonly Action<string> _onSwitchedOff;

        /// <param name="list">One entry per line (commas work too): "*.com", "/regex/", or a word.</param>
        /// <param name="onSwitchedOff">Told about an entry that took too long on a title and is no longer used.</param>
        public TitleCleaner(string list, Action<string> onSwitchedOff = null)
        {
            _onSwitchedOff = onSwitchedOff;
            var entries = Parse(list);
            var problems = new List<string>();
            if (entries.Count > MaxEntries)
            {
                problems.Add($"only the first {MaxEntries} entries are used; the other {entries.Count - MaxEntries} are skipped");
                entries = entries.Take(MaxEntries).ToList();
            }

            var endings = new List<string>();
            foreach (var entry in entries.Where(IsEnding))
            {
                var ending = entry.Substring(2).Trim('.');
                if (ending.Length > 0 && ending.Length <= MaxEndingLength && EndingText.IsMatch(ending)) endings.Add(ending);
                else problems.Add($"'{entry}' isn't a website ending: use letters, like *.com");
            }
            if (endings.Count > 0) _rules.Add(new Rule { Entry = "*." + string.Join(", *.", endings), Regex = WebsiteRule(endings) });

            foreach (var entry in entries.Where(e => !IsEnding(e)))
            {
                var problem = Build(entry, out var regex);
                if (problem != null) problems.Add($"'{Shorten(entry)}' {problem}");
                else _rules.Add(new Rule { Entry = entry, Regex = regex });
            }

            EntryCount = entries.Count;
            Problems = problems;
            Signature = string.Join("\n", entries);
        }

        /// <summary>Entries that are skipped, each with the reason, for the settings page and the run log.</summary>
        public IReadOnlyList<string> Problems { get; }

        /// <summary>How many entries the list has, skipped ones included.</summary>
        public int EntryCount { get; }

        /// <summary>The entries in use, for the settings signature.</summary>
        public string Signature { get; }

        private static string Shorten(string entry) => entry.Length <= 60 ? entry : entry.Substring(0, 57) + "...";

        /// <returns>Why the entry can't be used, or null with its regex.</returns>
        private static string Build(string entry, out Regex regex)
        {
            regex = null;
            if (entry.Length > MaxEntryLength) return $"is longer than {MaxEntryLength} characters";

            if (!IsRegex(entry))
            {
                // Whole words only, so "YTS" doesn't eat into "Bytes". An underscore is a gap, as release names use it for one
                // ("GROUP_TAG"), and a space in the entry matches any gap: "SITE TAG" also takes "SITE_TAG".
                var words = Regex.Escape(entry).Replace(@"\ ", @"[\s._-]+");
                regex = new Regex(@"(?<![\p{L}\p{N}])" + words + @"(?![\p{L}\p{N}])", Options, RegexTimeout);
                return TakesOrdinaryTitles(regex);
            }

            var pattern = entry.Substring(1, entry.Length - 2);
            try
            {
                regex = new Regex(pattern, Options, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                return "isn't a valid regular expression (" + ex.Message + ")";
            }

            try
            {
                if (NestedRepeat.IsMatch(pattern)) return "has a repeat inside a repeat, like (a+)+, which can take forever: write it without";
                if (regex.IsMatch(string.Empty)) return "also matches nothing at all, which would put gaps all through every title";
                var ordinary = TakesOrdinaryTitles(regex);
                if (ordinary != null) return ordinary;
                foreach (var title in StressTitles) regex.Replace(title, " ");
                return null;
            }
            catch (RegexMatchTimeoutException)
            {
                return "is too slow on some titles";
            }
        }

        private static string TakesOrdinaryTitles(Regex regex)
        {
            var taken = OrdinaryTitles.FirstOrDefault(t =>
            {
                var m = regex.Match(t);
                return m.Success && m.Length == t.Length;
            });
            return taken == null ? null : $"would remove ordinary titles like '{taken}'";
        }

        private static bool IsEnding(string entry) => entry.StartsWith("*.", StringComparison.Ordinal);

        private static bool IsRegex(string entry) => entry.Length > 2 && entry[0] == '/' && entry[entry.Length - 1] == '/';

        // A regex is never split at its commas ("/a{1,3}/").
        private static List<string> Parse(string list) =>
            (list ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .SelectMany(line => IsRegex(line) ? new[] { line } : line.Split(','))
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        // A name with one of the endings, standing on its own: "SomeSite.com", "www.site.net", "site.co.uk", "https://site.in/x",
        // with a "Downloaded from" in front of it or an "Encodes" after it. Not part of a dotted name ("Back.to.the.Future")
        // or a number ("5.1").
        private static Regex WebsiteRule(IEnumerable<string> endings) => new Regex(
            @"(?:\b(?:downloaded|encoded|ripped|uploaded)\s+(?:from|by|at|on)\s+)?" +
            @"(?<![\w.-])(?:https?://)?(?:www\.)?[a-z0-9][a-z0-9-]*(?:\.(?:co|com|net|org))?" +
            @"\.(?:" + string.Join("|", endings.Select(Regex.Escape)) + ")" +
            @"(?![\w.-])(?:/[^\s|]*)?" +
            @"(?:\s+(?:encodes?|encoded|rips?|releases?)\b)?",
            Options, RegexTimeout);

        /// <returns>
        /// The title without its ads; "" when nothing else is left or what's left is only a file or release name; the same
        /// string when there was nothing to clean.
        /// </returns>
        public string Clean(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;

            var text = title;
            foreach (var rule in _rules.Where(r => !r.SwitchedOff))
            {
                try
                {
                    text = rule.Regex.Replace(text, " ");
                }
                catch (RegexMatchTimeoutException)
                {
                    // Too slow once is likely too slow again: switched off, so a run over thousands of videos doesn't stall.
                    rule.SwitchedOff = true;
                    _onSwitchedOff?.Invoke(rule.Entry);
                }
            }
            if (text != title)
            {
                text = EmptyBrackets.Replace(text, " ");
                text = DoubleSeparator.Replace(text, m => " " + m.Value.Trim()[0] + " ");
                text = Spaces.Replace(text, " ").Trim(LeftoverSeparators);
            }
            return IsFileOrReleaseName(text) ? string.Empty : text;
        }

        // Says nothing a player can't show better itself. Real titles have spaces ("English mp4a.40.2", "H.264 - Main Profile")
        // or no release tag ("Back.to.the.Future").
        private static bool IsFileOrReleaseName(string text) =>
            FileName.IsMatch(text)
            || (text.IndexOf(' ') < 0 && text.Count(c => c == '.') >= 2 && ReleaseTag.IsMatch(text));
    }
}
