using Emby.AudioRemover.Core;
using Xunit;

namespace Emby.AudioRemover.Tests;

public class FfmpegArgsTests
{
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has space", "\"has space\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData(@"C:\Movies\A B\", "\"C:\\Movies\\A B\\\\\"")]
    [InlineData(@"C:\NoSpaces\file.mkv", @"C:\NoSpaces\file.mkv")]
    public void Quotes_arguments_safely(string input, string expected)
    {
        Assert.Equal(expected, FfmpegArgs.Quote(input));
    }

    [Fact]
    public void Remux_keeps_everything_except_the_removed_streams_in_order()
    {
        var args = FfmpegArgs.Remux("in.mkv", "out.tmp", "matroska", new[] { 7, 3 });
        var line = string.Join(" ", args);

        Assert.Contains("-i in.mkv -map 0 -map -0:3 -map -0:7 -c copy", line);
        Assert.EndsWith("-f matroska out.tmp", line);
    }

    [Fact]
    public void Remux_changes_default_flags_by_their_index_in_the_new_file()
    {
        var args = FfmpegArgs.Remux("in.mkv", "out.tmp", "matroska", new[] { 2 }, new Dictionary<int, bool> { [4] = true, [3] = false });
        var line = string.Join(" ", args);

        Assert.Contains("-map_chapters 0 -disposition:2 -default -disposition:3 +default -max_muxing_queue_size", line);
    }

    [Fact]
    public void Remux_sets_titles_by_their_index_in_the_new_file()
    {
        var args = FfmpegArgs.Remux("in.mkv", "out.tmp", "matroska", new[] { 2 }, null, new Dictionary<int, string> { [5] = "Forced", [0] = "" },
            "My Movie (2020)");
        var line = string.Join(" ", args);

        Assert.Contains("-metadata title=My Movie (2020) -metadata:s:0 title= -metadata:s:4 title=Forced -max_muxing_queue_size", line);
        Assert.DoesNotContain("-metadata", string.Join(" ", FfmpegArgs.Remux("in.mkv", "out.tmp", "matroska", new[] { 2 })));
    }

    [Theory]
    [InlineData("a.mkv", true)]
    [InlineData("a.mp4", true)]
    [InlineData("a.ts", false)]
    [InlineData("a.avi", false)]
    public void Default_flags_are_only_changed_where_the_container_stores_them(string path, bool expected)
    {
        Assert.Equal(expected, FfmpegArgs.CanSetDefaultFlags(path));
    }

    [Theory]
    [InlineData("a.mkv", "matroska")]
    [InlineData("a.MP4", "mp4")]
    [InlineData("a.m2ts", "mpegts")]
    [InlineData("a.iso", null)]
    [InlineData("a.strm", null)]
    public void Picks_the_muxer_from_the_extension(string path, string expected)
    {
        Assert.Equal(expected, FfmpegArgs.MuxerFor(path));
    }
}
