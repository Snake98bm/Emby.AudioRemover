using System.Text.Json;
using Emby.AudioRemover.Core;

namespace Emby.AudioRemover.Tests;

internal static class TestData
{
    public static ProbeStream Audio(int index, string lang, string codec = "ac3", bool isDefault = false, string title = null,
        bool commentary = false, bool description = false, int channels = 6)
    {
        var tags = new Dictionary<string, string>();
        if (lang != null) tags["language"] = lang;
        if (title != null) tags["title"] = title;
        return new ProbeStream
        {
            index = index,
            codec_type = "audio",
            codec_name = codec,
            channels = channels,
            tags = tags,
            disposition = new Dictionary<string, int>
            {
                ["default"] = isDefault ? 1 : 0, ["comment"] = commentary ? 1 : 0, ["visual_impaired"] = description ? 1 : 0
            }
        };
    }

    /// <summary>A video stream, the given audio tracks, then one English subtitle.</summary>
    public static ProbeResult Movie(params ProbeStream[] audio)
    {
        var result = new ProbeResult { format = new ProbeFormat { duration = "5400.000000" } };
        result.streams.Add(new ProbeStream { index = 0, codec_type = "video", codec_name = "h264" });
        result.streams.AddRange(audio);
        result.streams.Add(new ProbeStream
        {
            index = audio.Length + 1, codec_type = "subtitle", codec_name = "subrip",
            tags = new Dictionary<string, string> { ["language"] = "eng" }
        });
        return result;
    }

    public static ProbeResult Parse(string ffprobeJson) => JsonSerializer.Deserialize<ProbeResult>(ffprobeJson);
}
