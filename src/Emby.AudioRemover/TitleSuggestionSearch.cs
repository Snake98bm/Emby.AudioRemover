using System.Collections.Generic;
using System.Linq;
using Emby.AudioRemover.Core;

namespace Emby.AudioRemover
{
    /// <summary>The last "Find text to remove": still running, or what it found and which suggestions are switched on.</summary>
    internal sealed class TitleSuggestionSearch
    {
        private readonly object _lock = new object();
        private readonly HashSet<int> _selected = new HashSet<int>();

        public bool Running { get; private set; }

        /// <summary>What the last search found; null before one finished, or after its picks were added.</summary>
        public List<TitleSuggestion> Found { get; private set; }

        public int VideosLookedAt { get; private set; }

        public string Problem { get; private set; }

        /// <returns>False when a search is already running.</returns>
        public bool TryStart()
        {
            lock (_lock)
            {
                if (Running) return false;
                Running = true;
                Found = null;
                Problem = null;
                _selected.Clear();
                return true;
            }
        }

        public void Finish(List<TitleSuggestion> found, int videos)
        {
            lock (_lock)
            {
                Found = found;
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

        public bool IsSelected(int index)
        {
            lock (_lock) return _selected.Contains(index);
        }

        public void Toggle(int index)
        {
            lock (_lock)
            {
                if (Found == null || index < 0 || index >= Found.Count) return;
                if (!_selected.Remove(index)) _selected.Add(index);
            }
        }

        /// <summary>The switched-on suggestions; the list is done with once they're added.</summary>
        public List<string> TakeSelected()
        {
            lock (_lock)
            {
                if (Found == null) return new List<string>();
                var picked = _selected.Where(i => i < Found.Count).OrderBy(i => i).Select(i => Found[i].Entry).ToList();
                if (picked.Count > 0)
                {
                    Found = null;
                    _selected.Clear();
                }
                return picked;
            }
        }
    }
}
