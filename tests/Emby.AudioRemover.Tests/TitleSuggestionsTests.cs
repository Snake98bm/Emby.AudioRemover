using Emby.AudioRemover.Core;
using Xunit;

namespace Emby.AudioRemover.Tests;

public sealed class TitleSuggestionsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("audioremover-suggest-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private static List<TitleSuggestion> Find(params (string Video, string Title)[] names) => TitleSuggestions.Find(names, TitleCleaner.Default);

    [Fact]
    public void Tags_that_turn_up_across_different_films_are_suggested()
    {
        var found = Find(
            ("Movie One (2012)", "GRP"), ("Movie Two (2011)", "GRP"),
            ("Movie Three (2014)", "@XY [ENG] DTS 5.1"), ("Movie Four (2015)", "@XY [ENG] AC3 5.1"),
            ("Movie Five (2016)", "[Some Torrents] DD 5.1"), ("Movie Six (2017)", "[Some Torrents] TrueHD 7.1"));

        Assert.Equal(new[] { "@XY", "GRP", "[Some Torrents]" }, found.Select(s => s.Entry));
        Assert.All(found, s => Assert.Equal(2, s.Videos));
        Assert.Equal("Movie One (2012): \"GRP\"", found.Single(s => s.Entry == "GRP").Example);
        Assert.Equal(new[] { "Movie One (2012)", "Movie Two (2011)" }, found.Single(s => s.Entry == "GRP").VideoNames);
    }

    [Fact]
    public void The_log_lists_each_suggestion_with_the_films_and_shows_it_is_in_by_name()
    {
        var found = Find(
            ("Movie Two (2011)", "GRP"), ("My Show", "GRP"), ("Movie One (2012)", "GRP English"), ("My Show", "GRP"),
            ("Movie Three (2014)", "[Some Torrents]"), ("Movie Four (2015)", "[Some Torrents]"));

        Assert.Equal(
            "text in track names the 'Text to remove' list misses (in 2+ films or shows); add it with 'Suggested text' on the settings page:" +
            "\n    GRP (3): Movie One (2012), Movie Two (2011), My Show" +
            "\n    [Some Torrents] (2): Movie Four (2015), Movie Three (2014)",
            TitleSuggestions.Describe(found));

        // Long lists are cut short.
        Assert.Equal(
            "text in track names the 'Text to remove' list misses (in 2+ films or shows); add it with 'Suggested text' on the settings page:" +
            "\n    GRP (3): Movie One (2012), Movie Two (2011) and 1 more" +
            "\n    and 1 more",
            TitleSuggestions.Describe(found, most: 1, namesEach: 2));
    }

    [Fact]
    public void Describing_words_one_shows_own_name_and_what_the_list_removes_are_not_suggested()
    {
        var found = Find(
            // Descriptions, in any language
            ("Movie One (2012)", "English Commentary"), ("Movie Two (2011)", "English Commentary"),
            ("Movie Three (2013)", "Italiano [ENG] Forced SDH"), ("Movie Four (2014)", "Italiano [ENG] Forced SDH"),
            // format detail has digits
            ("Movie Five (2015)", "DDP5.1 640kbps"), ("Movie Six (2016)", "DDP5.1 640kbps"),
            // one show only: its episodes are one show
            ("My Show", "My Show Season 4"), ("My Show", "My Show Season 4"), ("My Show", "Bonus Words"),
            // the film's own name
            ("Big Monster (1984)", "Big Monster English"), ("Big Monster II (1989)", "Big Monster English"),
            // already removed by the list
            ("Movie Seven (2017)", "- SomeSite.com"), ("Movie Eight (2018)", "- SomeSite.com"));

        Assert.Empty(found);
    }

    [Fact]
    public void Suggestions_never_include_anything_the_box_would_refuse()
    {
        // "Forced" alone would wipe ordinary names, so it's left out even when it's everywhere (and it's a describing word).
        Assert.Empty(Find(("A (2001)", "Forced"), ("B (2002)", "Forced"), ("C (2003)", "Forced")));
    }

    [Fact]
    public void Added_suggestions_keep_the_list_sorted_and_free_of_repeats()
    {
        var list = "*.top\n/x+Z/\n*.com\nZED";

        Assert.Equal("*.com\n*.top\n@XY\nGRP\nZED\n/x+Z/", Plugin.WithEntries(list, new[] { "GRP", "@XY", "zed" }));
    }

    [Fact]
    public void Countries_and_regions_in_language_labels_are_never_suggested()
    {
        var found = Find(
            ("Movie One (2012)", "English (United States)"), ("Movie Two (2011)", "English (United States)"),
            ("Movie Three (2013)", "Português (Brasil)"), ("Movie Four (2014)", "Portuguese (Brazil)"),
            ("Movie Five (2015)", "Spanish (Latinoamérica)"), ("Movie Six (2016)", "Español (Latinoamérica)"),
            ("Movie Seven (2017)", "pt-BR"), ("Movie Eight (2018)", "es-MX [GRP]"),
            ("Movie Nine (2019)", "English-SRT"), ("Movie Ten (2020)", "English-SRT [GRP]"),
            ("Movie Eleven (2021)", "French (Canada)"), ("Movie Twelve (2022)", "Français (Belgique)"),
            ("Movie Thirteen (2023)", "Chinese (Hong Kong)"), ("Movie Fourteen (2024)", "Chinese (Taiwan)"));

        // Only the real tag is left.
        Assert.Equal(new[] { "[GRP]" }, found.Select(s => s.Entry));
        Assert.True(Regions.Count > 400);
    }

    [Fact]
    public void Ordinary_track_names_from_a_real_library_are_never_suggested()
    {
        // Each label in two different films, as a library has them; only the release tag is an ad.
        var labels = new[]
        {
            "Commentary by director Jane Doe and producer John Roe", "Closed Captions", "English Hearing Impaired",
            "Compatibility Track / Dolby Digital Audio / 5.1 / 48 kHz / 640 kbps", "x264 1920x800 1845 kb/s", "Audio Stream",
            "Video", "WEB-DL", "WEB-DL SDH", "French (Parisian)", "Français (Canadien)", "Español (Latinoamericano)",
            "Greek (Modern)", "Srpski (Latinica)", "中文（简体）", "中文（繁體）", "廣東話", "Bahasa Melayu", "Lietuvių", "Български",
            "Tiếng Việt", "Hrvatski", "ekk", "HQ White SUP", "HQ Geel IDX", "Normal SRT", "Regular Full", "Stripped SRT",
            "Encoded by GRP", "Original Surround Mix / DTS-HD Master Audio / 5.1", "DTS-CORE 1536K", "BDRip 720p multisub GRP"
        };
        var names = labels.SelectMany(l => new[] { ("Movie One (2012)", l), ("Movie Two (2013)", l) }).ToArray();

        Assert.Equal(new[] { "GRP" }, Find(names).Select(s => s.Entry));
    }

    [Fact]
    public void Suggestions_build_up_across_runs()
    {
        var data = new SuggestionData();
        TitleSuggestions.Merge(data, new[] { ("Movie One (2012)", "English [GRP]") }, TitleCleaner.Default);
        Assert.Empty(TitleSuggestions.Waiting(data, TitleCleaner.Default)); // one film so far

        TitleSuggestions.Merge(data, new[] { ("My Show", "English [GRP]"), ("My Show", "English [GRP]") }, TitleCleaner.Default);
        var waiting = Assert.Single(TitleSuggestions.Waiting(data, TitleCleaner.Default));
        Assert.Equal("[GRP]", waiting.Entry);
        Assert.Equal(new[] { "Movie One (2012)", "My Show" }, waiting.VideoNames);
        Assert.Equal("[GRP]", TitleSuggestions.BoxText(data, new[] { waiting }));

        // Once the list has it, it stops showing.
        Assert.Empty(TitleSuggestions.Waiting(data, new TitleCleaner(TitleCleaner.DefaultList + "\n[GRP]")));
    }

    [Fact]
    public void A_line_deleted_from_the_box_is_never_suggested_again_and_a_typed_one_stays()
    {
        var data = new SuggestionData();
        TitleSuggestions.Merge(data, new[] { ("Movie One (2012)", "GRP ZED"), ("Movie Two (2011)", "GRP ZED") }, TitleCleaner.Default);
        string Box() => TitleSuggestions.BoxText(data, TitleSuggestions.Waiting(data, TitleCleaner.Default));
        var shown = Box();
        Assert.Equal("GRP\nZED", shown);

        TitleSuggestions.KeepEdits(data, shown, "ZED\n/GRP\\d+/");
        Assert.Equal("/GRP\\d+/\nZED", Box());

        // Found again by a later run: still gone.
        TitleSuggestions.Merge(data, new[] { ("Movie Three (2013)", "GRP") }, TitleCleaner.Default);
        Assert.DoesNotContain("GRP", TitleSuggestions.Lines(Box()));
    }

    [Fact]
    public void Add_to_the_list_adds_every_line_that_passes_the_checks_and_keeps_the_rest()
    {
        var store = new SuggestionStore(_dir, new TestJsonSerializer());
        store.Update(data => TitleSuggestions.KeepEdits(data, "", "GRP\nForced"));
        var options = new PluginOptions { TitleAds = "*.com", SuggestedText = "GRP\nForced" };

        var status = Plugin.AddSuggestedText(options, store);

        Assert.Equal("*.com\nGRP", options.TitleAds);
        Assert.Equal("GRP", options.AddedFromSuggestions);
        Assert.Equal(Emby.Web.GenericEdit.Elements.ItemStatus.Warning, status.Status);
        Assert.StartsWith("Added to the list: GRP. The next run cleans them out", status.StatusText);
        // Refused by the list's checks: it stays in the box. Added: never suggested again, even off the list later.
        var data = new SuggestionStore(_dir, new TestJsonSerializer()).Read();
        Assert.Equal(new[] { "Forced" }, data.Typed);
        Assert.Contains("GRP", data.Dismissed);
    }
}
