using System;
using System.Collections.Generic;
using System.Linq;

namespace Emby.AudioRemover.Core
{
    public class PlannerSettings
    {
        public ISet<string> KeepLanguages { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool KeepUndefined { get; set; } = true;
        public bool KeepFirstTrack { get; set; }
        public bool RemoveCommentary { get; set; }
        public bool RemoveAudioDescription { get; set; }
        public bool SetDefault { get; set; } = true;

        /// <summary>Language whose main track gets the default, when it's kept. Null: the video's own default decides.</summary>
        public string DefaultLanguage { get; set; }

        /// <summary>The default goes to the best-sounding kept track in the language that would get it.</summary>
        public bool BestAudioDefault { get; set; }

        /// <summary>Takes what <see cref="Titles"/> lists out of the track names.</summary>
        public bool CleanTitles { get; set; }

        /// <summary>With <see cref="CleanTitles"/>: the file's own title too.</summary>
        public bool CleanFileTitle { get; set; }

        /// <summary>Names alone are reason enough to rewrite a video. Off: they're only cleaned in a rewrite for the audio.</summary>
        public bool RewriteForTitles { get; set; }

        public TitleCleaner Titles { get; set; } = TitleCleaner.Default;

        /// <summary>
        /// Entries added from the suggestions: names with any of them get a rewrite of their own, even with
        /// <see cref="RewriteForTitles"/> off. Null when there are none or it's switched off.
        /// </summary>
        public TitleCleaner CleanAdded { get; set; }

        /// <summary>A track's language is what Whisper heard, when it's sure, rather than its tag.</summary>
        public bool UseHeardLanguages { get; set; }

        /// <summary>Heard languages are double-checked before they're used. Only here for <see cref="Signature"/>: the listening does it.</summary>
        public bool DoubleCheckHeard { get; set; } = true;

        /// <summary>A lone remaining track with a tag not in <see cref="KeepLanguages"/> is listened to and retagged. For <see cref="Signature"/>.</summary>
        public bool RetagLoneTrack { get; set; }

        /// <summary>A video whose tracks all lack a language tag gets the ones Whisper heard, in a rewrite of its own if need be.</summary>
        public bool TagUntaggedTracks { get; set; }

        public static ISet<string> ParseLanguages(string csv) =>
            new HashSet<string>(
                (csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Languages.Normalize),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>Changes whenever a setting that affects the plan changes, so already-checked videos get checked again.</summary>
        public string Signature() => string.Join("|",
            string.Join(",", KeepLanguages.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)),
            KeepUndefined, KeepFirstTrack, RemoveCommentary, RemoveAudioDescription, SetDefault, DefaultLanguage, BestAudioDefault,
            CleanTitles, CleanFileTitle, RewriteForTitles, Titles.Signature, CleanAdded?.Signature, UseHeardLanguages, UseHeardLanguages && DoubleCheckHeard,
            RetagLoneTrack, RetagLoneTrack && DoubleCheckHeard, TagUntaggedTracks, TagUntaggedTracks && DoubleCheckHeard);
    }

    /// <summary>A name with an ad or release name in it. StreamIndex is null for the file's own title; After is "" when nothing is left.</summary>
    public class TitleChange
    {
        public int? StreamIndex { get; set; }
        public string Before { get; set; }
        public string After { get; set; }
    }

    public class AudioPlan
    {
        public List<ProbeStream> Keep { get; } = new List<ProbeStream>();
        public List<ProbeStream> Remove { get; } = new List<ProbeStream>();

        /// <summary>Kept track that gets the "default" flag; every other kept audio track loses it. Null leaves the flags alone.</summary>
        public ProbeStream NewDefault { get; set; }

        /// <summary>Why nothing is removed although some tracks aren't in a kept language, for the log. Null otherwise.</summary>
        public string LeftAlone { get; set; }

        /// <summary>Names to clean: the file's title first (when asked), then the kept tracks'. Removed tracks' names go with them.</summary>
        public List<TitleChange> TitleChanges { get; } = new List<TitleChange>();

        /// <summary>Names that need cleaning but don't get a rewrite of their own ("Rewrite a video just for clean-up" is off).</summary>
        public List<TitleChange> TitlesLeft { get; } = new List<TitleChange>();

        public bool NeedsRemux => Remove.Count > 0 || NewDefault != null || TitleChanges.Count > 0 || LanguageChanges.Count > 0;
        public bool HasWork => NeedsRemux;

        /// <summary>Kept tracks whose tag says another language than Whisper heard, by input stream index: the language it heard.</summary>
        public Dictionary<int, string> LanguageChanges { get; } = new Dictionary<int, string>();

        /// <summary>New track names, by input stream index; "" removes the name.</summary>
        public IDictionary<int, string> StreamTitles() =>
            TitleChanges.Where(c => c.StreamIndex != null).ToDictionary(c => c.StreamIndex.Value, c => c.After);

