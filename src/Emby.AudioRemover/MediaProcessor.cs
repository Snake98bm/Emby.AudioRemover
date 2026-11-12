using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.AudioRemover.Core;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
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
        private readonly WhisperInstall _whisper;
        private bool _whisperProblemLogged;

        /// <summary>What this processor did (or would do in test mode) so far; one processor is made per run.</summary>
        public RunTotals Totals { get; } = new RunTotals();

        /// <summary>Every track name this run read, with its film or show, for the suggestions at the end of the log.</summary>
        public List<(string Video, string Title)> TrackNames { get; } = new List<(string, string)>();

        /// <summary>The film, or for an episode its show: what "found in N films or shows" counts.</summary>
        internal static string VideoName(BaseItem item) =>
            item is Episode episode && !string.IsNullOrEmpty(episode.SeriesName) ? episode.SeriesName : item.Name ?? System.IO.Path.GetFileNameWithoutExtension(item.Path);

        private readonly HashMatchedSubtitles _hashSubtitles;
        private readonly SubRemoverSettings _subRemoverSettings;
        private readonly Lazy<SubRemoverChecks> _subRemoverChecks;
        private bool _checksLogged;

        /// <summary>Videos left for later by "Wait until SubRemover has checked a video" (or that would be, in test mode).</summary>
        public List<string> WaitingForSubRemover { get; } = new List<string>();

        /// <param name="subRemoverChecks">Reads what SubRemover has checked; only called when the wait option is on.</param>
        public MediaProcessor(FfmpegRunner runner, ILibraryMonitor libraryMonitor, IProviderManager providerManager,
            IFileSystem fileSystem, IJsonSerializer json, ILogger log, WhisperInstall whisper = null,
            Func<ILogger, HashMatchedSubtitles> hashSubtitles = null, SubRemoverSettings subRemoverSettings = null,
            Func<SubRemoverChecks> subRemoverChecks = null)
        {
            _hashSubtitles = hashSubtitles?.Invoke(log);
            _subRemoverSettings = subRemoverSettings;
            _subRemoverChecks = subRemoverChecks == null ? null : new Lazy<SubRemoverChecks>(subRemoverChecks);
            _runner = runner;
            _libraryMonitor = libraryMonitor;
            _providerManager = providerManager;
            _fileSystem = fileSystem;
            _json = json;
            _log = log;
            _whisper = whisper;
        }

        /// <summary>Step 2 switched on (with step 1): what's heard decides what's kept.</summary>
        internal static bool ActsOnHeard(PluginOptions o) => o.Listen && o.ActOnHeard;

        /// <summary>
        /// Step 3 switched on (with step 1): the bigger model hears 10 more clips when the quick listen disagrees with a tag or
        /// can't tell. With step 2 off it still runs, so the log shows what acting on it would do.
        /// </summary>
        internal static bool DoubleChecks(PluginOptions o) => o.Listen && o.ExtraSafe;

        // More than any real video has; a file with dozens of audio tracks doesn't hold a run up for long, and the log names
        // any tracks past it.
        private const int MostTracksHeard = 16;

        public static PlannerSettings SettingsFrom(PluginOptions o, Action<string> onTitleEntrySwitchedOff = null) => new PlannerSettings
        {
            KeepLanguages = PlannerSettings.ParseLanguages(o.KeepLanguages),
            KeepUndefined = o.KeepUndefined,
            KeepFirstTrack = o.KeepFirstTrack,
            RemoveCommentary = o.RemoveCommentary,
            RemoveAudioDescription = o.RemoveAudioDescription,
            SetDefault = o.SetDefault,
            DefaultLanguage = string.IsNullOrWhiteSpace(o.DefaultLanguage) ? null : Languages.Normalize(o.DefaultLanguage),
            BestAudioDefault = o.BestAudioDefault,
            CleanTitles = o.CleanTitles,
            CleanFileTitle = o.CleanFileTitle,
            RewriteForTitles = o.RewriteForTitles,
            Titles = new TitleCleaner(o.TitleAds, onTitleEntrySwitchedOff),
            CleanAdded = AddedCleaner(o),
            UseHeardLanguages = ActsOnHeard(o),
            DoubleCheckHeard = DoubleChecks(o),
            RetagLoneTrack = o.Listen && o.RetagLoneTrack,
            TagUntaggedTracks = o.Listen && o.TagUntaggedTracks
        };

        // Entries added from the suggestions that are still on the list, for "Clean names where added suggestions were found".
        internal static TitleCleaner AddedCleaner(PluginOptions o)
        {
            if (!o.CleanTitles || !o.CleanWhereSuggested) return null;
            var list = new HashSet<string>(TitleSuggestions.Lines(o.TitleAds), StringComparer.OrdinalIgnoreCase);
            var added = TitleSuggestions.Lines(o.AddedFromSuggestions).Where(list.Contains).ToList();
            return added.Count > 0 ? new TitleCleaner(string.Join("\n", added)) : null;
        }

        // What's heard changes this video: what's kept (step 2), or the tags of tracks that have none.
        private static bool DependsOnHeard(PluginOptions o, ProbeResult probe) =>
            ActsOnHeard(o) || (o.Listen && o.TagUntaggedTracks && AudioPlanner.HasOnlyUntaggedTracks(probe));

        // Built once per run rather than per video: the title list compiles into regular expressions.
        private PlannerSettings _settings;
        private PluginOptions _settingsFor;

        private PlannerSettings Settings(PluginOptions options)
        {
            if (!ReferenceEquals(options, _settingsFor))
            {
                // One pre-built string: a regex is full of braces.
                _settings = SettingsFrom(options, entry => _log.Warn("AudioRemover: '" + entry + "' in 'Text to remove' " +
                                                                     "took too long on a title; it's switched off for the rest of this run"));
                _settingsFor = options;
            }
            return _settings;
        }

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
            if (MustWaitForSubRemover(path, options, state)) return Outcome.NothingToDo;

            var before = await Probe(path, ct).ConfigureAwait(false);
            if (before == null) return Outcome.Failed;
            var video = VideoName(item);
            TrackNames.AddRange(before.streams.Where(s => s.Title.Length > 0).Select(s => (video, s.Title)));

            if (_whisper != null && options.Listen
                && !await ListenWhereInDoubt(path, before, options, ct).ConfigureAwait(false)
                && DependsOnHeard(options, before))
            {
                // It can't act on what it couldn't hear. Rather than decide by the tags it doubts (and save that as done),
                // the video waits for a run where Whisper works.
                _log.Info("AudioRemover: " + path + ": left for a later run - it needs listening, and Whisper isn't working (see the warning above)");
                return Outcome.NothingToDo;
            }

            var plan = AudioPlanner.Plan(before, Settings(options), path);

            // The one track a video has or keeps, tagged with a language you don't keep (no tag: left alone): heard (unless it
            // already was), and its tag corrected when Whisper is sure - in the rewrite the video gets anyway, or one of its own.
            var lone = _whisper != null && options.Listen && options.RetagLoneTrack ? AudioPlanner.LoneTrackToCheck(plan, Settings(options), path) : null;
            if (lone != null)
            {
                if (lone.Heard == null && !await ListenTo(path, before, new List<ProbeStream> { lone }, options, ct).ConfigureAwait(false))
                {
                    _log.Info("AudioRemover: " + path + ": left for a later run - its lone track needs listening, and Whisper isn't working (see the warning above)");
                    return Outcome.NothingToDo;
                }
                AudioPlanner.Retag(plan, lone);
            }

            if (plan.LeftAlone != null)
            {
                // One pre-built string: paths and titles may contain braces.
                _log.Info($"AudioRemover: {path}: {plan.LeftAlone} ({string.Join(", ", plan.Keep.Select(s => s.Describe()))})");
            }
            if (!plan.HasWork)
            {
                // One pre-built string: paths and names may contain braces.
                if (plan.TitlesLeft.Count > 0)
                    _log.Info("AudioRemover: " + path + ": only names to clean, so not rewritten ('Rewrite a video just for clean-up' is off)" +
                              "\n    titles:  " + DescribeTitles(plan.TitlesLeft, "to clean"));
                else if (plan.LeftAlone == null) _log.Debug("AudioRemover: nothing to do for {0}", path);
                Totals.Add(plan);
                if (!options.DryRun)
                {
                    state.Processed[path] = stamp;
                    store.Save(state);
                }
                return Outcome.NothingToDo;
            }

            // SubRemover's "Download hash-matched subtitles before changing a video": the rewrite changes the hash, so when
            // AudioRemover gets to a video first, it does that step.
            if (_subRemoverSettings?.Unreadable == true && !options.DryRun && IsOriginal(path, state))
            {
                _log.Info("AudioRemover: " + path + ": left for a later run - SubRemover's settings couldn't be read, so it isn't known " +
                          "whether hash-matched subtitles should be downloaded first");
                return Outcome.NothingToDo;
            }
            if (_hashSubtitles != null && _subRemoverSettings?.DownloadsHashMatched == true && IsOriginal(path, state)
                && !_hashSubtitles.AlreadyChecked(path))
            {
                var languages = HashMatchedSubtitles.LanguagesToSearch(_subRemoverSettings.KeptLanguages(),
                    before.streams.Where(s => SubRemoverSettings.IsTextSubtitle(s.codec_type, s.codec_name)).Select(s => s.Language),
                    SubtitleFileLanguages(path));
                if ((await _hashSubtitles.Lookup(item, languages, options.DryRun ? HashMode.LogOnly : HashMode.Download, ct, _subRemoverSettings.HashDownloadsPerDay).ConfigureAwait(false)).Check == HashCheck.TryLater)
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

        /// <summary>
        /// Audio tracks to listen to: all of them, but only when there are at least two and none is tagged with a language
        /// you keep (no language at all doesn't count). A video with one is decided by its tags, and a video's only track
        /// always stays, whatever it sounds like, so there's nothing to decide.
        /// </summary>
        internal static List<ProbeStream> InDoubt(ProbeResult probe, PlannerSettings settings)
        {
            var audio = probe.streams.Where(s => s.IsAudio).ToList();
            if (audio.Count < 2 || audio.Any(s => settings.KeepLanguages.Contains(s.Language))) return new List<ProbeStream>();
            return audio;
        }

        /// <returns>False when the video needed listening and Whisper couldn't do it; true otherwise.</returns>
        private Task<bool> ListenWhereInDoubt(string path, ProbeResult probe, PluginOptions options, CancellationToken ct) =>
            ListenTo(path, probe, InDoubt(probe, Settings(options)), options, ct);

        /// <returns>False when Whisper couldn't listen to the tracks; true otherwise (also when there are none).</returns>
        private async Task<bool> ListenTo(string path, ProbeResult probe, List<ProbeStream> tracks, PluginOptions options, CancellationToken ct)
        {
            if (tracks.Count == 0) return true;

            // Downloads Whisper the first time it's needed; extra safe also needs the bigger model for the 10-clip check.
            var doubleCheck = DoubleChecks(options);
            var checkModel = doubleCheck ? WhisperFiles.CheckModelFor(options.WhisperModel).Name : null;
            var problem = await _whisper.Ensure(options.WhisperModel, ct).ConfigureAwait(false)
                          ?? (checkModel != null ? await _whisper.Ensure(checkModel, ct).ConfigureAwait(false) : null);
            if (problem != null) return WhisperNotWorking(problem);

            var heard = tracks.Take(MostTracksHeard).ToList();
            var listener = new LanguageListener(_runner, _whisper, options.WhisperModel, checkModel);
            try
            {
                foreach (var s in heard)
                {
                    s.Heard = await listener.Listen(path, s, probe.DurationSeconds, ct).ConfigureAwait(false);
                    if (!doubleCheck) continue;
                    // Step 3. Before a language other than the tag changes the tag or what's kept, the bigger model has to
                    // agree; and when the quick listen couldn't tell, the bigger model gets to try.
                    if (s.Heard.Language == null)
                        s.Heard = await listener.Identify(path, s, probe.DurationSeconds, s.Heard, ct).ConfigureAwait(false);
                    else if (s.Heard.Language != s.Language)
                        s.Heard = await listener.Confirm(path, s, probe.DurationSeconds, s.Heard, ct).ConfigureAwait(false);
                }
            }
            catch (WhisperFailedException ex)
            {
                // Half an answer is no answer: the tracks already heard go back to their tags too.
                foreach (var s in heard) s.Heard = null;
                return WhisperNotWorking(ex.Message);
            }

            Totals.AddHeard(heard);
            var notHeard = tracks.Skip(MostTracksHeard).Select(s => "#" + s.index).ToList();
            // One pre-built string: paths may contain braces.
            _log.Info("AudioRemover: listened to " + path + ":" + string.Join("", heard.Select(s => $"\n    #{s.index} {s.Language}: {s.Heard.Detail()}")) +
                      (notHeard.Count > 0 ? $"\n    {string.Join(", ", notHeard)}: not listened to (only the first {MostTracksHeard} tracks are); their tags decide" : ""));
            return true;
        }

        // Warned about once per run: every other video that needs listening runs into the same problem.
        private bool WhisperNotWorking(string problem)
        {
            if (!_whisperProblemLogged) _log.Warn("AudioRemover: can't listen to audio: " + problem);
            _whisperProblemLogged = true;
            return false;
        }

        // Logged as one pre-built string: paths and titles may contain braces that would break format placeholders.
        private void LogPlan(string path, AudioPlan plan, bool dryRun) => _log.Info(DescribePlan(path, plan, dryRun));

        internal static string DescribePlan(string path, AudioPlan plan, bool dryRun)
        {
            string Streams(IEnumerable<ProbeStream> s) => s.Any() ? string.Join(", ", s.Select(x => x.Describe())) : "none";

            return "AudioRemover " + (dryRun ? "[TEST MODE - no changes] " : "") + path +
                   "\n    keep:    " + Streams(plan.Keep) +
                   "\n    remove:  " + Streams(plan.Remove) +
                   (plan.NewDefault != null ? "\n    default: " + plan.NewDefault.Describe() + DefaultNote(plan) : "") +
                   (plan.LanguageChanges.Count > 0
                       ? "\n    tags:    " + string.Join(", ", plan.LanguageChanges.Select(c => $"#{c.Key} {plan.Keep.First(s => s.index == c.Key).Language} -> {c.Value}"))
                       : "") +
                   (plan.TitleChanges.Count > 0 ? "\n    titles:  " + DescribeTitles(plan.TitleChanges) : "");
        }

        // A download site puts the same ad on dozens of tracks: the count, then each different title once (three at most).
        internal static string DescribeTitles(IList<TitleChange> changes, string what = "cleaned")
        {
            var different = changes.GroupBy(c => c.Before).Select(g => g.First()).ToList();
            var shown = different.Take(3).Select(c => "\"" + c.Before + "\" -> " + (c.After.Length > 0 ? "\"" + c.After + "\"" : "none"));
            return changes.Count + " " + what + ": " + string.Join(", ", shown) + (different.Count > 3 ? ", ..." : "");
        }

        // Which tracks lose the default flag, so the line says what changes even when the new default already has it.
        private static string DefaultNote(AudioPlan plan)
        {
            var before = plan.Keep.Concat(plan.Remove).Where(s => s.IsDefault && s != plan.NewDefault).OrderBy(s => s.index).ToList();
            if (before.Count > 0) return " (taken off " + string.Join(", ", before.Select(s => "#" + s.index)) + ")";
            return plan.NewDefault.IsDefault ? "" : " (no default before)";
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
        /// <summary>
        /// "Wait until SubRemover has checked a video": true (and logged) for an original video SubRemover hasn't been
        /// through and whose hash-matched subtitles nobody has looked up yet.
        /// </summary>
        private bool MustWaitForSubRemover(string path, PluginOptions options, AudioRemoverState state)
        {
            // No SubRemover in this Emby: nothing to wait for.
            if (!options.WaitForSubRemover || _subRemoverChecks == null || _subRemoverSettings == null) return false;
            var checks = _subRemoverChecks.Value;
            if (!_checksLogged)
            {
                _checksLogged = true;
                _log.Info(checks.Unreadable
                    ? "AudioRemover: SubRemover's record of checked videos couldn't be read, so videos it hasn't changed wait for a later run"
                    : $"AudioRemover: waiting for SubRemover where needed; it has checked {checks.Count} videos so far");
            }
            if (checks.HasChecked(path) || !IsOriginal(path, state) || (_hashSubtitles?.AlreadyChecked(path) ?? false)) return false;

            WaitingForSubRemover.Add(path);
            _log.Info("AudioRemover: " + path + ": " + (options.DryRun ? "would wait" : "left for later") +
                      " - SubRemover hasn't checked it yet ('Wait until SubRemover has checked a video')");
            return true;
        }

        /// <summary>
        /// Not yet rewritten by AudioRemover (no backup of it) or SubRemover (no backup beside it): its hash is still the
        /// one subtitle sites know.
        /// </summary>
        private static bool IsOriginal(string path, AudioRemoverState state) =>
            !state.Backups.Any(b => string.Equals(b.OriginalPath, path, StringComparison.OrdinalIgnoreCase))
            && !File.Exists(path + ".subremover.bak");

        private static readonly HashSet<string> SubtitleExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".srt", ".ass", ".ssa", ".sub", ".vtt", ".smi", ".sup"
        };

        /// <summary>Languages of the subtitle files next to the video ("Movie.en.srt", "Movie.eng.forced.srt").</summary>
        internal static IEnumerable<string> SubtitleFileLanguages(string videoPath)
        {
            var dir = Path.GetDirectoryName(videoPath);
            var name = Path.GetFileNameWithoutExtension(videoPath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) yield break;
            foreach (var file in Directory.EnumerateFiles(dir, name + ".*"))
            {
                if (!SubtitleExtensions.Contains(Path.GetExtension(file))) continue;
                var stem = Path.GetFileNameWithoutExtension(file);
                if (stem.Length <= name.Length) continue;
                var language = stem.Substring(name.Length + 1).Split('.').Where(Languages.IsKnown).Select(Languages.Normalize).FirstOrDefault();
                if (language != null) yield return language;
            }
        }

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
                var result = await _runner.RunFfmpeg(
                    FfmpegArgs.Remux(path, temp, muxer, removed, plan.DefaultChanges(), plan.StreamTitles(), plan.FileTitle, plan.LanguageChanges),
                    ct).ConfigureAwait(false);
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
