namespace SVNMergeCheckerUI
{
    public enum FileCoinvoltiNodeKind
    {
        FileHeader,
        IssueHeaderNested,
        IssueHeaderTop,
        RevisionMarker
    }

    public sealed record FileCoinvoltiNode(
        FileCoinvoltiNodeKind Kind,
        string Label,
        RevisionDisplayState? State,
        bool IsMarkerHeader,
        string Indent,
        IReadOnlyList<string> Files,
        IReadOnlyList<FileCoinvoltiNode> Children,
        bool TrailingBlankLine = false);

    public sealed record FileCoinvoltiModel(IReadOnlyList<FileCoinvoltiNode> RootNodes);

    public interface IRevisionRenderingService
    {
        FileCoinvoltiModel BuildFileCoinvoltiModel(
            string rawSection,
            string groupBy,
            IReadOnlyDictionary<int, RevisionDisplayState> revisionStates,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>? perIssueRevisionStates = null);
    }

    public class RevisionRenderingService : IRevisionRenderingService
    {
        private readonly IReportParserService _reportParser;

        public RevisionRenderingService(IReportParserService reportParser)
        {
            _reportParser = reportParser;
        }

        public FileCoinvoltiModel BuildFileCoinvoltiModel(
            string rawSection,
            string groupBy,
            IReadOnlyDictionary<int, RevisionDisplayState> revisionStates,
            IReadOnlyDictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>? perIssueRevisionStates = null)
        {
            if (string.IsNullOrWhiteSpace(rawSection))
                return new FileCoinvoltiModel(Array.Empty<FileCoinvoltiNode>());

            // Parsing: identifica blocchi issue e revisioni
            var issueBlocks = new List<(string issueLabel, List<(string revLabel, List<string> files)> revisions)>();
            string? currentIssue = null;
            string? currentRev = null;
            List<(string revLabel, List<string> files)>? currentIssueRevs = null;
            List<string>? currentRevFiles = null;

            foreach (var rawLine in rawSection.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();

                // Separatore di sotto-gruppo "--- Revisioni senza issue diretta ---": ignorato
                if (trimmed.StartsWith("---") && trimmed.EndsWith("---"))
                    continue;

                var normalized = trimmed.Trim('=', ' ');
                if (normalized.StartsWith("[") && normalized.EndsWith("]"))
                {
                    if (currentRev != null && currentRevFiles != null && currentIssueRevs != null)
                        currentIssueRevs.Add((currentRev, currentRevFiles));

                    if (currentIssue != null && currentIssueRevs != null)
                        issueBlocks.Add((currentIssue, currentIssueRevs));

                    currentIssue = normalized;
                    currentIssueRevs = new List<(string, List<string>)>();
                    currentRev = null;
                    currentRevFiles = null;
                }
                else if (trimmed.StartsWith(">") && trimmed.Contains("REVISIONE"))
                {
                    if (currentRev != null && currentRevFiles != null && currentIssueRevs != null)
                        currentIssueRevs.Add((currentRev, currentRevFiles));

                    var revText = System.Text.RegularExpressions.Regex.Replace(
                        trimmed.TrimStart('>', ' ').Trim(),
                        "(?i)\\bREVISIONE\\b\\s*",
                        string.Empty).TrimEnd(':', ' ').Trim();
                    currentRev = _reportParser.NormalizeRevisionText(revText);
                    currentRevFiles = new List<string>();
                }
                else if (trimmed.StartsWith("-") && currentRevFiles != null)
                {
                    var file = trimmed.TrimStart('-').Trim();
                    if (!string.IsNullOrWhiteSpace(file) && !file.StartsWith("Nessun file"))
                        currentRevFiles.Add(file);
                }
            }

            if (currentRev != null && currentRevFiles != null && currentIssueRevs != null)
                currentIssueRevs.Add((currentRev, currentRevFiles));
            if (currentIssue != null && currentIssueRevs != null)
                issueBlocks.Add((currentIssue, currentIssueRevs));

            if (issueBlocks.Count == 0)
                return BuildLegacyModel(rawSection, groupBy, revisionStates);

            var roots = new List<FileCoinvoltiNode>();

            if (groupBy == "File")
            {
                var fileOrder = new List<string>();
                var fileToIssueRevs = new Dictionary<string, List<(string issueLabel, string revLabel)>>(StringComparer.OrdinalIgnoreCase);

                foreach (var (issueLabel, revisions) in issueBlocks)
                {
                    foreach (var (revLabel, files) in revisions)
                    {
                        foreach (var file in files)
                        {
                            if (!fileToIssueRevs.ContainsKey(file))
                            {
                                fileToIssueRevs[file] = new List<(string, string)>();
                                fileOrder.Add(file);
                            }
                            fileToIssueRevs[file].Add((issueLabel, revLabel));
                        }
                    }
                }

                foreach (var file in fileOrder)
                {
                    var issueOrder = new List<string>();
                    var issueToRevs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (issueLabel, revLabel) in fileToIssueRevs[file])
                    {
                        if (!issueToRevs.ContainsKey(issueLabel))
                        {
                            issueToRevs[issueLabel] = new List<string>();
                            issueOrder.Add(issueLabel);
                        }
                        issueToRevs[issueLabel].Add(revLabel);
                    }

                    var issueNodes = new List<FileCoinvoltiNode>();
                    foreach (var issueLabel in issueOrder)
                    {
                        var issueKey = issueLabel.Trim('[', ']');
                        IReadOnlyDictionary<int, RevisionDisplayState>? issueStates = null;
                        perIssueRevisionStates?.TryGetValue(issueKey, out issueStates);

                        var revNodes = issueToRevs[issueLabel]
                            .Select(rev => MakeRevisionMarkerNode(rev, revisionStates, issueStates, isMarkerHeader: true, indent: "    "))
                            .ToList();

                        issueNodes.Add(new FileCoinvoltiNode(
                            FileCoinvoltiNodeKind.IssueHeaderNested,
                            $"=== {issueLabel} ===",
                            null, false, "", Array.Empty<string>(), revNodes));
                    }

                    roots.Add(new FileCoinvoltiNode(
                        FileCoinvoltiNodeKind.FileHeader,
                        file, null, false, "", Array.Empty<string>(), issueNodes, TrailingBlankLine: true));
                }
            }
            else
            {
                foreach (var (issueLabel, revisions) in issueBlocks)
                {
                    var issueKey = issueLabel.Trim('[', ']');
                    IReadOnlyDictionary<int, RevisionDisplayState>? issueStates = null;
                    perIssueRevisionStates?.TryGetValue(issueKey, out issueStates);

                    var revNodes = revisions
                        .Select(r => MakeRevisionMarkerNode(r.revLabel, revisionStates, issueStates, isMarkerHeader: true, indent: "", files: r.files))
                        .ToList();

                    roots.Add(new FileCoinvoltiNode(
                        FileCoinvoltiNodeKind.IssueHeaderTop,
                        $"=== {issueLabel} ===",
                        null, false, "", Array.Empty<string>(), revNodes, TrailingBlankLine: true));
                }
            }

