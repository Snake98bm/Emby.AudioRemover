
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
            ("Stop",
                "Stops the run in progress, like Stop on Emby's Scheduled Tasks page. The video being changed at that moment is left " +
                "exactly as it was (its half-made copy is deleted), videos already done stay done, and the next run carries on " +
                "with the rest. The log's last line says how far it got. Stop doesn't save the page."),
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
                "description, the default moves to the first kept main track. Videos where only this changes are rewritten too, but " +
                "not just to clear a second default flag: players start on the first one, so that's only tidied when tracks are removed anyway."),
            ("Default audio language",
                "Overrides the video: the first main audio track in this language becomes the default, even if the video marks " +
                "another track. Only the languages you keep are listed; save to update the list. Automatic: the video decides."),
            ("Default to the best-quality track",
                "When a language has more than one main track - often a stereo and a 5.1 copy of the same sound - the default goes " +
                "to the best one: the most channels (7.1 over 5.1 over stereo), then lossless (TrueHD, DTS-HD MA, FLAC, PCM) over " +
                "lossy, then the higher bitrate. Only tracks in the same language are compared: an English stereo track never " +
                "loses the default to a French 5.1 one. A track with no language tag could be any language, so it's only compared " +
                "once Whisper has named it ('Act on what it hears' or 'Tag tracks that have no language'). The language is the one " +
                "'Default audio language' picks, or else the video's own default. Moving the flag rewrites the file once. A phone " +
                "or browser that can't play the better track (AC3, DTS or TrueHD) still plays it: Emby converts the sound for it."),
            ("Listen when no kept language is found",
                "Some files have the wrong language on a track, or none: a 'Finnish' track that's really English, or 'unknown'. " +
                "Only when a video has two or more audio tracks and none is tagged with a language you keep (a video that would " +
                "otherwise be left alone), Whisper - a free speech model that runs offline on the server - listens to three 30-second " +
                "clips of each track, from 20%, 50% and 80% of the way in. Videos with a track in a language you keep are decided by " +
                "their tags, and a video with a single track is skipped: its only track always stays, whatever it sounds like. A " +
                "language counts only when at least two clips name it, each at least 80% sure; music or silence doesn't get that far. " +
                "This is step 1. On its own it only LOGS what it heard, e.g. '#1 fin: sounds eng: 3/3 clips, 100% sure'; what's kept, " +
                "removed and tagged is still decided by the tags. Use it to see what listening would do. Steps 2 and 3 are the switches " +
                "under it. Test mode applies as usual. Listening takes a few seconds per track. " +
                "If Whisper can't run (the download failed, or security software blocks whisper-cli), the log warns once; with 'Act " +
                "on what it hears' the videos that needed listening are left for a later run rather than decided by the tags in doubt. " +
                "READING THE LOG: 'sounds eng: 3/3 clips, 97% sure' means all 3 clips named English and Whisper was 97% sure on " +
                "average over them - how many clips agree is the best measure of how right it is; 'confirmed 10/10 by the Small model' " +
                "is step 3. 'couldn't tell' means no two clips agreed at 80% or more. The 'listened to' line lists every clip's " +
                "own answer, e.g. [clips: en 98%, en 96%, en 97%], so you can see where it hesitated."),
            ("Act on what it hears",
                "Step 2, the standard run. Instead of only logging, the track counts as the language Whisper heard: a mislabeled " +
                "English track is kept and the foreign ones go, and since the video is rewritten anyway, the wrong tag is corrected too " +
                "('fin -> eng'). With 'Extra safe' off, the quick listen alone decides (2 of 3 clips, 80% sure) - quicker, with no " +
                "second model to download, but one quick listen decides what's removed. With 'Extra safe' on (recommended), a tag is " +
                "only overridden once step 3 confirms it."),
            ("Extra safe: check with a bigger model",
                "Step 3, a second, more accurate ear: 10 more clips, from 10% to 90% of the way in, with the next bigger Whisper " +
                "model (Base is checked by Small, Tiny by Base, Small by Medium, Medium by itself; it's downloaded too). It runs only " +
                "in two cases. WHEN STEP 1 DISAGREES WITH THE TAG (heard English, tagged Finnish): the heard language is used only if " +
                "at least 7 of the 10 clips confirm it, each at least 90% sure; otherwise the tag stays in charge and the log says " +
                "so. WHEN STEP 1 CAN'T TELL (music, little speech): the bigger model gets to try, and whatever language at least 7 " +
                "of its 10 clips name at 90%+ counts; otherwise it's still 'couldn't tell' and the tag is used. When step 1 agrees " +
                "with the tag, step 3 never runs. 90% because real speech scored 89-100% and music or noise never more than about 73%. " +
                "It takes up to a minute per track, but only on those rare tracks. With step 2 off, it still runs, so the log shows " +
                "what acting on it would do."),
            ("Check and retag a lone track",
                "For the one audio track a video has - or the one left after the others are removed - when it's tagged with a " +
                "language you don't keep, like 'Dutch' or 'Finnish'. A track with no language tag is left as it is. Whisper listens to it (the quick listen, then step 3 if 'Extra " +
                "safe' is on) and, when it's sure of the language, the tag is corrected, e.g. 'dut -> eng', so Emby shows the right " +
                "language. The track itself always stays: a video's only track is never removed. Correcting a tag means rewriting the " +
                "file, so a single-track video that's otherwise fine gets one full copy (once; after that its tag is right). Only MKV " +
                "and MP4 can hold the tag, so other files are skipped. Test mode shows the 'tags:' line it would write."),
            ("Tag tracks that have no language",
                "For a video with two or more audio tracks where none has a language tag - often a stereo and a 5.1 copy of the " +
                "same sound. Whisper listens to them anyway (no track is in a language you keep), and with this on, each track it's " +
                "sure of gets that language as its tag, e.g. 'und -> eng', so Emby shows 'English' instead of 'Unknown'. With 'Extra " +
                "safe' on, step 3 has to agree first. Nothing is removed for it: which tracks stay is up to 'Act on what it hears'. " +
                "Writing tags means rewriting the file, once. A video with a single untagged track is left alone, and only MKV and " +
                "MP4 can hold the tag. Test mode shows the 'tags:' line it would write."),
            ("Whisper model",
                "The speech model Whisper uses. It's downloaded once, with whisper.cpp (about 10 MB), into AudioRemover-whisper in " +
                "Emby's plugin settings folder: when you click 'Download Whisper' (which saves first), or else by the first run that " +
                "needs it. whisper.cpp comes from github.com/ggml-org/whisper.cpp and the model from huggingface.co/ggerganov/whisper.cpp. " +
                "The status under the button shows what's downloaded and, checked once a day, whether there's a newer whisper.cpp or " +
                "a changed model; newer files come with an AudioRemover update, which replaces the old ones by itself. " +
                "Base is the best balance for telling languages apart; Tiny is quicker but less sure; " +
                "Small and Medium are more accurate but much slower. With 'Extra safe' on, the next bigger model is downloaded too, for " +
                "the 10-clip check (Base: Small, 465 MB). Each file comes from one fixed address and is kept only if its " +
                "checksum matches, and only the program and the libraries it needs are unpacked. Ready-made downloads exist for " +
                "Windows; on other systems, put whisper-cli and the model file in that folder yourself."),
            ("Clean up track names",
                "Download sites put their name on every track, e.g. 'English - SomeSite.com', and Emby shows it in the audio and " +
                "subtitle menus. This takes what 'Text to remove' lists out of the names of the tracks that stay, with the ' - ' or " +
                "brackets around it. A name that's only an ad, or only the download's file or release name " +
                "('My.Movie.2020.1080p.WEBRip.x265-GROUP.mkv'), is removed, and Emby shows the track's language and format instead. " +
                "Only MKV files have track names; MP4 doesn't store them. Exactly what it looks for and removes is under 'How names " +
                "are cleaned' below."),
            ("Also clean up the file's title",
                "The title stored in the file itself (not its file name), e.g. 'SomeSite.com | My.Movie.2020.1080p...'. Players such " +
                "as VLC show it; Emby only does if the library is set to use embedded titles. Off by default: the file's title is left " +
                "as it is, and it's often the only record of which release the file is."),
            ("Rewrite a video just for clean-up",
                "Cleaning a name means writing a new copy of the whole video, like removing a track does. Off: names are only " +
                "cleaned when a video is rewritten for its audio anyway, and the totals at the end of a run count the videos left " +
                "with names to clean. On: those videos are rewritten too - that can be a lot of them, so test mode first."),
            ("Text to remove",
                "Everything that's taken out of names, one per line (commas work too); capitals never matter. '*.com' removes any " +
                "website name ending in .com, e.g. 'SomeSite.com' or 'Downloaded from SomeSite.com'. A line between slashes is a " +
                "regular expression, e.g. '/GROUP\\.\\w+/'; one that isn't valid is skipped and named in the run log. Anything else " +
                "is removed as a whole word, e.g. a release group's tag, 'GROUP'. Add your own lines to the list. To keep a mistake " +
                "from doing harm, an entry is skipped (and named under the box and in the run log) when it would remove ordinary " +
                "titles like 'English', matches an empty title, has a repeat inside a repeat like (a+)+, is too slow on test " +
                "titles, or is over 200 characters; only the first 300 entries are used. A regular expression that's still too slow " +
                "on a real title is switched off for the rest of that run. 'Check the list' does these checks without a run. " +
                "'Reset to default' puts back the list AudioRemover comes with, and saves."),
            ("Try the list on a name",
                "Optional. Paste a track name or title, e.g. one from your run log, and click 'Check the list' to see what's left of it. It isn't saved."),
            ("Find text to remove",
                "Saves, then reads the track names Emby stored for every video in your libraries when it scanned them - no file is " +
                "opened, so it takes seconds. It runs the list over them first, then suggests what's left that keeps turning up in " +
                "at least 2 different films or shows but describes nothing: a release group's or a site's tag such as 'ETRG', or a " +
                "bracketed one like '[SomeSite Torrents]'. Words that describe a track (languages, formats like DTS or 5.1, Commentary, " +
                "Forced, SDH…), words with digits, and words from the film's or show's own name are never suggested, and every " +
                "suggestion passes the same checks as the box. They're listed most common first, each with an example; switch on the " +
                "ones you want and click 'Add selected to the list'. The list is then saved sorted: website endings, then words, then " +
                "regular expressions, each A-Z. Nothing changes in any file until a run, and test mode shows what it would do. Each " +
                "run's log also ends with the same kind of suggestions, from the track names that run read."),
            ("How names are cleaned",
                "Each track name (and the file's title, if you chose that) is cleaned on its own, in four steps. " +
                "1) Website addresses: a name that ends in one of the '*.' endings in 'Text to remove' (.com, .net, .org, .to, .in, " +
                ".top and the rest of the list) goes, together with what belongs to it: 'http://', 'https://' and 'www.' in front; a " +
                "two-part ending like '.co.uk'; a path after it, like '/movies/123'; 'Downloaded from', 'Encoded by', 'Ripped by' or " +
                "'Uploaded by' before it; and 'Encode', 'Encodes', 'Encoded', 'Rip', 'Rips', 'Release' or 'Releases' right after it. " +
                "So 'https://SomeSite.in', 'www.SomeSite.com/movies' and 'SomeSite.in Encodes' all go completely. Names that only " +
                "look like an address are left alone: a dotted title ('Back.to.the.Future'), numbers ('5.1') and codecs ('H.264'). " +
                "2) Your own lines in 'Text to remove': a word goes only as a whole word, capitals don't matter ('YTS' never cuts into " +
                "'Bytes'); a line between slashes is a regular expression and removes whatever it matches. " +
                "3) Tidying up what's left: empty brackets ('()', '[]'), a doubled separator ('English -  - Forced' becomes 'English - " +
                "Forced'), separators left at the start or end (- | ~ : • / and similar), and extra spaces. " +
                "4) Whole names: a name that's now empty, or is only a file name (ending in .mkv, .mp4, .avi and the like) or a release " +
                "name (no spaces, dots between the words, and a tag such as 1080p, 2160p, WEB-DL, WEBRip, BluRay, HDTV, x264, x265, HEVC " +
                "or 10bit), is removed altogether, and Emby shows the track's language and format instead. " +
                "Examples: 'European (Forced) - SomeSite.com' becomes 'European (Forced)'; '- SomeSite.com', 'https://SomeSite.in' and " +
                "'My.Movie.2020.1080p.WEBRip.x265-GROUP' are removed; 'English', 'Dolby Digital 5.1' and 'Director's Commentary' stay " +
                "exactly as they are. A name with nothing to clean is never touched, and the run log lists every name it would change."),
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
