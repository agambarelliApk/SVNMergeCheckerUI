using System.Text;
using System.Text.RegularExpressions;

namespace SVNMergeCheckerUI
{
    public interface IReportParserService
    {
        string Parse(string fullReportOutput, string resultType);
        string PivotFileCoinvolti(string section);
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, List<int>>> ParseAlberoDipendenze(string section);
        IReadOnlyList<(string line, RevisionDisplayState? state)> ParseRevisioniLines(
            string section,
            IReadOnlyDictionary<int, RevisionDisplayState> revisionStates,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>? perIssueRevisionStates = null,
            string groupBy = "Issue",
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, List<int>>>? perIssueDependencyTree = null);
        int? ExtractRevisionNumber(string text);
        string NormalizeRevisionText(string text);
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

        // Matches a leading 'r' immediately before a digit (es. "r12345" -> "12345")
        private static readonly Regex LeadingRPattern =
            new(@"(?i)^\s*r(?=\d)", RegexOptions.Compiled);

        /// <summary>
        /// Extracts the first revision number found in the given text, or null if none found.
        /// </summary>
        public int? ExtractRevisionNumber(string text)
        {
            var match = RevisionPattern.Match(text);
            if (match.Success && int.TryParse(match.Groups["rev"].Value, out var rev))
                return rev;
            return null;
        }

        /// <summary>
        /// Removes a leading 'r' immediately before a digit (es. "r12345" -> "12345").
        /// </summary>
        public string NormalizeRevisionText(string text) =>
            LeadingRPattern.Replace(text, string.Empty);

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

        public IReadOnlyDictionary<string, IReadOnlyDictionary<int, List<int>>> ParseAlberoDipendenze(string section)
        {
            var result = new Dictionary<string, Dictionary<int, List<int>>>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(section))
                return result.ToDictionary(k => k.Key, k => (IReadOnlyDictionary<int, List<int>>)k.Value, StringComparer.OrdinalIgnoreCase);

            string currentIssue = string.Empty;
            int? currentParentRev = null;
            var childRevRegex = new Regex(@"(?:da file in\s+|>\s*r?)(?<rev>\d{1,7})", RegexOptions.Compiled | RegexOptions.IgnoreCase);

            foreach (var rawLine in section.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                var normalized = trimmed.Trim('=', ' ');
                if (normalized.StartsWith("[") && normalized.EndsWith("]"))
                {
                    currentIssue = normalized.Trim('[', ']');
                    if (!result.ContainsKey(currentIssue))
                        result[currentIssue] = new Dictionary<int, List<int>>();
                    currentParentRev = null;
                    continue;
                }

                if (string.IsNullOrEmpty(currentIssue))
                {
                    currentIssue = "$DEFAULT$";
                    if (!result.ContainsKey(currentIssue))
                        result[currentIssue] = new Dictionary<int, List<int>>();
                }

                if (!trimmed.StartsWith(">") && !trimmed.StartsWith("-"))
                {
                    var parentRev = ExtractRevisionNumber(trimmed);
                    if (parentRev.HasValue)
                    {
                        currentParentRev = parentRev.Value;
                        if (!result[currentIssue].ContainsKey(currentParentRev.Value))
                            result[currentIssue][currentParentRev.Value] = new List<int>();
                    }
                }
                else if (currentParentRev.HasValue && !trimmed.Contains("Nessuna dipendenza", StringComparison.OrdinalIgnoreCase))
                {
                    var m = childRevRegex.Match(trimmed);
                    if (m.Success && int.TryParse(m.Groups["rev"].Value, out var childRev))
                    {
                        if (!result[currentIssue][currentParentRev.Value].Contains(childRev))
                            result[currentIssue][currentParentRev.Value].Add(childRev);
                    }
                }
            }

