using Emby.AudioRemover.Core;
using Xunit;
using static Emby.AudioRemover.Tests.TestData;

namespace Emby.AudioRemover.Tests;

public class RemuxVerifierTests
{
    private static readonly ProbeResult Before = Movie(Audio(1, "eng"), Audio(2, "fre"));

    [Fact]
    public void Accepts_a_correct_remux()
    {
        Assert.Null(RemuxVerifier.Verify(Before, Movie(Audio(1, "eng")), 1, 1000, 600));
    }

    [Fact]
    public void Rejects_the_wrong_audio_count()
    {
        Assert.Contains("audio", RemuxVerifier.Verify(Before, Movie(Audio(1, "eng"), Audio(2, "fre")), 1, 1000, 990));
        Assert.Contains("audio", RemuxVerifier.Verify(Before, Movie(), 1, 1000, 990));
    }

    [Fact]
    public void Never_accepts_a_file_without_audio()
    {
        Assert.Contains("no audio", RemuxVerifier.Verify(Before, Movie(), 2, 1000, 990));
    }

    [Fact]
    public void Rejects_a_lost_subtitle()
    {
        var after = Movie(Audio(1, "eng"));
        after.streams.RemoveAll(s => s.codec_type == "subtitle");
        Assert.Contains("subtitle", RemuxVerifier.Verify(Before, after, 1, 1000, 600));
    }

    [Fact]
    public void Rejects_a_truncated_file()
    {
        var after = Movie(Audio(1, "eng"));
        after.format.duration = "2700";
        Assert.Contains("duration", RemuxVerifier.Verify(Before, after, 1, 1000, 600));
        Assert.Contains("size", RemuxVerifier.Verify(Before, Movie(Audio(1, "eng")), 1, 1000, 90));
    }

    [Fact]
    public void Rejects_an_empty_probe()
    {
        Assert.NotNull(RemuxVerifier.Verify(Before, new ProbeResult(), 1, 1000, 990));
        Assert.NotNull(RemuxVerifier.Verify(Before, null, 1, 1000, 990));
    }

    [Fact]
    public void Checks_that_only_the_new_default_audio_is_flagged()
    {
        Assert.Null(RemuxVerifier.CheckDefault(Movie(Audio(1, "eng"), Audio(2, "eng", isDefault: true)), 2));
        Assert.Equal("expected only audio #2 to be default but found none", RemuxVerifier.CheckDefault(Movie(Audio(1, "eng"), Audio(2, "eng")), 2));
        Assert.Equal("expected only audio #2 to be default but found #1, #2",
            RemuxVerifier.CheckDefault(Movie(Audio(1, "eng", isDefault: true), Audio(2, "eng", isDefault: true)), 2));
    }
}