            return new FileCoinvoltiModel(roots);
        }

        private FileCoinvoltiModel BuildLegacyModel(
            string rawSection, string groupBy, IReadOnlyDictionary<int, RevisionDisplayState> revisionStates)
        {
            var revToFiles = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var fileToRevs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var revOrder = new List<string>();
            var fileOrder = new List<string>();

            string? currentRev = null;
            foreach (var rawLine in rawSection.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();

                // Separatore di sotto-gruppo "--- Revisioni senza issue diretta ---": ignorato
                if (trimmed.StartsWith("---") && trimmed.EndsWith("---"))
                    continue;

                if (trimmed.StartsWith(">"))
                {
                    var revText = System.Text.RegularExpressions.Regex.Replace(
                        trimmed.TrimStart('>', ' ').Trim(), "(?i)\\bREVISIONE\\b\\s*", string.Empty).TrimEnd(':', ' ').Trim();
                    currentRev = _reportParser.NormalizeRevisionText(revText);
                    if (!revToFiles.ContainsKey(currentRev)) { revToFiles[currentRev] = new List<string>(); revOrder.Add(currentRev); }
                }
                else if (trimmed.StartsWith("-") && currentRev is not null)
                {
                    var file = trimmed.TrimStart('-').Trim();
                    if (string.IsNullOrWhiteSpace(file)) continue;
                    revToFiles[currentRev].Add(file);
                    if (!fileToRevs.ContainsKey(file)) { fileToRevs[file] = new List<string>(); fileOrder.Add(file); }
                    fileToRevs[file].Add(currentRev);
                }
            }

            var roots = new List<FileCoinvoltiNode>();

            if (groupBy == "Documento")
            {
                foreach (var file in fileOrder)
                {
                    var revNodes = fileToRevs[file]
                        .Select(rev => MakeRevisionMarkerNode(rev, revisionStates, isMarkerHeader: false, indent: ""))
                        .ToList();

                    roots.Add(new FileCoinvoltiNode(
                        FileCoinvoltiNodeKind.FileHeader,
                        file, null, false, "", Array.Empty<string>(), revNodes));
                }
            }
            else
            {
                foreach (var rev in revOrder)
                {
                    roots.Add(MakeRevisionMarkerNode(rev, revisionStates, isMarkerHeader: true, indent: "", files: revToFiles[rev]));
                }
            }

            return new FileCoinvoltiModel(roots);
        }

        private FileCoinvoltiNode MakeRevisionMarkerNode(
            string revLabel,
            IReadOnlyDictionary<int, RevisionDisplayState> globalStates,
            IReadOnlyDictionary<int, RevisionDisplayState>? perIssueStates,
            bool isMarkerHeader,
            string indent,
            List<string>? files = null)
        {
            RevisionDisplayState? state = null;
            var revNumber = _reportParser.ExtractRevisionNumber(revLabel);
            if (revNumber is int rev)
            {
                if (perIssueStates != null && perIssueStates.TryGetValue(rev, out var foundPerIssue))
                    state = foundPerIssue;
                else if (globalStates.TryGetValue(rev, out var foundGlobal))
                    state = foundGlobal;
            }

            return new FileCoinvoltiNode(
                FileCoinvoltiNodeKind.RevisionMarker,
                revLabel, state, isMarkerHeader, indent,
                files ?? new List<string>(), Array.Empty<FileCoinvoltiNode>());
        }

        private FileCoinvoltiNode MakeRevisionMarkerNode(
            string revLabel,
            IReadOnlyDictionary<int, RevisionDisplayState> revisionStates,
            bool isMarkerHeader,
            string indent,
            List<string>? files = null) =>
            MakeRevisionMarkerNode(revLabel, revisionStates, perIssueStates: null, isMarkerHeader, indent, files);
    }
}
