using SVNMergeCheckerUI;

namespace SVNMergeCheckerUI.Tests;

/// <summary>
/// Golden-file test per <see cref="ReportParserService"/>: usa come fixture un report
/// rappresentativo del formato prodotto da <c>SvnCheckerHelper.BuildReportText</c>
/// (stesso set di header/sezioni) e confronta l'output di <see cref="ReportParserService.Parse"/>
/// con file di riferimento salvati su disco, per intercettare regressioni nel formato del report.
/// </summary>
public class ReportParserServiceGoldenFileTests
{
    private readonly ReportParserService _sut = new();

    private static string FixturesDir => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(FixturesDir, fileName)).ReplaceLineEndings("\n").TrimEnd('\n');

    private static string NormalizeActual(string actual) =>
        actual.ReplaceLineEndings("\n").TrimEnd('\n');

    [Fact]
    public void Parse_ElencoRevisioni_MatchesGoldenFile()
    {
        var report = ReadFixture("SampleReport.txt");
        var expected = ReadFixture("SampleReport.ElencoRevisioni.expected.txt");

        var result = _sut.Parse(report, "Elenco Revisioni");

        Assert.Equal(expected, NormalizeActual(result));
    }

    [Fact]
    public void Parse_AlberoDipendenze_MatchesGoldenFile()
    {
        var report = ReadFixture("SampleReport.txt");
        var expected = ReadFixture("SampleReport.AlberoDipendenze.expected.txt");

        var result = _sut.Parse(report, "Albero Dipendenze");

        Assert.Equal(expected, NormalizeActual(result));
    }

    [Fact]
    public void Parse_FileCoinvoltiRaw_MatchesGoldenFile()
    {
        var report = ReadFixture("SampleReport.txt");
        var expected = ReadFixture("SampleReport.FileCoinvoltiRaw.expected.txt");

        var result = _sut.Parse(report, "File Coinvolti - Raw");

        Assert.Equal(expected, NormalizeActual(result));
    }

    [Fact]
    public void Parse_FileCoinvolti_Pivoted_MatchesGoldenFile()
    {
        var report = ReadFixture("SampleReport.txt");
        var expected = ReadFixture("SampleReport.FileCoinvolti.expected.txt");

        var result = _sut.Parse(report, "File Coinvolti");

        Assert.Equal(expected, NormalizeActual(result));
    }
}
