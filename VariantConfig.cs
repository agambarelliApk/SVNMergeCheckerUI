using System.Text.Json;

namespace SVNMergeCheckerUI {
    // Variante di verifica parallela SVN (voce della combo "Includi").
    // Label = testo mostrato in UI e nell'header di report; FolderName = ultimo segmento dell'URL SVN.
    public record VariantDefinition(string Label, string FolderName);

    public interface IVariantConfigService {
        IReadOnlyList<VariantDefinition> LoadAll();
        void Save(IEnumerable<VariantDefinition> variants);
    }

    public class JsonVariantConfigService : IVariantConfigService {
        public const string FileName = "parallel_modules.json";
        public const string GeneraleLabel = "Generale";
        public const string TuttoLabel = "Tutto";

        private static readonly JsonSerializerOptions JsonOptions = new() {
            PropertyNameCaseInsensitive = true
        };

        private readonly string _storePath;

        public JsonVariantConfigService()
            : this(Path.Combine(AppContext.BaseDirectory, FileName)) { }

        // Overload per i test (percorso esplicito del file).
        public JsonVariantConfigService(string storePath) => _storePath = storePath;

        public IReadOnlyList<VariantDefinition> LoadAll() {
            List<VariantDefinition>? fromFile = null;
            try {
                if (File.Exists(_storePath)) {
                    var json = File.ReadAllText(_storePath);
                    fromFile = JsonSerializer.Deserialize<List<VariantDefinition>>(json, JsonOptions);
                }
            } catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) {
                fromFile = null; // file illeggibile/non valido: si usano i default
            }

            return Normalize(fromFile);
        }

        public void Save(IEnumerable<VariantDefinition> variants) {
            var normalized = Normalize(variants);
            try {
                var json = JsonSerializer.Serialize(normalized, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storePath, json);
            } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
                throw new InvalidOperationException($"Impossibile salvare il file '{FileName}': {ex.Message}", ex);
            }
        }

        // Ultimo segmento dell'URL SVN (es. ".../branches/tributinet/" -> "tributinet"); null se non ricavabile.
        public static string? ExtractFolderName(string? url) {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var trimmed = url.Trim().TrimEnd('/');
            var idx = trimmed.LastIndexOf('/');
            var segment = idx >= 0 ? trimmed[(idx + 1)..] : trimmed;
            return string.IsNullOrWhiteSpace(segment) ? null : segment;
        }

        // Porta in maiuscolo la sola prima lettera, lasciando invariato il resto.
        public static string Capitalize(string? label) =>
            string.IsNullOrEmpty(label) ? string.Empty : char.ToUpperInvariant(label[0]) + label[1..];

        // Regole: scarta voci vuote o "Tutto", porta in maiuscolo la prima lettera della label,
        // rimuove i duplicati (case-insensitive, vince la prima),
        // garantisce la presenza di "Generale" e lo colloca sempre in ultima posizione.
        public static IReadOnlyList<VariantDefinition> Normalize(IEnumerable<VariantDefinition>? entries) {
            var result = new List<VariantDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generale = new VariantDefinition(GeneraleLabel, GeneraleLabel.ToLowerInvariant());

            foreach (var e in entries ?? Enumerable.Empty<VariantDefinition>()) {
                var label = Capitalize(e?.Label?.Trim());
                if (string.IsNullOrEmpty(label)) continue;
                if (string.Equals(label, TuttoLabel, StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(label)) continue;

                var folder = e!.FolderName?.Trim().Trim('/');
                if (string.IsNullOrEmpty(folder)) folder = label.ToLowerInvariant();

                if (string.Equals(label, GeneraleLabel, StringComparison.OrdinalIgnoreCase))
                    generale = new VariantDefinition(GeneraleLabel, folder);
                else
                    result.Add(new VariantDefinition(label, folder));
            }

            result.Add(generale);
            return result;
        }
    }
}
