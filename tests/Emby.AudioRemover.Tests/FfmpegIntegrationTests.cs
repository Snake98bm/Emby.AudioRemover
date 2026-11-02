using Emby.AudioRemover.Core;
using Xunit;

namespace Emby.AudioRemover.Tests;

// Runs real ffmpeg on a tiny generated video. Uses Emby's ffmpeg copied into tools/ffmpeg (or the folder in
// the AUDIOREMOVER_FFMPEG_DIR environment variable); skipped when it isn't there.
public sealed class FfmpegIntegrationTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("audioremover-ffmpeg-").FullName;
    private readonly FfmpegRunner _runner;

    public FfmpegIntegrationTests()
    {
        var folder = FindFfmpegFolder();
        if (folder == null) return;
        var exe = OperatingSystem.IsWindows() ? ".exe" : "";
        _runner = new FfmpegRunner(() => Path.Combine(folder, "ffmpeg" + exe), () => Path.Combine(folder, "ffprobe" + exe));
    }

    public void Dispose() => Directory.Delete(_dir, true);

    private static string FindFfmpegFolder()
    {
        var exe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        var fromEnv = Environment.GetEnvironmentVariable("AUDIOREMOVER_FFMPEG_DIR");
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(Path.Combine(fromEnv, exe))) return fromEnv;

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "ffmpeg");
            if (File.Exists(Path.Combine(candidate, exe))) return candidate;
        }
        return null;
    }

    private void RequireFfmpeg()
    {
        if (_runner == null) Assert.Skip("Emby's ffmpeg not found in tools/ffmpeg (or AUDIOREMOVER_FFMPEG_DIR)");
    }

    private async Task<ProbeResult> Probe(string path)
    {
        var result = await _runner.RunFfprobe(FfmpegArgs.Probe(path), TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.StdErr);
        return TestData.Parse(result.StdOut);
    }

    // 3-second video with four audio tracks - French (default), English, English commentary, and one with no
    // language - plus an English subtitle (MKV) so we can check it survives.
    private async Task<string> MakeSampleVideo(string name, bool withSubtitle = true)
    {
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-v", "error", "-y",
            "-f", "lavfi", "-i", "testsrc=duration=3:size=160x120:rate=10"
        };
        for (var i = 0; i < 4; i++) args.AddRange(new[] { "-f", "lavfi", "-i", $"sine=frequency={300 + i * 100}:duration=3" });
        if (withSubtitle)
        {
            var srt = Path.Combine(_dir, "sub.srt");
            File.WriteAllText(srt, "1\n00:00:00,500 --> 00:00:02,000\nHello\n\n");
            args.AddRange(new[] { "-i", srt });
        }

        args.AddRange(new[] { "-map", "0:v", "-map", "1:a", "-map", "2:a", "-map", "3:a", "-map", "4:a" });
        if (withSubtitle) args.AddRange(new[] { "-map", "5:s", "-c:s", "srt", "-metadata:s:s:0", "language=eng" });
        args.AddRange(new[]
        {
            "-c:v", "mpeg4", "-c:a", "aac",
            "-metadata:s:a:0", "language=fre", "-metadata:s:a:0", "title=Français",
            "-metadata:s:a:1", "language=eng",
            "-metadata:s:a:2", "language=eng", "-metadata:s:a:2", "title=Commentary",
            "-disposition:a:0", "default", "-disposition:a:1", "0", "-disposition:a:2", "comment", "-disposition:a:3", "0"
        });

        var output = Path.Combine(_dir, name);
        args.Add(output);

        var result = await _runner.RunFfmpeg(args, TestContext.Current.CancellationToken);
        Assert.True(result.Success, result.StdErr);
        return output;
    }

    [Theory]
    [InlineData("Sample Movie (2020).mkv", "matroska")]
    [InlineData("Sample Movie (2020).mp4", "mp4")]
    public async Task Removes_foreign_audio_moves_the_default_and_passes_verification(string name, string muxer)
    {
        RequireFfmpeg();
        var video = await MakeSampleVideo(name, withSubtitle: muxer == "matroska");
        var before = await Probe(video);
        Assert.Equal(4, before.Count("audio"));

        var settings = new PlannerSettings { KeepLanguages = PlannerSettings.ParseLanguages("eng"), RemoveCommentary = true };
        var plan = AudioPlanner.Plan(before, settings, video);
        Assert.Equal(new[] { 1, 3 }, plan.Remove.Select(s => s.index));
        Assert.Equal(2, plan.NewDefault.index);

        var temp = video + ".audioremover-tmp";
        var removed = plan.Remove.Select(s => s.index).ToList();
        var remux = await _runner.RunFfmpeg(FfmpegArgs.Remux(video, temp, muxer, removed, plan.DefaultChanges()), TestContext.Current.CancellationToken);
        Assert.True(remux.Success, remux.StdErr);

        var after = await Probe(temp);
        Assert.Null(RemuxVerifier.Verify(before, after, plan.Remove.Count, new FileInfo(video).Length, new FileInfo(temp).Length));
        Assert.Null(RemuxVerifier.CheckDefault(after, FfmpegArgs.OutputIndex(plan.NewDefault.index, removed)));

        var audio = after.streams.Where(s => s.IsAudio).ToList();
        Assert.Equal(new[] { "eng", "und" }, audio.Select(s => s.Language));
        Assert.True(audio[0].IsDefault);

        var swap = BackupManager.Replace(video, temp, BackupMode.NextToOriginal, null);
        Assert.True(swap.Success, swap.Message);
        Assert.Equal(2, (await Probe(video)).Count("audio"));
        Assert.Equal(4, (await Probe(video + BackupPaths.SideBySideSuffix)).Count("audio"));
    }

    [Fact]
    public async Task Reads_audio_flags_and_titles_from_ffprobe()
    {
        RequireFfmpeg();
        var audio = (await Probe(await MakeSampleVideo("Flags.mkv"))).streams.Where(s => s.IsAudio).ToList();

        Assert.Equal("Français", audio[0].Title);
        Assert.True(audio[0].IsDefault);
        Assert.True(audio[2].IsCommentary);
        Assert.Equal("und", audio[3].Language);
        Assert.Equal(1, audio[1].channels);
    }

    [Fact]
    public async Task A_video_cut_off_mid_read_is_caught_even_though_ffmpeg_reports_success()
    {
        RequireFfmpeg();
        var ct = TestContext.Current.CancellationToken;
        var full = Path.Combine(_dir, "Full.mkv");
        var make = await _runner.RunFfmpeg(new List<string>
        {
            "-hide_banner", "-nostdin", "-v", "error", "-y",
            "-f", "lavfi", "-i", "testsrc=duration=21:size=320x240:rate=10", "-f", "lavfi", "-i", "sine=duration=21",
            "-f", "lavfi", "-i", "sine=frequency=600:duration=21",
            "-map", "0:v", "-map", "1:a", "-map", "2:a", "-c:v", "mpeg4", "-q:v", "3", "-c:a", "aac",
            "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=fre", full
        }, ct);
        Assert.True(make.Success, make.StdErr);

        // Same as a network drop: the reader only gets the first half of the file.
        var cut = Path.Combine(_dir, "Cut.mkv");
        var bytes = File.ReadAllBytes(full);
        File.WriteAllBytes(cut, bytes.Take(bytes.Length / 2).ToArray());
        var before = await Probe(full);

        var remuxTo = Path.Combine(_dir, "Cut.mkv.audioremover-tmp");
        var remux = await _runner.RunFfmpeg(FfmpegArgs.Remux(cut, remuxTo, "matroska", new[] { 2 }), ct);
        var problem = OutputChecks.TruncationMessage(remux.StdErr)
                      ?? RemuxVerifier.Verify(before, await Probe(remuxTo), 1, bytes.Length, new FileInfo(remuxTo).Length);
        Assert.NotNull(problem);
    }

    [Fact]
    public async Task Handles_paths_with_spaces_quotes_and_unicode()
    {
        RequireFfmpeg();
        var video = await MakeSampleVideo("Zoë's \"Café\" Night (2020).mkv".Replace("\"", OperatingSystem.IsWindows() ? "" : "\""));
        var probe = await Probe(video);
        Assert.Equal(1, probe.Count("video"));
        Assert.Equal(4, probe.Count("audio"));
    }
}
