using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Emby.AudioRemover.Core;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Common;
using Emby.Web.GenericEdit.Elements;
using Emby.Web.GenericEdit.Elements.List;
using MediaBrowser.Model.Attributes;

namespace Emby.AudioRemover
{
    // Emby builds the plugin's settings page from these properties, in this order.
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "AudioRemover v" + Plugin.VersionText;

        public override string EditorDescription =>
            "Removes foreign-language audio tracks from your videos. Leave Test mode on for your first run and check its log " +
            "at the bottom of the page. Each setting is explained under Help, at the very bottom.";

        [DisplayName("")]
        [Description("Saves your settings and starts a run.")]
        public ButtonItem RunNowButton { get; set; } = new ButtonItem("Run now")
        {
            CommandId = SettingsPageView.RunNowCommand,
            Icon = IconNames.play_arrow
        };

        [DisplayName("")]
        [Description("Stops the run in progress. What's done stays done; the next run goes on from there.")]
        public ButtonItem StopButton { get; set; } = new ButtonItem("Stop")
        {
            CommandId = SettingsPageView.StopCommand,
            Icon = IconNames.stop
        };

        // Our own Save, next to Run now; Emby's Save at the bottom of the page is hidden (SettingsPageView.ShowSave).
        [DisplayName("")]
        public ButtonItem SaveButton { get; set; } = new ButtonItem("Save")
        {
            CommandId = SettingsPageView.SaveCommand,
            Icon = IconNames.save
        };

        [DisplayName("")]
        public StatusItem RunStatus { get; set; } = new StatusItem("Status", "Not run yet");

        [DisplayName("Test mode (dry run)")]
        [Description("Nothing is changed; the log shows what would happen.")]
        public bool DryRun { get; set; } = true;

