namespace SVNMergeCheckerUI
{
    public record SvnCheckerParameters(
        string WorkingCopy,
        string SourceRepository,
        IReadOnlyList<string> Issues,
        IReadOnlyList<int> Revisions,
        IReadOnlySet<int> SkipRevisions,
        int MaxNewRevs,
        string? OutFile
    );

    // Stati di visualizzazione a 4 valori:
    // - Mergiato: revisione già mergiata
    // - DaMergiareDiretta: revisione direttamente coinvolta (issue/manuale) ancora da mergiare
    // - DaMergiareIndiretta: dipendenza indiretta ancora da mergiare
    // - DaMergiareIndirettaAlta: dipendenza indiretta con numero di revisione superiore
    //   rispetto a tutte le revisioni dirette ancora da mergiare
    public enum RevisionDisplayState {
        Mergiato,
        DaMergiareDiretta,
        DaMergiareIndiretta,
        DaMergiareIndirettaAlta
    }

    public class RevisionInfo
    {
        public string issues { get; set; } = string.Empty;
        public int Number     { get; init; }
        public DateTime Date  { get; init; }
        public string Author  { get; init; } = "Unknown";
        public string Message { get; init; } = string.Empty;
        public RevisionDisplayState DisplayState { get; set; } = RevisionDisplayState.DaMergiareDiretta;
        public List<string> Files { get; } = new();
        public HashSet<string> MatchedIssues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public RevisionInfo? ParentRev { get; set; }
    }


    public class SvnCheckerResult
    {
        public IReadOnlyList<RevisionInfo> Revisions { get; init; } = [];
        public IReadOnlyDictionary<int, List<string>> DependencyTree { get; init; } = new Dictionary<int, List<string>>();
        public IReadOnlySet<int> MergedRevisions { get; init; } = new HashSet<int>();
        public IReadOnlyDictionary<int, RevisionDisplayState> RevisionStates { get; init; } = new Dictionary<int, RevisionDisplayState>();
        public string ReportText { get; init; } = string.Empty;
        public string MergedRevisionsMarker =>
            $"##MERGED_REVISIONS:{string.Join(",", MergedRevisions)}";
    }
}
