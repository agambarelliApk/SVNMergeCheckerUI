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
}
