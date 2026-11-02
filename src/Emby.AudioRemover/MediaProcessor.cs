using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AudioRemover.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.AudioRemover
{
    public enum Outcome
    {
        AlreadyDone,
        NothingToDo,
        WouldChange,
        Changed,
        Failed
    }

    public class MediaProcessor
    {
        private const string TempSuffix = Leftovers.TempSuffix;

        private readonly FfmpegRunner _runner;
        private readonly ILibraryMonitor _libraryMonitor;
        private readonly IProviderManager _providerManager;
        private readonly IFileSystem _fileSystem;
        private readonly IJsonSerializer _json;
        private readonly ILogger _log;

        /// <summary>What this processor did (or would do in test mode) so far; one processor is made per run.</summary>
        public RunTotals Totals { get; } = new RunTotals();

        public MediaProcessor(FfmpegRunner runner, ILibraryMonitor libraryMonitor, IProviderManager providerManager,
            IFileSystem fileSystem, IJsonSerializer json, ILogger log)
        {
            _runner = runner;
            _libraryMonitor = libraryMonitor;
            _providerManager = providerManager;
            _fileSystem = fileSystem;
            _json = json;
            _log = log;
        }

        public static PlannerSettings SettingsFrom(PluginOptions o) => new PlannerSettings
        {
            KeepLanguages = PlannerSettings.ParseLanguages(o.KeepLanguages),
            KeepUndefined = o.KeepUndefined,
            KeepFirstTrack = o.KeepFirstTrack,
            RemoveCommentary = o.RemoveCommentary,
            RemoveAudioDescription = o.RemoveAudioDescription,
            SetDefault = o.SetDefault,
            DefaultLanguage = string.IsNullOrWhiteSpace(o.DefaultLanguage) ? null : Languages.Normalize(o.DefaultLanguage)
        };

        public async Task<Outcome> Process(BaseItem item, PluginOptions options, AudioRemoverState state, StateStore store, CancellationToken ct)
        {
            var path = item.Path;
            if (state.IsRestored(path))
            {
                _log.Debug("AudioRemover: {0} was restored from its backup, leaving it alone", path);
                return Outcome.AlreadyDone;
            }

            var stamp = FileStamp.Of(path);
            if (state.Processed.TryGetValue(path, out var done) && done.Matches(stamp)) return Outcome.AlreadyDone;

            var before = await Probe(path, ct).ConfigureAwait(false);
            if (before == null) return Outcome.Failed;

            var plan = AudioPlanner.Plan(before, SettingsFrom(options), path);
            if (plan.LeftAlone != null)
            {
                // One pre-built string: paths and titles may contain braces.
                _log.Info($"AudioRemover: {path}: {plan.LeftAlone} ({string.Join(", ", plan.Keep.Select(s => s.Describe()))})");
            }
            if (!plan.HasWork)
            {
                if (plan.LeftAlone == null) _log.Debug("AudioRemover: nothing to do for {0}", path);
                Totals.Add(plan);
                if (!options.DryRun)
                {
                    state.Processed[path] = stamp;
                    store.Save(state);
                }
                return Outcome.NothingToDo;
            }

            LogPlan(path, plan, options.DryRun);
            if (options.DryRun)
            {
                Totals.Add(plan);
                return Outcome.WouldChange;
            }

            // SubRemover rewrites videos the same way; two rewrites of one file at once would lose one of them.
            if (OtherToolIsWorkingOn(path))
            {
                _log.Info("AudioRemover: SubRemover is changing {0} right now; leaving it for the next run", path);
                return Outcome.NothingToDo;
            }

            state.InProgress = path;
            store.Save(state);
            try
            {
                if (!await RemuxAndSwap(path, stamp, before, plan, options, state, ct).ConfigureAwait(false)) return Outcome.Failed;
                Totals.Add(plan);

                state.Processed[path] = FileStamp.Of(path);
                store.Save(state);

                _providerManager.QueueRefresh(item.InternalId, new MetadataRefreshOptions(_fileSystem)
                {
                    MetadataRefreshMode = MetadataRefreshMode.Default,
                    ReplaceAllMetadata = false
                }, RefreshPriority.Normal);
                return Outcome.Changed;
            }
            finally
            {
                // Catches temp files from a remux that was cancelled or failed part-way.
                foreach (var file in Leftovers.Clean(path)) _log.Debug("AudioRemover: deleted leftover {0}", file);
                state.InProgress = null;
                store.Save(state);
            }
        }

        // Logged as one pre-built string: paths and titles may contain braces that would break format placeholders.
        private void LogPlan(string path, AudioPlan plan, bool dryRun) => _log.Info(DescribePlan(path, plan, dryRun));

        internal static string DescribePlan(string path, AudioPlan plan, bool dryRun)
        {
            string Streams(IEnumerable<ProbeStream> s) => s.Any() ? string.Join(", ", s.Select(x => x.Describe())) : "none";

            return "AudioRemover " + (dryRun ? "[TEST MODE - no changes] " : "") + path +
                   "\n    keep:    " + Streams(plan.Keep) +
                   "\n    remove:  " + Streams(plan.Remove) +
                   (plan.NewDefault != null ? "\n    default: " + plan.NewDefault.Describe() : "");
        }

        private async Task<ProbeResult> Probe(string path, CancellationToken ct)
        {
            var result = await _runner.RunFfprobe(FfmpegArgs.Probe(path), ct).ConfigureAwait(false);
            if (!result.Success)
            {
                _log.Error("AudioRemover: ffprobe failed for {0} (exit {1}): {2}", path, result.ExitCode, result.StdErr);
                return null;
            }
            try
            {
                return _json.DeserializeFromString<ProbeResult>(result.StdOut);
            }
            catch (Exception ex)
            {
                _log.ErrorException("AudioRemover: could not read ffprobe output for {0}", ex, path);
                return null;
            }
        }

        /// <summary>
        /// SubRemover's temporary file next to the video, still being written: it's rewriting that video at this moment.
        /// One left behind by a crash stops being written to, so it doesn't block the video for good.
        /// </summary>
        internal static bool OtherToolIsWorkingOn(string path, DateTime? nowUtc = null)
        {
            var temp = new FileInfo(path + ".subremover-tmp");
            return temp.Exists && (nowUtc ?? DateTime.UtcNow) - temp.LastWriteTimeUtc < TimeSpan.FromMinutes(10);
        }

        private async Task<bool> RemuxAndSwap(string path, FileStamp stamp, ProbeResult before, AudioPlan plan, PluginOptions options,
            AudioRemoverState state, CancellationToken ct)
        {
            var muxer = FfmpegArgs.MuxerFor(path);
            if (muxer == null)
            {
                _log.Warn("AudioRemover: unsupported container for {0}, skipping", path);
                return false;
            }

            var temp = path + TempSuffix;
            TryDelete(temp);
            var beforeBytes = new FileInfo(path).Length;
            var removed = plan.Remove.Select(s => s.index).ToList();
            _libraryMonitor.ReportFileSystemChangeBeginning(path);
            try
            {
                var result = await _runner.RunFfmpeg(FfmpegArgs.Remux(path, temp, muxer, removed, plan.DefaultChanges()), ct).ConfigureAwait(false);
                var failure = !result.Success ? $"exit {result.ExitCode}: {result.StdErr}" : OutputChecks.TruncationMessage(result.StdErr);
                if (failure != null)
                {
                    _log.Error("AudioRemover: ffmpeg failed for {0} ({1}). Original left unchanged.", path, failure);
                    TryDelete(temp);
                    return false;
                }

                var after = await Probe(temp, ct).ConfigureAwait(false);
                var problem = RemuxVerifier.Verify(before, after, plan.Remove.Count, beforeBytes, new FileInfo(temp).Length)
                              ?? (plan.NewDefault != null
                                  ? RemuxVerifier.CheckDefault(after, FfmpegArgs.OutputIndex(plan.NewDefault.index, removed))
                                  : null);
                if (problem != null)
                {
                    _log.Error("AudioRemover: new file for {0} failed verification ({1}). Original left unchanged.", path, problem);
                    TryDelete(temp);
                    return false;
                }

                // Swapping in our copy would undo whatever changed the video meanwhile (SubRemover, a download replacing it).
                if (!stamp.Matches(FileStamp.Of(path)))
                {
                    _log.Warn("AudioRemover: {0} changed while AudioRemover was working on it; left as it is now, the next run checks it again", path);
                    TryDelete(temp);
                    return false;
                }

                return Swap(path, temp, options, state);
            }
            catch (OperationCanceledException)
            {
                TryDelete(temp);
                throw;
            }
            finally
            {
                _libraryMonitor.ReportFileSystemChangeComplete(path, false);
            }
        }

        private bool Swap(string path, string temp, PluginOptions options, AudioRemoverState state)
        {
            var result = BackupManager.Replace(path, temp, options.BackupMode, options.BackupFolder);
            if (!result.Success)
            {
                _log.Error("AudioRemover: {0}: {1}", path, result.Message);
                return false;
            }

            if (result.Message != null) _log.Debug("AudioRemover: {0}: {1}", path, result.Message);
            if (result.NewBackup != null) state.Backups.Add(result.NewBackup);
            _log.Info("AudioRemover: updated {0}{1}", path, result.BackupPath != null ? " (backup: " + result.BackupPath + ")" : " (no backup kept)");
            return true;
        }

        private static void TryDelete(string path) => BackupManager.TryDelete(path);
    }
}
