using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Emby.AudioRemover.Core;
using Emby.AudioRemover.ScheduledTasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace Emby.AudioRemover
{
    /// <summary>
    /// "Process new videos as they're added": collects the videos Emby adds and, once none have come in for a few
    /// minutes, starts one run for just those. Waiting lets a whole season arrive as one batch with one log, and keeps
    /// AudioRemover off files that are still being copied.
    /// </summary>
    public class NewVideoWatcher : IServerEntryPoint
    {
        public static readonly TimeSpan WaitAfterLastVideo = TimeSpan.FromMinutes(3);
        private static readonly TimeSpan WaitWhileBusy = TimeSpan.FromMinutes(2);

        // "Wait until SubRemover has checked a video": new videos it left are tried again this often, for this long.
        internal static readonly TimeSpan RetryForSubRemover = TimeSpan.FromMinutes(10);
        internal static readonly TimeSpan GiveUpWaitingAfter = TimeSpan.FromDays(1);

        private static NewVideoWatcher _instance;
        private readonly Dictionary<string, DateTime> _firstWaited = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// New videos a run left for SubRemover: another run for them in 10 minutes. After a day of waiting a video is
        /// dropped from these runs (logged); the next full run still gets to it.
        /// </summary>
        internal static void RetryLater(IEnumerable<string> paths) => _instance?.Retry(paths);

        private void Retry(IEnumerable<string> paths)
        {
            var now = DateTime.UtcNow;
            var again = new List<string>();
            lock (_firstWaited)
            {
                foreach (var path in paths)
                {
                    if (!_firstWaited.TryGetValue(path, out var since)) _firstWaited[path] = since = now;
                    if (now - since < GiveUpWaitingAfter) again.Add(path);
                    else
                    {
                        _firstWaited.Remove(path);
                        _log.Info($"AudioRemover: {path} waited a day for SubRemover; the next full run gets to it");
                    }
                }
                // Forget videos that stopped waiting long ago.
                foreach (var old in _firstWaited.Where(p => now - p.Value > GiveUpWaitingAfter + GiveUpWaitingAfter).Select(p => p.Key).ToList())
                    _firstWaited.Remove(old);
            }
            if (again.Count == 0) return;
            foreach (var path in again) _waiting.Add(path);
            StartTimer(RetryForSubRemover);
        }

        private readonly ILibraryManager _libraryManager;
        private readonly ITaskManager _taskManager;
        private readonly ILogger _log;
        private readonly PendingVideos _waiting = new PendingVideos();
        private readonly object _timerLock = new object();
        private Timer _timer;

        public NewVideoWatcher(ILibraryManager libraryManager, ITaskManager taskManager, ILogManager logManager)
        {
            _libraryManager = libraryManager;
            _taskManager = taskManager;
            _log = logManager.GetLogger("AudioRemover");
            _instance = this;
        }

        public void Run() => _libraryManager.ItemAdded += OnItemAdded;

        public void Dispose()
        {
            _libraryManager.ItemAdded -= OnItemAdded;
            lock (_timerLock) _timer?.Dispose();
        }

        // A video AudioRemover rewrites keeps its path, so Emby updates it rather than adding it: no loop.
        private void OnItemAdded(object sender, ItemChangeEventArgs e)
        {
            if (Plugin.Instance?.Options.ProcessNewVideos != true) return;
            if (!(e.Item is Video video) || video.IsVirtualItem || string.IsNullOrEmpty(video.Path)) return;

            _waiting.Add(video.Path);
            StartTimer(WaitAfterLastVideo);
        }

        private void StartTimer(TimeSpan due)
        {
            lock (_timerLock)
            {
                if (_timer == null) _timer = new Timer(_ => StartRun(), null, due, Timeout.InfiniteTimeSpan);
                else _timer.Change(due, Timeout.InfiniteTimeSpan);
            }
        }

        private void StartRun()
        {
            try
            {
                var busy = _taskManager.ScheduledTasks
                    .Any(w => (w.ScheduledTask is RemoveAudioTask || w.ScheduledTask is RestoreBackupsTask) && w.State != TaskState.Idle);
                if (busy)
                {
                    StartTimer(WaitWhileBusy);
                    return;
                }

                var videos = _waiting.Take();
                // Switched off while they were waiting: drop them; the next full run gets to them.
                if (videos == null || Plugin.Instance?.Options.ProcessNewVideos != true) return;
                _log.Info($"AudioRemover: {videos.Count} new video{(videos.Count == 1 ? "" : "s")} added to the library; starting a run for them");
                Plugin.Instance.NewVideos.AddRange(videos);
                _taskManager.QueueIfNotRunning<RemoveAudioTask>();
            }
            catch (Exception ex)
            {
                _log.ErrorException("AudioRemover: couldn't start a run for new videos", ex);
            }
        }
    }
}
