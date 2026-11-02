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

        /// <summary>Drops the lists and log text that are only filled in for display, so they never go into the settings file.</summary>
        // Filled in only to show the page; not settings.
        private static readonly HashSet<string> DisplayOnly = new HashSet<string> { nameof(SelectedLog), nameof(LogText) };

        /// <summary>Every setting as "Name: value", in page order, for the top of each run log.</summary>
        internal List<string> DescribeSettings() =>
            GetType().GetProperties()
                .Where(p => p.CanWrite && !DisplayOnly.Contains(p.Name)
                            && (p.PropertyType == typeof(bool) || p.PropertyType == typeof(string) || p.PropertyType == typeof(int) || p.PropertyType.IsEnum)
                            && p.GetCustomAttribute<BrowsableAttribute>()?.Browsable != false)
                .Select(p =>
                {
                    var label = p.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName;
                    var value = p.GetValue(this);
                    var text = value is bool b ? (b ? "on" : "off")
                        : value is string s ? (s.Length == 0 ? "(empty)" : s)
                        : value?.ToString() ?? "(empty)";
                    return $"{(string.IsNullOrEmpty(label) ? p.Name : label)}: {text}";
                })
                .ToList();

        internal void ClearDisplayOnlyFields()
        {
            LibraryChoices = new List<EditorSelectOption>();
            LanguageChoices = new List<EditorSelectOption>();
            DefaultLanguageChoices = new List<EditorSelectOption>();
            LogChoices = new List<EditorSelectOption>();
            RestoreItems = new GenericItemList();
            RestoreListShown = null;
            SelectedLog = null;
            LogText = null;
            LogLinks = new GenericItemList();
        }
    }
}
