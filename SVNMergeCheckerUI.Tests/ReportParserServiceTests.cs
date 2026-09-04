using SVNMergeCheckerUI;

namespace SVNMergeCheckerUI.Tests;

public class ReportParserServiceTests
{
    private readonly ReportParserService _sut = new();

    // ---------------------------------------------------------------
    // ExtractSection1 / ExtractBetween / ExtractFrom (via Parse)
    // ---------------------------------------------------------------

    [Fact]
    public void Parse_ElencoRevisioni_UsesGroupedByIssueSection_WhenPresent()
    {
        var report =
            "1. REVISIONI RAGGRUPPATE PER ISSUE:\n" +
            "contenuto raggruppato\n" +
            "2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE\n" +
            "altro contenuto\n";

        var result = _sut.Parse(report, "Elenco Revisioni");

        Assert.Contains("contenuto raggruppato", result);
    }

    [Fact]
    public void Parse_ElencoRevisioni_FallsBackToHeader1_WhenGroupedSectionMissing()
    {
        var report =
            "1. ELENCO COMPLETO REVISIONI ORDINATO\n" +
            "riga revisione 1\n" +
            "2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE\n" +
            "altro contenuto\n";

        var result = _sut.Parse(report, "Elenco Revisioni");

        Assert.Contains("riga revisione 1", result);
        Assert.DoesNotContain("[Sezione", result);
    }

    [Fact]
    public void Parse_AlberoDipendenze_ReturnsErrorMarker_WhenSectionNotFound()
    {
        var report = "testo senza sezioni riconosciute";

        var result = _sut.Parse(report, "Albero Dipendenze");

        Assert.Equal(
            "[Sezione '2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE' non trovata nell'output.]",
            result);
    }

    [Fact]
    public void Parse_AlberoDipendenze_ExtractsContentBetweenHeader2AndHeader3()
    {
        var report =
            "1. ELENCO COMPLETO REVISIONI ORDINATO\n" +
            "sezione1\n" +
            "2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE\n" +
            "contenuto albero\n" +
            "3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE\n" +
            "sezione3\n";

        var result = _sut.Parse(report, "Albero Dipendenze");

        Assert.Contains("contenuto albero", result);
        Assert.DoesNotContain("sezione1", result);
        Assert.DoesNotContain("sezione3", result);
    }

    [Fact]
    public void Parse_FileCoinvoltiRaw_ReturnsErrorMarker_WhenHeader3Missing()
    {
        var report = "nessuna sezione qui";

        var result = _sut.Parse(report, "File Coinvolti - Raw");

        Assert.Equal(
            "[Sezione '3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE' non trovata nell'output.]",
            result);
    }

    // ---------------------------------------------------------------
    // ParseRevisioniLines
    // ---------------------------------------------------------------

    [Fact]
    public void ParseRevisioniLines_LineWithoutRevisionNumber_HasNullState()
    {
        var section = "[ISSUE-123]";
        var states = new Dictionary<int, RevisionDisplayState>();

        var result = _sut.ParseRevisioniLines(section, states);

        var line = Assert.Single(result);
        Assert.Null(line.state);
    }

    [Fact]
    public void ParseRevisioniLines_EmptyLine_HasNullState()
    {
        var section = "";

        var states = new Dictionary<int, RevisionDisplayState>();

        var result = _sut.ParseRevisioniLines(section, states);

        var line = Assert.Single(result);
        Assert.Equal(string.Empty, line.line);
        Assert.Null(line.state);
    }

    [Fact]
    public void ParseRevisioniLines_RevisionNotInDictionary_HasNullState()
    {
        var section = "r12345 messaggio commit";
        var states = new Dictionary<int, RevisionDisplayState>(); // vuoto: 12345 non presente

        var result = _sut.ParseRevisioniLines(section, states);

        var line = Assert.Single(result);
        Assert.Null(line.state);
    }

