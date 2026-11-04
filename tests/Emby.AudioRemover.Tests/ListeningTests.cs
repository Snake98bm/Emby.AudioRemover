using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Emby.AudioRemover.Core;
using Xunit;
using static Emby.AudioRemover.Tests.TestData;

namespace Emby.AudioRemover.Tests;

public sealed class ListeningTests : IDisposable
{
    private const string Video = @"D:\Movies\My Movie (2020)\My Movie (2020).mkv";
    private readonly string _dir = Directory.CreateTempSubdirectory("audioremover-listen-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static HeardLanguage Heard(string language, int percent = 98) =>
        new() { Language = language, Percent = percent, Agree = 3, Clips = { "x", "x", "x" } };

    private static PlannerSettings Keep(string languages, bool useHeard) =>
        new() { KeepLanguages = PlannerSettings.ParseLanguages(languages), UseHeardLanguages = useHeard };

    // ---- what Whisper says

    [Fact]
    public void Reads_the_language_from_whisper_output()
    {
        var answer = LanguageVote.Parse("whisper_init_from_file_with_params: loading model\n" +
                                        "whisper_full_with_state: auto-detected language: en (p = 0.977142)\n");

        Assert.Equal(("en", 0.977142), answer);
        Assert.Null(LanguageVote.Parse("error: failed to read audio file"));
    }

    [Theory]
    [InlineData("en 0.92, en 0.89, en 0.94", "eng")]
    [InlineData("ko 0.98, en 0.50, ko 0.99", "kor")] // a clip of music doesn't spoil it
    [InlineData("en 0.70, en 0.70, en 0.70", null)] // what a test tone gets: never sure enough
    [InlineData("en 0.95, ru 0.98, fi 0.90", null)] // no two agree
    [InlineData("en 0.99", null)] // one clip isn't enough
    public void A_language_counts_when_two_sure_clips_agree(string clips, string expected)
    {
        var parsed = clips.Split(", ").Select(c => c.Split(' ')).Select(p => (p[0], double.Parse(p[1], CultureInfo.InvariantCulture)));

        Assert.Equal(expected, LanguageVote.Decide(parsed).Language);
    }

    private static List<(string, double)> Clips(int english, int total) =>
        Enumerable.Range(0, total).Select(i => i < english ? ("en", 0.97) : ("en", 0.40)).ToList();

    [Fact]
    public void A_heard_language_needs_seven_of_ten_more_clips_before_it_is_used()
    {
        var first = LanguageVote.Decide(new[] { ("en", 1.0), ("en", 1.0), ("en", 1.0) });

        var confirmed = LanguageVote.Confirm(first, Clips(english: 9, total: 10), "Small");
        Assert.Equal("eng", confirmed.Language);
        Assert.Equal("sounds eng: 3/3 clips, 100% sure; confirmed 9/10 by the Small model, 97% sure", confirmed.Describe());
        Assert.Equal("sounds eng: 3/3 clips, 100% sure; confirmed 9/10 by the Small model, 97% sure [clips: en 100%, en 100%, en 100%] " +
                     "[10 more: en 97%, en 97%, en 97%, en 97%, en 97%, en 97%, en 97%, en 97%, en 97%, en 40%]", confirmed.Detail());

        var dropped = LanguageVote.Confirm(LanguageVote.Decide(new[] { ("en", 1.0), ("en", 1.0), ("en", 1.0) }), Clips(english: 6, total: 10), "Small");
        Assert.Null(dropped.Language);
        Assert.Equal("not confirmed: heard eng in 3/3 clips, but only 6/10 more agreed (Small model), so the tag stays", dropped.Describe());
    }

    [Fact]
    public void Confirming_clips_must_each_be_90_percent_sure()
    {
        // 85% counts for the quick listen, but not for overriding a tag.
        var sureEnoughForLogging = Enumerable.Repeat(("en", 0.85), 10).ToList();
        var first = LanguageVote.Decide(sureEnoughForLogging.Take(3));
        Assert.Equal("eng", first.Language);

        Assert.Null(LanguageVote.Confirm(first, sureEnoughForLogging, "Small").Language);
        Assert.Equal("eng", LanguageVote.Confirm(LanguageVote.Decide(sureEnoughForLogging.Take(3)), Enumerable.Repeat(("en", 0.90), 10).ToList(), "Small").Language);
    }

    [Fact]
    public void The_check_is_done_by_the_next_bigger_model()
    {
        Assert.Equal("Base", WhisperFiles.CheckModelFor("Tiny").Name);
        Assert.Equal("Small", WhisperFiles.CheckModelFor("Base").Name);
        Assert.Equal("Medium", WhisperFiles.CheckModelFor("Small").Name);
        Assert.Equal("Medium", WhisperFiles.CheckModelFor("Medium").Name);

        Assert.Equal("Small", Plugin.CheckModelOf(new PluginOptions { Listen = true, ActOnHeard = true, WhisperModel = "Base" }));
        Assert.Equal("Small", Plugin.CheckModelOf(new PluginOptions { Listen = true, WhisperModel = "Base" })); // logs what step 3 says too
        Assert.Null(Plugin.CheckModelOf(new PluginOptions { Listen = true, ActOnHeard = true, ExtraSafe = false, WhisperModel = "Base" }));
        Assert.Null(Plugin.CheckModelOf(new PluginOptions { Listen = false, WhisperModel = "Base" }));
        Assert.Null(Plugin.CheckModelOf(new PluginOptions { Listen = true, WhisperModel = "Medium" }));
    }

    [Theory]
    [InlineData("Log", true, false, true)]
    [InlineData("Act", true, true, false)]
    [InlineData("Use", true, true, true)]
    [InlineData("Off", false, false, true)]
    public void The_old_dropdown_setting_carries_over_to_the_switches(string old, bool listen, bool act, bool extraSafe)
    {
        var options = new PluginOptions { ListenMode = old };

        options.CarryOverOldSettings();

        Assert.Equal((listen, act, extraSafe), (options.Listen, options.ActOnHeard, options.ExtraSafe));
        Assert.Null(options.ListenMode);
    }

    [Fact]
    public void A_lone_track_not_tagged_with_a_kept_language_is_checked()
    {
        var settings = Keep("eng", useHeard: false);
        int? Lone(ProbeResult probe, string path = Video) => AudioPlanner.LoneTrackToCheck(AudioPlanner.Plan(probe, settings, path), settings, path)?.index;

        Assert.Equal(1, Lone(Movie(Audio(1, "dut"))));
        Assert.Null(Lone(Movie(Audio(1, "eng"))));
        Assert.Null(Lone(Movie(Audio(1, "eng"), Audio(2, "und"))));
        Assert.Null(Lone(Movie(Audio(1, "dut")), @"D:\Movies\My Movie (2020)\My Movie (2020).avi"));

        // No language tag at all: left as it is, alone or the one left after French goes.
        Assert.Null(Lone(Movie(Audio(1, "und"))));
        Assert.Null(Lone(Movie(Audio(1, "und"), Audio(2, "fre"))));

        // Tagged, and the one left after the others go: checked.
        settings.KeepFirstTrack = true;
        Assert.Equal(1, Lone(Movie(Audio(1, "jpn"), Audio(2, "fre"))));
    }

    [Fact]
    public void A_lone_track_is_retagged_only_when_whisper_is_sure_of_a_known_language()
    {
        var settings = Keep("eng", useHeard: false);
        var probe = Movie(Audio(1, "dut", isDefault: true));
        var plan = AudioPlanner.Plan(probe, settings, Video);
        Assert.False(plan.HasWork);

        probe.streams[1].Heard = Heard("eng");
        AudioPlanner.Retag(plan, probe.streams[1]);
        Assert.Equal(new Dictionary<int, string> { [1] = "eng" }, plan.LanguageChanges);
        Assert.True(plan.HasWork);
        Assert.Contains("tags:    #1 dut -> eng", MediaProcessor.DescribePlan(Video, plan, dryRun: true));

        foreach (var heard in new[] { new HeardLanguage(), Heard("jw"), Heard("dut") })
        {
            var other = AudioPlanner.Plan(Movie(Audio(1, "dut")), settings, Video);
            var track = other.Keep.Single();
            track.Heard = heard;
            AudioPlanner.Retag(other, track);
            Assert.Empty(other.LanguageChanges);
        }
    }

    [Fact]
    public void Tracks_get_the_language_heard_when_none_of_them_has_a_tag()
    {
        var settings = Keep("eng", useHeard: false);
        settings.TagUntaggedTracks = true;
        // A stereo and a 5.1 copy of the same English sound, neither tagged.
        var probe = Movie(Audio(1, "und", codec: "aac", isDefault: true, channels: 2), Audio(2, "und"));
        probe.streams[1].Heard = Heard("eng");
        probe.streams[2].Heard = Heard("eng");

        var plan = AudioPlanner.Plan(probe, settings, @"D:\Shows\My Show\Season 01\My Show - S01E01.mp4");

        // Both stay; the tags alone are the reason for the rewrite.
        Assert.Equal(new[] { 1, 2 }, plan.Keep.Select(s => s.index));
        Assert.Empty(plan.Remove);
        Assert.Null(plan.NewDefault);
        Assert.Equal(new Dictionary<int, string> { [1] = "eng", [2] = "eng" }, plan.LanguageChanges);
        Assert.True(plan.HasWork);
        Assert.Contains("tags:    #1 und -> eng, #2 und -> eng", MediaProcessor.DescribePlan(Video, plan, dryRun: true));

        // A track Whisper wasn't sure of keeps no tag.
        probe.streams[2].Heard = new HeardLanguage { Clips = { "en 70%", "fi 60%" } };
        Assert.Equal(new Dictionary<int, string> { [1] = "eng" }, AudioPlanner.Plan(probe, settings, Video).LanguageChanges);

        // Switched off, it's left as it was.
        settings.TagUntaggedTracks = false;
        Assert.False(AudioPlanner.Plan(probe, settings, Video).HasWork);
    }

    [Fact]
    public void Only_videos_whose_tracks_all_lack_a_tag_are_tagged()
    {
        var settings = Keep("eng", useHeard: false);
        settings.TagUntaggedTracks = true;
        ProbeResult HeardAsEnglish(params ProbeStream[] audio)
        {
            foreach (var s in audio) s.Heard = Heard("eng");
            return Movie(audio);
        }

        // A single untagged track is left alone.
        Assert.False(AudioPlanner.Plan(HeardAsEnglish(Audio(1, "und")), settings, Video).HasWork);
        // One tagged track: the others' missing tags are no reason for a rewrite.
        Assert.Empty(AudioPlanner.Plan(HeardAsEnglish(Audio(1, "und"), Audio(2, "eng")), settings, Video).LanguageChanges);
        // Only MKV and MP4 hold the tag.
        Assert.False(AudioPlanner.Plan(HeardAsEnglish(Audio(1, "und"), Audio(2, "und")), settings, @"D:\Movies\My Movie (2020)\My Movie (2020).avi").HasWork);
    }

    [Fact]
    public void A_rewrite_for_missing_tags_also_puts_wrong_ones_right()
    {
        var settings = Keep("eng", useHeard: true);
        settings.TagUntaggedTracks = true;
        var probe = Movie(Audio(1, "und"), Audio(2, "und"));
        probe.streams[1].Heard = Heard("eng");
        probe.streams[2].Heard = Heard("fre");

        var plan = AudioPlanner.Plan(probe, settings, Video);

        // Acting on what's heard, the French one goes; the English one is tagged.
        Assert.Equal(new[] { 2 }, plan.Remove.Select(s => s.index));
        Assert.Equal(new Dictionary<int, string> { [1] = "eng" }, plan.LanguageChanges);
    }

    [Fact]
    public void Untagged_tracks_compete_for_the_default_only_once_whisper_has_named_them()
    {
        var settings = Keep("eng", useHeard: false);
        settings.BestAudioDefault = true;
        ProbeResult StereoAnd51() => Movie(Audio(1, "und", codec: "aac", isDefault: true, channels: 2), Audio(2, "und"));

        // Not heard: they could be two languages, so the default stays where it is.
        Assert.Null(AudioPlanner.Plan(StereoAnd51(), settings, Video).NewDefault);

        // Heard as English and tagged: the 5.1 copy gets the default in the same rewrite.
        settings.TagUntaggedTracks = true;
        var probe = StereoAnd51();
        probe.streams[1].Heard = Heard("eng");
        probe.streams[2].Heard = Heard("eng");
        var plan = AudioPlanner.Plan(probe, settings, Video);
        Assert.Equal(2, plan.NewDefault?.index);
        Assert.Equal(new Dictionary<int, string> { [1] = "eng", [2] = "eng" }, plan.LanguageChanges);

        // Heard as two languages: no contest.
        probe.streams[2].Heard = Heard("fre");
        Assert.Null(AudioPlanner.Plan(probe, settings, Video).NewDefault);
    }

    [Fact]
    public void Tagging_tracks_that_have_none_is_off_until_switched_on_and_needs_listening()
    {
        Assert.False(new PluginOptions().TagUntaggedTracks);
        Assert.False(MediaProcessor.SettingsFrom(new PluginOptions { Listen = false, TagUntaggedTracks = true }).TagUntaggedTracks);
        Assert.True(MediaProcessor.SettingsFrom(new PluginOptions { Listen = true, TagUntaggedTracks = true }).TagUntaggedTracks);
        Assert.NotEqual(MediaProcessor.SettingsFrom(new PluginOptions { Listen = true }).Signature(),
            MediaProcessor.SettingsFrom(new PluginOptions { Listen = true, TagUntaggedTracks = true }).Signature());
    }

    [Fact]
    public void When_the_quick_listen_cannot_tell_the_bigger_model_gets_to_try()
    {
        var unsure = LanguageVote.Decide(new[] { ("en", 0.54), ("en", 0.93), ("en", 0.38) });
        Assert.Null(unsure.Language);

        var identified = LanguageVote.Identify(unsure, Clips(english: 8, total: 10), "Small");
        Assert.Equal("eng", identified.Language);
        Assert.Equal("sounds eng: couldn't tell from 3 clips, so the Small model listened: 8/10, 97% sure", identified.Describe());

        var still = LanguageVote.Identify(unsure, Clips(english: 5, total: 10), "Small");
        Assert.Null(still.Language);
        Assert.Equal("couldn't tell, not even the Small model (5/10 clips agreed at most)", still.Describe());
    }

    [Fact]
    public void The_status_names_the_check_model_and_wants_both_downloaded()
    {
        var whisper = new WhisperInstall(FakeInstall("base-only", WhisperFiles.ProgramVersion, WhisperFiles.Model("Base").File.Sha256));

        var status = Plugin.WhisperStatus(whisper, "Base", "Small");

        Assert.NotEqual(Emby.Web.GenericEdit.Elements.ItemStatus.Succeeded, status.Status);
        Assert.Contains("the Base model (141 MB, dated 2023-03-22), and the Small model (465 MB, dated 2023-03-22) for the 10-clip check", status.StatusText);
        Assert.Equal(Emby.Web.GenericEdit.Elements.ItemStatus.Succeeded, Plugin.WhisperStatus(whisper, "Base").Status);
    }

    [Fact]
    public void The_confirming_clips_cover_most_of_the_video()
    {
        var starts = LanguageListener.ClipStarts(5400, LanguageVote.ConfirmClips, 0.1, 0.9).ToList();

        Assert.Equal(10, starts.Count);
        Assert.Equal(540, starts.First());
        Assert.Equal(4860, starts.Last(), 3);
        Assert.Equal(starts.OrderBy(s => s), starts);
    }

    [Fact]
    public void What_it_heard_is_described_for_the_log()
    {
        Assert.Equal("sounds eng: 3/3 clips, 99% sure", LanguageVote.Decide(new[] { ("en", 1.0), ("en", 0.99), ("en", 0.98) }).Describe());
        Assert.Equal("sounds kor: 2/3 clips, 98% sure [clips: ko 98%, en 50%, ko 99%]",
            LanguageVote.Decide(new[] { ("ko", 0.98), ("en", 0.50), ("ko", 0.99) }).Detail());
        var unsure = LanguageVote.Decide(new[] { ("en", 0.5), ("ko", 0.7) });
        Assert.Equal("couldn't tell", unsure.Describe());
        Assert.Equal("couldn't tell [clips: en 50%, ko 70%]", unsure.Detail());
    }

    [Fact]
    public void Only_videos_with_no_kept_language_are_listened_to()
    {
        var settings = Keep("eng", useHeard: false);

        Assert.Equal(new[] { 1, 2 }, MediaProcessor.InDoubt(Movie(Audio(1, "fin"), Audio(2, "rus")), settings).Select(s => s.index));
        Assert.Equal(new[] { 1, 2 }, MediaProcessor.InDoubt(Movie(Audio(1, "unknown"), Audio(2, "fre")), settings).Select(s => s.index));
        Assert.Empty(MediaProcessor.InDoubt(Movie(Audio(1, "eng"), Audio(2, "unknown"), Audio(3, "fre")), settings));
        Assert.Empty(MediaProcessor.InDoubt(Movie(Audio(1, "eng"), Audio(2, "fre")), settings));

        // A single track always stays, whatever it sounds like: nothing to decide, so no time spent listening.
        Assert.Empty(MediaProcessor.InDoubt(Movie(Audio(1, "und")), settings));
        Assert.Empty(MediaProcessor.InDoubt(Movie(Audio(1, "dut")), settings));
    }

    // ---- what the plan does with it

    [Fact]
    public void A_mislabeled_english_track_is_kept_and_its_tag_corrected_in_the_rewrite()
    {
        var probe = Movie(Audio(1, "fin"), Audio(2, "rus", isDefault: true));
        probe.streams[1].Heard = Heard("eng", 100);
        probe.streams[2].Heard = Heard("rus");

        var plan = AudioPlanner.Plan(probe, Keep("eng", useHeard: true), Video);

        Assert.Equal(new[] { 1 }, plan.Keep.Select(s => s.index));
        Assert.Equal(new[] { 2 }, plan.Remove.Select(s => s.index));
        Assert.Equal(1, plan.NewDefault.index);
        Assert.Equal(new Dictionary<int, string> { [1] = "eng" }, plan.LanguageChanges);

        var text = MediaProcessor.DescribePlan(Video, plan, dryRun: true);
        Assert.Contains("keep:    #1 fin ac3 6ch (sounds eng: 3/3 clips, 100% sure)", text);
        Assert.Contains("tags:    #1 fin -> eng", text);
    }

    [Fact]
    public void Only_logging_what_it_hears_changes_nothing()
    {
        var probe = Movie(Audio(1, "fin"), Audio(2, "rus", isDefault: true));
        probe.streams[1].Heard = Heard("eng", 100);

        var plan = AudioPlanner.Plan(probe, Keep("eng", useHeard: false), Video);

        Assert.NotNull(plan.LeftAlone);
        Assert.False(plan.HasWork);
        Assert.Empty(plan.LanguageChanges);
    }

    [Fact]
    public void An_unsure_answer_leaves_the_tags_in_charge()
    {
        var probe = Movie(Audio(1, "fin"), Audio(2, "rus"));
        probe.streams[1].Heard = new HeardLanguage { Clips = { "en 70%", "fi 60%" } };

        Assert.NotNull(AudioPlanner.Plan(probe, Keep("eng", useHeard: true), Video).LeftAlone);
    }

    [Fact]
    public void A_wrong_tag_alone_is_no_reason_for_a_rewrite()
    {
        var probe = Movie(Audio(1, "dut", isDefault: true));
        probe.streams[1].Heard = Heard("eng");

        var plan = AudioPlanner.Plan(probe, Keep("eng", useHeard: true), Video);

        Assert.False(plan.HasWork);
        Assert.Null(plan.LeftAlone);
    }

    [Fact]
    public void Totals_count_tracks_heard_and_wrong_tags()
    {
        var fin = Audio(1, "fin");
        fin.Heard = Heard("eng");
        var untagged = Audio(2, "unknown");
        untagged.Heard = Heard("eng");
        var rus = Audio(3, "rus");
        rus.Heard = Heard("rus");
        var totals = new RunTotals();

        totals.AddHeard(new[] { fin, untagged, rus });

        Assert.Equal(3, totals.TracksHeard);
        Assert.Equal(1, totals.TagsWrong);
        Assert.Contains(RunTotals.Line("mislabeled tracks heard", 1), totals.Describe(dryRun: false));
        Assert.DoesNotContain("listened", new RunTotals().Describe(dryRun: false));
    }

    // ---- clips and ffmpeg

    [Fact]
    public void Clips_come_from_spread_out_points_that_fit_in_the_video()
    {
        Assert.Equal(new[] { 1080.0, 2700, 4320 }, LanguageListener.ClipStarts(5400));
        Assert.All(LanguageListener.ClipStarts(100), start => Assert.True(start + 30 <= 100));
    }

    [Fact]
    public void The_same_clip_is_never_taken_twice_so_it_cannot_agree_with_itself()
    {
        // Too short for separate clips, or no length from ffprobe: one clip, too few for any vote.
        Assert.Equal(new[] { 0.0 }, LanguageListener.ClipStarts(20));
        Assert.Equal(new[] { 0.0 }, LanguageListener.ClipStarts(null));
        Assert.Equal(new[] { 0.0 }, LanguageListener.ClipStarts(null, LanguageVote.ConfirmClips, 0.1, 0.9));
        Assert.Null(LanguageVote.Decide(new[] { ("en", 0.99) }).Language);

        // A 2-minute clip: the 10 confirming starts squeeze into 0-90 s; repeats are dropped, so fewer than 7 can agree.
        var starts = LanguageListener.ClipStarts(120, LanguageVote.ConfirmClips, 0.1, 0.9).ToList();
        Assert.Equal(starts.Distinct(), starts);
        Assert.True(starts.Count < LanguageVote.ConfirmClips);
    }

    [Fact]
    public void The_default_language_follows_what_was_heard()
    {
        var probe = Movie(Audio(1, "jpn", isDefault: true), Audio(2, "fin"));
        probe.streams[1].Heard = Heard("jpn");
        probe.streams[2].Heard = Heard("eng");
        var settings = Keep("eng", useHeard: true);
        settings.KeepFirstTrack = true;
        settings.DefaultLanguage = "eng";

        var plan = AudioPlanner.Plan(probe, settings, Video);

        Assert.Equal(new[] { 1, 2 }, plan.Keep.Select(s => s.index));
        Assert.Equal(2, plan.NewDefault.index);
    }

    [Fact]
    public void A_language_code_players_do_not_know_is_never_written_as_a_tag()
    {
        var probe = Movie(Audio(1, "und"), Audio(2, "eng", isDefault: true), Audio(3, "fre"));
        probe.streams[1].Heard = Heard("jw");
        var settings = Keep("eng", useHeard: true);
        settings.KeepFirstTrack = true;

        var plan = AudioPlanner.Plan(probe, settings, Video);

        Assert.Equal(new[] { 3 }, plan.Remove.Select(s => s.index));
        Assert.Empty(plan.LanguageChanges);
    }

    [Fact]
    public void Update_checks_run_once_a_day_even_when_they_get_no_answer()
    {
        var whisper = new WhisperInstall(_dir);
        var now = new DateTime(2026, 11, 2, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal((true, true), whisper.DueChecks(now, "Base"));
        Assert.Equal((false, false), whisper.DueChecks(now.AddHours(5), "Base"));
        Assert.Equal((false, true), whisper.DueChecks(now.AddHours(5), "Small"));
        Assert.Equal((true, true), whisper.DueChecks(now.AddDays(1), "Base"));
    }

    [Fact]
    public void A_clip_is_one_track_as_16khz_mono_wav()
    {
        Assert.Equal("-hide_banner -nostdin -v error -y -ss 1080.5 -t 30 -i in.mkv -map 0:3 -vn -ac 1 -ar 16000 -c:a pcm_s16le -f wav clip.wav",
            string.Join(" ", FfmpegArgs.AudioClip("in.mkv", 3, 1080.5, 30, "clip.wav")));
    }

    [Fact]
    public void Remux_corrects_language_tags_by_their_index_in_the_new_file()
    {
        var line = string.Join(" ", FfmpegArgs.Remux("in.mkv", "out.tmp", "matroska", new[] { 2 },
            setLanguages: new Dictionary<int, string> { [1] = "eng", [3] = "eng" }));

        Assert.Contains("-metadata:s:1 language=eng -metadata:s:2 language=eng", line);
    }

    // ---- downloads

    [Fact]
    public void Every_download_is_pinned_to_an_https_address_and_a_checksum()
    {
        var files = WhisperFiles.All.Select(m => m.File).Append(WhisperFiles.ProgramForThisServer()).Where(f => f != null).ToList();

        // The four models, and whisper.cpp itself where there's a ready-made download: on Linux it's installed by hand.
        Assert.Equal(OperatingSystem.IsWindows() ? 5 : 4, files.Count);
        Assert.All(files, f =>
        {
            Assert.StartsWith("https://", f.Url);
            Assert.Matches("^[0-9a-f]{64}$", f.Sha256);
            Assert.True(f.Size > 1_000_000);
        });
    }

    [Fact]
    public void Listening_is_off_and_base_is_the_recommended_model()
    {
        var options = new PluginOptions();
        Assert.Equal((false, false, true), (options.Listen, options.ActOnHeard, options.ExtraSafe));
        Assert.Equal("Base", new PluginOptions().WhisperModel);
        Assert.Equal("Base - 141 MB (recommended)", WhisperFiles.Model("base").Label);
        Assert.Equal("Base", WhisperFiles.Model("no such model").Name);
        Assert.Equal("1.4 GB", WhisperFiles.SizeText(1533763059));
    }

    // ---- what's downloaded, and whether there's anything newer

    private string FakeInstall(string name, string version, string modelSha)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
        File.WriteAllText(Path.Combine(folder, OperatingSystem.IsWindows() ? "whisper-cli.exe" : "whisper-cli"), "program");
        File.WriteAllText(Path.Combine(folder, "ggml-base.bin"), "model");
        if (version != null) File.WriteAllText(Path.Combine(folder, "whisper.cpp-version.txt"), version);
        if (modelSha != null) File.WriteAllText(Path.Combine(folder, "ggml-base.bin.sha256"), modelSha);
        return folder;
    }

    [Fact]
    public void The_status_shows_what_is_downloaded()
    {
        var whisper = new WhisperInstall(FakeInstall("current", WhisperFiles.ProgramVersion, WhisperFiles.Model("Base").File.Sha256));

        var status = Plugin.WhisperStatus(whisper, "Base");

        Assert.Equal(Emby.Web.GenericEdit.Elements.ItemStatus.Succeeded, status.Status);
        Assert.StartsWith($"Ready: whisper.cpp v1.9.5 (2026-10-06) with the Base model (141 MB, dated 2023-03-22), downloaded {DateTime.Now:yyyy-MM-dd}, in ",
            status.StatusText);
        Assert.All(WhisperFiles.All, m => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", m.Date));
    }

    [Fact]
    public void Files_from_an_older_audioremover_are_replaced_but_ones_put_there_by_hand_are_not()
    {
        var old = new WhisperInstall(FakeInstall("old", "1.9.4", new string('0', 64)));
        Assert.True(old.IsOldDownload);
        Assert.True(old.IsOldModel("Base"));
        Assert.False(old.IsReady("Base"));
        Assert.Contains("whisper.cpp v1.9.4 is downloaded; this AudioRemover uses whisper.cpp v1.9.5", Plugin.WhisperStatus(old, "Base").StatusText);

        var byHand = new WhisperInstall(FakeInstall("by-hand", null, null));
        Assert.Null(byHand.InstalledVersion);
        Assert.False(byHand.IsOldDownload);
        Assert.False(byHand.IsOldModel("Base"));
        Assert.StartsWith("Ready: whisper-cli (put there by hand) with the Base model", Plugin.WhisperStatus(byHand, "Base").StatusText);
    }

    [Theory]
    [InlineData("1.9.6", "1.9.5", true)]
    [InlineData("1.10.0", "1.9.5", true)]
    [InlineData("1.9.5", "1.9.5", false)]
    [InlineData("1.9.4", "1.9.5", false)]
    [InlineData("b5454", "1.9.5", false)]
    public void Versions_compare_as_numbers(string candidate, string current, bool newer)
    {
        Assert.Equal(newer, WhisperInstall.IsNewer(candidate, current));
    }

    [Fact]
    public void Reads_the_version_from_githubs_answer()
    {
        Assert.Equal("1.9.6", WhisperInstall.ReleaseVersion("{\"url\":\"x\",\"tag_name\": \"v1.9.6\",\"name\":\"v1.9.6\"}"));
        Assert.Null(WhisperInstall.ReleaseVersion("{\"tag_name\":\"b5454\"}"));
        Assert.Null(WhisperInstall.ReleaseVersion("not json"));
    }

    [Fact]
    public void The_latest_check_says_whether_anything_newer_is_out()
    {
        var whisper = new WhisperInstall(FakeInstall("checked", WhisperFiles.ProgramVersion, WhisperFiles.Model("Base").File.Sha256));
        var (model, check) = (WhisperFiles.Model("Base"), WhisperFiles.Model("Small"));
        Assert.Equal("", Plugin.NewerWhisper(whisper, model));

        whisper.LatestVersion = WhisperFiles.ProgramVersion;
        whisper.RecordModelCheck("Base", unchanged: true);
        Assert.Equal(DateTime.UtcNow.Date, whisper.AnsweredUtc?.Date);
        whisper.AnsweredUtc = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var on = whisper.AnsweredUtc.Value.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal($" Latest check ({on}): you have the current version of all files: whisper.cpp v1.9.5 and the Base model.",
            Plugin.NewerWhisper(whisper, model));

        // The check model's answer isn't in yet: each file is listed instead.
        Assert.Equal($" Latest check ({on}): that's the newest whisper.cpp; the Base model is current.", Plugin.NewerWhisper(whisper, model, check));
        whisper.RecordModelCheck("Small", unchanged: true);
        whisper.AnsweredUtc = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal($" Latest check ({on}): you have the current version of all files: whisper.cpp v1.9.5 and the Base and Small models.",
            Plugin.NewerWhisper(whisper, model, check));

        whisper.LatestVersion = "9.0.0";
        whisper.RecordModelCheck("Base", unchanged: false);
        whisper.AnsweredUtc = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal($" Latest check ({on}): whisper.cpp v9.0.0 is out; the Base model file on Hugging Face has changed; the Small model is current. " +
                     "AudioRemover moves to newer files in one of its own updates, after trying them.", Plugin.NewerWhisper(whisper, model, check));

        // whisper-cli put there by hand could be any build, so it's never called current.
        var byHand = new WhisperInstall(FakeInstall("checked-by-hand", null, null)) { LatestVersion = WhisperFiles.ProgramVersion };
        byHand.RecordModelCheck("Base", unchanged: true);
        Assert.EndsWith(": that's the newest whisper.cpp; the Base model is current.", Plugin.NewerWhisper(byHand, model));
    }

    [Fact]
    public void The_download_button_says_where_whisper_comes_from()
    {
        var description = PluginOptions.WhisperDownloadNote;

        Assert.Contains("github.com/ggml-org/whisper.cpp (v1.9.5)", description);
        Assert.Contains("huggingface.co/ggerganov/whisper.cpp", description);
    }

    [Fact]
    public void A_checksum_must_match_exactly()
    {
        var hash = SHA256.HashData("abc"u8);

        Assert.True(WhisperInstall.Matches(hash, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"));
        Assert.True(WhisperInstall.Matches(hash, "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"));
        Assert.False(WhisperInstall.Matches(hash, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ae"));
    }

    [Fact]
    public void Only_the_program_and_its_libraries_are_unpacked_and_only_into_the_folder()
    {
        var zipPath = Path.Combine(_dir, "whisper.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "Release/whisper-cli.exe", "Release/whisper.dll", "Release/ggml-cpu-x64.dll", "Release/SDL2.dll",
                         "Release/whisper-server.exe", "../ggml-evil.dll" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(name);
            }
        }
        var folder = Directory.CreateDirectory(Path.Combine(_dir, "whisper")).FullName;

        var written = WhisperInstall.ExtractProgram(zipPath, folder);

        Assert.Equal("whisper-cli.exe", Path.GetFileName(written.Last()));
        Assert.Equal(new[] { "ggml-cpu-x64.dll", "ggml-evil.dll", "whisper-cli.exe", "whisper.dll" },
            Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.False(File.Exists(Path.Combine(_dir, "ggml-evil.dll")));
    }
}
