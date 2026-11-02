using System;

namespace Emby.AudioRemover.Core
{
    /// <summary>
    /// ffmpeg exits with 0 even when its input ends early ("File ended prematurely"), e.g. after a network drop,
    /// so every output is checked before anything is replaced.
    /// </summary>
    public static class OutputChecks
    {
        private static readonly string[] TruncationMessages =
        {
            "ended prematurely", "i/o error", "error while reading", "error during demuxing", "truncat", "connection reset"
        };

        /// <returns>The ffmpeg message that shows the input was cut short, or null.</returns>
        public static string TruncationMessage(string stderr)
        {
            if (string.IsNullOrEmpty(stderr)) return null;
            foreach (var line in stderr.Split('\n'))
            {
                foreach (var marker in TruncationMessages)
                {
                    if (line.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0) return line.Trim();
                }
            }
            return null;
        }
    }
}
