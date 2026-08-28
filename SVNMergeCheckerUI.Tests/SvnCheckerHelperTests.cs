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
}
