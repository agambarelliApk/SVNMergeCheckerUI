using SVNMergeCheckerUI;

namespace SVNMergeCheckerUI.Tests;

/// <summary>
/// Test per <see cref="SvnService"/> limitati ai percorsi di codice che non richiedono
/// l'esecuzione reale di <c>svn.exe</c> (short-circuit su URL già risolti, percorsi remoti o
/// percorsi non esistenti). Il progetto non espone oggi un'astrazione <c>IProcessRunner</c>
/// iniettabile per isolare completamente <c>Process.Start</c>: introdurla è tracciato come
/// attività separata in <c>TASKS.md</c>.
/// </summary>
public class SvnServiceTests
{
    private readonly SvnService _sut = new();

    [Theory]
    [InlineData("http://svn.example.com/repo")]
    [InlineData("https://svn.example.com/repo")]
    [InlineData("svn://svn.example.com/repo")]
    [InlineData("svn+ssh://svn.example.com/repo")]
    public async Task GetSvnUrlAsync_ReturnsInputUnchanged_WhenAlreadyAUrl(string url)
    {
        var result = await _sut.GetSvnUrlAsync(url);

        Assert.Equal(url, result);
    }

    [Fact]
    public async Task GetSvnUrlAsync_ReturnsNull_WhenPathIsEmpty()
    {
        var result = await _sut.GetSvnUrlAsync(string.Empty);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("http://svn.example.com/repo")]
    [InlineData("https://svn.example.com/repo")]
    public async Task UpdateDirectoryAsync_Skips_WhenPathIsRemoteUrl(string url)
    {
        var result = await _sut.UpdateDirectoryAsync(url);

        Assert.StartsWith("[SKIP]", result);
        Assert.Contains("remoto", result);
    }

    [Fact]
    public async Task UpdateDirectoryAsync_Skips_WhenPathDoesNotExist()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "SVNMergeCheckerUI_" + Guid.NewGuid());

        var result = await _sut.UpdateDirectoryAsync(missingPath);

        Assert.StartsWith("[SKIP]", result);
        Assert.Contains("non esiste", result);
    }

    [Fact]
    public async Task UpdateDirectoryAsync_Skips_WhenPathIsEmpty()
    {
        var result = await _sut.UpdateDirectoryAsync(string.Empty);

        Assert.StartsWith("[SKIP]", result);
        Assert.Contains("non specificato", result);
    }

    [Fact]
    public async Task GetMergedRevisionsAsync_ReturnsEmptyList_WhenSourceUrlIsEmpty()
    {
        var result = await _sut.GetMergedRevisionsAsync(string.Empty, "C:\\wc");

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMergedRevisionsAsync_ReturnsEmptyList_WhenTargetPathIsEmpty()
    {
        var result = await _sut.GetMergedRevisionsAsync("https://svn.example.com/repo", string.Empty);

        Assert.Empty(result);
    }
}
