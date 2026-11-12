using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Serialization;

namespace Emby.AudioRemover.Core
{
    /// <summary>
    /// The videos SubRemover has been through, read from its own record ("Emby.Subremover.state.json"), for "Wait until
    /// SubRemover has checked a video". Read once per run: the file lists every video SubRemover checked.
    /// </summary>
    public class SubRemoverChecks
    {
        public const string FileName = "Emby.Subremover.state.json";

        // Only the parts AudioRemover needs; the rest of the file is skipped by the JSON reader. Public, so any JSON reader can fill them.
        public class SavedState
        {
            public Dictionary<string, SavedStamp> Processed { get; set; }
            public List<SavedBackup> Backups { get; set; }
            public List<string> Restored { get; set; }
        }

        public class SavedStamp
        {
            public long Size { get; set; }
        }

        public class SavedBackup
        {
            public string OriginalPath { get; set; }
        }

        private readonly HashSet<string> _checked;

        private SubRemoverChecks(HashSet<string> done, bool unreadable)
        {
            _checked = done;
            Unreadable = unreadable;
        }

        /// <summary>SubRemover's record is there but couldn't be read: every video waits, to be safe.</summary>
        public bool Unreadable { get; }

        public int Count => _checked.Count;

        public bool HasChecked(string videoPath) => !Unreadable && _checked.Contains(videoPath);

        /// <summary>For tests and for "SubRemover hasn't checked anything yet".</summary>
        public static SubRemoverChecks Of(IEnumerable<string> paths) =>
            new SubRemoverChecks(new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase), false);

        public static SubRemoverChecks Read(string configurationsFolder, IJsonSerializer json)
        {
            var path = Path.Combine(configurationsFolder, FileName);
            if (!File.Exists(path)) return Of(Array.Empty<string>());
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var state = json.DeserializeFromFile<SavedState>(path);
                    if (state != null)
                    {
                        var paths = (state.Processed?.Keys ?? Enumerable.Empty<string>())
                            .Concat((state.Backups ?? new List<SavedBackup>()).Select(b => b?.OriginalPath))
                            .Concat(state.Restored ?? new List<string>())
                            .Where(p => !string.IsNullOrEmpty(p));
                        return Of(paths);
                    }
                }
                catch (Exception)
                {
                }
                System.Threading.Thread.Sleep(300);
            }
            return new SubRemoverChecks(new HashSet<string>(StringComparer.OrdinalIgnoreCase), true);
        }
    }
}
