using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Emby.AudioRemover.Core;
using Emby.AudioRemover.ScheduledTasks;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Common;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Plugins.UI;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Tasks;

namespace Emby.AudioRemover
{
    public class Plugin : BasePlugin, IHasWebPages, IHasThumbImage
    {
        private readonly IApplicationHost _applicationHost;
        private readonly ILibraryManager _libraryManager;
        private readonly OptionsStore _store;
        private readonly StateStore _state;
        private readonly string _logDirectory;

        public Plugin(IApplicationHost applicationHost, ILibraryManager libraryManager)
        {
            _applicationHost = applicationHost;
            _libraryManager = libraryManager;
            _store = new OptionsStore(
                applicationHost.Resolve<IApplicationPaths>().PluginConfigurationsPath,
                SettingsFileName,
                applicationHost.Resolve<IJsonSerializer>(),
                applicationHost.Resolve<ILogManager>().GetLogger(Name));
            _logDirectory = applicationHost.Resolve<IApplicationPaths>().LogDirectoryPath;
            _state = new StateStore(applicationHost.Resolve<IApplicationPaths>().PluginConfigurationsPath, applicationHost.Resolve<IJsonSerializer>());
            Whisper = new WhisperInstall(Path.Combine(applicationHost.Resolve<IApplicationPaths>().PluginConfigurationsPath, "AudioRemover-whisper"));
            SuggestionStore = new SuggestionStore(applicationHost.Resolve<IApplicationPaths>().PluginConfigurationsPath, applicationHost.Resolve<IJsonSerializer>());
            Instance = this;
        }

        public static Plugin Instance { get; private set; }

        /// <summary>whisper.cpp and its model, for listening to audio; downloaded on first use.</summary>
        internal WhisperInstall Whisper { get; }

        /// <summary>The last "Find text to remove".</summary>
        internal TitleSuggestionSearch Suggestions { get; } = new TitleSuggestionSearch();

        /// <summary>What "Suggested text" builds up from, across runs and searches.</summary>
        internal SuggestionStore SuggestionStore { get; }

        internal const string SuggestionsCaption = "Find text to remove";

        // Runs on after the click: reading every video's track names from Emby's database takes a few seconds. What it finds
        // joins what runs found, in "Suggested text".
        internal void StartFindingSuggestions(PluginOptions options)
        {
            if (!Suggestions.TryStart()) return;
            var list = options.TitleAds;
            var libraries = options.Libraries;
            Task.Run(() =>
            {
                try
                {
                    var names = TrackNames(libraries, out var videos);
                    SuggestionStore.Update(data => TitleSuggestions.Merge(data, names, new TitleCleaner(list)));
                    Suggestions.Finish(videos);
                }
                catch (Exception ex)
                {
                    Suggestions.Fail(ex.Message);
                }
            });
        }

