using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Emby.AudioRemover.Core;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace Emby.AudioRemover.ScheduledTasks
{
    public class RemoveAudioTask : IScheduledTask
    {
        private readonly ILibraryManager _libraryManager;
        private readonly ILogger _embyLog;
        private readonly string _logDirectory;
        private readonly StateStore _store;
        private readonly Func<ILogger, MediaProcessor> _newProcessor;
        private ILogger _log;

        public RemoveAudioTask(ILibraryManager libraryManager, ILogManager logManager, IFfmpegManager ffmpegManager,
            ILibraryMonitor libraryMonitor, IProviderManager providerManager, IFileSystem fileSystem,
            IJsonSerializer json, IApplicationPaths appPaths)
        {
            _libraryManager = libraryManager;
            _embyLog = logManager.GetLogger("AudioRemover");
            _log = _embyLog;
            _logDirectory = appPaths.LogDirectoryPath;
            _store = new StateStore(appPaths.PluginConfigurationsPath, json);
            var runner = FfmpegRunner.FromEmby(ffmpegManager);
            _newProcessor = log => new MediaProcessor(runner, libraryMonitor, providerManager, fileSystem, json, log, Plugin.Instance?.Whisper);
        }

        public string Name => "Remove foreign audio tracks";
        public string Key => "AudioRemoverRemoveAudio";
        public string Description => "Removes audio tracks that aren't in your kept languages. Configure under Plugins → AudioRemover.";
        public string Category => "AudioRemover";

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

        public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
        {
            // Set when NewVideoWatcher started this run: check only the videos Emby just added.
            var newVideos = Plugin.Instance.NewVideos.Take();

            using (var runLog = new RunLog(_logDirectory, "audioremover", _embyLog))
            {
                if (!TaskGate.TryEnter())
                {
                    const string busy = "another AudioRemover task (restore) is running - try again when it has finished";
                    // Hand the new videos back, so the next run still gets to them.
                    Plugin.Instance.NewVideos.AddRange(newVideos);
                    runLog.Summary("AudioRemover: {0}", busy);
                    var state = _store.Load();
                    state.LastRun = new RunSummary { FinishedUtc = DateTime.UtcNow, Problem = busy, LogFile = runLog.FileName };
                    _store.Save(state);
                    return;
                }

                _log = runLog;
                try
                {
                    await Run(runLog, newVideos, cancellationToken, progress).ConfigureAwait(false);
                }
                finally
                {
                    _log = _embyLog;
                    TaskGate.Exit();
                }
            }
        }

        private async Task Run(RunLog runLog, ISet<string> newVideos, CancellationToken cancellationToken, IProgress<double> progress)
        {
            var options = Plugin.Instance.Options;
            var processor = _newProcessor(runLog);
            var version = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            runLog.Summary("AudioRemover {0} starting{1}{2}. Details: {3}", version,
                newVideos == null ? "" : " for new videos added to the library",
                options.DryRun ? " in TEST MODE (no files will be changed)" : "", runLog.FileName);
            // One pre-built string: values such as paths may contain braces.
            _log.Info("AudioRemover settings for this run:\n    " + string.Join("\n    ", options.DescribeSettings(LibraryNames())));

            var problem = Validate(options);
            if (problem != null)
            {
                _log.Error("AudioRemover: {0}. Fix it under Plugins → AudioRemover.", problem);
                var failedState = _store.Load();
                failedState.LastRun = new RunSummary { FinishedUtc = DateTime.UtcNow, DryRun = options.DryRun, Problem = problem, LogFile = runLog.FileName };
                _store.Save(failedState);
                return;
            }

            var state = _store.Load();
            if (state.InProgress != null)
            {
                foreach (var file in Leftovers.Clean(state.InProgress)) _log.Info("AudioRemover: deleted leftover {0} from an interrupted run", file);
                state.InProgress = null;
                _store.Save(state);
            }

            if (options.ReprocessAll && !options.DryRun)
            {
                _log.Info("AudioRemover: 'Re-check every video' is on - clearing the processed and restored lists");
                state.ForgetEverything();
                _store.Save(state);
                options.ReprocessAll = false;
                Plugin.Instance.Save(options);
            }

            var settings = MediaProcessor.SettingsFrom(options);
            if (options.CleanTitles)
            {
                // One pre-built string: a regex is full of braces.
                foreach (var skipped in settings.Titles.Problems)
                    _log.Warn("AudioRemover: 'Text to remove': " + skipped + ", so it's skipped");
            }
            var signature = settings.Signature();
            if (!options.DryRun && state.ApplySettingsSignature(signature))
            {
                _log.Info("AudioRemover: settings changed since the last run - checking every video again (restored videos stay untouched)");
            }
            if (!options.DryRun) _store.Save(state);

            // Test mode checks the videos a real run would check now, without forgetting anything.
            var history = options.DryRun ? state.PreviewFor(signature, options.ReprocessAll) : state;
            if (options.DryRun && options.ReprocessAll) _log.Info("AudioRemover: 'Re-check every video' is on - test mode includes videos already done or restored");

            var items = FindVideos(options, newVideos);
            if (newVideos != null)
            {
                _log.Info("AudioRemover: {0} new video{1} added; {2} of them in the selected libraries and path filter",
                    newVideos.Count, newVideos.Count == 1 ? "" : "s", items.Count);
            }
            _log.Info("AudioRemover: {0} video files to check", items.Count);

            var counts = Enum.GetValues(typeof(Outcome)).Cast<Outcome>().ToDictionary(o => o, o => 0);
            var limit = LimitFor(options, newVideosOnly: newVideos != null);
            var changes = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) Stopped(i, items.Count, cancellationToken);
                progress.Report(100.0 * i / items.Count);

                Outcome outcome;
                try
                {
                    outcome = await processor.Process(items[i], options, history, _store, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Stopped(i, items.Count, cancellationToken);
                    throw;
                }
                catch (Exception ex)
                {
                    _log.ErrorException("AudioRemover: unexpected error on {0}", ex, items[i].Path);
                    outcome = Outcome.Failed;
                }
                counts[outcome]++;

                if (outcome == Outcome.Changed || outcome == Outcome.WouldChange) changes++;
                if (limit > 0 && changes >= limit)
                {
                    _log.Info("AudioRemover: reached the limit of {0} videos per run", limit);
                    break;
                }
            }

            CleanOldBackups(options, state);

            state.LastRun = new RunSummary
            {
                FinishedUtc = DateTime.UtcNow,
                DryRun = options.DryRun,
                Changed = counts[Outcome.Changed],
                WouldChange = counts[Outcome.WouldChange],
                NothingToDo = counts[Outcome.NothingToDo],
                AlreadyDone = counts[Outcome.AlreadyDone],
                Failed = counts[Outcome.Failed],
                LogFile = runLog.FileName
            };
            _store.Save(state);

            progress.Report(100);
            runLog.Summary("AudioRemover finished: {0} changed, {1} would change (test mode), {2} nothing to do, {3} already done, {4} failed. Details: {5}",
                counts[Outcome.Changed], counts[Outcome.WouldChange], counts[Outcome.NothingToDo], counts[Outcome.AlreadyDone], counts[Outcome.Failed], runLog.FileName);
            _log.Info(processor.Totals.Describe(options.DryRun));

            if (options.CleanTitles)
            {
                var missed = TitleSuggestions.Find(processor.TrackNames, new TitleCleaner(options.TitleAds));
                // One pre-built string: names may contain braces.
                if (missed.Count > 0)
                    _log.Info("AudioRemover: text in track names the 'Text to remove' list misses (in 2+ films or shows): " +
                              string.Join(", ", missed.Take(10).Select(s => $"{s.Entry} ({s.Videos})")) + (missed.Count > 10 ? ", ..." : "") +
                              " - add them with 'Find text to remove' on the settings page");
            }
        }

        /// <returns>How many videos a run may change; 0 is no limit. A run for new videos does all of them.</returns>
        internal static int LimitFor(PluginOptions options, bool newVideosOnly) => newVideosOnly ? 0 : options.MaxItemsPerRun;

        private static string Validate(PluginOptions o)
        {
            if (string.IsNullOrWhiteSpace(o.Libraries)) return "no libraries selected";
            if (PlannerSettings.ParseLanguages(o.KeepLanguages).Count == 0) return "no languages to keep - refusing to remove every audio track";
            if (o.BackupMode == BackupMode.BackupFolder && !Directory.Exists(o.BackupFolder)) return "backup folder '" + o.BackupFolder + "' does not exist";
            return null;
        }

        // Stop on the settings page or in Scheduled Tasks. The last line of the log says where it got to.
        private void Stopped(int done, int total, CancellationToken cancellationToken)
        {
            _log.Info("AudioRemover: stopped after {0} of {1} videos. What's done stays done; the next run goes on from there.", done, total);
            cancellationToken.ThrowIfCancellationRequested();
        }

        private Dictionary<string, string> LibraryNames() =>
            _libraryManager.GetVirtualFolders()
                .Where(f => f.ItemId != null)
                .GroupBy(f => f.ItemId)
                .ToDictionary(g => g.Key, g => g.First().Name);

        /// <summary>Every video Emby has in these libraries: by their folders, or by the libraries themselves when that finds none.</summary>
        internal static BaseItem[] LibraryVideos(ILibraryManager libraryManager, IList<VirtualFolderInfo> folders)
        {
            var locations = folders.SelectMany(f => f.Locations ?? Array.Empty<string>()).Select(WithTrailingSeparator).ToArray();

            var items = locations.Length == 0
                ? Array.Empty<BaseItem>()
                : libraryManager.GetItemList(new InternalItemsQuery
                {
                    Recursive = true,
                    MediaTypes = new[] { "Video" },
                    IsVirtualItem = false,
                    HasPath = true,
                    PathStartsWithAny = locations
                });

            if (items.Length == 0)
            {
                var ids = folders.Select(f => long.TryParse(f.ItemId, out var id) ? id : 0).Where(id => id != 0).ToArray();
                if (ids.Length > 0)
                {
                    items = libraryManager.GetItemList(new InternalItemsQuery
                    {
                        Recursive = true,
                        MediaTypes = new[] { "Video" },
                        IsVirtualItem = false,
                        AncestorIds = ids
                    });
                }
            }
            return items;
        }

        /// <param name="onlyThese">New videos only: still limited to the libraries and path filter.</param>
        private List<BaseItem> FindVideos(PluginOptions options, ISet<string> onlyThese)
        {
            var selected = new HashSet<string>(options.Libraries.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            var folders = _libraryManager.GetVirtualFolders().Where(f => selected.Contains(f.ItemId)).ToList();
            // The settings list only has the libraries' ids; this names them. One pre-built string: paths may contain braces.
            _log.Info("AudioRemover libraries: " + (folders.Count == 0 ? "none found"
                : string.Join(", ", folders.Select(f => $"{f.Name} ({string.Join("; ", f.Locations ?? Array.Empty<string>())})"))));
            return LibraryVideos(_libraryManager, folders)
                .Where(i => !i.IsShortcut && !string.IsNullOrEmpty(i.Path))
                .Where(i => PathFilter.Matches(i.Path, options.OnlyPathContains))
                // Before File.Exists, so a run for a few new videos doesn't touch every file on the share.
                .Where(i => onlyThese == null || onlyThese.Contains(i.Path))
                .Where(i => FfmpegArgs.MuxerFor(i.Path) != null && File.Exists(i.Path))
                .GroupBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string WithTrailingSeparator(string path)
        {
            if (path.EndsWith("/") || path.EndsWith("\\")) return path;
            return path + (path.Contains("/") ? "/" : "\\");
        }

        private void CleanOldBackups(PluginOptions options, AudioRemoverState state)
        {
            if (options.BackupRetentionDays <= 0) return;

            var cutoff = DateTime.UtcNow.AddDays(-options.BackupRetentionDays);
            foreach (var entry in state.Backups.Where(b => b.CreatedUtc < cutoff).ToList())
            {
                if (options.DryRun)
                {
                    _log.Info("AudioRemover [TEST MODE]: would delete old backup {0}", entry.BackupPath);
                    continue;
                }
                try
                {
                    if (File.Exists(entry.BackupPath)) File.Delete(entry.BackupPath);
                    state.Backups.Remove(entry);
                    _log.Info("AudioRemover: deleted backup older than {0} days: {1}", options.BackupRetentionDays, entry.BackupPath);
                }
                catch (Exception ex)
                {
                    _log.ErrorException("AudioRemover: could not delete old backup {0}", ex, entry.BackupPath);
                }
            }
            if (!options.DryRun) _store.Save(state);
        }
    }
}
