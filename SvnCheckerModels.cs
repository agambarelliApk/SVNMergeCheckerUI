namespace SVNMergeCheckerUI
{
    public record SvnCheckerParameters(
        string WorkingCopy,
        string SourceRepository,
        IReadOnlyList<string> Issues,
        IReadOnlyList<int> Revisions,
        IReadOnlySet<int> SkipRevisions,
        int MaxNewRevs,
        string? OutFile,
        int SvnTimeoutSeconds = 60,
        // Numero di giorni prima della revisione più antica inserita dall'utente da considerare
        // come finestra minima di ricerca della cronologia dei file (precedente/successiva).
        // 0 = nessun limite inferiore (usa DateTime.MinValue).
        int SearchMinDateDays = 60
    );

    // Stati di visualizzazione:
    // - Mergiato: revisione già mergiata
    // - DaMergiareDiretta: revisione direttamente coinvolta (issue/manuale) ancora da mergiare
    // - DaMergiareIndiretta: dipendenza indiretta ancora da mergiare
    // - DaMergiareIndirettaAlta: dipendenza indiretta con numero di revisione superiore
    //   rispetto a tutte le revisioni dirette ancora da mergiare
    // - DipendenzaSuccessivaMergiata/DaMergiare: dipendenza scoperta tramite cronologia file,
    //   con numero di revisione SUCCESSIVO a quella che l'ha originata
    // - DipendenzaPrecedenteMergiata/DaMergiare: dipendenza scoperta tramite cronologia file,
    //   con numero di revisione PRECEDENTE a quella che l'ha originata
    public enum RevisionDisplayState {
        Mergiato,
        DaMergiareDiretta,
        DaMergiareIndiretta,
        DaMergiareIndirettaAlta,
        DipendenzaSuccessivaMergiata,
        DipendenzaSuccessivaDaMergiare,
        DipendenzaPrecedenteMergiata,
        DipendenzaPrecedenteDaMergiare,
        DipendenzaUtentePrecedenteDaMergiare
    }

    // Direzione temporale di una dipendenza scoperta tramite la cronologia di un file,
    // rispetto alla revisione dell'utente che ha portato alla sua scoperta.
    public enum DependencyDirection {
        None,
        Previous,
        Next
    }

    public class RevisionInfo
    {
        public string issues { get; set; } = string.Empty;
        public int Number     { get; init; }
        public DateTime Date  { get; init; }
        public string Author  { get; init; } = "Unknown";
        public string Message { get; init; } = string.Empty;
        public RevisionDisplayState DisplayState { get; set; } = RevisionDisplayState.DaMergiareDiretta;
        public bool IsUserCollision { get; set; }
        public List<string> Files { get; } = new();
        public HashSet<string> MatchedIssues { get; } = new(StringComparer.OrdinalIgnoreCase);
        public RevisionInfo? ParentRev { get; set; }
        public DependencyDirection Direction { get; set; } = DependencyDirection.None;
        // Issue con cui questa revisione condivide un file, scoperte durante l'analisi delle
        // dipendenze di un'altra issue, anche quando questa revisione risulta "diretta" per una
        // issue diversa (nel qual caso ParentRev/Direction vengono azzerati da MergeIssueResults
        // ma il legame incrociato va comunque preservato per la visualizzazione nel report).
        public HashSet<string> CrossIssueDependencies { get; } = new(StringComparer.OrdinalIgnoreCase);

        // Ruolo (dipendenza/diretta + genitore) che questa revisione ha nel contesto di ciascuna
        // issue che la referenzia. Necessario perché una stessa revisione può essere "diretta"
        // per un'issue e "dipendenza" (precedente/successiva) per un'altra: Direction/ParentRev
        // sono scalari e non bastano a rappresentare entrambi i ruoli contemporaneamente.
        public Dictionary<string, PerIssueRole> PerIssueRoles { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        // Stato di visualizzazione calcolato nel contesto di ciascuna issue (vedi PerIssueRoles).
        public Dictionary<string, RevisionDisplayState> PerIssueDisplayStates { get; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    // Ruolo di una revisione nel contesto di una specifica issue: genitore (se dipendenza) e
    // direzione temporale rispetto ad esso.
    public sealed record PerIssueRole(int? ParentRevNumber, DependencyDirection Direction);


    public class SvnCheckerResult
    {
        public IReadOnlyList<RevisionInfo> Revisions { get; init; } = [];
        public IReadOnlyDictionary<int, List<string>> DependencyTree { get; init; } = new Dictionary<int, List<string>>();
        public IReadOnlySet<int> MergedRevisions { get; init; } = new HashSet<int>();
        public IReadOnlyDictionary<int, RevisionDisplayState> RevisionStates { get; init; } = new Dictionary<int, RevisionDisplayState>();
        public IReadOnlyDictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>> PerIssueRevisionStates { get; init; } = new Dictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>();
        public string ReportText { get; init; } = string.Empty;
        public string MergedRevisionsMarker =>
            $"##MERGED_REVISIONS:{string.Join(",", MergedRevisions)}";
    }
}
