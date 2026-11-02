using Emby.AudioRemover.Core;
using Xunit;
using static Emby.AudioRemover.Tests.TestData;

namespace Emby.AudioRemover.Tests;

public class RunTotalsTests
{
    private const string Video = @"D:\Movies\My Movie (2020)\My Movie (2020).mkv";

    [Fact]
    public void Adds_up_what_each_plan_does()
    {
        var settings = new PlannerSettings { KeepLanguages = PlannerSettings.ParseLanguages("eng") };
        var totals = new RunTotals();

        totals.Add(AudioPlanner.Plan(Movie(Audio(1, "fre", isDefault: true), Audio(2, "eng"), Audio(3, "ger")), settings, Video));
        totals.Add(AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true)), settings, Video));
        totals.Add(AudioPlanner.Plan(Movie(Audio(1, "jpn", isDefault: true), Audio(2, "fre")), settings, Video));

        Assert.Equal(1, totals.VideosRewritten);
        Assert.Equal(2, totals.TracksRemoved);
        Assert.Equal(1, totals.DefaultsChanged);
        Assert.Equal(1, totals.LeftAlone);
        Assert.Equal(0, totals.TitlesCleaned);

        // Names alone: left for a rewrite for the audio, then rewritten once that's allowed.
        var onlyNames = Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com"));
        settings.CleanTitles = true;
        totals.Add(AudioPlanner.Plan(onlyNames, settings, Video));
        Assert.Equal(1, totals.VideosRewritten);
        Assert.Equal(1, totals.TitlesLeft);

        settings.RewriteForTitles = true;
        totals.Add(AudioPlanner.Plan(onlyNames, settings, Video));
        Assert.Equal(2, totals.VideosRewritten);
        Assert.Equal(1, totals.TitlesCleaned);

        var text = totals.Describe(dryRun: false);
        Assert.Contains("videos with names cleaned:      1", text);
        Assert.Contains("to rewrite just for clean-up:   1", text);
        Assert.Contains("(not rewritten: 'Rewrite a video just for clean-up' is off; turning it on adds 1 rewrite)", text);
    }

    [Fact]
    public void A_video_with_only_names_to_clean_is_logged_with_its_names()
    {
        var settings = new PlannerSettings { KeepLanguages = PlannerSettings.ParseLanguages("eng"), CleanTitles = true };

        var plan = AudioPlanner.Plan(Movie(Audio(1, "eng", isDefault: true, title: "- SomeSite.com")), settings, Video);

        Assert.False(plan.HasWork);
        Assert.Equal("1 to clean: \"- SomeSite.com\" -> none", MediaProcessor.DescribeTitles(plan.TitlesLeft, "to clean"));
        Assert.DoesNotContain("not rewritten", new RunTotals().Describe(dryRun: false));
    }

    [Fact]
    public void Describes_the_totals_for_the_end_of_the_log()
    {
        var totals = new RunTotals { VideosRewritten = 3, TracksRemoved = 7, DefaultsChanged = 2, LeftAlone = 1 };

        var text = totals.Describe(dryRun: true);

        Assert.StartsWith("Totals - what a real run would do:", text);
        Assert.Contains("audio tracks removed:           7", text);
        Assert.Contains("left alone (no kept audio):     1", text);
        Assert.StartsWith("Totals:", totals.Describe(dryRun: false));
    }
}
