using Emby.AudioRemover.Core;
using Xunit;

namespace Emby.AudioRemover.Tests;

public class TitleSuggestionsTests
{
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
    public void Only_switched_on_suggestions_are_taken_and_the_list_is_then_done_with()
    {
        var search = new TitleSuggestionSearch();
        Assert.True(search.TryStart());
        Assert.False(search.TryStart());
        search.Finish(new List<TitleSuggestion> { new() { Entry = "GRP" }, new() { Entry = "@XY" }, new() { Entry = "ZED" } }, 100);

        search.Toggle(2);
        search.Toggle(0);
        search.Toggle(5);
        Assert.True(search.IsSelected(0));

        Assert.Equal(new[] { "GRP", "ZED" }, search.TakeSelected());
        Assert.Null(search.Found);
        Assert.Empty(search.TakeSelected());
    }
}
