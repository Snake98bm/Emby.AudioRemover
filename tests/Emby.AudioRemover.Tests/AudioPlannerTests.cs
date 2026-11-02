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
    public void A_video_without_a_default_only_gets_one_when_it_is_rewritten_anyway()
    {
        Assert.Null(AudioPlanner.Plan(Movie(Audio(1, "eng"), Audio(2, "eng")), Keep("eng"), Video).NewDefault);
        Assert.Equal(2, AudioPlanner.Plan(Movie(Audio(1, "fre"), Audio(2, "eng")), Keep("eng"), Video).NewDefault.index);
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
    }

    [Fact]
    public void Plan_is_described_for_the_log()
    {
        var plan = AudioPlanner.Plan(Movie(Audio(1, "fre", isDefault: true, title: "Français"), Audio(2, "eng", codec: "truehd", channels: 8)), Keep("eng"), Video);

        var text = MediaProcessor.DescribePlan(Video, plan, dryRun: true);

        Assert.Contains("[TEST MODE - no changes]", text);
        Assert.Contains("keep:    #2 eng truehd 8ch", text);
        Assert.Contains("remove:  #1 fre ac3 6ch default \"Français\"", text);
        Assert.Contains("default: #2 eng truehd 8ch", text);
    }
}
