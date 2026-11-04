using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Emby.AudioRemover.Core;
using Emby.Web.GenericEdit.Elements;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;

namespace Emby.AudioRemover
{
    // ------------------------------------------------------------------------------------
    // Administrator-only endpoints for the settings page (Configuration/settings.html).
    // ------------------------------------------------------------------------------------

    [Route("/AudioRemover/Page", "GET", Summary = "Everything the settings page shows", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class GetSettingsPage : IReturn<PageDto>
    {
    }

    [Route("/AudioRemover/Settings", "POST", Summary = "Saves the settings page", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class SaveSettings : IReturn<SaveResultDto>
    {
        /// <summary>The settings as a JSON object of name → value text.</summary>
        public string Json { get; set; }

        /// <summary>"Suggested text" as it is in the box, and as the page last showed it; null when the page didn't show it.</summary>
        public string SuggestedText { get; set; }
        public string SuggestedTextShown { get; set; }
    }

    [Route("/AudioRemover/Run", "POST", Summary = "Starts a run", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class StartRun : IReturn<StatusDto>
    {
    }

    [Route("/AudioRemover/Stop", "POST", Summary = "Stops the run in progress", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class StopRun : IReturn<StatusDto>
    {
    }

    [Route("/AudioRemover/Status", "GET", Summary = "The run status line", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class GetRunStatus : IReturn<StatusDto>
    {
    }

    [Route("/AudioRemover/Whisper", "GET", Summary = "The Whisper status line", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class GetWhisperStatus : IReturn<StatusDto>
    {
    }

    [Route("/AudioRemover/Whisper", "POST", Summary = "Downloads Whisper for the saved model", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class StartWhisperDownload : IReturn<StatusDto>
    {
    }

    [Route("/AudioRemover/TitleAds/Check", "POST", Summary = "Checks a 'Text to remove' list, and what it leaves of a sample name", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class CheckTitleAds : IReturn<StatusDto>
    {
        public string List { get; set; }
        public string Sample { get; set; }
    }

    [Route("/AudioRemover/Suggestions", "GET", Summary = "'Suggested text' and the search status", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class GetSuggestions : IReturn<SuggestionsDto>
    {
    }

    [Route("/AudioRemover/Suggestions/Find", "POST", Summary = "Looks through the saved libraries' track names for text to remove", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class FindSuggestions : IReturn<SuggestionsDto>
    {
    }

    [Route("/AudioRemover/Suggestions/Add", "POST", Summary = "Adds 'Suggested text' to 'Text to remove'", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class AddSuggestions : IReturn<SuggestionsDto>
    {
        public string SuggestedText { get; set; }
        public string SuggestedTextShown { get; set; }
    }

    [Route("/AudioRemover/Backups", "GET", Summary = "Videos with a backup, for the restore list", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class GetBackups : IReturn<BackupListDto>
    {
    }

    [Route("/AudioRemover/Restore", "POST", Summary = "Restores the selected videos from their backups", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class StartRestore : IReturn<StatusDto>
    {
        /// <summary>The list the selection was made on, as GetBackups returned it.</summary>
        public string Key { get; set; }

        /// <summary>The selected rows, e.g. "0,3,5".</summary>
        public string Indexes { get; set; }
    }

    [Route("/AudioRemover/LogView", "GET", Summary = "One run log, for the Logs tab", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class GetLogView : IReturn<LogViewDto>
    {
        public string Name { get; set; }
    }

    public class PageDto
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public List<TabDto> Tabs { get; set; }
        public Dictionary<string, string> Values { get; set; }
        public List<OptionDto> Libraries { get; set; }
        public List<OptionDto> Languages { get; set; }
        public List<OptionDto> WhisperModels { get; set; }
        public StatusDto Status { get; set; }
        public StatusDto Whisper { get; set; }
        public string WhisperNote { get; set; }
        public StatusDto TitleAdsCheck { get; set; }
        public string DefaultTitleAds { get; set; }
        public SuggestionsDto Suggestions { get; set; }
        public string BackupCount { get; set; }
        public List<string> Logs { get; set; }
    }

    public class SaveResultDto
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public Dictionary<string, string> Values { get; set; }
        public SuggestionsDto Suggestions { get; set; }
    }

    public class StatusDto
    {
        public string Caption { get; set; }
        public string Text { get; set; }

        /// <summary>Emby's status names: None, InProgress, Succeeded, Failed, Warning, Unavailable, Unknown.</summary>
        public string State { get; set; }

        internal static StatusDto From(StatusItem item) =>
            new StatusDto { Caption = item?.Caption, Text = item?.StatusText, State = (item?.Status ?? ItemStatus.None).ToString() };
    }

    public class SuggestionsDto
    {
        public StatusDto Status { get; set; }
        public string Text { get; set; }
        public List<SuggestionPlaceDto> Places { get; set; }
        public Dictionary<string, string> Values { get; set; }
    }

    public class SuggestionPlaceDto
    {
        public string Entry { get; set; }
        public string Where { get; set; }
    }

    public class BackupListDto
    {
        public string Refusal { get; set; }
        public string Key { get; set; }
        public int Total { get; set; }
        public List<BackupRowDto> Items { get; set; }
    }

    public class BackupRowDto
    {
        public string File { get; set; }
        public string Folder { get; set; }
        public string Made { get; set; }
        public bool Missing { get; set; }
    }

    public class LogViewDto
    {
        public string Name { get; set; }
        public string Text { get; set; }
        public string OpenUrl { get; set; }
        public string DownloadUrl { get; set; }
    }

    public class SettingsApi : IService
    {
        private readonly IJsonSerializer _json;
        private readonly string _logDirectory;

        public SettingsApi(IJsonSerializer json, IApplicationPaths appPaths)
        {
            _json = json;
            _logDirectory = appPaths.LogDirectoryPath;
        }

        private static Plugin Plugin => Plugin.Instance ?? throw new InvalidOperationException("AudioRemover isn't loaded.");

        public object Get(GetSettingsPage request)
        {
            var plugin = Plugin;
            var options = plugin.OptionsForEditing();
            return new PageDto
            {
                Title = "AudioRemover v" + Plugin.VersionText,
                Description = PluginOptions.PageDescription,
                Tabs = SettingsLayout.Page(),
                Values = SettingsLayout.Values(options),
                Libraries = plugin.LibraryChoices().Select(c => new OptionDto { Value = c.Value, Name = c.Name }).ToList(),
                Languages = Plugin.LanguageChoicesFor(options.KeepLanguages).Select(c => new OptionDto { Value = c.Value, Name = c.Name }).ToList(),
                WhisperModels = WhisperFiles.All.Select(m => new OptionDto { Value = m.Name, Name = m.Label }).ToList(),
                Status = StatusDto.From(plugin.CurrentStatus()),
                Whisper = StatusDto.From(plugin.WhisperStatusChecked(options)),
                WhisperNote = PluginOptions.WhisperDownloadNote,
                TitleAdsCheck = StatusDto.From(Plugin.CheckTitleAds(options.TitleAds, null)),
                DefaultTitleAds = TitleCleaner.DefaultList,
                Suggestions = Suggestions(plugin, options),
                BackupCount = plugin.BackupCountText(),
                Logs = SafeLogList()
            };
        }

        private static SuggestionsDto Suggestions(Plugin plugin, PluginOptions options)
        {
            var (text, places) = plugin.SuggestionBox(options.TitleAds);
            return new SuggestionsDto { Status = StatusDto.From(plugin.SuggestionsStatus()), Text = text, Places = places };
        }

        public object Post(SaveSettings request)
        {
            var plugin = Plugin;
            var (options, problem) = Read(request.Json);
            if (problem != null) return new SaveResultDto { Ok = false, Message = "Not saved: " + problem };

            // Lines taken out of, typed in or changed in "Suggested text" are kept in the suggestions file.
            if (request.SuggestedText != null || request.SuggestedTextShown != null)
            {
                options.SuggestedText = request.SuggestedText;
                options.SuggestedTextShown = request.SuggestedTextShown;
                plugin.KeepSuggestionEdits(options);
            }
            plugin.Save(options);
            var saved = plugin.OptionsForEditing();
            return new SaveResultDto { Ok = true, Message = "Saved.", Values = SettingsLayout.Values(saved), Suggestions = Suggestions(plugin, saved) };
        }

        /// <summary>The saved settings with the page's values on top, or what's wrong with them.</summary>
        private (PluginOptions Options, string Problem) Read(string json)
        {
            Dictionary<string, string> values;
            try
            {
                values = string.IsNullOrEmpty(json) ? new Dictionary<string, string>() : _json.DeserializeFromString<Dictionary<string, string>>(json);
            }
            catch (Exception ex)
            {
                return (null, "couldn't read the settings: " + ex.Message);
            }

            var options = Plugin.OptionsForEditing();
            var problem = SettingsLayout.Apply(options, values);
            if (problem == null)
            {
                try { options.ValidateOrThrow(); }
                catch (Exception ex) { problem = ex.Message; }
            }
            return (options, problem);
        }

        public object Post(StartRun request) => StatusDto.From(Plugin.RunNow());

        public object Post(StopRun request) => StatusDto.From(Plugin.StopRun());

        public object Get(GetRunStatus request) => StatusDto.From(Plugin.CurrentStatus());

        public object Get(GetWhisperStatus request) => StatusDto.From(Plugin.WhisperStatusFor(Plugin.OptionsForEditing()));

        // The page saves first, so this downloads the model that's on the page.
        public object Post(StartWhisperDownload request)
        {
            var plugin = Plugin;
            var options = plugin.OptionsForEditing();
            plugin.StartWhisperDownload(options);
            return StatusDto.From(plugin.WhisperStatusFor(options));
        }

        // Checks the list as it is in the box, saved or not.
        public object Post(CheckTitleAds request) => StatusDto.From(Plugin.CheckTitleAds(request.List, request.Sample));

        public object Get(GetSuggestions request) => Suggestions(Plugin, Plugin.OptionsForEditing());

        // The page saves first, so the search uses the list and libraries that are on the page.
        public object Post(FindSuggestions request)
        {
            var plugin = Plugin;
            var options = plugin.OptionsForEditing();
            plugin.StartFindingSuggestions(options);
            return Suggestions(plugin, options);
        }

        // The page saves its settings first. The box's edits are kept, then every line that passes goes on the list.
        public object Post(AddSuggestions request)
        {
            var plugin = Plugin;
            var options = plugin.OptionsForEditing();
            options.SuggestedText = request.SuggestedText;
            options.SuggestedTextShown = request.SuggestedTextShown;
            plugin.KeepSuggestionEdits(options);
            var added = plugin.AddSuggestedText(options);
            plugin.Save(options);

            var saved = plugin.OptionsForEditing();
            var result = Suggestions(plugin, saved);
            result.Status = StatusDto.From(added);
            result.Values = SettingsLayout.Values(saved);
            return result;
        }

        public object Get(GetBackups request)
        {
            var (refusal, backups) = Plugin.RestorePlan();
            return new BackupListDto
            {
                Refusal = refusal,
                Key = RestoreKey(backups),
                Total = backups.Count,
                Items = backups.Take(Plugin.RestoreListLimit).Select(b => new BackupRowDto
                {
                    File = Path.GetFileName(b.OriginalPath),
                    Folder = Path.GetDirectoryName(b.OriginalPath),
                    Made = b.CreatedUtc.ToLocalTime().ToString("d MMM HH:mm", CultureInfo.InvariantCulture),
                    Missing = !File.Exists(b.BackupPath)
                }).ToList()
            };
        }

        // Restores only from the list the person looked at: if the backups changed since, nothing is restored.
        public object Post(StartRestore request)
        {
            var plugin = Plugin;
            var (refusal, backups) = plugin.RestorePlan();
            if (refusal != null) return Warning(refusal);
            if (string.IsNullOrEmpty(request.Key) || request.Key != RestoreKey(backups))
                return Warning("Nothing restored: the list of backups changed. Click 'Restore originals from backups…' to see it again.");

            var chosen = Plugin.SelectedBackups(backups, ParseIndexes(request.Indexes));
            if (chosen.Count == 0) return Warning("Nothing restored: no video was selected.");
            return StatusDto.From(plugin.StartRestore(chosen));
        }

        public object Get(GetLogView request)
        {
            var logs = SafeLogList();
            var name = logs.Contains(request.Name) ? request.Name : logs.FirstOrDefault();
            if (name == null) return new LogViewDto { Text = "No runs yet." };

            var key = Plugin.LogLinkKey.Current(DateTime.UtcNow);
            string text;
            try { text = RunLog.ReadForDisplay(_logDirectory, name); }
            catch (Exception ex) { text = "Couldn't read the run log: " + ex.Message; }
            return new LogViewDto
            {
                Name = name,
                Text = text,
                OpenUrl = GetAudioRemoverLog.Link(name, key, download: false),
                DownloadUrl = GetAudioRemoverLog.Link(name, key, download: true)
            };
        }

        private List<string> SafeLogList()
        {
            try { return RunLog.ListFiles(_logDirectory).ToList(); }
            catch { return new List<string>(); }
        }

        private static StatusDto Warning(string text) =>
            new StatusDto { Caption = Plugin.RestoreCaption, Text = text, State = ItemStatus.Warning.ToString() };

        internal static IEnumerable<int> ParseIndexes(string indexes) =>
            (indexes ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : -1)
                .Where(i => i >= 0);

        /// <summary>Stands for the list of backups the page showed, so a restore can tell the list hasn't changed since.</summary>
        internal static string RestoreKey(IEnumerable<BackupEntry> backups)
        {
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("|", backups.Select(b => b.OriginalPath))));
                return BitConverter.ToString(bytes, 0, 12).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