            return result.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlyDictionary<int, List<int>>)kvp.Value,
                StringComparer.OrdinalIgnoreCase);
        }

        private static int GetMergeStatePriority(RevisionDisplayState? state) => state switch
        {
            RevisionDisplayState.DaMergiareDiretta => 1,
            RevisionDisplayState.DipendenzaSuccessivaMergiata => 2,
            RevisionDisplayState.DipendenzaPrecedenteMergiata => 3,
            RevisionDisplayState.DaMergiareIndirettaAlta => 4,
            RevisionDisplayState.DaMergiareIndiretta => 5,
            RevisionDisplayState.DipendenzaPrecedenteDaMergiare => 6,
            RevisionDisplayState.DipendenzaSuccessivaDaMergiare => 7,
            RevisionDisplayState.Mergiato => 8,
            _ => 99
        };

        /// <summary>
        /// Parses the "Elenco Revisioni" section line by line and annotates each line
        /// with its revision display state (checking per-issue states first if available).
        /// When groupBy is "Merge Suggerito", returns a deduplicated, ascending list of revisions
        /// excluding merged revisions and prioritized by merge display state.
        /// </summary>
        public IReadOnlyList<(string line, RevisionDisplayState? state)> ParseRevisioniLines(
            string section,
            IReadOnlyDictionary<int, RevisionDisplayState> revisionStates,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>? perIssueRevisionStates = null,
            string groupBy = "Issue",
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, List<int>>>? perIssueDependencyTree = null)
        {
            string? currentIssue = null;

            if (string.Equals(groupBy, "Merge", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(groupBy, "Merge Suggerito", StringComparison.OrdinalIgnoreCase))
            {
                var revCandidates = new Dictionary<int, (string line, RevisionDisplayState? state)>();

                foreach (var rawLine in section.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    var trimmed = line.Trim();
                    var normalized = trimmed.Trim('=', ' ');
                    if (normalized.StartsWith("[") && normalized.EndsWith("]"))
                    {
                        currentIssue = normalized.Trim('[', ']');
                    }

                    var match = RevisionPattern.Match(line);
                    if (match.Success && int.TryParse(match.Groups["rev"].Value, out var rev))
                    {
                        RevisionDisplayState? state = null;
                        if (currentIssue != null && perIssueRevisionStates != null &&
                            perIssueRevisionStates.TryGetValue(currentIssue, out var issueStates) &&
                            issueStates.TryGetValue(rev, out var perIssueState))
                        {
                            state = perIssueState;
                        }
                        else if (revisionStates.TryGetValue(rev, out var globalState))
                        {
                            state = globalState;
                        }

                        if (!revCandidates.TryGetValue(rev, out var existing))
                        {
                            revCandidates[rev] = (line, state);
                        }
                        else if (GetMergeStatePriority(state) < GetMergeStatePriority(existing.state))
                        {
                            revCandidates[rev] = (line, state);
                        }
                    }
                }

                var mergeResult = new List<(string, RevisionDisplayState?)>
                {
                    ("REVISIONI ORDINATE PER MERGE", null)
                };

                foreach (var kvp in revCandidates.OrderBy(kv => kv.Key))
                {
                    if (kvp.Value.state == RevisionDisplayState.Mergiato ||
                        kvp.Value.state == RevisionDisplayState.DipendenzaPrecedenteMergiata)
                        continue;

                    mergeResult.Add((kvp.Value.line.Replace("    ", "  "), kvp.Value.state));
                }

                return mergeResult;
            }

            // GroupBy "Issue" (or default)
            var result = new List<(string, RevisionDisplayState?)>();
            var issueBlocks = new List<(string? issue, List<string> lines)>();
            string? currentBlockIssue = null;
            var currentLines = new List<string>();

            foreach (var rawLine in section.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.Trim();
                var normalized = trimmed.Trim('=', ' ');

                if (normalized.StartsWith("[") && normalized.EndsWith("]"))
                {
                    if (currentLines.Count > 0 || currentBlockIssue != null)
                    {
                        issueBlocks.Add((currentBlockIssue, currentLines));
                        currentLines = new List<string>();
                    }
                    currentBlockIssue = normalized.Trim('[', ']');
                }
                currentLines.Add(line);
            }
            if (currentLines.Count > 0 || currentBlockIssue != null)
            {
                issueBlocks.Add((currentBlockIssue, currentLines));
            }

            foreach (var (issueKey, blockLines) in issueBlocks)
            {
                var revMap = new Dictionary<int, (string cleanLine, RevisionDisplayState? state)>();
                var revOrder = new List<int>();
                var nonRevLines = new List<string>();
                var hadRevisionsInBlock = false;

                IReadOnlyDictionary<int, List<int>>? issueTree = null;
                if (issueKey != null && perIssueDependencyTree != null && perIssueDependencyTree.TryGetValue(issueKey, out var foundTree))
                {
                    issueTree = foundTree;
                }
                else if (perIssueDependencyTree != null && perIssueDependencyTree.TryGetValue("$DEFAULT$", out var defTree))
                {
                    issueTree = defTree;
                }

                foreach (var line in blockLines)
                {
                    var trimmed = line.Trim();

                    // Skip the separator "--- Revisioni senza issue diretta ---" when tree is present
                    if (issueTree != null && trimmed.StartsWith("---") && trimmed.EndsWith("---"))
                        continue;

                    var match = RevisionPattern.Match(line);
                    if (match.Success && int.TryParse(match.Groups["rev"].Value, out var rev))
                    {
                        hadRevisionsInBlock = true;
                        RevisionDisplayState? state = null;
                        if (issueKey != null && perIssueRevisionStates != null &&
                            perIssueRevisionStates.TryGetValue(issueKey, out var issueStates) &&
                            issueStates.TryGetValue(rev, out var perIssueState))
                        {
                            state = perIssueState;
                        }
                        else if (revisionStates.TryGetValue(rev, out var globalState))
                        {
                            state = globalState;
                        }

                        if (state == RevisionDisplayState.DipendenzaPrecedenteMergiata)
                            continue;

                        var clean = Regex.Replace(trimmed, @"^(?:=>|==|>)\s*", "");
                        if (!revMap.ContainsKey(rev))
                        {
                            revMap[rev] = (clean, state);
                            revOrder.Add(rev);
                        }
                    }
                    else
                    {
                        nonRevLines.Add(line);
                    }
                }

                if (hadRevisionsInBlock && revMap.Count == 0 && issueKey != null) continue;

                foreach (var nrl in nonRevLines)
                {
                    result.Add((nrl.Replace("    ", "  "), null));
                }

                if (revMap.Count == 0) continue;

                if (issueTree != null)
                {
                    var childToParent = new Dictionary<int, int>();
                    foreach (var kvp in issueTree)
                    {
                        var parent = kvp.Key;
                        if (!revMap.ContainsKey(parent)) continue;

                        foreach (var child in kvp.Value)
                        {
                            if (revMap.ContainsKey(child) && child != parent && !childToParent.ContainsKey(child))
                            {
                                childToParent[child] = parent;
                            }
                        }
                    }

                    var roots = revOrder.Where(r => !childToParent.ContainsKey(r)).ToList();
                    var visited = new HashSet<int>();

                    void EmitTree(int nodeRev, int level)
                    {
                        if (!visited.Add(nodeRev)) return;
                        if (!revMap.TryGetValue(nodeRev, out var nodeInfo)) return;

                        var indent = new string(' ', 2 + level * 2);
                        var formatted = $"{indent}=> {nodeInfo.cleanLine}";
                        result.Add((formatted, nodeInfo.state));

                        if (issueTree.TryGetValue(nodeRev, out var children))
                        {
                            foreach (var cRev in children)
                            {
                                if (revMap.ContainsKey(cRev) && !visited.Contains(cRev))
                                {
                                    EmitTree(cRev, level + 1);
                                }
                            }
                        }
                    }

                    foreach (var root in roots)
                    {
                        EmitTree(root, 0);
                    }

                    foreach (var rev in revOrder)
                    {
                        if (!visited.Contains(rev))
                        {
                            EmitTree(rev, 0);
                        }
                    }
                }
                else
                {
                    foreach (var rev in revOrder)
                    {
                        var nodeInfo = revMap[rev];
                        result.Add(($"  => {nodeInfo.cleanLine}", nodeInfo.state));
                    }
                }
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

                // Separatore di sotto-gruppo "--- Revisioni senza issue diretta ---": ignorato
                if (trimmed.StartsWith("---") && trimmed.EndsWith("---"))
                    continue;

                var normalized = trimmed.Trim('=', ' ');
                // Blocco issue: [ISSUE_LABEL] o === [ISSUE_LABEL] ===
                if (normalized.StartsWith("[") && normalized.EndsWith("]"))
                {
                    currentIssue = normalized;
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
