namespace Emby.AudioRemover
{
    /// <summary>The last "Find text to remove": still running, finished (what it found went into "Suggested text"), or failed.</summary>
    internal sealed class TitleSuggestionSearch
    {
        private readonly object _lock = new object();

        public bool Running { get; private set; }

        /// <summary>How many videos the last finished search looked through; null before one finished.</summary>
        public int? VideosLookedAt { get; private set; }

        public string Problem { get; private set; }

        /// <returns>False when a search is already running.</returns>
        public bool TryStart()
        {
            lock (_lock)
            {
                if (Running) return false;
                Running = true;
                VideosLookedAt = null;
                Problem = null;
                return true;
            }
        }

        public void Finish(int videos)
        {
            lock (_lock)
            {
                VideosLookedAt = videos;
                Running = false;
            }
        }

        public void Fail(string problem)
        {
            lock (_lock)
            {
                Problem = problem;
                Running = false;
            }
        }
    }
}