    [Fact]
    public void ParseRevisioniLines_RevisionInDictionary_HasMatchingState()
    {
        var section = "r12345 messaggio commit";
        var states = new Dictionary<int, RevisionDisplayState>
        {
            [12345] = RevisionDisplayState.DaMergiareIndirettaAlta
        };

        var result = _sut.ParseRevisioniLines(section, states);

        var line = Assert.Single(result);
        Assert.Equal(RevisionDisplayState.DaMergiareIndirettaAlta, line.state);
    }

    [Fact]
    public void ParseRevisioniLines_UsesPerIssueState_WhenAvailable()
    {
        var section =
            "=== [ISSUE-A] ===\n" +
            "  => 12345 del 01/01/2024 [mario] : commit A\n" +
            "=== [ISSUE-B] ===\n" +
            "  => 12345 del 01/01/2024 [mario] : commit A\n";

        var globalStates = new Dictionary<int, RevisionDisplayState>
        {
            [12345] = RevisionDisplayState.DaMergiareDiretta
        };

        var perIssueStates = new Dictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>
        {
            ["ISSUE-A"] = new Dictionary<int, RevisionDisplayState> { [12345] = RevisionDisplayState.DaMergiareDiretta },
            ["ISSUE-B"] = new Dictionary<int, RevisionDisplayState> { [12345] = RevisionDisplayState.DipendenzaPrecedenteDaMergiare }
        };

        var result = _sut.ParseRevisioniLines(section, globalStates, perIssueStates);

        var revLines = result.Where(r => r.state.HasValue).ToList();
        Assert.Equal(2, revLines.Count);
        Assert.Equal(RevisionDisplayState.DaMergiareDiretta, revLines[0].state);
        Assert.Equal(RevisionDisplayState.DipendenzaPrecedenteDaMergiare, revLines[1].state);
    }

    [Fact]
    public void ParseRevisioniLines_GroupByMerge_ReturnsHeaderExcludesMergedAndDeduplicatesAscending()
    {
        var section =
            "=== [ISSUE-A] ===\n" +
            "  => 12348 del 01/01/2024 [mario] : commit A\n" +
            "  => 12345 del 01/01/2024 [mario] : commit B\n" +
            "=== [ISSUE-B] ===\n" +
            "  => 12345 del 01/01/2024 [mario] : commit B (duplicate)\n" +
            "  => 12346 del 01/01/2024 [luigi] : commit C\n";

        var states = new Dictionary<int, RevisionDisplayState>
        {
            [12345] = RevisionDisplayState.DaMergiareDiretta,
            [12346] = RevisionDisplayState.Mergiato,
            [12348] = RevisionDisplayState.DaMergiareIndiretta
        };

        var result = _sut.ParseRevisioniLines(section, states, groupBy: "Merge");

        // Header + 2 non-merged revisions (12346 Mergiato is removed)
        Assert.Equal(3, result.Count);
        Assert.Equal(("REVISIONI ORDINATE PER MERGE", (RevisionDisplayState?)null), result[0]);
        Assert.Equal(("  => 12345 del 01/01/2024 [mario] : commit B", (RevisionDisplayState?)RevisionDisplayState.DaMergiareDiretta), result[1]);
        Assert.Equal(("  => 12348 del 01/01/2024 [mario] : commit A", (RevisionDisplayState?)RevisionDisplayState.DaMergiareIndiretta), result[2]);
    }

    [Fact]
    public void ParseRevisioniLines_GroupByMerge_PrioritizesStatesCorrectlyOnDuplicates()
    {
        var section =
            "=== [ISSUE-A] ===\n" +
            "  => 12345 del 01/01/2024 [mario] : commit da indiretta\n" +
            "=== [ISSUE-B] ===\n" +
            "  => 12345 del 01/01/2024 [mario] : commit da diretta\n";

        var perIssueStates = new Dictionary<string, IReadOnlyDictionary<int, RevisionDisplayState>>
        {
            ["ISSUE-A"] = new Dictionary<int, RevisionDisplayState> { [12345] = RevisionDisplayState.DipendenzaPrecedenteDaMergiare },
            ["ISSUE-B"] = new Dictionary<int, RevisionDisplayState> { [12345] = RevisionDisplayState.DaMergiareDiretta }
        };

        var result = _sut.ParseRevisioniLines(section, new Dictionary<int, RevisionDisplayState>(), perIssueStates, groupBy: "Merge");

        Assert.Equal(2, result.Count);
        Assert.Equal(("REVISIONI ORDINATE PER MERGE", (RevisionDisplayState?)null), result[0]);
        Assert.Equal(("  => 12345 del 01/01/2024 [mario] : commit da diretta", (RevisionDisplayState?)RevisionDisplayState.DaMergiareDiretta), result[1]);
    }