        /// <summary>The file's new title, "" to remove it, or null to leave it as it is.</summary>
        public string FileTitle => TitleChanges.FirstOrDefault(c => c.StreamIndex == null)?.After;

        /// <summary>Default flags to change, by input stream index: true sets it, false clears it.</summary>
        public IDictionary<int, bool> DefaultChanges()
        {
            var changes = new Dictionary<int, bool>();
            if (NewDefault == null) return changes;
            foreach (var s in Keep)
            {
                if (s == NewDefault && !s.IsDefault) changes[s.index] = true;
                else if (s != NewDefault && s.IsDefault) changes[s.index] = false;
            }
            return changes;
        }
    }

    public static class AudioPlanner
    {
        /// <summary>
        /// The one audio track a plan keeps, when it's the only one left and it's tagged with a language you don't keep, in
        /// a container that stores tags: the track "Check and retag a lone track" listens to. A track with no language tag
        /// is left as it is. Null otherwise.
        /// </summary>
        public static ProbeStream LoneTrackToCheck(AudioPlan plan, PlannerSettings settings, string videoPath)
        {
            var kept = plan.Keep.Where(s => s.IsAudio).ToList();
            if (kept.Count != 1 || !FfmpegArgs.CanSetDefaultFlags(videoPath)) return null;
            var language = kept[0].Language;
            return language == "und" || settings.KeepLanguages.Contains(language) ? null : kept[0];
        }

        /// <summary>Adds the lone track's corrected tag, when Whisper was sure of a language that isn't its tag.</summary>
        public static void Retag(AudioPlan plan, ProbeStream lone)
        {
            if (HeardAnotherLanguage(lone)) plan.LanguageChanges[lone.index] = lone.Heard.Language;
        }

        /// <summary>Two or more audio tracks and none with a language tag: what "Tag tracks that have no language" is for.</summary>
        public static bool HasOnlyUntaggedTracks(ProbeResult probe)
        {
            var audio = probe.streams.Where(s => s.IsAudio).ToList();
            return audio.Count > 1 && audio.All(s => s.Language == "und");
        }

        // Whisper was sure of a language other than the tag. Only codes in the language list: Whisper's own two-letter code
        // for a rare language isn't a tag players know.
        private static bool HeardAnotherLanguage(ProbeStream s) =>
            s.Heard?.Language != null && s.Heard.Language != s.Language && Languages.IsKnown(s.Heard.Language);

        public static AudioPlan Plan(ProbeResult probe, PlannerSettings settings, string videoPath)
        {
            var plan = new AudioPlan();
            var audio = probe.streams.Where(x => x.IsAudio).ToList();
            var chosen = ChooseTracks(plan, audio, settings);
            var canFlag = FfmpegArgs.CanSetDefaultFlags(videoPath);
            var titles = settings.CleanTitles && canFlag
                ? TitleChangesFor(probe, plan.Remove, settings.Titles, settings.CleanFileTitle)
                : new List<TitleChange>();

            // Decided before the default, so a video rewritten for its track names gets its default tidied for free. Text added
            // from the suggestions is reason enough by itself: only those videos, not every one with something to clean.
            var rewriteForTitles = titles.Count > 0 && (settings.RewriteForTitles
                || (settings.CleanAdded != null && TitleChangesFor(probe, plan.Remove, settings.CleanAdded, settings.CleanFileTitle).Count > 0));
            var tagsUntagged = settings.TagUntaggedTracks && canFlag && HasOnlyUntaggedTracks(probe);
            if (chosen && settings.SetDefault && canFlag)
                plan.NewDefault = ChooseDefault(audio, plan.Keep, plan.Remove.Count > 0 || rewriteForTitles, settings, tagsUntagged);

            // Without a rewrite of their own, names are cleaned whenever the video is rewritten for its audio anyway.
            if (plan.Remove.Count > 0 || plan.NewDefault != null || rewriteForTitles) plan.TitleChanges.AddRange(titles);
            else plan.TitlesLeft.AddRange(titles);

            // No tags at all: what was heard is a reason for a rewrite on its own. Before the wrong tags below, so a video
            // rewritten for this gets those put right too.
            if (tagsUntagged)
            {
                foreach (var s in plan.Keep.Where(HeardAnotherLanguage)) plan.LanguageChanges[s.index] = s.Heard.Language;
            }

            // A wrong tag is put right whenever the video is rewritten anyway; it's no reason for a rewrite on its own.
            if (settings.UseHeardLanguages && canFlag && plan.NeedsRemux)
            {
                foreach (var s in plan.Keep.Where(HeardAnotherLanguage)) plan.LanguageChanges[s.index] = s.Heard.Language;
            }
            return plan;
        }

