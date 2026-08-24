using System.Text;
using System.Text.RegularExpressions;

namespace SVNMergeCheckerUI
{
    public interface IReportParserService
    {
        string Parse(string fullReportOutput, string resultType);
        string PivotFileCoinvolti(string section);
        IReadOnlyList<(string line, RevisionDisplayState? state)> ParseRevisioniLines(
            string section, IReadOnlyDictionary<int, RevisionDisplayState> revisionStates);
    }

    public class ReportParserService : IReportParserService
    {
        // Header markers exactly as produced by the PowerShell script
        private const string Header1 = "1. ELENCO COMPLETO REVISIONI ORDINATO";
        private const string Header2 = "2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE";
        private const string Header3 = "3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE";

        // Matches a revision number anywhere in a line: r12345 or bare 12345 at start
        private static readonly Regex RevisionPattern =
            new(@"(?<!\d)r?(?<rev>\d{5,7})(?!\d)", RegexOptions.Compiled);

        public string Parse(string fullReportOutput, string resultType)
        {
            if (string.IsNullOrEmpty(fullReportOutput))
                return string.Empty;

            return resultType switch
            {
                "Elenco Revisioni"  => ExtractSection1(fullReportOutput),
                "Albero Dipendenze" => ExtractBetween(fullReportOutput, Header2, Header3),
                "File Coinvolti"              => PivotFileCoinvolti(ExtractFrom(fullReportOutput, Header3)),
                "File Coinvolti - Raw"        => ExtractFrom(fullReportOutput, Header3),
                "Log Console"       => fullReportOutput,
                _                   => fullReportOutput
            };
        }

        private static string ExtractSection1(string text)
        {
            var grouped = ExtractBetween(text, 
                "1. REVISIONI RAGGRUPPATE PER ISSUE:", 
                "2. STRUTTURA AD ALBERO");

            if (!grouped.StartsWith("[Sezione"))
                return grouped;

            return ExtractBetween(text,
                "1. ELENCO COMPLETO REVISIONI ORDINATO",
                "2. STRUTTURA AD ALBERO");
        }

        /// <summary>
        /// Parses the "Elenco Revisioni" section line by line and annotates each line
        /// with whether its revision number belongs to the already-merged set.
        /// </summary>
        public IReadOnlyList<(string line, RevisionDisplayState? state)> ParseRevisioniLines(
            string section, IReadOnlyDictionary<int, RevisionDisplayState> revisionStates)
        {
            var result = new List<(string, RevisionDisplayState?)>();
            foreach (var rawLine in section.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var match = RevisionPattern.Match(line);
                RevisionDisplayState? state = null;
                if (match.Success && int.TryParse(match.Groups["rev"].Value, out var rev)
                    && revisionStates.TryGetValue(rev, out var found))
                {
                    state = found;
                }
                result.Add((line, state));
            }
            return result;
        }

        /// <summary>
        /// Pivots the "File Coinvolti" section from revision?files to file?revisions layout.
        /// Input format:
        ///   > r12345
        ///    - file_a.cs
        ///    - file_b.cs
        ///   > r12346
        ///    - file_b.cs
        /// Output format:
        ///   > file_a.cs
        ///    - r12345
        ///   > file_b.cs
        ///    - r12345
        ///    - r12346
        /// </summary>
        public string PivotFileCoinvolti(string section)
        {
            // file ? list di (issueLabel, revisionHeaders)
            var fileToIssueRevs = new Dictionary<string, List<(string issueLabel, string revHeader)>>(StringComparer.OrdinalIgnoreCase);
            var fileOrder = new List<string>();

            string? currentIssue = null;
            string? currentRevHeader = null;

            foreach (var rawLine in section.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();

                // Blocco issue: [ISSUE_LABEL]
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentIssue = trimmed;
                    currentRevHeader = null;
                }
                // Intestazione revisione: > REVISIONE r123 del ...
                else if (trimmed.StartsWith(">") && trimmed.Contains("REVISIONE", StringComparison.OrdinalIgnoreCase))
                {
                    currentRevHeader = trimmed;
                }
                // File sotto la revisione: - nomefile.cs
                else if (trimmed.StartsWith("-") && currentRevHeader is not null)
                {
                    var file = trimmed.TrimStart('-').Trim();
                    if (string.IsNullOrWhiteSpace(file) || file.StartsWith("Nessun file")) continue;

                    if (!fileToIssueRevs.ContainsKey(file))
                    {
                        fileToIssueRevs[file] = new List<(string, string)>();
                        fileOrder.Add(file);
                    }

                    fileToIssueRevs[file].Add((currentIssue ?? "[Senza issue]", currentRevHeader));
                }
            }

            if (fileOrder.Count == 0)
                return section; // nothing to pivot, return raw

            var sb = new StringBuilder();
            // Preserve the section header line(s) up to the first '[' or '>' entry
            var sectionHeaderEnd = section.IndexOf('\n');
            if (sectionHeaderEnd > 0)
                sb.AppendLine(section[..sectionHeaderEnd].TrimEnd());

            foreach (var file in fileOrder)
            {
                sb.AppendLine($"> {file}");

                // Raggruppa per issue mantenendo l'ordine di prima occorrenza
                var issueOrder = new List<string>();
                var issueToRevs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                foreach (var (issueLabel, revHeader) in fileToIssueRevs[file])
                {
                    if (!issueToRevs.ContainsKey(issueLabel))
                    {
                        issueToRevs[issueLabel] = new List<string>();
                        issueOrder.Add(issueLabel);
                    }
                    issueToRevs[issueLabel].Add(revHeader);
                }

                foreach (var issueLabel in issueOrder)
                {
                    sb.AppendLine($" > {issueLabel}");
                    foreach (var rev in issueToRevs[issueLabel])
                    {
                        // normalize revision header: remove leading '>' and surrounding whitespace
                        var revText = rev.TrimStart('>', ' ').Trim();
                        // remove case-insensitive word 'REVISIONE' followed by optional space
                        revText = Regex.Replace(revText, "(?i)\\bREVISIONE\\b\\s*", string.Empty).Trim();
                        // then trim any trailing ':' or spaces
                        revText = revText.TrimEnd(':', ' ').Trim();
                        sb.AppendLine($"    - {revText}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }

        // ----------------------------------------------------------------
        private static string ExtractBetween(string text, string startMarker, string endMarker)
        {
            var startIdx = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (startIdx < 0) return $"[Sezione '{startMarker}' non trovata nell'output.]";

            var endIdx = text.IndexOf(endMarker, startIdx + startMarker.Length, StringComparison.OrdinalIgnoreCase);
            if (endIdx < 0) return text[startIdx..].Trim();

            return text[startIdx..endIdx].Trim();
        }

        private static string ExtractFrom(string text, string startMarker)
        {
            var startIdx = text.IndexOf(startMarker, StringComparison.OrdinalIgnoreCase);
            if (startIdx < 0) return $"[Sezione '{startMarker}' non trovata nell'output.]";
            return text[startIdx..].Trim();
        }
    }
}