    [Fact]
    public void ParseRevisioniLines_GroupByMerge_RevisionNotInDictionary_IncludedWithNullState()
    {
        var section = "  => 12345 del 01/01/2024 [mario] : commit 1\n  => 12340 del 01/01/2024 [mario] : commit 0";
        var states = new Dictionary<int, RevisionDisplayState>
        {
            [12345] = RevisionDisplayState.Mergiato
        };

        var result = _sut.ParseRevisioniLines(section, states, groupBy: "Merge");

        // Header + 12340 (12345 is Mergiato, so excluded)
        Assert.Equal(2, result.Count);
        Assert.Equal(("REVISIONI ORDINATE PER MERGE", (RevisionDisplayState?)null), result[0]);
        Assert.Equal(("  => 12340 del 01/01/2024 [mario] : commit 0", (RevisionDisplayState?)null), result[1]);
    }

    // ---------------------------------------------------------------
    // PivotFileCoinvolti
    // ---------------------------------------------------------------

    [Fact]
    public void PivotFileCoinvolti_ReturnsInputUnchanged_WhenNoFilesFound()
    {
        var section = "3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE\nnessun contenuto valido\n";

        var result = _sut.PivotFileCoinvolti(section);

        Assert.Equal(section, result);
    }

    [Fact]
    public void PivotFileCoinvolti_GroupsFilesByRevisionAndIssue()
    {
        var section =
            "3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE\n" +
            "[ISSUE-1]\n" +
            "> REVISIONE r100 del 2024-01-01\n" +
            "- file_a.cs\n" +
            "- file_b.cs\n" +
            "> REVISIONE r101 del 2024-01-02\n" +
            "- file_b.cs\n";

        var result = _sut.PivotFileCoinvolti(section);

        Assert.Contains("> file_a.cs", result);
        Assert.Contains("> file_b.cs", result);
        Assert.Contains("[ISSUE-1]", result);
        Assert.Contains("100 del 2024-01-01", result);
        Assert.Contains("101 del 2024-01-02", result);

        // file_b.cs deve comparire in entrambe le revisioni sotto la stessa issue
        var fileBIndex = result.IndexOf("> file_b.cs", StringComparison.Ordinal);
        var afterFileB = result[fileBIndex..];
        Assert.Contains("100 del 2024-01-01", afterFileB);
        Assert.Contains("101 del 2024-01-02", afterFileB);
    }

    [Fact]
    public void PivotFileCoinvolti_SkipsFilesMarkedAsNoneFound()
    {
        var section =
            "3. FILE COINVOLTI PER OGNI REVISIONE DA MERGIARE\n" +
            "[ISSUE-1]\n" +
            "> REVISIONE r100 del 2024-01-01\n" +
            "- Nessun file trovato\n" +
            "- file_a.cs\n";

        var result = _sut.PivotFileCoinvolti(section);

        Assert.Contains("> file_a.cs", result);
        Assert.DoesNotContain("Nessun file trovato", result);
    }

    // ---------------------------------------------------------------
    // ParseAlberoDipendenze & Multi-layer Tree ParseRevisioniLines
    // ---------------------------------------------------------------

