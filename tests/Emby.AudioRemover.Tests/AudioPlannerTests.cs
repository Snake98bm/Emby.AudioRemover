using Emby.AudioRemover.Core;
using Xunit;
using static Emby.AudioRemover.Tests.TestData;

namespace Emby.AudioRemover.Tests;

public class AudioPlannerTests
{
    private const string Video = @"D:\Movies\My Movie (2020)\My Movie (2020).mkv";

    private static PlannerSettings Keep(string languages) => new() { KeepLanguages = PlannerSettings.ParseLanguages(languages) };

    private static int[] Indexes(IEnumerable<ProbeStream> streams) => streams.Select(s => s.index).ToArray();

    [Fact]
    public void Keeps_chosen_languages_and_removes_the_rest()
    {
        var probe = Movie(Audio(1, "eng", isDefault: true), Audio(2, "fre"), Audio(3, "spa"), Audio(4, "ger"));

        var plan = AudioPlanner.Plan(probe, Keep("eng, spa"), Video);

        Assert.Equal(new[] { 1, 3 }, Indexes(plan.Keep));
        Assert.Equal(new[] { 2, 4 }, Indexes(plan.Remove));
        Assert.True(plan.NeedsRemux);
        Assert.Null(plan.NewDefault);
    }

    [Fact]
    public void Language_match_ignores_case_and_spelling()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "ENG"), Audio(2, "en"), Audio(3, "fre")), Keep("eng"), Video);

        Assert.Equal(new[] { 1, 2 }, Indexes(plan.Keep));
    }

    [Fact]
    public void Video_and_subtitle_tracks_are_never_touched()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "eng"), Audio(2, "fre")), Keep("eng"), Video);

        Assert.All(plan.Keep.Concat(plan.Remove), s => Assert.True(s.IsAudio));
    }

    [Fact]
    public void A_foreign_film_with_no_kept_language_keeps_all_its_audio()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "jpn", isDefault: true), Audio(2, "fre")), Keep("eng"), Video);

        Assert.Empty(plan.Remove);
        Assert.Equal(new[] { 1, 2 }, Indexes(plan.Keep));
        Assert.False(plan.HasWork);
        Assert.NotNull(plan.LeftAlone);
    }

    [Fact]
    public void Only_english_commentary_does_not_count_as_kept_audio()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "jpn", isDefault: true), Audio(2, "eng", commentary: true)), Keep("eng"), Video);

        Assert.Empty(plan.Remove);
        Assert.NotNull(plan.LeftAlone);
    }

    [Fact]
    public void A_video_with_only_kept_audio_has_nothing_to_do()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true), Audio(2, "eng", codec: "aac", channels: 2)), Keep("eng"), Video);

        Assert.False(plan.HasWork);
        Assert.Null(plan.LeftAlone);
    }

    [Fact]
    public void Untagged_audio_stays_unless_switched_off()
    {
        var probe = Movie(Audio(1, null), Audio(2, "eng"), Audio(3, "fre"));

        Assert.Equal(new[] { 1, 2 }, Indexes(AudioPlanner.Plan(probe, Keep("eng"), Video).Keep));

        var strict = Keep("eng");
        strict.KeepUndefined = false;
        Assert.Equal(new[] { 1, 3 }, Indexes(AudioPlanner.Plan(probe, strict, Video).Remove));
    }

    [Fact]
    public void Tags_that_mean_no_language_count_as_untagged()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "unknown", isDefault: true), Audio(2, "null"), Audio(3, "fre")), Keep("eng"), Video);

        Assert.Equal(new[] { 1, 2 }, Indexes(plan.Keep));
        Assert.Equal(new[] { 3 }, Indexes(plan.Remove));
    }

    [Fact]
    public void Keeping_the_first_track_keeps_the_original_language_next_to_the_dub()
    {
        var settings = Keep("eng");
        settings.KeepFirstTrack = true;

        var plan = AudioPlanner.Plan(Movie(Audio(1, "jpn", isDefault: true), Audio(2, "eng"), Audio(3, "fre")), settings, Video);

        Assert.Equal(new[] { 1, 2 }, Indexes(plan.Keep));
        Assert.Equal(new[] { 3 }, Indexes(plan.Remove));
        Assert.Null(plan.NewDefault);
    }

    [Fact]
    public void Commentary_and_audio_description_go_only_when_asked()
    {
        var probe = Movie(Audio(1, "eng", isDefault: true), Audio(2, "eng", commentary: true), Audio(3, "eng", title: "Audio Description"),
            Audio(4, "eng", title: "Director's Commentary"), Audio(5, "eng", description: true));

        Assert.Empty(AudioPlanner.Plan(probe, Keep("eng"), Video).Remove);

        var settings = Keep("eng");
        settings.RemoveCommentary = true;
        settings.RemoveAudioDescription = true;
        var plan = AudioPlanner.Plan(probe, settings, Video);

        Assert.Equal(new[] { 1 }, Indexes(plan.Keep));
        Assert.Equal(new[] { 2, 3, 4, 5 }, Indexes(plan.Remove));
    }

    [Theory]
    [InlineData("English [AD]", true)]
    [InlineData("Descriptive Video Service", true)]
    [InlineData("English - Visually Impaired", true)]
    [InlineData("Headphones mix", false)]
    [InlineData("Advanced Dolby Atmos", false)]
    public void Audio_description_is_recognised_by_its_title(string title, bool expected)
    {
        Assert.Equal(expected, Audio(1, "eng", title: title).IsAudioDescription);
    }

    [Fact]
    public void Default_moves_to_the_kept_track_when_the_flagged_one_goes()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "fre", isDefault: true), Audio(2, "eng"), Audio(3, "eng", commentary: true)), Keep("eng"), Video);

        Assert.Equal(2, plan.NewDefault.index);
        Assert.Equal(new Dictionary<int, bool> { [2] = true }, plan.DefaultChanges());
    }

    [Fact]
    public void Default_moves_off_a_kept_commentary_track()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "eng", commentary: true, isDefault: true), Audio(2, "eng")), Keep("eng"), Video);

        Assert.Equal(2, plan.NewDefault.index);
        Assert.Equal(new Dictionary<int, bool> { [1] = false, [2] = true }, plan.DefaultChanges());
        Assert.True(plan.NeedsRemux);
    }

    [Fact]
    public void A_chosen_default_language_overrides_the_video()
    {
        var settings = Keep("eng, jpn");
        settings.DefaultLanguage = "jpn";

        var plan = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true), Audio(2, "jpn")), settings, Video);

        Assert.Equal(2, plan.NewDefault.index);
        Assert.Equal(new Dictionary<int, bool> { [1] = false, [2] = true }, plan.DefaultChanges());
    }

    [Fact]
    public void The_best_quality_track_in_the_same_language_gets_the_default()
    {
        var settings = Keep("eng, fre");
        settings.BestAudioDefault = true;
        ProbeStream Stereo(int index, string lang, bool isDefault = false) => Audio(index, lang, codec: "aac", isDefault: isDefault, channels: 2);

        // A stereo copy marked default and a 5.1 copy of the same English sound: the 5.1 one gets it, a rewrite of its own.
        var plan = AudioPlanner.Plan(Movie(Stereo(1, "eng", isDefault: true), Audio(2, "eng")), settings, Video);
        Assert.Equal(2, plan.NewDefault?.index);
        Assert.Equal(new Dictionary<int, bool> { [1] = false, [2] = true }, plan.DefaultChanges());

        // Only in the same language: French 5.1 never takes it from English stereo.
        Assert.Null(AudioPlanner.Plan(Movie(Stereo(1, "eng", isDefault: true), Audio(2, "fre")), settings, Video).NewDefault);

        // Already on the best one: nothing to do.
        Assert.Null(AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true), Stereo(2, "eng")), settings, Video).NewDefault);

        // The chosen default language's best track.
        settings.DefaultLanguage = "fre";
        Assert.Equal(3, AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true), Stereo(2, "fre"), Audio(3, "fre")), settings, Video).NewDefault?.index);

        // Commentary never counts, however many channels it has.
        settings.DefaultLanguage = null;
        Assert.Null(AudioPlanner.Plan(Movie(Stereo(1, "eng", isDefault: true), Audio(2, "eng", commentary: true)), settings, Video).NewDefault);

        // Switched off, the video's own default stays.
        settings.BestAudioDefault = false;
        Assert.Null(AudioPlanner.Plan(Movie(Stereo(1, "eng", isDefault: true), Audio(2, "eng")), settings, Video).NewDefault);
    }

    [Fact]
    public void With_the_same_channels_lossless_wins_then_the_higher_bitrate()
    {
        var settings = Keep("eng");
        settings.BestAudioDefault = true;
        ProbeStream Track(int index, string codec, string bitRate = null, string profile = null, bool isDefault = false)
        {
            var s = Audio(index, "eng", codec: codec, isDefault: isDefault);
            s.bit_rate = bitRate;
            s.profile = profile;
            return s;
        }

        Assert.Equal(2, AudioPlanner.Plan(Movie(Track(1, "ac3", "448000", isDefault: true), Track(2, "dts", "1509000", "DTS")), settings, Video).NewDefault?.index);
        // TrueHD in an MKV often has no bitrate at all; lossless still wins.
        Assert.Equal(2, AudioPlanner.Plan(Movie(Track(1, "dts", "1509000", "DTS", isDefault: true), Track(2, "truehd")), settings, Video).NewDefault?.index);
        Assert.Equal(2, AudioPlanner.Plan(Movie(Track(1, "ac3", "640000", isDefault: true), Track(2, "dts", null, "DTS-HD MA")), settings, Video).NewDefault?.index);
        // A tie: the track that has it keeps it.
        Assert.Null(AudioPlanner.Plan(Movie(Track(1, "ac3", isDefault: true), Track(2, "ac3")), settings, Video).NewDefault);
    }

    [Fact]
    public void Bitrate_and_lossless_come_from_ffprobe_or_mkvmerge_tags()
    {
        var probe = Parse("""
            {"streams": [
              {"index": 1, "codec_type": "audio", "codec_name": "aac", "profile": "LC", "channels": 2, "bit_rate": "159573"},
              {"index": 2, "codec_type": "audio", "codec_name": "dts", "profile": "DTS-HD MA", "channels": 8, "tags": {"BPS": "3456789"}},
              {"index": 3, "codec_type": "audio", "codec_name": "eac3", "channels": 6, "tags": {"BPS-eng": "768000"}},
              {"index": 4, "codec_type": "audio", "codec_name": "pcm_s24le", "channels": 2}
            ]}
            """);

        Assert.Equal(new long[] { 159573, 3456789, 768000, 0 }, probe.streams.Select(s => s.BitRate));
        Assert.Equal(new[] { false, true, false, true }, probe.streams.Select(s => s.IsLossless));
    }

    [Fact]
    public void A_video_without_a_default_only_gets_one_when_it_is_rewritten_anyway()
    {
        Assert.Null(AudioPlanner.Plan(Movie(Audio(1, "eng"), Audio(2, "eng")), Keep("eng"), Video).NewDefault);
        Assert.Equal(2, AudioPlanner.Plan(Movie(Audio(1, "fre"), Audio(2, "eng")), Keep("eng"), Video).NewDefault.index);
    }

    [Fact]
    public void A_second_default_flag_alone_is_not_worth_a_rewrite()
    {
        Assert.False(AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true), Audio(2, "eng", isDefault: true)), Keep("eng"), Video).HasWork);

        var settings = Keep("eng, jpn");
        settings.DefaultLanguage = "eng";
        Assert.False(AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true), Audio(2, "jpn", isDefault: true)), settings, Video).HasWork);
    }

    [Fact]
    public void A_second_default_flag_is_cleared_when_the_video_is_rewritten_anyway()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true), Audio(2, "eng", isDefault: true), Audio(3, "ita")), Keep("eng"), Video);

        Assert.Equal(1, plan.NewDefault.index);
        Assert.Equal(new Dictionary<int, bool> { [2] = false }, plan.DefaultChanges());
        Assert.EndsWith("default: #1 eng ac3 6ch default (taken off #2)", MediaProcessor.DescribePlan(Video, plan, dryRun: true));
    }

    [Fact]
    public void Default_flags_are_left_alone_when_switched_off_or_the_container_cannot_store_them()
    {
        var probe = Movie(Audio(1, "fre", isDefault: true), Audio(2, "eng"));
        var off = Keep("eng");
        off.SetDefault = false;

        Assert.Null(AudioPlanner.Plan(probe, off, Video).NewDefault);
        Assert.Null(AudioPlanner.Plan(probe, Keep("eng"), @"D:\Movies\My Movie (2020)\My Movie (2020).ts").NewDefault);
    }

    [Fact]
    public void Settings_signature_changes_with_every_setting()
    {
        var a = Keep("eng").Signature();
        var b = Keep("eng");
        b.KeepFirstTrack = true;
        var c = Keep("eng");
        c.RemoveAudioDescription = true;

        Assert.NotEqual(a, b.Signature());
        Assert.NotEqual(a, c.Signature());
        Assert.NotEqual(b.Signature(), c.Signature());

        var signatures = new[]
        {
            a, b.Signature(), c.Signature(), CleanTitles().Signature(), CleanTitles("YTS").Signature(),
            CleanTitles(fileTitle: false).Signature(), CleanTitles(rewrite: false).Signature()
        };
        Assert.Equal(signatures.Length, signatures.Distinct().Count());
    }

    // ---- website ads in titles

    private static PlannerSettings CleanTitles(string words = null, bool fileTitle = true, bool rewrite = true)
    {
        var settings = Keep("eng");
        settings.CleanTitles = true;
        settings.CleanFileTitle = fileTitle;
        settings.RewriteForTitles = rewrite;
        if (words != null) settings.Titles = new TitleCleaner(TitleCleaner.DefaultList + "\n" + words);
        return settings;
    }

    [Fact]
    public void Text_added_from_the_suggestions_gets_its_videos_a_rewrite_of_their_own()
    {
        var settings = CleanTitles(words: "GRP", rewrite: false);
        settings.CleanAdded = new TitleCleaner("GRP");

        var added = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true, title: "English - GRP")), settings, Video);
        Assert.True(added.HasWork);
        Assert.Equal(new Dictionary<int, string> { [1] = "English" }, added.StreamTitles());

        // Only text the list had before: it still waits for a rewrite for the audio.
        var other = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true, title: "English - SomeSite.com")), settings, Video);
        Assert.False(other.HasWork);
        Assert.NotEmpty(other.TitlesLeft);
    }

    [Fact]
    public void Only_added_text_still_on_the_list_counts_and_only_when_switched_on()
    {
        Assert.True(new PluginOptions().CleanWhereSuggested);
        Assert.True(new PluginOptions().SuggestDuringRuns);

        var options = new PluginOptions { CleanTitles = true, TitleAds = "*.com\nGRP", AddedFromSuggestions = "GRP\nTAKEN-OFF" };
        Assert.Equal("GRP", MediaProcessor.AddedCleaner(options)!.Signature);

        options.CleanWhereSuggested = false;
        Assert.Null(MediaProcessor.AddedCleaner(options));
    }

    // The file, the video track and the subtitle get an ad too.
    private static ProbeResult WithAds(ProbeResult probe)
    {
        probe.format.tags = new Dictionary<string, string> { ["title"] = "My Movie (2020) - SomeSite.com" };
        probe.streams[0].tags = new Dictionary<string, string> { ["title"] = "SomeSite.com" };
        probe.streams.Last().tags["title"] = "Forced - SomeSite.com";
        return probe;
    }

    [Fact]
    public void Ads_come_out_of_the_file_title_and_every_track_that_stays()
    {
        var probe = WithAds(Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com"), Audio(2, "fre", title: "European - SomeSite.com")));

        var plan = AudioPlanner.Plan(probe, CleanTitles(), Video);

        Assert.Equal(new int?[] { null, 0, 1, 3 }, plan.TitleChanges.Select(c => c.StreamIndex));
        Assert.Equal("My Movie (2020)", plan.FileTitle);
        Assert.Equal(new Dictionary<int, string> { [0] = "", [1] = "", [3] = "Forced" }, plan.StreamTitles());
    }

    [Fact]
    public void The_file_title_is_left_alone_unless_asked()
    {
        var probe = WithAds(Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com")));

        var plan = AudioPlanner.Plan(probe, CleanTitles(fileTitle: false), Video);

        Assert.Equal(new int?[] { 0, 1, 2 }, plan.TitleChanges.Select(c => c.StreamIndex));
        Assert.Null(plan.FileTitle);
        Assert.False(new PluginOptions().CleanFileTitle);
    }

    [Fact]
    public void Without_a_rewrite_of_their_own_names_wait_for_one_for_the_audio()
    {
        Assert.False(new PluginOptions().RewriteForTitles);
        var settings = CleanTitles(rewrite: false);

        var onlyNames = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com"), Audio(2, "eng", isDefault: true)),
            settings, Video);
        Assert.False(onlyNames.HasWork);
        Assert.Empty(onlyNames.TitleChanges);
        Assert.Single(onlyNames.TitlesLeft);

        var withAudio = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com"), Audio(2, "fre")), settings, Video);
        Assert.Equal(new Dictionary<int, string> { [1] = "" }, withAudio.StreamTitles());
        Assert.Empty(withAudio.TitlesLeft);
    }

    [Fact]
    public void Ads_alone_are_worth_a_rewrite_which_also_tidies_the_default()
    {
        var probe = Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com"), Audio(2, "eng", isDefault: true));

        Assert.False(AudioPlanner.Plan(probe, Keep("eng"), Video).HasWork);

        var plan = AudioPlanner.Plan(probe, CleanTitles(), Video);
        Assert.True(plan.HasWork);
        Assert.Empty(plan.Remove);
        Assert.Equal(new Dictionary<int, bool> { [2] = false }, plan.DefaultChanges());
    }

    [Fact]
    public void A_foreign_film_left_alone_still_gets_its_titles_cleaned()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "jpn", isDefault: true, title: "Japanese - SomeSite.com")), CleanTitles(), Video);

        Assert.NotNull(plan.LeftAlone);
        Assert.Empty(plan.Remove);
        Assert.Null(plan.NewDefault);
        Assert.Equal("Japanese", plan.StreamTitles()[1]);
    }

    [Fact]
    public void Titles_are_left_alone_where_the_container_cannot_store_them()
    {
        var plan = AudioPlanner.Plan(WithAds(Movie(Audio(1, "eng"))), CleanTitles(), @"D:\Movies\My Movie (2020)\My Movie (2020).ts");

        Assert.Empty(plan.TitleChanges);
    }

    [Fact]
    public void Cleaned_titles_are_described_for_the_log()
    {
        var plan = AudioPlanner.Plan(WithAds(Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com"))), CleanTitles(), Video);

        var text = MediaProcessor.DescribePlan(Video, plan, dryRun: true);

        Assert.EndsWith("titles:  4 cleaned: \"My Movie (2020) - SomeSite.com\" -> \"My Movie (2020)\", \"SomeSite.com\" -> none, " +
                        "\"- SomeSite.com\" -> none, ...", text);
    }

    [Fact]
    public void Plan_is_described_for_the_log()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "fre", isDefault: true, title: "Français"), Audio(2, "eng", codec: "truehd", channels: 8)), Keep("eng"), Video);

        var text = MediaProcessor.DescribePlan(Video, plan, dryRun: true);

        Assert.Contains("[TEST MODE - no changes]", text);
        Assert.Contains("keep:    #2 eng truehd 8ch", text);
        Assert.Contains("remove:  #1 fre ac3 6ch default \"Français\"", text);
        Assert.Contains("default: #2 eng truehd 8ch (taken off #1)", text);
    }

    [Fact]
    public void Log_says_when_a_video_had_no_default_before()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "fre"), Audio(2, "eng")), Keep("eng"), Video);

        Assert.EndsWith("default: #2 eng ac3 6ch (no default before)", MediaProcessor.DescribePlan(Video, plan, dryRun: true));
    }
}
