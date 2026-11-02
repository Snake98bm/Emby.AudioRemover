using System;
using System.Linq;

namespace Emby.AudioRemover.Core
{
    public static class RemuxVerifier
    {
        public const double DurationToleranceSeconds = 5;

        /// <returns>null when the new file looks correct, otherwise the reason it was rejected.</returns>
        public static string Verify(ProbeResult before, ProbeResult after, int removedAudio, long beforeBytes, long afterBytes)
        {
            if (after == null || after.streams.Count == 0) return "new file has no streams";

            if (after.Count("video") != before.Count("video"))
                return $"video track count changed ({before.Count("video")} -> {after.Count("video")})";

            var expectedAudio = before.Count("audio") - removedAudio;
            if (after.Count("audio") != expectedAudio)
                return $"expected {expectedAudio} audio tracks but found {after.Count("audio")}";
            if (expectedAudio < 1) return "the new file would have no audio";

            if (after.Count("subtitle") != before.Count("subtitle"))
                return $"subtitle track count changed ({before.Count("subtitle")} -> {after.Count("subtitle")})";

            var d1 = before.DurationSeconds;
            var d2 = after.DurationSeconds;
            if (d1.HasValue && d2.HasValue)
            {
                // A stream copy keeps the length; a few seconds covers rounding. A percentage would let a
                // 2-hour movie lose over a minute at the end without being noticed.
                if (Math.Abs(d1.Value - d2.Value) > DurationToleranceSeconds)
                    return $"duration changed ({d1:F1}s -> {d2:F1}s)";
            }

            // Removed audio can be most of a file (several lossless tracks next to a small video), so the size check
            // allows for what the removed tracks would take; only a file far smaller than that is suspicious.
            if (afterBytes < beforeBytes / 10)
                return $"new file is less than a tenth of the original size ({beforeBytes} -> {afterBytes} bytes)";

            return null;
        }

        /// <returns>A problem unless the audio track at <paramref name="outputIndex"/> is the only one flagged default.</returns>
        public static string CheckDefault(ProbeResult after, int outputIndex)
        {
            var flagged = after.streams.Where(s => s.IsAudio && s.IsDefault).Select(s => s.index).ToList();
            return flagged.Count == 1 && flagged[0] == outputIndex
                ? null
                : $"expected only audio #{outputIndex} to be default but found {(flagged.Count == 0 ? "none" : "#" + string.Join(", #", flagged))}";
        }
    }
}