    [Fact]
    public void ParseAlberoDipendenze_ExtractsPerIssueTreeCorrectly()
    {
        var section =
            "2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE RAGRUPPATE PER ISSUE:\n" +
            "=== [ISSUE-1] ===\n" +
            "  10001\n" +
            "    > revisione precedente derivata da 10001 da file in 10002\n" +
            "    > revisione successiva derivata da 10001 da file in 10003\n" +
            "  10002\n" +
            "    > revisione precedente derivata da 10002 da file in 10004\n" +
            "  10005\n" +
            "    > [Nessuna dipendenza trovata]\n";

        var tree = _sut.ParseAlberoDipendenze(section);

        Assert.True(tree.ContainsKey("ISSUE-1"));
        var issueTree = tree["ISSUE-1"];

        Assert.Equal(3, issueTree.Count);
        Assert.Equal(new[] { 10002, 10003 }, issueTree[10001]);
        Assert.Equal(new[] { 10004 }, issueTree[10002]);
        Assert.Empty(issueTree[10005]);
    }

    [Fact]
    public void ParseRevisioniLines_WithPerIssueDependencyTree_RendersMultiLayerHierarchyWithTwoSpacesIndent()
    {
        var section =
            "1. REVISIONI RAGGRUPPATE PER ISSUE:\n" +
            "=== [ISSUE-1] ===\n" +
            "     => 10001 del 01/01/2024 10:00 [mario] : commit root 1\n" +
            "     => 10005 del 01/01/2024 10:00 [mario] : commit root 2\n" +
            "  --- Revisioni senza issue diretta ---\n" +
            "     => 10002 del 01/01/2024 09:00 [mario] : commit dep child 1\n" +
            "     => 10004 del 01/01/2024 08:00 [mario] : commit dep grandchild\n" +
            "     => 10003 del 01/01/2024 11:00 [mario] : commit dep child 2\n" +
            "     => 10006 del 01/01/2024 12:00 [mario] : commit dep child of root 2\n";

        var alberoSection =
            "2. STRUTTURA AD ALBERO DELLE REVISIONI - DIPENDENZE RAGRUPPATE PER ISSUE:\n" +
            "=== [ISSUE-1] ===\n" +
            "  10001\n" +
            "    > revisione precedente derivata da 10001 da file in 10002\n" +
            "    > revisione successiva derivata da 10001 da file in 10003\n" +
            "  10002\n" +
            "    > revisione precedente derivata da 10002 da file in 10004\n" +
            "  10005\n" +
            "    > revisione successiva derivata da 10005 da file in 10006\n";

        var states = new Dictionary<int, RevisionDisplayState>
        {
            [10001] = RevisionDisplayState.DaMergiareDiretta,
            [10002] = RevisionDisplayState.DipendenzaPrecedenteDaMergiare,
            [10003] = RevisionDisplayState.DipendenzaSuccessivaDaMergiare,
            [10004] = RevisionDisplayState.DipendenzaPrecedenteDaMergiare,
            [10005] = RevisionDisplayState.DaMergiareDiretta,
            [10006] = RevisionDisplayState.DipendenzaSuccessivaDaMergiare
        };

        var tree = _sut.ParseAlberoDipendenze(alberoSection);
        var lines = _sut.ParseRevisioniLines(section, states, groupBy: "Issue", perIssueDependencyTree: tree);

        var revLines = lines.Where(l => l.state.HasValue).Select(l => l.line).ToList();

        // 10001 (level 0: 2 spaces)
        //   10002 (level 1: 4 spaces)
        //     10004 (level 2: 6 spaces)
        //   10003 (level 1: 4 spaces)
        // 10005 (level 0: 2 spaces)
        //   10006 (level 1: 4 spaces)
        Assert.Equal(6, revLines.Count);
        Assert.StartsWith("  => 10001", revLines[0]);
        Assert.StartsWith("    => 10002", revLines[1]);
        Assert.StartsWith("      => 10004", revLines[2]);
        Assert.StartsWith("    => 10003", revLines[3]);
        Assert.StartsWith("  => 10005", revLines[4]);
        Assert.StartsWith("    => 10006", revLines[5]);
    }

