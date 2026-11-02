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

        /// <summary>Test mode: counts what the plan would do. A real run counts the same once the new file is in place.</summary>
        public void Add(AudioPlan plan)
        {
            if (plan.LeftAlone != null) LeftAlone++;
            if (!plan.NeedsRemux) return;
            VideosRewritten++;
            TracksRemoved += plan.Remove.Count;
            if (plan.NewDefault != null) DefaultsChanged++;
        }

        public string Describe(bool dryRun)
        {
            var lines = new List<string>
            {
                $"video files rewritten:       {VideosRewritten}",
                $"audio tracks removed:        {TracksRemoved}",
                $"default audio changed:       {DefaultsChanged}",
                $"left alone (no kept audio):  {LeftAlone}"
            };
            return (dryRun ? "Totals - what a real run would do:" : "Totals:") + "\n    " + string.Join("\n    ", lines);
        }
    }
}
