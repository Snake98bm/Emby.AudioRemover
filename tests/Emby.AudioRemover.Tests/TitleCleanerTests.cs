using Emby.AudioRemover.Core;
using Xunit;

namespace Emby.AudioRemover.Tests;

public class TitleCleanerTests
{
    private static string Clean(string title) => TitleCleaner.Default.Clean(title);

    [Theory]
    [InlineData("- SomeSite.com", "")]
    [InlineData("SomeSite.com", "")]
    [InlineData("European (Forced) - SomeSite.com", "European (Forced)")]
    [InlineData("My Movie (2020) - SomeSite.com", "My Movie (2020)")]
    [InlineData("[www.Some-Site.net] English", "English")]
    [InlineData("English (SomeSite.com)", "English")]
    [InlineData("English | Downloaded from somesite.to", "English")]
    [InlineData("English - somesite.co.uk - Forced", "English - Forced")]
    [InlineData("https://somesite.in", "")]
    [InlineData("http://www.SomeSite.com/movies/", "")]
    [InlineData("English - https://somesite.to/x?y=1", "English")]
    [InlineData("SomeSite.in Encodes", "")]
    [InlineData("SomeSite.in encode", "")]
    [InlineData("SomeSite.in - My Show Season 4", "My Show Season 4")]
    [InlineData("www.SomeSite.com/movies", "")]
    [InlineData("Encoded by SomeSite.com", "")]
    [InlineData("English | Ripped by somesite.net", "English")]
    [InlineData("SomeSite.top Releases", "")]
    [InlineData("English (SomeSite.com) - Forced", "English - Forced")]
    public void Website_names_go_with_what_was_around_them(string title, string expected)
    {
        Assert.Equal(expected, Clean(title));
    }

    [Theory]
    [InlineData("SomeSite.top | My.Movie.2020.1080p.WEBRip.x265-GROUP.mkv")]
    [InlineData("My.Movie.2020.1080p.WEBRip.x265-GROUP")]
    [InlineData("My.Show.S01E02.2160p.WEB-DL.DDP5.1.HEVC-GROUP")]
    [InlineData("My Movie (2020).mkv")]
    public void Titles_that_are_only_a_file_or_release_name_go(string title)
    {
        Assert.Equal("", Clean(title));
    }

    [Theory]
    [InlineData("Dolby Digital 5.1")]
    [InlineData("DTS-HD MA 7.1")]
    [InlineData("Back.to.the.Future")]
    [InlineData("Dolby.Atmos.7.1")]
    [InlineData("English mp4a.40.2")]
    [InlineData("H.264 - Main Profile")]
    [InlineData("1080p")]
    [InlineData("Director's Commentary")]
    [InlineData("")]
    public void Titles_without_ads_stay_exactly_as_they_are(string title)
    {
        Assert.Same(title, Clean(title));
        Assert.Null(Clean(null));
    }

    [Fact]
    public void Words_added_to_the_list_go_as_whole_words_only()
    {
        var cleaner = new TitleCleaner(TitleCleaner.DefaultList + "\nYTS, [TGx]\n\n  yts  ");

        Assert.Equal("English", cleaner.Clean("English - yts"));
        Assert.Equal("Forced", cleaner.Clean("[TGx] Forced"));
        Assert.Equal("", cleaner.Clean("YTS"));
        Assert.Equal("English YTSX", cleaner.Clean("English YTSX"));
        Assert.Equal("English", cleaner.Clean("English - SomeSite.com"));
    }

    [Fact]
    public void An_underscore_is_a_gap_between_words_and_a_space_in_an_entry_matches_any_gap()
    {
        var tag = new TitleCleaner("TAG");
        Assert.Equal("English 5.1 - SITE", tag.Clean("English 5.1 - SITE_TAG"));
        Assert.Equal("English 5.1 - SITE", tag.Clean("English 5.1 - SITE TAG"));
        Assert.Equal("English TAGGED", tag.Clean("English TAGGED"));

        var both = new TitleCleaner("SITE TAG");
        foreach (var title in new[] { "English 5.1 - SITE TAG", "English 5.1 - SITE_TAG", "English 5.1 - SITE.TAG", "English 5.1 - SITE-TAG" })
            Assert.Equal("English 5.1", both.Clean(title));
        Assert.Equal("English SITETAG", both.Clean("English SITETAG"));
    }

