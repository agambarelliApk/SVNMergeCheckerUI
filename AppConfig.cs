using System.Text.Json;
using System.Text.Json.Serialization;

namespace SVNMergeCheckerUI
{
    public class AppConfig
    {
        public string ConfigLabel { get; set; } = string.Empty;
        public string WorkingCopy { get; set; } = string.Empty;
        public string SourceRepository { get; set; } = string.Empty;
        public string Issues { get; set; } = string.Empty;
        public string Revisions { get; set; } = string.Empty;
        public string SkipRevisions { get; set; } = string.Empty;
        public int MaxNewRevs { get; set; } = 500;
        public string OutFile { get; set; } = string.Empty;
        public string ResultType { get; set; } = "Script output";
        public int SvnTimeoutSeconds { get; set; } = 60;
    }

    public interface IConfigService
    {
        /// <summary>Path to the single shared JSON store.</summary>
        string StorePath { get; }

        /// <summary>
        /// Saves <paramref name="config"/> keyed by its <c>ConfigLabel</c>.
        /// Returns <c>false</c> (and does nothing) when the label already exists and
        /// the caller chose not to overwrite; the caller is responsible for asking the user.
        /// Pass <paramref name="overwrite"/>=true to force replacement.
        /// </summary>
        void Save(AppConfig config, bool overwrite = false);

        /// <summary>Returns all saved configs keyed by label (read-only snapshot).</summary>
        IReadOnlyDictionary<string, AppConfig> LoadAll();

        /// <summary>Returns true when a config with the given label already exists.</summary>
        bool LabelExists(string label);
    }

    public class JsonConfigService : IConfigService
    {
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        };

        public string StorePath { get; }

        public JsonConfigService()
        {
            // Place svn_config.json next to the executable
            StorePath = Path.Combine(AppContext.BaseDirectory, "svn_config.json");
        }

        public bool LabelExists(string label)
        {
            var store = ReadStore();
            return store.ContainsKey(label);
        }

        public void Save(AppConfig config, bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(config.ConfigLabel))
                throw new ArgumentException("Il campo Etichetta non può essere vuoto.");

            var store = ReadStore();

            if (store.ContainsKey(config.ConfigLabel) && !overwrite)
                throw new InvalidOperationException(
                    $"Una configurazione con l'etichetta \"{config.ConfigLabel}\" esiste già.");

            store[config.ConfigLabel] = config;
            WriteStore(store);
        }

        public IReadOnlyDictionary<string, AppConfig> LoadAll()
        {
            return ReadStore();
        }

        // ----------------------------------------------------------------
        private Dictionary<string, AppConfig> ReadStore()
        {
            if (!File.Exists(StorePath))
                return new Dictionary<string, AppConfig>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var json = File.ReadAllText(StorePath);
                return JsonSerializer.Deserialize<Dictionary<string, AppConfig>>(json, _options)
                       ?? new Dictionary<string, AppConfig>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, AppConfig>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void WriteStore(Dictionary<string, AppConfig> store)
        {
            var json = JsonSerializer.Serialize(store, _options);
            File.WriteAllText(StorePath, json);
        }
    }
}