    [Fact]
    public void ParseRevisioniLines_FiltersDipendenzaPrecedenteMergiata_AndExcludesEmptyIssueBlock()
    {
        var section =
            "=== [ISSUE-VALID] ===\n" +
            "     => 10001 del 01/01/2024 10:00 [mario] : commit 1\n" +
            "     => 10002 del 01/01/2024 09:00 [mario] : commit 2 (merged dep)\n" +
            "=== [ISSUE-MERGED-ONLY] ===\n" +
            "     => 10003 del 01/01/2024 08:00 [mario] : commit 3 (merged dep)\n";

        var states = new Dictionary<int, RevisionDisplayState>
        {
            [10001] = RevisionDisplayState.DaMergiareDiretta,
            [10002] = RevisionDisplayState.DipendenzaPrecedenteMergiata,
            [10003] = RevisionDisplayState.DipendenzaPrecedenteMergiata
        };

        var tree = new Dictionary<string, IReadOnlyDictionary<int, List<int>>>();
        var lines = _sut.ParseRevisioniLines(section, states, groupBy: "Issue", perIssueDependencyTree: tree);

        var revLines = lines.Where(l => l.state.HasValue).Select(l => l.line).ToList();
        var allLines = lines.Select(l => l.line).ToList();

        // Only 10001 should remain
        Assert.Single(revLines);
        Assert.Contains("10001", revLines[0]);

        // ISSUE-MERGED-ONLY should not be present
        Assert.Contains(allLines, l => l.Contains("[ISSUE-VALID]"));
        Assert.DoesNotContain(allLines, l => l.Contains("[ISSUE-MERGED-ONLY]"));
    }

    [Fact]
    public void ParseRevisioniLines_PreservesDirectMergedAndUserCollisionRevisions()
    {
        var section =
            "=== [ISSUE-1] ===\n" +
            "     => 10001 del 01/01/2024 10:00 [mario] : commit direct merged\n" +
            "     => 10002 del 01/01/2024 09:00 [mario] : commit user collision\n";

        var states = new Dictionary<int, RevisionDisplayState>
        {
            [10001] = RevisionDisplayState.Mergiato,
            [10002] = RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare
        };

        var lines = _sut.ParseRevisioniLines(section, states, groupBy: "Issue");
        var revLines = lines.Where(l => l.state.HasValue).ToList();

        Assert.Equal(2, revLines.Count);
        Assert.Equal(RevisionDisplayState.Mergiato, revLines[0].state);
        Assert.Equal(RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare, revLines[1].state);
    }

    [Fact]
    public void ParseRevisioniLines_SplitsUserCollidingRevisions_AsDirectRootAndIndentedDependency()
    {
        var section =
            "=== [ISSUE-1] ===\n" +
            "     => 10001 del 01/01/2024 10:00 [mario] : commit root\n" +
            "     => 10002 del 01/01/2024 09:00 [mario] : commit user collision\n";

        var states = new Dictionary<int, RevisionDisplayState>
        {
            [10001] = RevisionDisplayState.DaMergiareDiretta,
            [10002] = RevisionDisplayState.DaMergiareDiretta
        };

        var tree = new Dictionary<string, IReadOnlyDictionary<int, List<int>>>
        {
            ["ISSUE-1"] = new Dictionary<int, List<int>>
            {
                [10001] = new List<int> { 10002 },
                [10002] = new List<int>()
            }
        };

        var lines = _sut.ParseRevisioniLines(section, states, groupBy: "Issue", perIssueDependencyTree: tree);
        var revLines = lines.Where(l => l.state.HasValue).ToList();

        // 10001 as root (DaMergiareDiretta, Blue)
        // 10002 indented under 10001 (DipendenzaUtentePrecedenteDaMergiare, LightGray)
        // 10002 as root (DaMergiareDiretta, Blue)
        Assert.Equal(3, revLines.Count);
        Assert.Equal(RevisionDisplayState.DaMergiareDiretta, revLines[0].state);
        Assert.StartsWith("  => 10001", revLines[0].line);

        Assert.Equal(RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare, revLines[1].state);
        Assert.StartsWith("    => 10002", revLines[1].line);

        Assert.Equal(RevisionDisplayState.DaMergiareDiretta, revLines[2].state);
        Assert.StartsWith("  => 10002", revLines[2].line);
    }