        // The names Emby stored when it scanned each file: no file is opened.
        private List<(string Video, string Title)> TrackNames(string libraries, out int videos)
        {
            var selected = new HashSet<string>((libraries ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            var folders = _libraryManager.GetVirtualFolders().Where(f => selected.Contains(f.ItemId)).ToList();
            var items = RemoveAudioTask.LibraryVideos(_libraryManager, folders);
            var streams = _applicationHost.Resolve<IMediaSourceManager>();
            var names = new List<(string, string)>();
            foreach (var item in items)
            {
                var video = MediaProcessor.VideoName(item);
                names.AddRange(streams.GetMediaStreams(item.InternalId)
                    .Where(s => !string.IsNullOrWhiteSpace(s.Title))
                    .Select(s => (video, s.Title)));
            }
            videos = items.Length;
            return names;
        }

        internal StatusItem SuggestionsStatus()
        {
            var search = Suggestions;
            if (search.Running)
                return new StatusItem(SuggestionsCaption, "Looking through the track names Emby has for your libraries… This line updates when it's done.", ItemStatus.InProgress);
            if (search.Problem != null)
                return new StatusItem(SuggestionsCaption, "Couldn't look through the track names: " + search.Problem, ItemStatus.Failed);
            if (search.VideosLookedAt != null)
                return new StatusItem(SuggestionsCaption,
                    $"Looked through {search.VideosLookedAt} videos' track names; what keeps turning up is in 'Suggested text' below.", ItemStatus.Succeeded);
            return new StatusItem(SuggestionsCaption,
                "Looks through your libraries' track names for text that keeps turning up across different films and shows but isn't " +
                "on the list yet, and puts it in 'Suggested text' below. Runs add to it too.",
                ItemStatus.None);
        }

        /// <summary>"Suggested text" as the page shows it: what's waiting and what was typed there, and where each was found.</summary>
        internal (string Text, List<SuggestionPlaceDto> Places) SuggestionBox(string titleAds)
        {
            var data = SuggestionStore.Read();
            var waiting = TitleSuggestions.Waiting(data, new TitleCleaner(titleAds));
            return (TitleSuggestions.BoxText(data, waiting), SuggestionPlacesFor(waiting));
        }

        internal static List<SuggestionPlaceDto> SuggestionPlacesFor(IEnumerable<TitleSuggestion> waiting) =>
            waiting.Select(s => new SuggestionPlaceDto
            {
                Entry = s.Entry,
                Where = $"In {(s.Videos >= TitleSuggestions.MostNamesKept ? TitleSuggestions.MostNamesKept + "+" : s.Videos.ToString(CultureInfo.InvariantCulture))} " +
                        $"films or shows: {string.Join(", ", s.VideoNames.Take(10))}{(s.VideoNames.Count > 10 ? $" and {s.VideoNames.Count - 10} more" : "")}"
            }).ToList();

        /// <summary>Keeps what was done in "Suggested text" before the page is saved: lines taken out, typed or changed.</summary>
        internal void KeepSuggestionEdits(PluginOptions options)
        {
            if (options.SuggestedText == null && options.SuggestedTextShown == null) return;
            var shown = options.SuggestedTextShown;
            var now = options.SuggestedText;
            SuggestionStore.Update(data => TitleSuggestions.KeepEdits(data, shown, now));
            options.SuggestedTextShown = now;
        }

        internal StatusItem AddSuggestedText(PluginOptions options) => AddSuggestedText(options, SuggestionStore);

        /// <summary>"Add to the list": every line in the box that passes the list's checks goes on the list; the rest stay.</summary>
        internal static StatusItem AddSuggestedText(PluginOptions options, SuggestionStore store)
        {
            var lines = TitleSuggestions.Lines(options.SuggestedText);
            if (lines.Count == 0)
                return new StatusItem(SuggestionsCaption, "Nothing to add: 'Suggested text' is empty.", ItemStatus.Warning);

            var refused = lines.Select(l => (Line: l, Problems: new TitleCleaner(l).Problems)).Where(r => r.Problems.Count > 0).ToList();
            var added = lines.Where(l => refused.All(r => r.Line != l)).ToList();
            if (added.Count > 0)
            {
                options.TitleAds = WithEntries(options.TitleAds, added);
                options.AddedFromSuggestions = string.Join("\n", TitleSuggestions.Lines(options.AddedFromSuggestions + "\n" + string.Join("\n", added)));
                store.Update(data => TitleSuggestions.Added(data, added));
            }

            var text = added.Count > 0
                ? $"Added to the list: {string.Join(", ", added)}. " +
                  (options.CleanWhereSuggested ? "The next run cleans them out of the names they're in; test mode shows what it would change." : "Runs use them from now on.")
                : "Nothing added.";
            if (refused.Count > 0) text += " Left in the box: " + string.Join("; ", refused.SelectMany(r => r.Problems)) + ".";
            return new StatusItem(SuggestionsCaption, text, refused.Count > 0 ? ItemStatus.Warning : ItemStatus.Succeeded);
        }

        /// <summary>
        /// The list with the picked entries added, each once, and sorted so it stays easy to read: website endings, then
        /// words, then regular expressions, each A-Z.
        /// </summary>
        internal static string WithEntries(string list, IEnumerable<string> entries)
        {
            var lines = (list ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            foreach (var entry in entries)
                if (!lines.Any(l => string.Equals(l, entry, StringComparison.OrdinalIgnoreCase))) lines.Add(entry);
            int Kind(string l) => l.StartsWith("*.", StringComparison.Ordinal) ? 0 : l.Length > 2 && l[0] == '/' && l[l.Length - 1] == '/' ? 2 : 1;
            return string.Join("\n", lines.OrderBy(Kind).ThenBy(l => l, StringComparer.OrdinalIgnoreCase));
        }

        // Not awaited: it runs on after the click, and the page shows how far it is. It sets its progress before it
        // first waits, so the status right after the click already says it's downloading.
        internal void StartWhisperDownload(PluginOptions options) => _ = DownloadWhisper(options.WhisperModel, CheckModelOf(options));

        // The chosen model first, then (for "Act on it") the check model; the second waits for the first.
        private async Task DownloadWhisper(string model, string checkModel)
        {
            if (await Whisper.Ensure(model, CancellationToken.None).ConfigureAwait(false) == null && checkModel != null)
                await Whisper.Ensure(checkModel, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>The model for the 10-clip check when acting on what's heard; null when it isn't needed or is the chosen one.</summary>
        internal static string CheckModelOf(PluginOptions options)
        {
            if (!MediaProcessor.DoubleChecks(options)) return null;
            var check = WhisperFiles.CheckModelFor(options.WhisperModel).Name;
            return check == WhisperFiles.Model(options.WhisperModel).Name ? null : check;
        }

        internal StatusItem WhisperStatusFor(PluginOptions options) => WhisperStatus(Whisper, options.WhisperModel, CheckModelOf(options));

        private static string ModelText(WhisperModelInfo info) => $"the {info.Name} model ({WhisperFiles.SizeText(info.File.Size)}, dated {info.Date})";

        internal static StatusItem WhisperStatus(WhisperInstall whisper, string model, string checkModel = null)
        {
            var info = WhisperFiles.Model(model);
            var check = checkModel != null ? WhisperFiles.Model(checkModel) : null;
            var pinned = $"whisper.cpp v{WhisperFiles.ProgramVersion} ({WhisperFiles.ProgramDate})";
            var models = ModelText(info) + (check != null ? $", and {ModelText(check)} for the 10-clip check" : "");
            if (whisper.Downloading != null)
                return new StatusItem(PluginOptions.WhisperCaption, "Downloading " + whisper.Downloading + ". This line updates as it goes.", ItemStatus.InProgress);
            if (whisper.IsReady(model) && (check == null || whisper.IsReady(check.Name)))
            {
                var installed = whisper.InstalledVersion == null ? "whisper-cli (put there by hand)"
                    : whisper.InstalledVersion == WhisperFiles.ProgramVersion ? pinned
                    : "whisper.cpp v" + whisper.InstalledVersion;
                var downloaded = whisper.ModelDownloaded(model)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return new StatusItem(PluginOptions.WhisperCaption,
                    $"Ready: {installed} with {models}, downloaded {downloaded}, in {whisper.Folder}.{NewerWhisper(whisper, info, check)}",
                    ItemStatus.Succeeded);
            }
            if (whisper.LastProblem != null)
                return new StatusItem(PluginOptions.WhisperCaption, "Download failed: " + whisper.LastProblem, ItemStatus.Failed);
            if (whisper.IsOldDownload)
                return new StatusItem(PluginOptions.WhisperCaption,
                    $"whisper.cpp v{whisper.InstalledVersion} is downloaded; this AudioRemover uses {pinned}. It's downloaded the next time " +
                    "Whisper is needed, or click Download Whisper.", ItemStatus.None);
            var old = new[] { info, check }.FirstOrDefault(m => m != null && whisper.IsOldModel(m.Name));
            if (old != null)
                return new StatusItem(PluginOptions.WhisperCaption,
                    $"An older {old.Name} model file is downloaded; this AudioRemover uses a newer one. It's downloaded the next time " +
                    "Whisper is needed, or click Download Whisper.", ItemStatus.None);
            return new StatusItem(PluginOptions.WhisperCaption,
                $"Not downloaded yet: {pinned}, about 10 MB, and {models}. " +
                "Click Download Whisper, or the first run that needs it downloads it.", ItemStatus.None);
        }

        // Only once the daily check has an answer. AudioRemover doesn't fetch newer files by itself: they come with an
        // AudioRemover update, after they've been tried, with their own checksums.
        internal static string NewerWhisper(WhisperInstall whisper, WhisperModelInfo model, WhisperModelInfo checkModel = null)
        {
            var models = new[] { model, checkModel }.Where(m => m != null).ToList();
            var changed = models.Where(m => whisper.ModelUnchanged(m.Name) == false).ToList();
            var current = models.Where(m => whisper.ModelUnchanged(m.Name) == true).ToList();
            var program = whisper.LatestVersion;
            if (program == null && changed.Count == 0 && current.Count == 0) return "";

            var programNewer = program != null && WhisperInstall.IsNewer(program, WhisperFiles.ProgramVersion);
            var on = whisper.AnsweredUtc?.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var latest = " Latest check" + (on != null ? $" ({on})" : "") + ": ";

            // Every answer is in and nothing is newer. Only said of a whisper.cpp AudioRemover downloaded itself: one put
            // there by hand could be any build.
            if (program != null && !programNewer && current.Count == models.Count && whisper.InstalledVersion == WhisperFiles.ProgramVersion)
                return latest + $"you have the current version of all files: whisper.cpp v{WhisperFiles.ProgramVersion} and {Models(models, " model", " models")}.";

            var news = new List<string>();
            if (program != null) news.Add(programNewer ? $"whisper.cpp v{program} is out" : "that's the newest whisper.cpp");
            if (changed.Count > 0) news.Add(Models(changed, " model file on Hugging Face has changed", " model files on Hugging Face have changed"));
            if (current.Count > 0) news.Add(Models(current, " model is current", " models are current"));
            return latest + string.Join("; ", news) + "." +
                   (programNewer || changed.Count > 0 ? " AudioRemover moves to newer files in one of its own updates, after trying them." : "");
        }

        // "the Base model is current", "the Base and Small models are current".
        private static string Models(IList<WhisperModelInfo> models, string one, string several) =>
            "the " + string.Join(" and ", models.Select(m => m.Name)) + (models.Count > 1 ? several : one);

        // The settings file keeps the name earlier versions used, so saved settings carry over after the rename.
        private const string SettingsFileName = "AudioRemover";

        /// <summary>"0.6.9", or "0.6.9-alpha.3" for a local build.</summary>
        public static string VersionText
        {
            get
            {
                var informational = typeof(Plugin).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (!string.IsNullOrEmpty(informational)) return informational.Split('+')[0];
                var v = typeof(Plugin).Assembly.GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }

        public override string Name => "AudioRemover";

        public override string Description => "Removes foreign-language audio tracks from video files, with test mode and backups.";

        public override Guid Id => new Guid("06efe7db-0598-4638-a4b2-a0917e318ca9");

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public Stream GetThumbImage() => GetType().Assembly.GetManifestResourceStream("Emby.AudioRemover.thumb.png");

        internal const string PageName = "AudioRemoverSettings";

        /// <summary>
        /// The settings page (Configuration/settings.html) and its script. It's the page under Plugins and, when
        /// 'Show in the server sidebar menu' is on, also in the sidebar.
        /// </summary>
        public IEnumerable<PluginPageInfo> GetPages() => new[]
        {
            new PluginPageInfo
            {
                Name = PageName,
                DisplayName = Name,
                EmbeddedResourcePath = "Emby.AudioRemover.Configuration.settings.html",
                IsMainConfigPage = true,
                EnableInMainMenu = Options.ShowInSidebar,
                MenuIcon = "volume_off"
            },
            new PluginPageInfo
            {
                Name = PageName + "Js",
                EmbeddedResourcePath = "Emby.AudioRemover.Configuration.settings.js"
            }
        };

        public PluginOptions Options => _store.Get();

        internal PluginOptions OptionsForEditing() => _store.GetCopy();

        public void Save(PluginOptions options) => _store.Save(options);

        public override PluginInfo GetPluginInfo()
        {
            var info = base.GetPluginInfo();
            info.ConfigurationFileName = _store.FilePath;
            return info;
        }

        // The task manager is looked up when needed rather than at startup, when not every service is ready yet.
        private IScheduledTaskWorker RemoveTaskWorker() =>
            _applicationHost.Resolve<ITaskManager>().ScheduledTasks.FirstOrDefault(w => w.ScheduledTask is RemoveAudioTask);

        /// <returns>The status line to show after the click.</returns>
        internal StatusItem RunNow()
        {
            var worker = RemoveTaskWorker();
            if (worker != null && worker.State != TaskState.Idle) return CurrentStatus();

            NewVideos.Take(); // a full run checks them anyway
            _applicationHost.Resolve<ITaskManager>().QueueIfNotRunning<RemoveAudioTask>();
            return RunStatus.Started(DateTime.Now);
        }

        /// <returns>The status line to show after the click.</returns>
        internal StatusItem StopRun()
        {
            var worker = RemoveTaskWorker();
            // Already stopping: the status says so ("Stopping…"); clicking again changes nothing.
            if (worker?.State == TaskState.Cancelling) return CurrentStatus();
            if (worker == null || worker.State != TaskState.Running) return RunStatus.NothingToStop(CurrentStatus());

            // Like Stop on Emby's Scheduled Tasks page: the video being changed is left as it was, its new copy deleted.
            _applicationHost.Resolve<ITaskManager>().Cancel(worker);
            return RunStatus.Stopping();
        }

        // Never let a status problem stop the settings page from opening.
        public const string RestoreCaption = "Restore";

        /// <summary>
        /// Every video with a backup, newest first so what was just changed is at the top, or why the Restore button won't.
        /// The path filter isn't used: the ticks on the list decide what's restored.
        /// </summary>
        internal (string Refusal, List<BackupEntry> Backups) RestorePlan()
        {
            var busy = _applicationHost.Resolve<ITaskManager>().ScheduledTasks
                .Any(w => (w.ScheduledTask is RemoveAudioTask || w.ScheduledTask is RestoreBackupsTask) && w.State != TaskState.Idle);
            var backups = NewestFirst(_state.Load().Backups);
            return (RestoreRefusal(busy, backups.Count), backups);
        }

        internal static List<BackupEntry> NewestFirst(IEnumerable<BackupEntry> backups) =>
            backups.OrderByDescending(b => b.CreatedUtc).ThenBy(b => b.OriginalPath, StringComparer.OrdinalIgnoreCase).ToList();

        // New videos Emby added, handed over by NewVideoWatcher: the next run checks only these.
        internal PendingVideos NewVideos { get; } = new PendingVideos();

        // The videos selected on the restore list, for the restore task to take instead of the path filter.
        private List<string> _pendingRestore;

        internal List<string> TakePendingRestore() => System.Threading.Interlocked.Exchange(ref _pendingRestore, null);

        /// <returns>The status line to show under the Restore button.</returns>
        internal StatusItem StartRestore(IList<BackupEntry> chosen)
        {
            _pendingRestore = chosen.Select(b => b.OriginalPath).ToList();
            _applicationHost.Resolve<ITaskManager>().QueueIfNotRunning<RestoreBackupsTask>();
            return new StatusItem(RestoreCaption,
                $"Restoring {chosen.Count} video{(chosen.Count == 1 ? "" : "s")}, started at {DateTime.Now:HH:mm}. Its log is on the Logs tab.", ItemStatus.InProgress);
        }

        public const int RestoreListLimit = 200;

        /// <summary>The listed videos that are selected, in list order.</summary>
        internal static List<BackupEntry> SelectedBackups(IList<BackupEntry> listed, IEnumerable<int> selection) =>
            (selection ?? Enumerable.Empty<int>())
                .Where(i => i >= 0 && i < Math.Min(listed.Count, RestoreListLimit))
                .Distinct()
                .OrderBy(i => i)
                .Select(i => listed[i])
                .ToList();

        internal static string RestoreRefusal(bool busy, int backups)
        {
            if (busy) return "Nothing restored: AudioRemover is running. Try again when it has finished.";
            if (backups == 0) return "There are no backups to restore.";
            return null;
        }

        internal StatusItem CurrentStatus()
        {
            try
            {
                var worker = RemoveTaskWorker();
                return RunStatus.Describe(worker?.State, worker?.CurrentProgress, worker?.LastExecutionResult, _state.Load().LastRun);
            }
            catch (Exception ex)
            {
                return new StatusItem("Status", "Status unavailable: " + ex.Message, ItemStatus.Unknown);
            }
        }

        internal string BackupCountText()
        {
            try
            {
                var count = _state.Load().Backups.Count;
                return count == 1 ? "1 video has a backup." : $"{count} videos have a backup.";
            }
            catch (Exception ex)
            {
                return "Backups unavailable: " + ex.Message;
            }
        }

        /// <summary>What the settings page shows under "Text to remove": skipped entries and why, and the sample name cleaned.</summary>
        internal static StatusItem CheckTitleAds(string list, string sample)
        {
            var cleaner = new TitleCleaner(list);
            var text = cleaner.Problems.Count == 0
                ? $"All {cleaner.EntryCount} entries are fine."
                : $"{cleaner.Problems.Count} skipped: " + string.Join("; ", cleaner.Problems) + ".";
            if (!string.IsNullOrWhiteSpace(sample))
            {
                var after = cleaner.Clean(sample.Trim());
                text += $" '{sample.Trim()}' becomes " + (after.Length == 0 ? "no title (removed)." : $"'{after}'.");
            }
            return new StatusItem(PluginOptions.TitleAdsCaption, text, cleaner.Problems.Count == 0 ? ItemStatus.Succeeded : ItemStatus.Warning);
        }

        /// <summary>The libraries that can be picked: everything but music, books and photos.</summary>
        internal List<EditorSelectOption> LibraryChoices() =>
            _libraryManager.GetVirtualFolders()
                .Where(f => f.CollectionType != "music" && f.CollectionType != "books" && f.CollectionType != "photos")
                .Select(f => new EditorSelectOption { Value = f.ItemId, Name = f.Name, IsEnabled = true })
                .ToList();

        /// <summary>The Whisper line on the Listen tab. While listening is on, it also looks for newer files once a day.</summary>
        internal StatusItem WhisperStatusChecked(PluginOptions options)
        {
            if (options.Listen)
            {
                Whisper.CheckForNewerDaily(DateTime.UtcNow, options.WhisperModel);
                var check = CheckModelOf(options);
                if (check != null) Whisper.CheckForNewerDaily(DateTime.UtcNow, check);
            }
            return WhisperStatusFor(options);
        }

        /// <summary>The secret in the log links on the settings page.</summary>
        internal LogLinkKey LogLinkKey { get; } = new LogLinkKey();

        // Only the kept languages: the default can't go to an audio track that gets removed.
        internal static List<EditorSelectOption> DefaultLanguageChoicesFor(string keepLanguages)
        {
            var choices = new List<EditorSelectOption> { new EditorSelectOption { Value = string.Empty, Name = "Automatic", IsEnabled = true } };
            choices.AddRange(PlannerSettings.ParseLanguages(keepLanguages)
                .Where(code => code != "und")
                .OrderBy(Languages.NameOf, StringComparer.OrdinalIgnoreCase)
                .Select(code => new EditorSelectOption { Value = code, Name = $"{Languages.NameOf(code)} ({code})", IsEnabled = true }));
            return choices;
        }

        // Codes saved earlier that aren't in the standard list stay selectable, so saving never drops them.
        internal static List<EditorSelectOption> LanguageChoicesFor(string selected)
        {
            var standard = Languages.All.Select(l => new EditorSelectOption { Value = l.Code, Name = $"{l.Name} ({l.Code})", IsEnabled = true });
            var extra = PlannerSettings.ParseLanguages(selected)
                .Where(code => Languages.All.All(l => l.Code != code))
                .Select(code => new EditorSelectOption { Value = code, Name = code, IsEnabled = true });
            return standard.Concat(extra).ToList();
        }
    }
}