    [Fact]
    public void Website_endings_come_from_the_list()
    {
        var onlyCom = new TitleCleaner("*.com");
        Assert.Equal("English", onlyCom.Clean("English - SomeSite.com"));
        Assert.Equal("English - SomeSite.top", onlyCom.Clean("English - SomeSite.top"));

        var added = new TitleCleaner(TitleCleaner.DefaultList + "\n*.watch");
        Assert.Equal("English", added.Clean("English - SomeSite.watch"));

        // An empty list still drops titles that are only a release name.
        Assert.Equal("English - SomeSite.com", new TitleCleaner("").Clean("English - SomeSite.com"));
        Assert.Equal("", new TitleCleaner("").Clean("My.Movie.2020.1080p.WEBRip.x265-GROUP"));
    }

    [Fact]
    public void A_line_between_slashes_is_a_regular_expression()
    {
        var cleaner = new TitleCleaner(@"/GROUP\.\w+/" + "\n" + "/x{1,3}Z/");

        Assert.Equal("English", cleaner.Clean("GROUP.biz | English"));
        Assert.Equal("English", cleaner.Clean("English xxZ"));
        Assert.Empty(cleaner.Problems);
    }

    [Theory]
    [InlineData("/[unclosed/", "isn't a valid regular expression")]
    [InlineData("/(a+)+b/", "has a repeat inside a repeat")]
    [InlineData(@"/(\w*)*$/", "has a repeat inside a repeat")]
    [InlineData("/.*/", "also matches nothing at all")]
    [InlineData("/x?/", "also matches nothing at all")]
    [InlineData("/.+/", "would remove ordinary titles like 'English'")]
    [InlineData(@"/[^|]+/", "would remove ordinary titles like 'English'")]
    [InlineData("/(a|aa)*c/", "is too slow on some titles")]
    [InlineData("English", "would remove ordinary titles like 'English'")]
    [InlineData("*.c*m", "isn't a website ending")]
    [InlineData("*.", "isn't a website ending")]
    public void Harmful_or_broken_entries_are_skipped_and_say_why(string entry, string reason)
    {
        var cleaner = new TitleCleaner(entry + "\n*.com");

        var problem = Assert.Single(cleaner.Problems);
        Assert.Contains(reason, problem);
        Assert.Equal(2, cleaner.EntryCount);
        Assert.Equal("English", cleaner.Clean("English - SomeSite.com"));
        Assert.Equal("Forced", cleaner.Clean("Forced"));
    }

    [Fact]
    public void Overlong_entries_and_lists_are_cut_short()
    {
        Assert.Contains("longer than 200", Assert.Single(new TitleCleaner("/" + new string('x', 200) + "/").Problems));

        var many = new TitleCleaner(string.Join("\n", Enumerable.Range(0, 310).Select(i => "Word" + i)));
        Assert.Contains("only the first 300 entries are used; the other 10 are skipped", Assert.Single(many.Problems));
        Assert.Equal("", many.Clean("Word299"));
        Assert.Equal("Word305", many.Clean("Word305"));
    }

    [Fact]
    public void A_regular_expression_too_slow_on_a_real_title_is_switched_off_for_the_run()
    {
        var switchedOff = new List<string>();
        // Fine on the test titles (no b's in them), but backtracks for ages on a long run of b's.
        var cleaner = new TitleCleaner("/(b|bb)*c/\n*.com", switchedOff.Add);
        Assert.Empty(cleaner.Problems);

        var slow = new string('b', 40) + "!";
        Assert.Equal(slow, cleaner.Clean(slow));
        Assert.Equal(new[] { "/(b|bb)*c/" }, switchedOff);

        // Not tried again, while the rest of the list still works.
        Assert.Equal(slow, cleaner.Clean(slow));
        Assert.Single(switchedOff);
        Assert.Equal("English", cleaner.Clean("English - SomeSite.com"));
    }

    [Fact]
    public void The_default_list_is_one_website_ending_per_line()
    {
        var lines = TitleCleaner.DefaultList.Split('\n');

        Assert.Contains("*.com", lines);
        Assert.Contains("*.top", lines);
        Assert.All(lines, l => Assert.StartsWith("*.", l));
        Assert.Equal(TitleCleaner.DefaultList, new PluginOptions().TitleAds);
        Assert.Equal(TitleCleaner.Default.Signature, new TitleCleaner(new PluginOptions().TitleAds).Signature);
    }
}