    [Fact]
    public void ParseRevisioniLines_UserCollisionNode_DoesNotEmitSubDependenciesUnderLightGrayNode()
    {
        var section =
            "=== [ISSUE-1] ===\n" +
            "     => 10001 del 01/01/2024 10:00 [mario] : commit root 1\n" +
            "     => 10002 del 01/01/2024 09:00 [mario] : commit user collision\n" +
            "  --- Revisioni senza issue diretta ---\n" +
            "     => 10003 del 01/01/2024 08:00 [mario] : commit dep of 10002\n";

        var states = new Dictionary<int, RevisionDisplayState>
        {
            [10001] = RevisionDisplayState.DaMergiareDiretta,
            [10002] = RevisionDisplayState.DaMergiareDiretta,
            [10003] = RevisionDisplayState.DipendenzaPrecedenteDaMergiare
        };

        var tree = new Dictionary<string, IReadOnlyDictionary<int, List<int>>>
        {
            ["ISSUE-1"] = new Dictionary<int, List<int>>
            {
                [10001] = new List<int> { 10002 },
                [10002] = new List<int> { 10003 },
                [10003] = new List<int>()
            }
        };

        var lines = _sut.ParseRevisioniLines(section, states, groupBy: "Issue", perIssueDependencyTree: tree);
        var revLines = lines.Where(l => l.state.HasValue).ToList();

        // 1. 10001 as root (DaMergiareDiretta, Blue)
        // 2. 10002 indented under 10001 (DipendenzaUtentePrecedenteDaMergiare, LightGray) - NO 10003 here
        // 3. 10002 as root (DaMergiareDiretta, Blue)
        // 4. 10003 indented under 10002 root (DipendenzaPrecedenteDaMergiare)
        Assert.Equal(4, revLines.Count);
        Assert.Equal(RevisionDisplayState.DaMergiareDiretta, revLines[0].state);
        Assert.StartsWith("  => 10001", revLines[0].line);

        Assert.Equal(RevisionDisplayState.DipendenzaUtentePrecedenteDaMergiare, revLines[1].state);
        Assert.StartsWith("    => 10002", revLines[1].line);

        Assert.Equal(RevisionDisplayState.DaMergiareDiretta, revLines[2].state);
        Assert.StartsWith("  => 10002", revLines[2].line);

        Assert.Equal(RevisionDisplayState.DipendenzaPrecedenteDaMergiare, revLines[3].state);
        Assert.StartsWith("    => 10003", revLines[3].line);
    }

    [Fact]
    public void ParseRevisioniLines_SkipsDuplicateRevisionStateCombinationInTree()
    {
        var section =
            "=== [ISSUE-1] ===\n" +
            "     => 10001 del 01/01/2024 10:00 [mario] : commit root 1\n" +
            "     => 10002 del 01/01/2024 09:00 [mario] : commit root 2\n" +
            "  --- Revisioni senza issue diretta ---\n" +
            "     => 10003 del 01/01/2024 08:00 [mario] : shared dep\n";

        var states = new Dictionary<int, RevisionDisplayState>
        {
            [10001] = RevisionDisplayState.DaMergiareDiretta,
            [10002] = RevisionDisplayState.DaMergiareDiretta,
            [10003] = RevisionDisplayState.DipendenzaPrecedenteDaMergiare
        };

        var tree = new Dictionary<string, IReadOnlyDictionary<int, List<int>>>
        {
            ["ISSUE-1"] = new Dictionary<int, List<int>>
            {
                [10001] = new List<int> { 10003 },
                [10002] = new List<int> { 10003 },
                [10003] = new List<int>()
            }
        };

        var lines = _sut.ParseRevisioniLines(section, states, groupBy: "Issue", perIssueDependencyTree: tree);
        var revLines = lines.Where(l => l.state.HasValue).ToList();

        // 10001 root
        //   10003 dep under 10001
        // 10002 root
        //   10003 NOT repeated under 10002 since (10003, DipendenzaPrecedenteDaMergiare) was already emitted
        Assert.Equal(3, revLines.Count);
        Assert.Equal(10001, _sut.ExtractRevisionNumber(revLines[0].line));
        Assert.Equal(10003, _sut.ExtractRevisionNumber(revLines[1].line));
        Assert.Equal(10002, _sut.ExtractRevisionNumber(revLines[2].line));
    }
}
