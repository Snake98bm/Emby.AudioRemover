using System.Collections.Generic;

namespace Emby.AudioRemover.Core
{
    /// <summary>What one run did (or, in test mode, would do), added up over every video for the end of the run log.</summary>
    public class RunTotals
    {
        public int VideosRewritten { get; set; }
        public int TracksRemoved { get; set; }
        public int DefaultsChanged { get; set; }
        public int LeftAlone { get; set; }
        public int TitlesCleaned { get; set; }

        /// <summary>Videos whose names need cleaning but aren't rewritten just for that ("Rewrite a video just for clean-up" is off).</summary>
        public int TitlesLeft { get; set; }

        public int TracksHeard { get; set; }

        /// <summary>Tracks Whisper was sure about that are tagged with another language.</summary>
        public int TagsWrong { get; set; }

        public void AddHeard(IEnumerable<ProbeStream> tracks)
        {
            foreach (var s in tracks)
            {
                TracksHeard++;
                if (s.Heard?.Language != null && s.Language != "und" && s.Heard.Language != s.Language) TagsWrong++;
            }
        }

        /// <summary>Test mode: counts what the plan would do. A real run counts the same once the new file is in place.</summary>
        public void Add(AudioPlan plan)
        {
            if (plan.LeftAlone != null) LeftAlone++;
            if (plan.TitlesLeft.Count > 0) TitlesLeft++;
            if (!plan.NeedsRemux) return;
            VideosRewritten++;
            TracksRemoved += plan.Remove.Count;
            if (plan.NewDefault != null) DefaultsChanged++;
            if (plan.TitleChanges.Count > 0) TitlesCleaned++;
        }

        public string Describe(bool dryRun)
        {
            var lines = new List<string>
            {
                Line("video files rewritten", VideosRewritten),
                Line("audio tracks removed", TracksRemoved),
                Line("default audio changed", DefaultsChanged),
                Line("left alone (no kept audio)", LeftAlone),
                Line("videos with names cleaned", TitlesCleaned),
                Line("to rewrite just for clean-up", TitlesLeft)
            };
            if (TitlesLeft > 0)
                lines.Add($"  (not rewritten: 'Rewrite a video just for clean-up' is off; turning it on adds {TitlesLeft} rewrite{(TitlesLeft == 1 ? "" : "s")})");
            if (TracksHeard > 0)
            {
                lines.Add(Line("audio tracks listened to", TracksHeard));
                lines.Add(Line("mislabeled tracks heard", TagsWrong));
            }
            return (dryRun ? "Totals - what a real run would do:" : "Totals:") + "\n    " + string.Join("\n    ", lines);
        }

        internal static string Line(string label, int value) => (label + ":").PadRight(32) + value;
    }
}
