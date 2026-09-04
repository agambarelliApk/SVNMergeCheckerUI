using SVNMergeCheckerUI;

namespace SVNMergeCheckerUI.Tests;

/// <summary>
/// Test per <see cref="SvnCheckerHelper.RunAsync"/> basati su un <see cref="ISvnService"/> finto,
/// senza invocare mai <c>svn.exe</c>. Coprono i percorsi che non richiedono l'esecuzione di
/// comandi <c>svn log</c>/<c>svn diff</c> reali (nessuna issue e nessuna revisione manuale
/// specificata), lasciando invece a test di integrazione manuale la copertura dei flussi che
/// dipendono dal processo esterno.
/// </summary>
public class SvnCheckerHelperTests
{
    private sealed class FakeSvnService : ISvnService
    {
        public string? UrlToReturn { get; set; }
        public List<int> MergedRevisions { get; set; } = new();

        public Task<bool> IsSvnAvailableAsync() => Task.FromResult(true);

        public Task<string?> GetSvnUrlAsync(string pathOrUrl, CancellationToken ct = default, int timeoutMs = 60_000)
            => Task.FromResult(UrlToReturn);

        public Task<string> UpdateDirectoryAsync(string path, int timeoutMs = 60_000)
            => Task.FromResult("[OK] svn update completato:\n");

        public Task<List<int>> GetMergedRevisionsAsync(string sourceUrl, string targetPath, CancellationToken ct = default, int timeoutMs = 60_000)
            => Task.FromResult(MergedRevisions);
    }

    private sealed class ListProgress : IProgress<string>
    {
        public List<string> Messages { get; } = new();
        public void Report(string value) => Messages.Add(value);
    }

    private static SvnCheckerParameters CreateParameters(
        IReadOnlyList<string>? issues = null,
        IReadOnlyList<int>? revisions = null) => new(
        WorkingCopy: "C:\\wc",
        SourceRepository: "https://svn.example.com/repo",
        Issues: issues ?? Array.Empty<string>(),
        Revisions: revisions ?? Array.Empty<int>(),
        SkipRevisions: new HashSet<int>(),
        MaxNewRevs: 10,
        OutFile: null);

    [Fact]
    public async Task RunAsync_Throws_WhenRepoUrlCannotBeResolved()
    {
        var svc = new FakeSvnService { UrlToReturn = null };
        var helper = new SvnCheckerHelper(svc);
        var progress = new ListProgress();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => helper.RunAsync(CreateParameters(), progress));
    }

    [Fact]
    public async Task RunAsync_ReturnsEmptyResult_WhenNoIssuesAndNoRevisionsProvided()
    {
        var svc = new FakeSvnService { UrlToReturn = "https://svn.example.com/repo" };
        var helper = new SvnCheckerHelper(svc);
        var progress = new ListProgress();

        var result = await helper.RunAsync(CreateParameters(), progress);

        Assert.Empty(result.Revisions);
        Assert.Empty(result.DependencyTree);
        Assert.Empty(result.MergedRevisions);
        Assert.Empty(result.RevisionStates);
        Assert.Contains(progress.Messages, m => m.Contains("Nessuna revisione da elaborare"));
    }

    [Fact]
    public async Task RunAsync_UsesResolvedRepoUrl_EvenWhenSourceRepositoryIsALocalPath()
    {
        var svc = new FakeSvnService { UrlToReturn = "https://svn.example.com/repo-risolto" };
        var helper = new SvnCheckerHelper(svc);
        var progress = new ListProgress();

        var parameters = CreateParameters() with { SourceRepository = "C:\\repo-locale" };

        var result = await helper.RunAsync(parameters, progress);

        // Nessuna issue/revisione: il flusso si ferma prima di generare un report,
        // ma la risoluzione dell'URL tramite ISvnService deve comunque avvenire senza eccezioni.
        Assert.Empty(result.Revisions);
    }

    [Fact]
    public void ParseDiffHunkRanges_ExtractsRangesCorrectly()
    {
        var diff =
            "Index: file.cs\n" +
            "===================================================================\n" +
            "--- file.cs (revision 100)\n" +
            "+++ file.cs (revision 101)\n" +
            "@@ -10,5 +12,8 @@\n" +
            " context\n" +
            "@@ -50,2 +55,1 @@\n" +
            " context 2\n";

        var ranges = SvnCheckerHelper.ParseDiffHunkRanges(diff);

        Assert.Equal(2, ranges.Count);
        Assert.Equal((10, 19), ranges[0]);
        Assert.Equal((50, 55), ranges[1]);
    }

    [Fact]
    public void CheckRangeCollision_DetectsCollisionAndDisjointCorrectly()
    {
        var rangesA = new List<(int Start, int End)> { (10, 20) };
        var rangesOverlapping = new List<(int Start, int End)> { (15, 25) };
        var rangesAdjacentWithinMargin = new List<(int Start, int End)> { (23, 30) };
        var rangesDisjoint = new List<(int Start, int End)> { (35, 45) };

        Assert.True(SvnCheckerHelper.CheckRangeCollision(rangesA, rangesOverlapping));
        Assert.True(SvnCheckerHelper.CheckRangeCollision(rangesA, rangesAdjacentWithinMargin, margin: 3));
        Assert.False(SvnCheckerHelper.CheckRangeCollision(rangesA, rangesDisjoint, margin: 3));
    }

    [Fact]
    public void CheckRangeCollision_EmptyRanges_FallbacksToTrue()
    {
        var empty = new List<(int Start, int End)>();
        var nonEmpty = new List<(int Start, int End)> { (10, 20) };

        Assert.True(SvnCheckerHelper.CheckRangeCollision(empty, nonEmpty));
        Assert.True(SvnCheckerHelper.CheckRangeCollision(empty, empty));
    }
}
