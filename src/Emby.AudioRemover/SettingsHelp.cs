
using Emby.Web.GenericEdit.Elements;

namespace Emby.AudioRemover
{
    /// <summary>
    /// The Help section at the bottom of the settings page: the full explanation of each setting, in page order. The
    /// settings themselves only carry a one-line description.
    /// </summary>
    internal static class SettingsHelp
    {
        internal static readonly (string Setting, string Text)[] Entries =
        {
            ("Run now",
                "Saves your settings and starts a run. The status under the button shows progress, and the log appears under Log files at the bottom " +
                "of the page. You can also run or schedule it from Settings > Scheduled Tasks > AudioRemover."),
            ("Test mode (dry run)",
                "When on, nothing is changed: each run only writes to its log what it WOULD do. Leave it on for your first run and turn it " +
                "off when the log looks right. Test mode checks the same videos a real run would."),
            ("Libraries to process",
                "Only videos in the ticked libraries are checked."),
            ("Quick setup",
                "Ticks only English under 'Audio languages to keep', turns on 'Keep audio with no language set', and saves."),
            ("Audio languages to keep",
                "Tick every language you want to keep. Audio tracks in any language that isn't ticked are removed. All spellings of " +
                "a language match, e.g. ger, deu and de. A video never loses all its sound: when none of its main audio tracks is in " +
                "a language you keep (a foreign film with only its own language, say), every track stays and the log says so."),
            ("Keep audio with no language set",
                "Tracks tagged 'und' (undefined) are often the film's original language, so keeping them is the safe choice."),
            ("Always keep the first audio track",
                "Keeps the video's first audio track whatever its language. In most releases that's the original language, so a " +
                "Japanese film keeps its Japanese audio next to the English dub. Off: only the languages you keep stay."),
            ("Remove commentary tracks",
                "Removes audio tracks marked as commentary or titled 'Commentary', even in a language you keep. Only while a main " +
                "audio track stays."),
            ("Remove audio description tracks",
                "Removes tracks where a narrator describes what's on screen for blind and partially sighted viewers (marked as such, " +
                "or titled 'Audio Description', 'Descriptive' or 'AD'), even in a language you keep. Only while a main audio track stays."),
            ("Set the default audio track",
                "Players start on the audio track a video marks as default. When that track is removed, or is commentary or audio " +
                "description, the default moves to the first kept main track. Videos where only this changes are rewritten too."),
            ("Default audio language",
                "Overrides the video: the first main audio track in this language becomes the default, even if the video marks " +
                "another track. Only the languages you keep are listed; save to update the list. Automatic: the video decides."),
            ("Backup of the original file",
                "NextToOriginal saves 'movie.mkv.audioremover.bak' beside the video (Emby ignores it). BackupFolder copies originals to " +
                "the folder you choose. None deletes the original, which isn't recommended: without a backup a change can't be undone."),
            ("Backup folder",
                "Where originals are copied when 'Backup of the original file' is BackupFolder."),
            ("Delete backups older than (days)",
                "Backups older than this are deleted at the start of a run. 0 keeps them forever."),
            ("Max videos to change per run",
                "A run stops after changing this many videos. 0 means no limit. Use 1 to 5 while testing; each change can take several " +
                "minutes over the network. Runs for new videos don't use it: they do every new video."),
            ("Only process paths containing",
                "Only videos whose full path contains this text are checked; capitals don't matter. 'My Movie (2020)' matches only that " +
                "movie, '\\Movies\\' only your Movies folder. For several, separate them with ';' (not commas, titles contain those): " +
                "'My Movie (2020); My Show - S01E02'. Leave empty for everything. Restore doesn't use it."),
            ("Process new videos as they're added",
                "When Emby adds a video (real-time monitoring or a library scan), AudioRemover checks it with these same settings, " +
                "Test mode included. It waits until no new video has come in for 3 minutes, so a whole season is one run with one log. " +
                "Only videos in the libraries above and matching the path filter count. 'Max videos to change per run' doesn't apply: " +
                "every new video is done. It never runs next to another run: it waits and goes after."),
            ("Restore originals from backups",
                "Lists every video that has a backup, newest first, with nothing selected. Select the ones to restore, then click 'Yes, " +
                "restore...': each one is replaced by its original, undoing what AudioRemover changed. " +
                "Test mode doesn't apply. Later runs leave restored videos alone."),
            ("Show in the server sidebar menu",
                "Lists AudioRemover under Advanced in the server sidebar. It's always under Plugins too. Reload the page (or restart Emby) " +
                "after changing this."),
            ("Re-check every video on next run",
                "Forgets which videos were already done, including ones you restored, so the next real run checks all of them again; it " +
                "then switches itself off. In test mode it only includes them and forgets nothing."),
            ("Logs",
                "Shows the last run. Pick an older run to see its log. The links under it open the whole log in a new window or " +
                "download it. The newest 30 are kept."),
        };

        // The setting's name in bold with its explanation wrapped underneath; plain, without a status icon.
        internal static StatusItem Item(int index) => new StatusItem(Entries[index].Setting, Entries[index].Text, ItemStatus.None);
    }
}
