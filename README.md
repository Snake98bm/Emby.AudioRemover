# Emby.AudioRemover

An Emby Server plugin that removes **foreign-language audio tracks** from your videos. You choose which languages to keep, and every other audio track is stripped out. Video, the kept audio and subtitles are copied untouched (no re-encoding).

It's the audio twin of [SubRemover](https://github.com/Snake98bm/Emby.Subremover), which does the same for subtitles.

## Features

- **Test mode (on by default)** – logs exactly what *would* change and touches nothing.
- **Never leaves a video silent** – when none of a video's main audio tracks is in a language you keep (a foreign film with only its own language), every track stays and the log says so.
- **Safe replace** – writes a new file, then checks it has the same video and subtitle tracks, the expected audio tracks and the same length. If any check fails, the original stays untouched.
- **Backups** – the original is kept as `movie.mkv.audioremover.bak` next to the video, or copied to a backup folder you choose. Old backups can be deleted automatically after a number of days.
- **Undo** – the **Restore originals from backups** button on the settings page lists every video that has a backup; select the ones to put back. The *Restore* scheduled task restores the videos matching *Only process paths containing* instead. Videos moved on the share, even a whole library, are found again by file name on the next run, and their backups and records follow them.
- **Test on a few files first** – *Max videos per run* and *Only process paths containing* let you try it on a single movie. Separate several titles with `;`, e.g. `My Movie (2020); My Show - S01E02`.
- Optional: always keep the first audio track (usually the film's original language), so a foreign film keeps its own audio next to the dub.
- Optional: remove commentary tracks and audio description tracks, even in languages you keep (only while a main track stays).
- Default audio: when the default track is removed, or is commentary or audio description, the default moves to the first kept main track. Or choose a default audio language. Optionally, the default goes to the best-quality track in that same language (5.1 over a stereo copy, then lossless, then the higher bitrate).
- Optional: listen when no kept language is found. When a video has two or more audio tracks and none is tagged with a language you keep (a video that would otherwise be left alone), [Whisper](https://github.com/ggml-org/whisper.cpp) listens offline to three short clips of each track and names the language spoken. Three switches: **Listen** (step 1, only logs what it heard), **Act on what it hears** (step 2: a "Finnish" track that's really English is kept and its tag corrected) and **Extra safe** (step 3: when the quick listen disagrees with a tag or can't tell, the next bigger Whisper model hears 10 more clips, and at least 7 must agree, each 90% sure). whisper.cpp and the model you choose (Base, 141 MB, recommended) are downloaded on first use from fixed addresses and checked against their checksums.
- Optional, with listening on: fix language tags on their own. **Check and retag a lone track** corrects a video's only track when it's tagged with a language you don't keep (a "Dutch" track that's English); **Tag tracks that have no language** tags a video's tracks when it has two or more and none has a tag (often a stereo and a 5.1 copy of the same sound). Each rewrites the file once; a single untagged track is left alone.
- Optional: clean up track names. Download sites put their name on every track (`English - SomeSite.com`) and Emby shows it in the audio and subtitle menus; this takes it out. What's removed is an editable list on the settings page: `*.com` for any website ending in .com, a word such as a release group's tag, or a regular expression between slashes (`/GROUP\.\w+/`), with a button to check it and one to reset it to the default. A name that's only an ad, or only the download's file or release name (`My.Movie.2020.1080p.WEBRip.x265-GROUP.mkv`), is removed, so Emby shows the track's language and format instead. The file's own title can be cleaned too (off by default). Names are cleaned when a video is rewritten for its audio anyway, or, if you choose, a video is rewritten just for that. Text that keeps turning up in track names across different films and shows is suggested in an editable **Suggested text** box that every run adds to (countries and regions in language labels such as `English (United States)` or `pt-BR` are left alone); text added from it can be cleaned out of the names it was found in straight away.
- Optional: process new videos as Emby adds them. A few minutes after the last one arrives, one run checks just the new videos with your settings (Test mode included), so a whole season gets one run and one log.
- Its own settings page with tabs (**Run**, **Audio**, **Listen (Whisper)**, **Track names**, **Backups**, **Logs**), each explaining its settings at the bottom: **Run now** and **Stop**, run status, a restore list and a log viewer (open a log in a new window or download it), plus an entry in the server sidebar under **Advanced** (can be turned off).

## How track names are cleaned

With **Clean up track names** on, each track name (and the file's title, if you choose that) is cleaned on its own, in four steps:

1. **Website addresses.** A name ending in one of the `*.` endings in **Text to remove** (`.com`, `.net`, `.org`, `.to`, `.in`, `.top` and the rest of the list) goes, together with what belongs to it:
   - `http://`, `https://` and `www.` in front, and a two-part ending like `.co.uk`
   - a path after it, like `/movies/123`
   - `Downloaded from`, `Encoded by`, `Ripped by` or `Uploaded by` before it
   - `Encode(s)`, `Encoded`, `Rip(s)` or `Release(s)` right after it

   Names that only look like an address stay: a dotted title (`Back.to.the.Future`), numbers (`5.1`) and codecs (`H.264`).
2. **Your own lines** in **Text to remove**: a word goes only as a whole word, ignoring capitals (`YTS` never cuts into `Bytes`); a line between slashes is a regular expression.
3. **Tidying up:** empty brackets, a doubled separator (`English -  - Forced` → `English - Forced`), separators left at the start or end (`- | ~ : • /`…), and extra spaces.
4. **Whole names:** a name that's now empty, or is only a file name (`….mkv`, `.mp4`, `.avi`…) or a release name (no spaces, dots between the words, and a tag such as `1080p`, `2160p`, `WEB-DL`, `WEBRip`, `BluRay`, `HDTV`, `x264`, `x265`, `HEVC` or `10bit`), is removed, so Emby shows the track's language and format instead.

| Name | After |
|---|---|
| `European (Forced) - SomeSite.com` | `European (Forced)` |
| `- SomeSite.com`, `https://SomeSite.in`, `SomeSite.in Encodes` | *(removed)* |
| `My.Movie.2020.1080p.WEBRip.x265-GROUP` | *(removed)* |
| `English`, `Dolby Digital 5.1`, `Director's Commentary` | unchanged |

A name with nothing to clean is never touched, and the run log lists every name a run would change.

## Install

1. Download `Emby.AudioRemover.dll` from the [Releases](../../releases) page, or build it yourself (below).
2. Stop Emby Server and copy the DLL into Emby's `plugins` folder. The default locations are:

   | System | Plugins folder |
   |---|---|
   | Windows | `%AppData%\Emby-Server\programdata\plugins` |
   | Linux | `/var/lib/emby/plugins` |
   | Docker (emby/embyserver, linuxserver/emby) | `/config/plugins` inside the container |

   If you installed Emby somewhere else, it's the `plugins` folder inside Emby's data folder (the one with `config`, `data` and `logs`).

   **Docker:** on the host, that's the `plugins` folder inside the folder you mapped to `/config`, e.g. `/docker/emby/config/plugins`. Stop the container first (`docker stop emby`), and give the DLL the same owner as the other files in that folder (your PUID/PGID). Or copy it straight into the container, using your container's name:

   ```bash
   docker cp Emby.AudioRemover.dll emby:/config/plugins/
   ```
3. Start Emby (Docker: `docker start emby`) and press **Ctrl+F5** in your browser so the menus reload.
4. Go to **Settings → Plugins → AudioRemover** (or **AudioRemover** under **Advanced** in the server sidebar). On the **Run** tab pick your libraries and leave **Test mode** on; on the **Audio** tab pick your languages, or click **Quick setup: English + no language**. Click **Save**.
5. Click **Run now**. The status on the **Run** tab shows progress, and its log appears on the **Logs** tab.
6. When the log looks right, turn Test mode off, set **Max videos to change per run** to a few, and run it again. Check those files in Emby before letting it do the rest.

The task is also under **Settings → Scheduled Tasks → AudioRemover**, where you can give it a schedule. Each run writes its own `audioremover-….txt` log in Emby's log folder; the newest 30 are kept.

## Build it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Then, in PowerShell from this folder:

```powershell
.\build.ps1            # build + run tests + make artifacts\Emby.AudioRemover-<version>.zip
.\build.ps1 test       # just run the tests
.\build.ps1 deploy -PluginsFolder "$env:APPDATA\Emby-Server\programdata\plugins"
```

`deploy` builds, runs the tests, and copies the plugin into Emby. It refuses to deploy if any test fails, and it first saves the installed DLL to a `audioremover-backups` folder so you can go back.

### Tests with Emby's own ffmpeg

Most tests need nothing extra. A few tests run real ffmpeg on a tiny generated video. To enable them, copy `ffmpeg.exe`, `ffprobe.exe` and their DLLs from your Emby server's `system` folder into `tools\ffmpeg\` (git ignores that folder). Without them, those tests show as *skipped*.
