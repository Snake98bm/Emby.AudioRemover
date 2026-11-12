using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Emby.AudioRemover.Core;
using Emby.Web.GenericEdit;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Model.Attributes;

namespace Emby.AudioRemover
{
    // The saved settings. Each one's label, description and limits are what the settings page shows (SettingsLayout.cs puts
    // them on tabs) and what each run log lists. Lists to choose from come from the server (SettingsApi.cs).
    public class PluginOptions : EditableOptionsBase
    {
        public override string EditorTitle => "AudioRemover v" + Plugin.VersionText;

        public override string EditorDescription => PageDescription;

        // Where the page gets each list's choices.
        internal const string LibrariesSource = "Libraries";
        internal const string LanguagesSource = "Languages";
        internal const string DefaultLanguageSource = "DefaultLanguage";
        internal const string WhisperModelsSource = "WhisperModels";

        /// <summary>Under the Download Whisper button: where the files come from.</summary>
        internal const string WhisperDownloadNote =
            "From github.com/ggml-org/whisper.cpp (v" + WhisperFiles.ProgramVersion + ") and huggingface.co/ggerganov/whisper.cpp, " +
            "each file checked against its checksum.";

        internal const string PageDescription =
            "Removes foreign-language audio tracks from your videos. Leave Test mode on for your first run and check its log " +
            "on the Logs tab. Each tab explains its settings at the bottom.";

        [DisplayName("Test mode (dry run)")]
        [Description("Nothing is changed; the log shows what would happen.")]
        public bool DryRun { get; set; } = true;

        [DisplayName("Libraries to process")]
        [Description("Only videos in these libraries are checked.")]
        [EditMultilSelect]
        [SelectItemsSource(LibrariesSource)]
        public string Libraries { get; set; } = string.Empty;

        [DisplayName("Audio languages to keep")]
        [Description("Audio in any other language is removed.")]
        [EditMultilSelect]
        [SelectItemsSource(LanguagesSource)]
        public string KeepLanguages { get; set; } = "eng";

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
        [SelectItemsSource(DefaultLanguageSource)]
        public string DefaultLanguage { get; set; } = string.Empty;

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
        [SelectItemsSource(WhisperModelsSource)]
        public string WhisperModel { get; set; } = WhisperFiles.DefaultModel;

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

        // Only for "Check the list"; never saved, and not a setting: the page's own part shows it.
        [Browsable(false)]
        [DisplayName("Try the list on a name")]
        [Description("Optional: paste a track name or title to see what's left of it when you click Check the list.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public string TitleAdsSample { get; set; }

        internal const string TitleAdsCaption = "Text to remove";

        [DisplayName("Collect suggestions during runs")]
        [Description("Each run adds text it finds in track names to 'Suggested text' below, so it builds up over time.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public bool SuggestDuringRuns { get; set; } = true;

        // Shown from, and saved into, the suggestions file; never the settings file. Not a setting: the page's own part shows it.
        [Browsable(false)]
        [DisplayName("Suggested text")]
        [Description("Found in your track names, not on the list yet. Edit lines, delete them (they're not suggested again) or type " +
                     "your own, same format as above. Then click Add to the list.")]
        [EditMultiline(6)]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public string SuggestedText { get; set; }

        // The box as it was shown, so a save can tell which lines were taken out or typed.
        [Browsable(false)]
        public string SuggestedTextShown { get; set; }

        [DisplayName("Clean names where added suggestions were found")]
        [Description("Videos whose track names have text you added from 'Suggested text' are rewritten to take it out, even with " +
                     "'Rewrite a video just for clean-up' off.")]
        [VisibleCondition(nameof(CleanTitles), SimpleCondition.IsTrue)]
        public bool CleanWhereSuggested { get; set; } = true;

        // What 'Add to the list' added, for the switch above; only entries still on the list count.
        [Browsable(false)]
        public string AddedFromSuggestions { get; set; }

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

        [DisplayName("Wait until SubRemover has checked a video")]
        [Description("Leaves a video unchanged until SubRemover has been through it, so SubRemover can first download subtitles that match the original file (hash-matched). No effect without SubRemover.")]
        public bool WaitForSubRemover { get; set; }

        [DisplayName("Show in the server sidebar menu")]
        [Description("Also lists AudioRemover in the server sidebar.")]
        public bool ShowInSidebar { get; set; } = true;

        [DisplayName("Re-check every video on next run")]
        [Description("Checks every video again on the next real run.")]
        [IsAdvanced]
        public bool ReprocessAll { get; set; }

        /// <summary>Every setting as "Name: value", in page order, for the top of each run log.</summary>
        /// <param name="libraryNames">Emby's library names by id, so the libraries read "Movies (12082)" rather than just the id.</param>
        internal List<string> DescribeSettings(IDictionary<string, string> libraryNames = null) =>
            GetType().GetProperties()
                .Where(p => p.CanWrite
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

        /// <summary>What the page fills in from elsewhere and never saves: the sample name and the suggestions box.</summary>
        internal void ClearDisplayOnlyFields()
        {
            TitleAdsSample = null;
            SuggestedText = null;
            SuggestedTextShown = null;
        }
    }
}
