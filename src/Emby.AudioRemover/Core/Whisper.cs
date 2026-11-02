using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.AudioRemover.Core
{
    /// <summary>A file AudioRemover downloads for listening: from one fixed address, and kept only with this exact checksum.</summary>
    public sealed class PinnedDownload
    {
        public PinnedDownload(string url, long size, string sha256)
        {
            Url = url;
            Size = size;
            Sha256 = sha256;
        }

        public string Url { get; }
        public long Size { get; }
        public string Sha256 { get; }
        public string FileName => Path.GetFileName(new Uri(Url).AbsolutePath);
    }

    /// <summary>One size of the Whisper speech model, for the settings page's list.</summary>
    public sealed class WhisperModelInfo
    {
        public string Name { get; set; }
        public string Note { get; set; }
        public PinnedDownload File { get; set; }

        /// <summary>When the file last changed on Hugging Face (yyyy-MM-dd): the models have no version numbers.</summary>
        public string Date { get; set; }

        /// <summary>"Base - 141 MB (recommended)".</summary>
        public string Label => $"{Name} - {WhisperFiles.SizeText(File.Size)}{Note}";
    }

    /// <summary>
    /// whisper.cpp (an offline, open-source version of OpenAI's Whisper) and its models. Every address and checksum is
    /// fixed here: a file that doesn't match is thrown away, so nothing else can ever be run in its place.
    /// </summary>
    public static class WhisperFiles
    {
        // A newer whisper.cpp only means a new version, build and checksums here; servers with the old one get the new one
        // the next time Whisper is needed. The models don't change.
        public const string ProgramVersion = "1.9.5";
        public const string ProgramDate = "2026-10-06";

        // whisper-cli.exe from that version's x64 download: recognises a copy an earlier AudioRemover unpacked before it wrote
        // the version down.
        internal const string ProgramExeSha256 = "331dba46d6427105d2b802cdbc7eae916ea1c5abf9b0d3b5cfe460d8db8e4366";
        private const string ModelsDate = "2023-03-22";
        private const string Release = "https://github.com/ggml-org/whisper.cpp/releases/download/b5454/";
        private const string Models = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/";

        public const string DefaultModel = "Base";

        public static readonly IReadOnlyList<WhisperModelInfo> All = new[]
        {
            new WhisperModelInfo
            {
                Name = "Tiny", Note = ", fastest, least sure", Date = ModelsDate,
                File = new PinnedDownload(Models + "ggml-tiny.bin", 77691713, "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21")
            },
            new WhisperModelInfo
            {
                Name = "Base", Note = " (recommended)", Date = ModelsDate,
                File = new PinnedDownload(Models + "ggml-base.bin", 147951465, "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe")
            },
            new WhisperModelInfo
            {
                Name = "Small", Note = ", more accurate, slower", Date = ModelsDate,
                File = new PinnedDownload(Models + "ggml-small.bin", 487601967, "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b")
            },
            new WhisperModelInfo
            {
                Name = "Medium", Note = ", slowest", Date = ModelsDate,
                File = new PinnedDownload(Models + "ggml-medium.bin", 1533763059, "6c14d5adee5f86394037b4e4e8b59f1673b6cee10e3cf0b11bbdbee79c156208")
            }
        };

        public static string SizeText(long bytes) =>
            bytes >= 1L << 30
                ? (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB"
                : Math.Round(bytes / 1048576.0).ToString(CultureInfo.InvariantCulture) + " MB";

        /// <summary>The chosen model, or Base for a name that isn't on the list.</summary>
        public static WhisperModelInfo Model(string name) =>
            All.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All.First(m => m.Name == DefaultModel);

        /// <summary>The model for the 10-clip check: one size up from the chosen one, a second, more accurate ear (Medium checks with itself).</summary>
        public static WhisperModelInfo CheckModelFor(string name)
        {
            var index = All.ToList().IndexOf(Model(name));
            return All[Math.Min(index + 1, All.Count - 1)];
        }

        /// <summary>The ready-made whisper.cpp for this server, or null where there isn't one.</summary>
        public static PinnedDownload ProgramForThisServer()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return null;
            switch (RuntimeInformation.OSArchitecture)
            {
                case Architecture.X64:
                    return new PinnedDownload(Release + "whisper-bin-x64.zip", 8928640, "6ba69e3482d7826214f90a6a9c84ca07782aec1e1d0c6a7c30c994fd5d816ccb");
                case Architecture.Arm64:
                    return new PinnedDownload(Release + "whisper-bin-win-cpu-arm64.zip", 4371193, "28c37e7b598c3d9bbfef94f3bd67f2ee6f12b7c86f3da5edcf5e4308d615ff6d");
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// The folder with whisper.cpp and the model, downloaded on first use. One per server: the settings page shows the
    /// same download a run waits for.
    /// </summary>
    public sealed class WhisperInstall
    {
        // Up to the answer's headers only; the download itself is given up on when it stops coming in (StallTimeout), so a
        // slow link still gets a big model in, and a dead one doesn't hold every later run up.
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        private static readonly TimeSpan StallTimeout = TimeSpan.FromMinutes(2);
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public WhisperInstall(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        public string ProgramPath => Path.Combine(Folder, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "whisper-cli.exe" : "whisper-cli");

        public string ModelPath(string model) => Path.Combine(Folder, WhisperFiles.Model(model).File.FileName);

        public bool IsReady(string model) => File.Exists(ProgramPath) && !IsOldDownload && File.Exists(ModelPath(model)) && !IsOldModel(model);

        /// <summary>The day the model was downloaded (or put here), or null when it isn't here.</summary>
        public DateTime? ModelDownloaded(string model) => File.Exists(ModelPath(model)) ? File.GetLastWriteTime(ModelPath(model)) : (DateTime?)null;

        // The checksum the model was downloaded with, next to it. A model put here by hand has none and is left alone.
        private string ModelChecksumFile(string model) => ModelPath(model) + ".sha256";

        /// <summary>A model file downloaded for an older AudioRemover that expects a different one: replaced the next time it's needed.</summary>
        public bool IsOldModel(string model)
        {
            try
            {
                var file = ModelChecksumFile(model);
                return File.Exists(file) && !string.Equals(File.ReadAllText(file).Trim(), WhisperFiles.Model(model).File.Sha256, StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return false;
            }
        }

        // Written next to the program it came with. A whisper-cli someone put here by hand has none and is left alone.
        private string VersionFile => Path.Combine(Folder, "whisper.cpp-version.txt");

        /// <summary>The whisper.cpp version AudioRemover downloaded here; null when it didn't (nothing, or put here by hand).</summary>
        public string InstalledVersion
        {
            get
            {
                try
                {
                    if (File.Exists(VersionFile)) return File.ReadAllText(VersionFile).Trim();
                    if (!File.Exists(ProgramPath) || !IsPinnedProgram(ProgramPath)) return null;

                    // Unpacked by an AudioRemover from before the version was written down: recognised, and written down now.
                    File.WriteAllText(VersionFile, WhisperFiles.ProgramVersion);
                    return WhisperFiles.ProgramVersion;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    return null;
                }
            }
        }

        /// <summary>Downloaded by an older AudioRemover that used an older whisper.cpp: replaced the next time Whisper is needed.</summary>
        public bool IsOldDownload => InstalledVersion != null && InstalledVersion != WhisperFiles.ProgramVersion;

        /// <summary>The newest whisper.cpp version on GitHub, once checked; null before that or when the check failed.</summary>
        public string LatestVersion { get; internal set; }

        /// <summary>When GitHub or Hugging Face last answered a check; null before either has.</summary>
        public DateTime? AnsweredUtc { get; internal set; }

        internal void RecordModelCheck(string model, bool unchanged)
        {
            lock (_modelUnchanged)
            {
                _modelUnchanged[WhisperFiles.Model(model).Name] = unchanged;
            }
            AnsweredUtc = DateTime.UtcNow;
        }

        private DateTime _checkedUtc = DateTime.MinValue;

        // Hugging Face answers a HEAD request with the file's checksum, but only before it redirects to the download.
        private static readonly HttpClient Checks = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(30) };

        private readonly Dictionary<string, bool> _modelUnchanged = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the model file on Hugging Face still has the checksum AudioRemover expects: true, false, or null before
        /// it's been checked (or when the check failed).
        /// </summary>
        public bool? ModelUnchanged(string model)
        {
            lock (_modelUnchanged)
            {
                return _modelUnchanged.TryGetValue(WhisperFiles.Model(model).Name, out var unchanged) ? unchanged : (bool?)null;
            }
        }

        /// <summary>
        /// Asks GitHub for the newest whisper.cpp, and Hugging Face for the model's checksum, at most once a day (and for a
        /// newly chosen model), without waiting for the answers.
        /// </summary>
        public void CheckForNewerDaily(DateTime nowUtc, string model)
        {
            var (program, modelFile) = DueChecks(nowUtc, model);
            if (program) _ = CheckForNewer();
            if (modelFile) _ = CheckModel(model);
        }

        private readonly Dictionary<string, DateTime> _modelCheckedUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Which checks are due, marking them done: each once a day, counted from when it was tried, so one that fails
        /// (offline, or no answer) isn't sent again on every page open.
        /// </summary>
        internal (bool Program, bool Model) DueChecks(DateTime nowUtc, string model)
        {
            var name = WhisperFiles.Model(model).Name;
            lock (_modelUnchanged)
            {
                var program = nowUtc - _checkedUtc >= TimeSpan.FromDays(1);
                if (program) _checkedUtc = nowUtc;
                var modelDue = !_modelCheckedUtc.TryGetValue(name, out var at) || nowUtc - at >= TimeSpan.FromDays(1);
                if (modelDue) _modelCheckedUtc[name] = nowUtc;
                return (program, modelDue);
            }
        }

        private async Task CheckModel(string model)
        {
            var info = WhisperFiles.Model(model);
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Head, info.File.Url))
                using (var response = await Checks.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.Headers.TryGetValues("X-Linked-ETag", out var values)) return;
                    var current = values.First().Trim().Trim('"');
                    RecordModelCheck(info.Name, string.Equals(current, info.File.Sha256, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception)
            {
                // Only a nice-to-know, like the version check.
            }
        }

        private async Task CheckForNewer()
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/ggml-org/whisper.cpp/releases/latest"))
                {
                    // GitHub refuses requests without one.
                    request.Headers.UserAgent.ParseAdd("Emby.AudioRemover");
                    using (var response = await Http.SendAsync(request).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode) return;
                        LatestVersion = ReleaseVersion(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                        AnsweredUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception)
            {
                // Only a nice-to-know; the page just doesn't mention newer versions.
            }
        }

        private static readonly Regex TagName = new Regex(@"""tag_name""\s*:\s*""v?([0-9]+(?:\.[0-9]+)+)""", RegexOptions.CultureInvariant);

        /// <summary>"1.9.6" from GitHub's answer about a release; null when it isn't a plain version.</summary>
        internal static string ReleaseVersion(string json)
        {
            var m = TagName.Match(json ?? string.Empty);
            return m.Success ? m.Groups[1].Value : null;
        }

        internal static bool IsNewer(string candidate, string current) =>
            Version.TryParse(candidate, out var a) && Version.TryParse(current, out var b) && a > b;

        /// <summary>"ggml-base.bin: 45%" while a download runs; null otherwise.</summary>
        public string Downloading { get; private set; }

        /// <summary>Why the last download failed; null after one that worked.</summary>
        public string LastProblem { get; private set; }

        /// <summary>Downloads whatever is missing. Waits for a download that's already running.</summary>
        /// <returns>null when Whisper is ready, otherwise why not.</returns>
        public async Task<string> Ensure(string model, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!File.Exists(ProgramPath) || IsOldDownload)
                {
                    var program = WhisperFiles.ProgramForThisServer();
                    if (program == null)
                        return LastProblem = "there's no ready-made whisper.cpp for this system; put whisper-cli and " +
                                             WhisperFiles.Model(model).File.FileName + " in " + Folder + " yourself";
                    var zip = await Download(program, ct).ConfigureAwait(false);
                    try
                    {
                        ExtractProgram(zip, Folder);
                        File.WriteAllText(VersionFile, WhisperFiles.ProgramVersion);
                    }
                    finally
                    {
                        BackupManager.TryDelete(zip);
                    }
                }

                if (!File.Exists(ModelPath(model)) || IsOldModel(model))
                {
                    var pinned = WhisperFiles.Model(model).File;
                    var file = await Download(pinned, ct).ConfigureAwait(false);
                    BackupManager.TryDelete(ModelPath(model));
                    File.Move(file, ModelPath(model));
                    File.WriteAllText(ModelChecksumFile(model), pinned.Sha256);
                }
                return LastProblem = null;
            }
            // A timeout or a stalled download, not Stop: a problem to report, not a reason to end the run.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return LastProblem = "the download timed out or stopped coming in; it's tried again the next time Whisper is needed";
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                return LastProblem = ex.Message;
            }
            finally
            {
                Downloading = null;
                _lock.Release();
            }
        }

        /// <returns>The downloaded file, next to where it goes, once its checksum matched.</returns>
        private async Task<string> Download(PinnedDownload download, CancellationToken ct)
        {
            Directory.CreateDirectory(Folder);
            var temp = Path.Combine(Folder, download.FileName + ".download");
            Downloading = download.FileName + ": 0%";
            try
            {
                using (var response = await Http.GetAsync(download.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) throw new IOException($"download of {download.FileName} failed: HTTP {(int)response.StatusCode}");
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var target = File.Create(temp))
                    using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                    using (var stalled = CancellationTokenSource.CreateLinkedTokenSource(ct))
                    {
                        var buffer = new byte[81920];
                        long total = 0;
                        while (true)
                        {
                            // Restarted before every read: only a connection that stops sending for this long is given up on.
                            stalled.CancelAfter(StallTimeout);
                            var read = await source.ReadAsync(buffer, 0, buffer.Length, stalled.Token).ConfigureAwait(false);
                            if (read == 0) break;
                            await target.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                            hash.AppendData(buffer, 0, read);
                            total += read;
                            Downloading = $"{download.FileName}: {Math.Min(99, total * 100 / Math.Max(1, download.Size))}%";
                        }
                        if (!Matches(hash.GetHashAndReset(), download.Sha256))
                            throw new IOException($"{download.FileName} didn't match its checksum, so it was thrown away");
                    }
                }
                return temp;
            }
            catch
            {
                BackupManager.TryDelete(temp);
                throw;
            }
        }

        internal static bool IsPinnedProgram(string path)
        {
            using (var file = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                return Matches(sha.ComputeHash(file), WhisperFiles.ProgramExeSha256);
            }
        }

        internal static bool Matches(byte[] hash, string sha256) =>
            string.Equals(BitConverter.ToString(hash).Replace("-", ""), sha256, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Only the program and the libraries it loads, all into the folder itself: a name can't put a file anywhere else.
        /// The program goes last, so it's only there once everything it needs is.
        /// </summary>
        internal static List<string> ExtractProgram(string zipPath, string folder)
        {
            var written = new List<string>();
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries.Where(e => IsNeeded(Path.GetFileName(e.FullName))).OrderBy(e => IsProgram(e.Name)))
                {
                    var target = Path.Combine(folder, Path.GetFileName(entry.FullName));
                    entry.ExtractToFile(target, overwrite: true);
                    written.Add(target);
                }
            }
            return written;
        }

        private static bool IsProgram(string name) => name.Equals("whisper-cli.exe", StringComparison.OrdinalIgnoreCase);

        private static bool IsNeeded(string name) =>
            IsProgram(name)
            || name.Equals("whisper.dll", StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith("ggml", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>What Whisper heard on one audio track.</summary>
    public sealed class HeardLanguage
    {
        /// <summary>The language, when the clips agreed and were sure; null otherwise.</summary>
        public string Language { get; set; }

        public int Percent { get; set; }

        /// <summary>Each clip's answer ("en 98%"), for the log.</summary>
        public List<string> Clips { get; set; } = new List<string>();

        /// <summary>How many clips named the language, each sure enough: the vote.</summary>
        public int Agree { get; set; }

        /// <summary>The longer listen, when there was one: its clips, how many agreed, and how sure they were.</summary>
        public List<string> CheckClips { get; set; }

        public int CheckAgree { get; set; }
        public int CheckPercent { get; set; }

        /// <summary>The model that did the longer listen.</summary>
        public string CheckModel { get; set; }

        /// <summary>The quick listen couldn't tell, so the longer listen tried to name the language itself.</summary>
        public bool IdentifiedByCheck { get; set; }

        /// <summary>What the first listen heard, kept when the longer one didn't confirm it.</summary>
        public string FirstHeard { get; set; }

        public int FirstAgree { get; set; }

        /// <summary>For the plan lines: the language with its vote and confidence, e.g. "sounds eng: 3/3 clips, 99% sure".</summary>
        public string Describe()
        {
            if (IdentifiedByCheck)
                return Language != null
                    ? $"sounds {Language}: couldn't tell from {Clips.Count} clips, so the {CheckModel} model listened: {CheckAgree}/{CheckClips.Count}, {CheckPercent}% sure"
                    : $"couldn't tell, not even the {CheckModel} model ({CheckAgree}/{CheckClips.Count} clips agreed at most)";
            if (Language == null && FirstHeard != null)
                return $"not confirmed: heard {FirstHeard} in {FirstAgree}/{Clips.Count} clips, but only {CheckAgree}/{CheckClips.Count} more agreed " +
                       $"({CheckModel} model), so the tag stays";
            if (Language == null) return "couldn't tell";
            return $"sounds {Language}: {Agree}/{Clips.Count} clips, {Percent}% sure" +
                   (CheckClips != null ? $"; confirmed {CheckAgree}/{CheckClips.Count} by the {CheckModel} model, {CheckPercent}% sure" : "");
        }

        /// <summary>For the "listened to" line: the same, plus every clip's answer.</summary>
        public string Detail() =>
            Describe() + " [clips: " + string.Join(", ", Clips) + "]" +
            (CheckClips != null ? " [" + CheckClips.Count + " more: " + string.Join(", ", CheckClips) + "]" : "");
    }

    public static class LanguageVote
    {
        // Real speech scores 0.89-1.0; music, silence or a test tone still comes back as some language at 0.5-0.7.
        internal const double MinConfidence = 0.8;

        private static readonly Regex Answer = new Regex(@"auto-detected language: ([a-z]+) \(p = ([0-9.]+)\)", RegexOptions.CultureInvariant);

        /// <returns>The language code Whisper named ("en") and how sure it was, or null when its output has no answer.</returns>
        public static (string Code, double Confidence)? Parse(string whisperOutput)
        {
            var m = Answer.Match(whisperOutput ?? string.Empty);
            if (!m.Success || !double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return null;
            return (m.Groups[1].Value, p);
        }

        // A quick listen: 3 clips, 2 must agree. Before it changes a tag or what's kept: 10 more, 7 must agree.
        internal const int QuickClips = 3;
        internal const int QuickAgree = 2;
        internal const int ConfirmClips = 10;
        internal const int ConfirmAgree = 7;

        /// <summary>A language counts when at least <paramref name="minAgreeing"/> clips name it, each sure enough.</summary>
        public static HeardLanguage Decide(IEnumerable<(string Code, double Confidence)> clips, int minAgreeing = QuickAgree)
        {
            var list = clips.ToList();
            var heard = new HeardLanguage { Clips = Answers(list) };
            var winner = list.Where(c => c.Confidence >= MinConfidence)
                .GroupBy(c => Languages.Normalize(c.Code))
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            if (winner == null || winner.Count() < minAgreeing) return heard;

            heard.Language = winner.Key;
            heard.Agree = winner.Count();
            heard.Percent = Percent(winner);
            return heard;
        }

        // Overriding a tag decides what's removed, so it takes more: real speech scored 89-100%, music and noise never above
        // 73% - 90% leaves room on both sides.
        internal const double ConfirmConfidence = 0.9;

        /// <summary>The first answer, when the longer listen agrees with it; otherwise no answer, so the tag decides.</summary>
        public static HeardLanguage Confirm(HeardLanguage first, IList<(string Code, double Confidence)> longer, string checkModel)
        {
            var agreeing = longer.Where(c => c.Confidence >= ConfirmConfidence && Languages.Normalize(c.Code) == first.Language).ToList();
            var checkClips = Answers(longer);
            if (agreeing.Count >= ConfirmAgree)
            {
                first.CheckClips = checkClips;
                first.CheckAgree = agreeing.Count;
                first.CheckPercent = Percent(agreeing);
                first.CheckModel = checkModel;
                return first;
            }
            return new HeardLanguage
            {
                Clips = first.Clips,
                FirstHeard = first.Language,
                FirstAgree = first.Agree,
                CheckClips = checkClips,
                CheckAgree = agreeing.Count,
                CheckModel = checkModel
            };
        }

        /// <summary>
        /// When the quick listen couldn't tell: whatever language at least 7 of the longer listen's clips name, each 90% sure -
        /// the same bar as overriding a tag. Otherwise still no answer.
        /// </summary>
        public static HeardLanguage Identify(HeardLanguage first, IList<(string Code, double Confidence)> longer, string checkModel)
        {
            var winner = longer.Where(c => c.Confidence >= ConfirmConfidence)
                .GroupBy(c => Languages.Normalize(c.Code))
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            return new HeardLanguage
            {
                Language = winner != null && winner.Count() >= ConfirmAgree ? winner.Key : null,
                Percent = winner != null ? Percent(winner) : 0,
                Clips = first.Clips,
                CheckClips = Answers(longer),
                CheckAgree = winner?.Count() ?? 0,
                CheckPercent = winner != null ? Percent(winner) : 0,
                CheckModel = checkModel,
                IdentifiedByCheck = true
            };
        }

        private static List<string> Answers(IEnumerable<(string Code, double Confidence)> clips) =>
            clips.Select(c => $"{c.Code} {Math.Round(c.Confidence * 100)}%").ToList();

        private static int Percent(IEnumerable<(string Code, double Confidence)> clips) =>
            (int)Math.Round(clips.Average(c => c.Confidence) * 100);
    }

    /// <summary>
    /// Lets Whisper listen to 30-second clips of an audio track: three, from 20% to 80% of the way in, and ten more, from
    /// 10% to 90%, to confirm an answer before it's acted on.
    /// </summary>
    public sealed class LanguageListener
    {
        private const int ClipSeconds = 30;
        private readonly FfmpegRunner _runner;
        private readonly WhisperInstall _whisper;
        private readonly string _model;
        private readonly string _checkModel;

        /// <param name="model">The chosen model, for the quick listen.</param>
        /// <param name="checkModel">The model for the 10-clip check; the chosen one when null.</param>
        public LanguageListener(FfmpegRunner runner, WhisperInstall whisper, string model, string checkModel = null)
        {
            _runner = runner;
            _whisper = whisper;
            _model = WhisperFiles.Model(model).Name;
            _checkModel = checkModel != null ? WhisperFiles.Model(checkModel).Name : _model;
            ClearOldClips();
        }

        private const string ClipPattern = "audioremover-*.wav";
        private static DateTime _lastClear = DateTime.MinValue;

        // Clips a delete couldn't remove (still held after every try, or the server stopped mid-listen), at most once an hour.
        private static void ClearOldClips()
        {
            var now = DateTime.UtcNow;
            if (now - _lastClear < TimeSpan.FromHours(1)) return;
            _lastClear = now;
            try
            {
                foreach (var old in Directory.GetFiles(Path.GetTempPath(), ClipPattern).Where(f => File.GetLastWriteTimeUtc(f) < now.AddHours(-1)))
                    BackupManager.TryDelete(old);
            }
            catch (Exception)
            {
                // Temp folder unreadable: the clips just wait for the next try.
            }
        }

        // Security software often holds a file it has just seen written for a moment, so one delete isn't enough.
        private static async Task DeleteClip(string wav)
        {
            for (var attempt = 0; attempt < 5 && File.Exists(wav); attempt++)
            {
                if (attempt > 0) await Task.Delay(200).ConfigureAwait(false);
                BackupManager.TryDelete(wav);
            }
        }

        // Spread evenly between two points, so the opening music and the end credits don't decide it. Each point once: in a
        // short video, or one whose length ffprobe doesn't give (all 0), the same clip twice would be counted as agreeing
        // with itself, so there are just fewer clips - and too few to reach the vote means "couldn't tell".
        internal static IEnumerable<double> ClipStarts(double? durationSeconds, int count = LanguageVote.QuickClips, double from = 0.2, double to = 0.8)
        {
            var duration = durationSeconds ?? 0;
            return Enumerable.Range(0, count)
                .Select(i => from + (to - from) * i / Math.Max(1, count - 1))
                .Select(f => Math.Round(Math.Max(0, Math.Min(duration * f, duration - ClipSeconds)), 1))
                .Distinct();
        }

        /// <summary>The quick listen.</summary>
        public async Task<HeardLanguage> Listen(string videoPath, ProbeStream stream, double? durationSeconds, CancellationToken ct) =>
            LanguageVote.Decide(await Clips(videoPath, stream, ClipStarts(durationSeconds), _model, ct).ConfigureAwait(false));

        /// <summary>The longer listen, with the check model, when the quick one couldn't tell.</summary>
        public async Task<HeardLanguage> Identify(string videoPath, ProbeStream stream, double? durationSeconds, HeardLanguage first, CancellationToken ct) =>
            LanguageVote.Identify(first,
                await Clips(videoPath, stream, ClipStarts(durationSeconds, LanguageVote.ConfirmClips, 0.1, 0.9), _checkModel, ct).ConfigureAwait(false),
                _checkModel);

        /// <summary>The longer listen, with the check model, before a heard language changes a tag or what's kept.</summary>
        public async Task<HeardLanguage> Confirm(string videoPath, ProbeStream stream, double? durationSeconds, HeardLanguage first, CancellationToken ct) =>
            LanguageVote.Confirm(first,
                await Clips(videoPath, stream, ClipStarts(durationSeconds, LanguageVote.ConfirmClips, 0.1, 0.9), _checkModel, ct).ConfigureAwait(false),
                _checkModel);

        private async Task<List<(string, double)>> Clips(string videoPath, ProbeStream stream, IEnumerable<double> starts, string model, CancellationToken ct)
        {
            var clips = new List<(string, double)>();
            foreach (var start in starts)
            {
                var wav = Path.Combine(Path.GetTempPath(), "audioremover-" + Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    var cut = await _runner.RunFfmpeg(FfmpegArgs.AudioClip(videoPath, stream.index, start, ClipSeconds, wav), ct).ConfigureAwait(false);
                    if (!cut.Success || !File.Exists(wav)) continue;

                    var threads = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
                    ProcessResult heard;
                    try
                    {
                        heard = await _runner.RunProgram(_whisper.ProgramPath, new List<string>
                        {
                            "-m", _whisper.ModelPath(model), "-l", "auto", "-dl", "-t", threads.ToString(CultureInfo.InvariantCulture), "-f", wav
                        }, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        // Blocked by security software, or files missing: it won't work for any other track either.
                        throw new WhisperFailedException("whisper-cli couldn't start: " + ex.Message, ex);
                    }

                    var answer = LanguageVote.Parse(heard.StdErr + "\n" + heard.StdOut);
                    if (answer == null && !heard.Success)
                        throw new WhisperFailedException($"whisper-cli failed (exit {heard.ExitCode}): {LastLine(heard.StdErr)}");
                    if (answer != null) clips.Add(answer.Value);
                }
                finally
                {
                    await DeleteClip(wav).ConfigureAwait(false);
                }
            }
            return clips;
        }

        private static string LastLine(string text) =>
            (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "no output";
    }

    /// <summary>whisper-cli can't be run at all: listening isn't possible this run, so it's skipped rather than guessed.</summary>
    public sealed class WhisperFailedException : Exception
    {
        public WhisperFailedException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }
}
