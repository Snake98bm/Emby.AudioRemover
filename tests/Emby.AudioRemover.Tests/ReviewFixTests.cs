using Emby.AudioRemover.Core;
using MediaBrowser.Model.Logging;
using Xunit;
using static Emby.AudioRemover.Tests.TestData;

namespace Emby.AudioRemover.Tests;

public sealed class ReviewFixTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("audioremover-review-").FullName;

    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteFile(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void A_video_subremover_is_rewriting_right_now_is_left_alone()
    {
        var video = WriteFile("My Movie (2020).mkv", "video");
        Assert.False(MediaProcessor.OtherToolIsWorkingOn(video));

        var temp = WriteFile("My Movie (2020).mkv.subremover-tmp", "half a remux");
        Assert.True(MediaProcessor.OtherToolIsWorkingOn(video));

        // Left behind by a crash: no longer written to, so it doesn't block the video for good.
        File.SetLastWriteTimeUtc(temp, DateTime.UtcNow.AddHours(-1));
        Assert.False(MediaProcessor.OtherToolIsWorkingOn(video));
    }

    // ---- 1. ffmpeg cut short must be noticed

    [Theory]
    [InlineData("[matroska,webm @ 0x1] File ended prematurely", true)]
    [InlineData("av_interleaved_write_frame(): I/O error", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Ffmpeg_truncation_messages_are_recognised(string stderr, bool truncated)
    {
        Assert.Equal(truncated, OutputChecks.TruncationMessage(stderr) != null);
    }

    // ---- 2. a shortened video must not pass

    [Fact]
    public void Two_hour_movie_missing_the_last_minute_fails_verification()
    {
        var before = Movie(Audio(1, "eng"), Audio(2, "fre"));
        before.format.duration = "7200";
        var cut = Movie(Audio(1, "eng"));
        cut.format.duration = "7140";
        var fine = Movie(Audio(1, "eng"));
        fine.format.duration = "7198.5";

        Assert.Contains("duration changed", RemuxVerifier.Verify(before, cut, 1, 1000, 990));
        Assert.Null(RemuxVerifier.Verify(before, fine, 1, 1000, 990));
    }

    // ---- 3. state saved safely, one task at a time

    [Fact]
    public void State_round_trips_and_is_replaced_without_temp_leftovers()
    {
        var store = new StateStore(_dir, new TestJsonSerializer());
        var state = new AudioRemoverState { InProgress = "/m/a.mkv" };
        state.MarkRestored("/m/b.mkv");
        store.Save(state);
        state.Backups.Add(new BackupEntry { OriginalPath = "/m/c.mkv", BackupPath = "/m/c.mkv.audioremover.bak" });
        store.Save(state);

        var loaded = new StateStore(_dir, new TestJsonSerializer()).Load();

        Assert.Equal("/m/a.mkv", loaded.InProgress);
        Assert.True(loaded.IsRestored("/M/B.MKV"));
        Assert.Single(loaded.Backups);
        Assert.Equal(new[] { "Emby.AudioRemover.state.json" }, Directory.GetFiles(_dir).Select(Path.GetFileName));
    }

    [Fact]
    public void Only_one_task_can_hold_the_gate()
    {
        Assert.True(TaskGate.TryEnter());
        try
        {
            Assert.False(TaskGate.TryEnter());
        }
        finally
        {
            TaskGate.Exit();
        }
        Assert.True(TaskGate.TryEnter());
        TaskGate.Exit();
    }

    // ---- 4. restored videos stay restored

    [Fact]
    public void Settings_change_rechecks_videos_but_keeps_restored_ones_alone()
    {
        var state = new AudioRemoverState { SettingsSignature = "old" };
        state.Processed["/m/a.mkv"] = new FileStamp();
        state.MarkRestored("/m/b.mkv");

        Assert.True(state.ApplySettingsSignature("new"));
        Assert.Empty(state.Processed);
        Assert.True(state.IsRestored("/m/b.mkv"));
        Assert.False(state.ApplySettingsSignature("new"));

        state.ForgetEverything();
        Assert.False(state.IsRestored("/m/b.mkv"));
    }

    [Fact]
    public void Test_mode_sees_what_a_real_run_would_check_without_changing_the_state()
    {
        var state = new AudioRemoverState { SettingsSignature = "old" };
        state.Processed["/m/a.mkv"] = new FileStamp();
        state.MarkRestored("/m/b.mkv");

        var sameSettings = state.PreviewFor("old", recheckEverything: false);
        Assert.Single(sameSettings.Processed);
        Assert.True(sameSettings.IsRestored("/m/b.mkv"));

        var newSettings = state.PreviewFor("new", recheckEverything: false);
        Assert.Empty(newSettings.Processed);
        Assert.True(newSettings.IsRestored("/m/b.mkv"));

        var recheck = state.PreviewFor("old", recheckEverything: true);
        Assert.Empty(recheck.Processed);
        Assert.False(recheck.IsRestored("/m/b.mkv"));

        Assert.Equal("old", state.SettingsSignature);
        Assert.Single(state.Processed);
        Assert.True(state.IsRestored("/m/b.mkv"));
    }

    // ---- 5. display-only data never reaches the settings file

    [Fact]
    public void Settings_page_copy_and_saves_never_write_display_data()
    {
        var store = new OptionsStore(_dir, "AudioRemover", new TestJsonSerializer(), new NullLogger());
        var forPage = store.GetCopy();
        forPage.LogText = "a very big log text";
        forPage.LanguageChoices.Add(new Emby.Web.GenericEdit.Common.EditorSelectOption { Value = "eng", Name = "English (eng)" });
        forPage.KeepLanguages = "eng,spa";

        Assert.Null(store.Get().LogText);

        store.Save(forPage);

        var json = File.ReadAllText(store.FilePath);
        Assert.DoesNotContain("a very big log text", json);
        Assert.DoesNotContain("English (eng)", json);
        Assert.Equal("eng,spa", new OptionsStore(_dir, "AudioRemover", new TestJsonSerializer(), new NullLogger()).Get().KeepLanguages);
        Assert.Equal("a very big log text", forPage.LogText);
    }

    [Fact]
    public void Display_data_already_in_an_old_settings_file_is_dropped_on_load()
    {
        var old = new PluginOptions { LogText = "leaked log", KeepLanguages = "eng" };
        File.WriteAllText(Path.Combine(_dir, "AudioRemover.json"), new TestJsonSerializer().SerializeToString(old));

        var loaded = new OptionsStore(_dir, "AudioRemover", new TestJsonSerializer(), new NullLogger()).Get();

        Assert.Null(loaded.LogText);
        Assert.Equal("eng", loaded.KeepLanguages);
    }

    // ---- 6. leftovers from an interrupted run are cleaned up

    [Fact]
    public void Leftover_temp_files_for_the_interrupted_video_are_deleted()
    {
        var video = WriteFile("Movie (2020).mkv", "video");
        WriteFile("Movie (2020).mkv.audioremover-tmp", "half a remux");
        WriteFile("Movie (2020).eng.srt.audioremover-tmp", "half a subtitle");
        WriteFile("Movie (2020).mkv.audioremover-restore", "half a restore");
        WriteFile("Movie (2020).eng.srt", "keep me");
        WriteFile("Movie (2020).mkv.audioremover.bak", "backup, keep me");
        WriteFile("Other (2021).mkv.audioremover-tmp", "not this video");

        var deleted = Leftovers.Clean(video).Select(Path.GetFileName).OrderBy(n => n).ToList();

        Assert.Equal(new[] { "Movie (2020).eng.srt.audioremover-tmp", "Movie (2020).mkv.audioremover-restore", "Movie (2020).mkv.audioremover-tmp" }, deleted);
        Assert.True(File.Exists(video));
        Assert.True(File.Exists(Path.Combine(_dir, "Movie (2020).eng.srt")));
        Assert.True(File.Exists(Path.Combine(_dir, "Movie (2020).mkv.audioremover.bak")));
        Assert.True(File.Exists(Path.Combine(_dir, "Other (2021).mkv.audioremover-tmp")));
    }
}