        // Splits the audio into kept and removed. False when every track stays because the video has no audio or none of
        // its main tracks is in a kept language; its default then stays as it is too.
        private static bool ChooseTracks(AudioPlan plan, IList<ProbeStream> audio, PlannerSettings settings)
        {
            if (audio.Count == 0) return false;

            var first = audio[0];
            var byLanguage = audio.Where(x => KeptByLanguage(x, settings) || (settings.KeepFirstTrack && x == first)).ToList();

            // Never leave a video without sound: with nothing in a kept language (a foreign film with only its own
            // language), every track stays.
            var main = byLanguage.Where(IsMain).ToList();
            if (main.Count == 0)
            {
                plan.Keep.AddRange(audio);
                if (audio.Any(x => !KeptByLanguage(x, settings)))
                    plan.LeftAlone = "no main audio track in a language you keep, so every audio track stays";
                return false;
            }

            foreach (var s in audio)
            {
                // Commentary and audio description only go while a main track stays, which it always does here.
                var keep = byLanguage.Contains(s)
                           && !(settings.RemoveCommentary && s.IsCommentary)
                           && !(settings.RemoveAudioDescription && s.IsAudioDescription);
                (keep ? plan.Keep : plan.Remove).Add(s);
            }
            return true;
        }

        // The names of the tracks that stay (video, kept audio, subtitles), and the file's own title when asked.
        private static List<TitleChange> TitleChangesFor(ProbeResult probe, ICollection<ProbeStream> removed, TitleCleaner titles, bool fileTitle)
        {
            var names = new List<(int? Index, string Title)>();
            var title = fileTitle ? probe.format?.Tag("title") : null;
            if (title != null) names.Add((null, title));
            names.AddRange(probe.streams.Where(s => !removed.Contains(s)).Select(s => ((int?)s.index, s.Title)));

            return names
                .Select(n => new TitleChange { StreamIndex = n.Index, Before = n.Title, After = titles.Clean(n.Title) })
                .Where(c => c.After != c.Before)
                .ToList();
        }

        /// <summary>A track with the film's sound: not commentary, not audio description.</summary>
        private static bool IsMain(ProbeStream s) => !s.IsCommentary && !s.IsAudioDescription;

        private static bool SameLanguage(string a, string b) => b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // A chosen language overrides the video: its first main track becomes the only default. Otherwise the video
        // decides: a flag on a kept main track stays, and when the flagged track goes or is commentary/description, the
        // first kept main track takes it, so players don't start on whatever happens to be first. A video with no flag
        // gets one only when it's rewritten anyway. Asked for the best audio, it then goes to the best-sounding main track
        // in that same language.
        private static ProbeStream ChooseDefault(IList<ProbeStream> audio, IList<ProbeStream> kept, bool rewritten, PlannerSettings settings, bool tagsUntagged)
        {
            var flagged = audio.FirstOrDefault(x => x.IsDefault);
            var target = kept.FirstOrDefault(x => IsMain(x) && SameLanguage(LanguageOf(x, settings), settings.DefaultLanguage));
            if (target == null)
            {
                if (flagged != null && kept.Contains(flagged) && IsMain(flagged)) target = flagged;
                else if (flagged == null && !rewritten) return null;
                else target = kept.FirstOrDefault(IsMain);
                if (target == null) return null;
            }

            if (settings.BestAudioDefault)
            {
                // The language a track has, or gets in this rewrite. With none it could be any, so it's compared with nothing.
                string Language(ProbeStream x) => tagsUntagged && HeardAnotherLanguage(x) ? x.Heard.Language : LanguageOf(x, settings);
                var language = Language(target);
                if (language != "und")
                {
                    // Most channels, then lossless, then the higher bitrate; on a tie the track that had it keeps it.
                    target = kept.Where(x => IsMain(x) && SameLanguage(Language(x), language))
                        .OrderByDescending(x => x.channels).ThenByDescending(x => x.IsLossless).ThenByDescending(x => x.BitRate)
                        .ThenByDescending(x => x == target)
                        .First();
                }
            }

            // Players start on the first flagged track. When that's already the right one, a second flag isn't worth
            // rewriting the whole file for; it's only cleared when the video is rewritten anyway.
            if (!rewritten && flagged == target) return null;

            var keptFlagged = kept.Where(x => x.IsDefault).ToList();
            return keptFlagged.Count == 1 && keptFlagged[0] == target ? null : target;
        }

        /// <summary>What Whisper heard, when that's what decides and it was sure; otherwise the tag.</summary>
        private static string LanguageOf(ProbeStream s, PlannerSettings settings) =>
            settings.UseHeardLanguages && s.Heard?.Language != null ? s.Heard.Language : s.Language;

        private static bool KeptByLanguage(ProbeStream s, PlannerSettings settings)
        {
            var language = LanguageOf(s, settings);
            if (settings.KeepLanguages.Contains(language)) return true;
            return language == "und" && settings.KeepUndefined;
        }
    }
}
