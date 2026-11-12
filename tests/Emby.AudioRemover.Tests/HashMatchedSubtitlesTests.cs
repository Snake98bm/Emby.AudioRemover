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

    [Fact]
    public void Reads_which_videos_SubRemover_has_checked()
    {
        var folder = NewFolder();
        try
        {
            // SubRemover hasn't run yet: nothing is checked, so every video waits.
            var none = SubRemoverChecks.Read(folder, new TestJsonSerializer());
            Assert.False(none.Unreadable);
            Assert.False(none.HasChecked(@"D:\Movies\My Movie (2020)\My Movie (2020).mkv"));

            File.WriteAllText(Path.Combine(folder, SubRemoverChecks.FileName),
                "{\"Processed\":{\"D:\\\\Movies\\\\My Movie (2020)\\\\My Movie (2020).mkv\":{\"Size\":5,\"LastWriteTicksUtc\":1}}," +
                "\"Backups\":[{\"OriginalPath\":\"D:\\\\Shows\\\\My Show\\\\S01E01.mkv\",\"BackupPath\":\"x\"}]," +
                "\"Restored\":[\"D:\\\\Movies\\\\Other (2021)\\\\Other (2021).mkv\"],\"MadeFiles\":[],\"AdRulesSignature\":\"a\"}");
            var checks = SubRemoverChecks.Read(folder, new TestJsonSerializer());
            Assert.Equal(3, checks.Count);
            Assert.True(checks.HasChecked(@"d:\movies\my movie (2020)\my movie (2020).mkv"));
            Assert.True(checks.HasChecked(@"D:\Shows\My Show\S01E01.mkv"));
            Assert.True(checks.HasChecked(@"D:\Movies\Other (2021)\Other (2021).mkv"));
            Assert.False(checks.HasChecked(@"D:\Shows\My Show\S01E02.mkv"));

            File.WriteAllText(Path.Combine(folder, SubRemoverChecks.FileName), "{ broken");
            var broken = SubRemoverChecks.Read(folder, new TestJsonSerializer());
            Assert.True(broken.Unreadable);
            Assert.False(broken.HasChecked(@"D:\Shows\My Show\S01E01.mkv"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void SubRemover_installed_without_a_settings_file_means_its_defaults()
    {
        var folder = NewFolder();
        try
        {
            Assert.Null(SubRemoverSettings.Read(folder, new TestJsonSerializer(), subRemoverLoaded: false));
            var settings = SubRemoverSettings.Read(folder, new TestJsonSerializer(), subRemoverLoaded: true);
            Assert.True(settings.DownloadsHashMatched);
            Assert.False(settings.Unreadable);
            Assert.Equal(new[] { "eng" }, settings.KeptLanguages().ToArray());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Settings_that_cant_be_read_are_marked_so_videos_wait()
    {
        var folder = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, SubRemoverSettings.FileName), "{ not json");
            Assert.True(SubRemoverSettings.Read(folder, new TestJsonSerializer(), subRemoverLoaded: true).Unreadable);
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
