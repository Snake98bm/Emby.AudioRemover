using System;
using System.IO;
using System.Linq;
using Emby.AudioRemover.Core;
using Xunit;

namespace Emby.AudioRemover.Tests;

public class HashMatchedSubtitlesTests
{
    [Fact]
    public void Hash_of_zeros_is_just_the_size()
    {
        Assert.Equal("0000000000020000", OpenSubtitlesHash.Compute(new MemoryStream(new byte[128 * 1024])));
    }

    [Fact]
    public void Reads_SubRemovers_switch_and_languages()
    {
        var folder = NewFolder();
        try
        {
            Assert.Null(SubRemoverSettings.Read(folder, new TestJsonSerializer()));
            File.WriteAllText(Path.Combine(folder, SubRemoverSettings.FileName),
                "{\"DryRun\":false,\"KeepLanguages\":\"eng,ger\",\"DownloadHashMatchedSubtitles\":true,\"OcrModel\":\"Both\"}");
            var settings = SubRemoverSettings.Read(folder, new TestJsonSerializer());
            Assert.True(settings.DownloadHashMatchedSubtitles);
            Assert.Equal(new[] { "eng", "ger" }, settings.KeptLanguages().ToArray());
            Assert.Equal(10, settings.HashDownloadsPerDay);
            File.WriteAllText(Path.Combine(folder, SubRemoverSettings.FileName), "{\"HashDownloadsPerDay\":3}");
            Assert.Equal(3, SubRemoverSettings.Read(folder, new TestJsonSerializer()).HashDownloadsPerDay);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Theory]
    [InlineData("{\"DownloadHashMatchedSubtitles\":false,\"OptionsVersion\":1}", true)]
    [InlineData("{\"DownloadHashMatchedSubtitles\":false,\"OptionsVersion\":2}", false)]
    [InlineData("{\"OptionsVersion\":2}", true)]
    public void Follows_SubRemovers_switch_as_SubRemover_reads_it(string json, bool on)
    {
        var folder = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, SubRemoverSettings.FileName), json);
            Assert.Equal(on, SubRemoverSettings.Read(folder, new TestJsonSerializer()).DownloadsHashMatched);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Text_subtitles_count_and_pictures_dont()
    {
        Assert.True(SubRemoverSettings.IsTextSubtitle("subtitle", "subrip"));
        Assert.False(SubRemoverSettings.IsTextSubtitle("subtitle", "hdmv_pgs_subtitle"));
        Assert.False(SubRemoverSettings.IsTextSubtitle("audio", "subrip"));
    }

    [Fact]
    public void Finds_the_languages_of_subtitle_files_next_to_the_video()
    {
        var folder = NewFolder();
        try
        {
            var video = Path.Combine(folder, "My Movie (2020).mkv");
            foreach (var f in new[] { "My Movie (2020).mkv", "My Movie (2020).en.srt", "My Movie (2020).ger.forced.srt", "My Movie (2020).nfo", "Other.fre.srt" })
                File.WriteAllText(Path.Combine(folder, f), "");
            var languages = MediaProcessor.SubtitleFileLanguages(video).OrderBy(l => l).ToArray();
            Assert.Equal(new[] { "eng", "ger" }, languages);
            Assert.Equal(new[] { "spa" }, HashMatchedSubtitles.LanguagesToSearch(new[] { "eng", "spa" }, new string[0], languages));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "audioremover-hash-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