        [DisplayName("Libraries to process")]
        [Description("Only videos in these libraries are checked.")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LibraryChoices))]
        public string Libraries { get; set; } = string.Empty;

        [Browsable(false)]
        public List<EditorSelectOption> LibraryChoices { get; set; } = new List<EditorSelectOption>();

        [DisplayName("")]
        [Description("Keeps English and audio with no language, and saves.")]
        public ButtonItem QuickSetupButton { get; set; } = new ButtonItem("Quick setup: English + no language")
        {
            CommandId = SettingsPageView.QuickSetupCommand,
            Icon = IconNames.language
        };

        [DisplayName("Audio languages to keep")]
        [Description("Audio in any other language is removed.")]
        [EditMultilSelect]
        [SelectItemsSource(nameof(LanguageChoices))]
        public string KeepLanguages { get; set; } = "eng";

        [Browsable(false)]
        public List<EditorSelectOption> LanguageChoices { get; set; } = new List<EditorSelectOption>();

        [DisplayName("Keep audio with no language set")]
        [Description("It's often the original language, so keeping it is safe.")]
        public bool KeepUndefined { get; set; } = true;

        [DisplayName("Always keep the first audio track")]
        [Description("Usually the original language of the film.")]
        public bool KeepFirstTrack { get; set; }

        [DisplayName("Remove commentary tracks")]
        [Description("Even in languages you keep.")]
        public bool RemoveCommentary { get; set; }

        [DisplayName("Remove audio description tracks")]
        [Description("Narrated tracks for blind viewers, even in languages you keep.")]
        public bool RemoveAudioDescription { get; set; }

        [DisplayName("Set the default audio track")]
        [Description("Moves the default off removed, commentary or description tracks.")]
        public bool SetDefault { get; set; } = true;

        [DisplayName("Default audio language")]
        [Description("This language's audio becomes the default. Automatic: the video decides.")]
        [VisibleCondition(nameof(SetDefault), SimpleCondition.IsTrue)]
        [SelectItemsSource(nameof(DefaultLanguageChoices))]
        public string DefaultLanguage { get; set; } = string.Empty;

        [Browsable(false)]
        public List<EditorSelectOption> DefaultLanguageChoices { get; set; } = new List<EditorSelectOption>();

        [DisplayName("Default to the best-quality track")]
        [Description("Only between tracks in the same language: most channels wins (5.1 over stereo), then lossless, then the higher bitrate.")]
        [VisibleCondition(nameof(SetDefault), SimpleCondition.IsTrue)]
        public bool BestAudioDefault { get; set; }

        [DisplayName("Listen when no kept language is found")]
        [Description("Step 1: for videos with 2+ audio tracks and none tagged in a language you keep, Whisper listens to 3 clips. On its own it only logs what it hears.")]
        public bool Listen { get; set; }

        [DisplayName("Act on what it hears")]
        [Description("Step 2: keeps and removes tracks by the language heard (2 of the 3 clips must agree) and corrects wrong tags.")]
        [VisibleCondition(nameof(Listen), SimpleCondition.IsTrue)]
        public bool ActOnHeard { get; set; }

        [DisplayName("Extra safe: check with a bigger model")]
        [Description("Step 3: when step 1 disagrees with a tag or can't tell, the next bigger model hears 10 more clips; 7 must agree, 90% sure.")]
        [VisibleCondition(nameof(Listen), SimpleCondition.IsTrue)]
        public bool ExtraSafe { get; set; } = true;

        [DisplayName("Check and retag a lone track")]
        [Description("When a video has, or will have, just one audio track and it's tagged with a language you don't keep, Whisper listens and corrects the tag. No tag: left alone. Rewrites the file.")]
        [VisibleCondition(nameof(Listen), SimpleCondition.IsTrue)]
        public bool RetagLoneTrack { get; set; }

        [DisplayName("Tag tracks that have no language")]
        [Description("When a video has two or more audio tracks and none has a language tag, each gets the language Whisper hears. Rewrites the file.")]
        [VisibleCondition(nameof(Listen), SimpleCondition.IsTrue)]
        public bool TagUntaggedTracks { get; set; }

        // Earlier alphas' single dropdown: "Off", "Log", "Act" or "Use" (extra safe). Read once into the switches, then cleared.
        [Browsable(false)]
        public string ListenMode { get; set; }

        internal void CarryOverOldSettings()
        {
            if (string.IsNullOrEmpty(ListenMode)) return;
            var mode = ListenMode;
            Listen = mode == "Log" || mode == "Act" || mode == "Use";
            ActOnHeard = mode == "Act" || mode == "Use";
            ExtraSafe = mode != "Act";
            ListenMode = null;
        }

        [DisplayName("Whisper model")]
        [Description("Downloaded once. Bigger is more accurate but slower.")]
        [VisibleCondition(nameof(Listen), SimpleCondition.IsTrue)]
        [SelectItemsSource(nameof(WhisperModelChoices))]
        public string WhisperModel { get; set; } = WhisperFiles.DefaultModel;

        [Browsable(false)]
        public List<EditorSelectOption> WhisperModelChoices { get; set; } = new List<EditorSelectOption>();

        [DisplayName("")]
        [Description("From github.com/ggml-org/whisper.cpp (v" + WhisperFiles.ProgramVersion + ") and huggingface.co/ggerganov/whisper.cpp, " +
                     "each file checked against its checksum.")]
        [VisibleCondition(nameof(Listen), SimpleCondition.IsTrue)]
        public ButtonItem DownloadWhisperButton { get; set; } = new ButtonItem("Download Whisper")
        {
            CommandId = SettingsPageView.DownloadWhisperCommand,
            Icon = IconNames.file_download
        };

        [DisplayName("")]
        [VisibleCondition(nameof(Listen), SimpleCondition.IsTrue)]
        public StatusItem WhisperStatus { get; set; } = new StatusItem(WhisperCaption, "");

        internal const string WhisperCaption = "Whisper";

        [DisplayName("Clean up track names")]
        [Description("Takes ads like \"- SomeSite.com\" or \"https://www.SomeSite.in\", and release names, out of track names. Details: Help, 'How names are cleaned'.")]
        public bool CleanTitles { get; set; }

        [DisplayName("Also clean up the file's title")]
        [Description("Off: the title stored in the file itself is left as it is.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public bool CleanFileTitle { get; set; }

        [DisplayName("Rewrite a video just for clean-up")]
        [Description("Off: names are only cleaned when a video is rewritten for its audio anyway.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public bool RewriteForTitles { get; set; }

        [DisplayName("Text to remove")]
        [Description("One per line. *.com: any website ending in .com, with any http://, https://, www. or /path around it, and " +
                     "'Downloaded from' or 'Encodes' next to it. /…/: a regular expression. Anything else: that word.")]
        [EditMultiline(8)]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public string TitleAds { get; set; } = TitleCleaner.DefaultList;

        // Only for "Check the list"; never saved.
        [DisplayName("Try the list on a name")]
        [Description("Optional: paste a track name or title to see what's left of it when you click Check the list.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public string TitleAdsSample { get; set; }

        [DisplayName("")]
        [Description("Checks every entry without a run: what's skipped and why.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public ButtonItem CheckTitleAdsButton { get; set; } = new ButtonItem("Check the list")
        {
            CommandId = SettingsPageView.CheckTitleAdsCommand,
            Icon = IconNames.fact_check
        };

        [DisplayName("")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public StatusItem TitleAdsStatus { get; set; } = new StatusItem(TitleAdsCaption, "");

        internal const string TitleAdsCaption = "Text to remove";

        [DisplayName("")]
        [Description("Puts back the list AudioRemover comes with, and saves.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public ButtonItem ResetTitleAdsButton { get; set; } = new ButtonItem("Reset to default")
        {
            CommandId = SettingsPageView.ResetTitleAdsCommand,
            Icon = IconNames.settings_backup_restore
        };

        [DisplayName("")]
        [Description("Saves, then reads the track names Emby has for your libraries (no files are opened) and suggests text the list misses.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public ButtonItem FindTitleAdsButton { get; set; } = new ButtonItem("Find text to remove")
        {
            CommandId = SettingsPageView.FindTitleAdsCommand,
            Icon = IconNames.manage_search
        };

        [DisplayName("")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public StatusItem SuggestionsStatus { get; set; } = new StatusItem(Plugin.SuggestionsCaption, "");

        [DisplayName("Suggestions")]
        [Description("Most common first. Switch on the ones to add.")]
        [VisibleCondition(nameof(SuggestionsShown), SimpleCondition.IsNotNullOrEmpty)]
        public GenericItemList SuggestionItems { get; set; } = new GenericItemList();

        // Set while suggestions are on the page, so the list and its button show.
        [Browsable(false)]
        public string SuggestionsShown { get; set; }

        [DisplayName("")]
        [Description("Adds the switched-on suggestions to 'Text to remove' (kept sorted) and saves.")]
        [VisibleCondition(nameof(SuggestionsShown), SimpleCondition.IsNotNullOrEmpty)]
        public ButtonItem AddSuggestionsButton { get; set; } = new ButtonItem("Add selected to the list")
        {
            CommandId = SettingsPageView.AddSuggestionsCommand,
            Icon = IconNames.playlist_add
        };

        [DisplayName("Backup of the original file")]
        [Description("Where the original goes before a video is changed.")]
        public BackupMode BackupMode { get; set; } = BackupMode.NextToOriginal;

        [DisplayName("Backup folder")]
        [EditFolderPicker]
        [VisibleCondition(nameof(BackupMode), ValueCondition.IsEqual, BackupMode.BackupFolder)]
        public string BackupFolder { get; set; } = string.Empty;

        [DisplayName("Delete backups older than (days)")]
        [Description("0 keeps them forever.")]
        [MinValue(0)]
        [MaxValue(3650)]
        public int BackupRetentionDays { get; set; }

        [DisplayName("Max videos to change per run")]
        [Description("0 = no limit. Use 1–5 while testing.")]
        [MinValue(0)]
        [MaxValue(100000)]
        public int MaxItemsPerRun { get; set; } = 5;

        [DisplayName("Only process paths containing")]
        [Description("Only paths containing this text. Several: separate with ';'.")]
        public string OnlyPathContains { get; set; } = string.Empty;

        [DisplayName("Process new videos as they're added")]
        [Description("Checks videos Emby adds, a few minutes after they arrive.")]
        public bool ProcessNewVideos { get; set; }

        [DisplayName("")]
        [Description("Lists the videos with a backup to choose from.")]
        public ButtonItem RestoreButton { get; set; } = NewRestoreButton();

        internal static ButtonItem NewRestoreButton(int? confirmCount = null) => new ButtonItem(
            confirmCount == null ? "Restore originals from backups…" : confirmCount == 1 ? "Yes, restore this video" : "Yes, restore the selected videos")
        {
            CommandId = confirmCount == null ? SettingsPageView.RestoreCommand : SettingsPageView.RestoreConfirmCommand,
            Icon = confirmCount == null ? IconNames.restore : IconNames.warning,
            // Emby asks this before sending the command.
            ConfirmationPrompt = confirmCount == null ? null
                : "Restore the selected videos? Each one is replaced by its original from the backup, undoing what AudioRemover changed."
        };

        [DisplayName("")]
        public StatusItem RestoreStatus { get; set; } = new StatusItem(Plugin.RestoreCaption, "");

        [DisplayName("Videos that will be restored")]
        [Description("Switch Restore on for each video to restore.")]
        [VisibleCondition(nameof(RestoreListShown), SimpleCondition.IsNotNullOrEmpty)]
        public GenericItemList RestoreItems { get; set; } = new GenericItemList();

        // Set while the restore list is on the page, so the list and its buttons show.
        [Browsable(false)]
        public string RestoreListShown { get; set; }

        [DisplayName("")]
        [VisibleCondition(nameof(RestoreListShown), SimpleCondition.IsNotNullOrEmpty)]
        public ButtonItem RestoreSelectAllButton { get; set; } = NewSelectAllButton(allSelected: false);

        /// <summary>One button: "Unselect all" while every video is selected, otherwise "Select all".</summary>
        internal static ButtonItem NewSelectAllButton(bool allSelected) => new ButtonItem(allSelected ? "Unselect all" : "Select all")
        {
            CommandId = SettingsPageView.RestoreSelectAllCommand,
            Icon = allSelected ? IconNames.check_box_outline_blank : IconNames.check_box
        };

        [DisplayName("Show in the server sidebar menu")]
        [Description("Also lists AudioRemover in the server sidebar.")]
        public bool ShowInSidebar { get; set; } = true;

        [DisplayName("Re-check every video on next run")]
        [Description("Checks every video again on the next real run.")]
        [IsAdvanced]
        public bool ReprocessAll { get; set; }

        [DisplayName("")]
        public CaptionItem LogsCaption { get; set; } = new CaptionItem("Log files");

        [DisplayName("Logs")]
        [Description("Pick an older run to see its log.")]
        [SelectItemsSource(nameof(LogChoices))]
        [AutoPostBack(SettingsPageView.ShowLogCommand, nameof(SelectedLog))]
        public string SelectedLog { get; set; }

        [Browsable(false)]
        public List<EditorSelectOption> LogChoices { get; set; } = new List<EditorSelectOption>();

        // "Open this log in a new window" and "Download this log", for the log picked above.
        [DisplayName("")]
        public GenericItemList LogLinks { get; set; } = new GenericItemList();

        [DisplayName("")]
        [EditMultiline(20)]
        public string LogText { get; set; }

        [DisplayName("")]
        public CaptionItem HelpCaption { get; set; } = new CaptionItem("Help");

        // Help: each setting's name in bold with its explanation underneath, in page order. Emby has no way to add these
        // from a list, so there's one entry per setting; a test checks there's one for every entry in SettingsHelp.
        [DisplayName("")] public StatusItem Help01 { get; set; } = SettingsHelp.Item(0);
        [DisplayName("")] public StatusItem Help02 { get; set; } = SettingsHelp.Item(1);
        [DisplayName("")] public StatusItem Help03 { get; set; } = SettingsHelp.Item(2);
        [DisplayName("")] public StatusItem Help04 { get; set; } = SettingsHelp.Item(3);
        [DisplayName("")] public StatusItem Help05 { get; set; } = SettingsHelp.Item(4);
        [DisplayName("")] public StatusItem Help06 { get; set; } = SettingsHelp.Item(5);
        [DisplayName("")] public StatusItem Help07 { get; set; } = SettingsHelp.Item(6);
        [DisplayName("")] public StatusItem Help08 { get; set; } = SettingsHelp.Item(7);
        [DisplayName("")] public StatusItem Help09 { get; set; } = SettingsHelp.Item(8);
        [DisplayName("")] public StatusItem Help10 { get; set; } = SettingsHelp.Item(9);
        [DisplayName("")] public StatusItem Help11 { get; set; } = SettingsHelp.Item(10);
        [DisplayName("")] public StatusItem Help12 { get; set; } = SettingsHelp.Item(11);
        [DisplayName("")] public StatusItem Help13 { get; set; } = SettingsHelp.Item(12);
        [DisplayName("")] public StatusItem Help14 { get; set; } = SettingsHelp.Item(13);
        [DisplayName("")] public StatusItem Help15 { get; set; } = SettingsHelp.Item(14);
        [DisplayName("")] public StatusItem Help16 { get; set; } = SettingsHelp.Item(15);
        [DisplayName("")] public StatusItem Help17 { get; set; } = SettingsHelp.Item(16);
        [DisplayName("")] public StatusItem Help18 { get; set; } = SettingsHelp.Item(17);
        [DisplayName("")] public StatusItem Help19 { get; set; } = SettingsHelp.Item(18);
        [DisplayName("")] public StatusItem Help20 { get; set; } = SettingsHelp.Item(19);
        [DisplayName("")] public StatusItem Help21 { get; set; } = SettingsHelp.Item(20);
        [DisplayName("")] public StatusItem Help22 { get; set; } = SettingsHelp.Item(21);
        [DisplayName("")] public StatusItem Help23 { get; set; } = SettingsHelp.Item(22);
        [DisplayName("")] public StatusItem Help24 { get; set; } = SettingsHelp.Item(23);
        [DisplayName("")] public StatusItem Help25 { get; set; } = SettingsHelp.Item(24);
        [DisplayName("")] public StatusItem Help26 { get; set; } = SettingsHelp.Item(25);
        [DisplayName("")] public StatusItem Help27 { get; set; } = SettingsHelp.Item(26);
        [DisplayName("")] public StatusItem Help28 { get; set; } = SettingsHelp.Item(27);
        [DisplayName("")] public StatusItem Help29 { get; set; } = SettingsHelp.Item(28);
        [DisplayName("")] public StatusItem Help30 { get; set; } = SettingsHelp.Item(29);
        [DisplayName("")] public StatusItem Help31 { get; set; } = SettingsHelp.Item(30);
        [DisplayName("")] public StatusItem Help32 { get; set; } = SettingsHelp.Item(31);
        [DisplayName("")] public StatusItem Help33 { get; set; } = SettingsHelp.Item(32);
        [DisplayName("")] public StatusItem Help34 { get; set; } = SettingsHelp.Item(33);
        [DisplayName("")] public StatusItem Help35 { get; set; } = SettingsHelp.Item(34);
        [DisplayName("")] public StatusItem Help36 { get; set; } = SettingsHelp.Item(35);

        /// <summary>Drops the lists and log text that are only filled in for display, so they never go into the settings file.</summary>
        // Filled in only to show the page; not settings.
        private static readonly HashSet<string> DisplayOnly = new HashSet<string> { nameof(SelectedLog), nameof(LogText), nameof(TitleAdsSample) };

        /// <summary>Every setting as "Name: value", in page order, for the top of each run log.</summary>
        /// <param name="libraryNames">Emby's library names by id, so the libraries read "Movies (12082)" rather than just the id.</param>
        internal List<string> DescribeSettings(IDictionary<string, string> libraryNames = null) =>
            GetType().GetProperties()
                .Where(p => p.CanWrite && !DisplayOnly.Contains(p.Name)
                            && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(string) || p.PropertyType == typeof(int) || p.PropertyType.IsEnum)
                            && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
                .Select(p =>
                {
                    var label = p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
                    var value = p.GetValue(this);
                    if (p.Name == nameof(Libraries) && libraryNames != null) value = NameLibraries(Libraries, libraryNames);
                    // A multi-line box goes on one line.
                    var text = value is bool b ? (b ? "on" : "off")
                        : value is string s ? (s.Length == 0 ? "(empty)" : string.Join(", ", s.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)))
                        : value?.ToString() ?? "(empty)";
                    return $"{(string.IsNullOrEmpty(label) ? p.Name : label)}: {text}";
                })
                .ToList();

        /// <summary>"Movies (12082), TV Shows (12083)": the ids the setting holds, each with its name when Emby still has it.</summary>
        internal static string NameLibraries(string ids, IDictionary<string, string> names) =>
            string.Join(", ", (ids ?? string.Empty).Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Select(id => names.TryGetValue(id, out var name) ? $"{name} ({id})" : id));

        internal void ClearDisplayOnlyFields()
        {
            LibraryChoices = new List<EditorSelectOption>();
            LanguageChoices = new List<EditorSelectOption>();
            DefaultLanguageChoices = new List<EditorSelectOption>();
            WhisperModelChoices = new List<EditorSelectOption>();
            WhisperStatus = new StatusItem(WhisperCaption, "");
            LogChoices = new List<EditorSelectOption>();
            RestoreItems = new GenericItemList();
            RestoreListShown = null;
            SelectedLog = null;
            LogText = null;
            LogLinks = new GenericItemList();
            TitleAdsSample = null;
            SuggestionsStatus = new StatusItem(Plugin.SuggestionsCaption, "");
            SuggestionItems = new GenericItemList();
            SuggestionsShown = null;
            TitleAdsStatus = new StatusItem(TitleAdsCaption, "");
        }
    }
}
