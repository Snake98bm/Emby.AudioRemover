using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Model.Serialization;

namespace Emby.AudioRemover.Core
{
    /// <summary>
    /// What "Suggested text" builds up from, across runs: the text runs and "Find text to remove" found in track names, with
    /// the films and shows it was in, plus what was typed into the box or taken out of it.
    /// </summary>
    public sealed class SuggestionData
    {
        /// <summary>Each candidate seen so far, with the films and shows it turned up in (the first <see cref="TitleSuggestions.MostNamesKept"/>).</summary>
        public Dictionary<string, List<string>> Seen { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Lines taken out of the box: never suggested again.</summary>
        public List<string> Dismissed { get; set; } = new List<string>();

        /// <summary>Lines typed into the box or changed there, kept as they are until they're added to the list or taken out.</summary>
        public List<string> Typed { get; set; } = new List<string>();
    }

    /// <summary>Its own file next to the settings, written once per run or page action rather than with every video.</summary>
    public sealed class SuggestionStore
    {
        // Static: the run and the settings page each reach the file, and their changes must not undo each other.
        private static readonly object Lock = new object();
        private readonly string _path;
        private readonly IJsonSerializer _json;

        public SuggestionStore(string folder, IJsonSerializer json)
        {
            _path = Path.Combine(folder, "Emby.AudioRemover.suggestions.json");
            _json = json;
        }

        public SuggestionData Read()
        {
            lock (Lock) return Load();
        }

        /// <summary>Reads, changes and writes the file in one go.</summary>
        public void Update(Action<SuggestionData> change)
        {
            lock (Lock)
            {
                var data = Load();
                change(data);
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                _json.SerializeToFile(data, _path);
            }
        }

        private SuggestionData Load()
        {
            SuggestionData data = null;
            try
            {
                if (File.Exists(_path)) data = _json.DeserializeFromFile<SuggestionData>(_path);
            }
            catch (Exception)
            {
                // Only suggestions: a damaged file starts over rather than stopping a run or the page.
            }
            data = data ?? new SuggestionData();

            // The file's own dictionary minds capitals; one entry per word whatever its capitals.
            var seen = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in data.Seen ?? new Dictionary<string, List<string>>())
                if (kv.Key != null && !seen.ContainsKey(kv.Key)) seen[kv.Key] = kv.Value ?? new List<string>();
            data.Seen = seen;
            data.Dismissed = data.Dismissed ?? new List<string>();
            data.Typed = data.Typed ?? new List<string>();
            return data;
        }
    }
}
