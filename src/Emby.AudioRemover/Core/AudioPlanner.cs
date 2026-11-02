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

        public static ISet<string> ParseLanguages(string csv) =>
            new HashSet<string>(
                (csv ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(Languages.Normalize),
                StringComparer.OrdinalIgnoreCase);

        /// <summary>Changes whenever a setting that affects the plan changes, so already-checked videos get checked again.</summary>
        public string Signature() => string.Join("|",
            string.Join(",", KeepLanguages.OrderBy(l => l, StringComparer.OrdinalIgnoreCase)),
            KeepUndefined, KeepFirstTrack, RemoveCommentary, RemoveAudioDescription, SetDefault, DefaultLanguage);
    }

    public class AudioPlan
    {
        public List<ProbeStream> Keep { get; } = new List<ProbeStream>();
        public List<ProbeStream> Remove { get; } = new List<ProbeStream>();

        /// <summary>Kept track that gets the "default" flag; every other kept audio track loses it. Null leaves the flags alone.</summary>
        public ProbeStream NewDefault { get; set; }

        /// <summary>Why nothing is removed although some tracks aren't in a kept language, for the log. Null otherwise.</summary>
        public string LeftAlone { get; set; }

        public bool NeedsRemux => Remove.Count > 0 || NewDefault != null;
        public bool HasWork => NeedsRemux;

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
        public static AudioPlan Plan(ProbeResult probe, PlannerSettings settings, string videoPath)
        {
            var plan = new AudioPlan();
            var audio = probe.streams.Where(x => x.IsAudio).ToList();
            if (audio.Count == 0) return plan;

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
                return plan;
            }

            foreach (var s in audio)
            {
                // Commentary and audio description only go while a main track stays, which it always does here.
                var keep = byLanguage.Contains(s)
                           && !(settings.RemoveCommentary && s.IsCommentary)
                           && !(settings.RemoveAudioDescription && s.IsAudioDescription);
                (keep ? plan.Keep : plan.Remove).Add(s);
            }

            if (settings.SetDefault && FfmpegArgs.CanSetDefaultFlags(videoPath))
                plan.NewDefault = ChooseDefault(audio, plan.Keep, plan.Remove.Count > 0, settings.DefaultLanguage);
            return plan;
        }

        /// <summary>A track with the film's sound: not commentary, not audio description.</summary>
        private static bool IsMain(ProbeStream s) => !s.IsCommentary && !s.IsAudioDescription;

        private static bool SameLanguage(string a, string b) => b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // A chosen language overrides the video: its first main track becomes the only default. Otherwise the video
        // decides: a flag on a kept main track stays, and when the flagged track goes or is commentary/description, the
        // first kept main track takes it, so players don't start on whatever happens to be first. A video with no flag
        // gets one only when it's rewritten anyway.
        private static ProbeStream ChooseDefault(IList<ProbeStream> audio, IList<ProbeStream> kept, bool removing, string defaultLanguage)
        {
            var target = kept.FirstOrDefault(x => IsMain(x) && SameLanguage(x.Language, defaultLanguage));
            if (target == null)
            {
                var flagged = audio.FirstOrDefault(x => x.IsDefault);
                if (flagged != null && kept.Contains(flagged) && IsMain(flagged)) target = flagged;
                else if (flagged == null && !removing) return null;
                else target = kept.FirstOrDefault(IsMain);
                if (target == null) return null;
            }

            var keptFlagged = kept.Where(x => x.IsDefault).ToList();
            return keptFlagged.Count == 1 && keptFlagged[0] == target ? null : target;
        }

        private static bool KeptByLanguage(ProbeStream s, PlannerSettings settings)
        {
            if (settings.KeepLanguages.Contains(s.Language)) return true;
            return s.Language == "und" && settings.KeepUndefined;
        }
    }
}
