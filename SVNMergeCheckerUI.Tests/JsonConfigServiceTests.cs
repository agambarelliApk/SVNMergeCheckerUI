using SVNMergeCheckerUI;

namespace SVNMergeCheckerUI.Tests;

/// <summary>
/// Test per <see cref="JsonConfigService"/>: verificano la persistenza su file JSON,
/// i casi di errore di validazione e la corretta serializzazione/deserializzazione dei profili.
/// Ogni test lavora su una directory temporanea isolata per non richiedere lo stato reale
/// dell'eseguibile né interferire con <c>svn_config.json</c> di altri test.
/// </summary>
public class JsonConfigServiceTests : IDisposable
{
    private readonly string _tempDir;

    public JsonConfigServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SVNMergeCheckerUI.Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort cleanup */ }
    }

    /// <summary>
    /// Crea un <see cref="JsonConfigService"/> il cui <c>StorePath</c> punta a una directory
    /// temporanea, tramite il costruttore <c>internal</c> dedicato ai test (esposto via
    /// <c>InternalsVisibleTo</c>), invece di dipendere da <c>AppContext.BaseDirectory</c>.
    /// </summary>
    private JsonConfigService CreateService() => new(_tempDir);

    private static AppConfig CreateConfig(string label = "Profilo1") => new()
    {
        ConfigLabel = label,
        WorkingCopy = "C:\\wc",
        SourceRepository = "https://svn.example.com/repo",
        Issues = "ISSUE-1,ISSUE-2",
        Revisions = "100,101",
        SkipRevisions = "99",
        MaxNewRevs = 250,
        OutFile = "C:\\out\\report.txt",
        ResultType = "Log Console",
        SvnTimeoutSeconds = 30
    };

    [Fact]
    public void Save_Throws_WhenLabelIsEmpty()
    {
        var svc = CreateService();
        var config = CreateConfig(label: "   ");

        var ex = Assert.Throws<ArgumentException>(() => svc.Save(config));
        Assert.Contains("Etichetta", ex.Message);
    }

    [Fact]
    public void Save_Throws_WhenLabelAlreadyExists_AndOverwriteIsFalse()
    {
        var svc = CreateService();
        svc.Save(CreateConfig("Duplicato"));

        var ex = Assert.Throws<InvalidOperationException>(
            () => svc.Save(CreateConfig("Duplicato"), overwrite: false));
        Assert.Contains("Duplicato", ex.Message);
    }

    [Fact]
    public void Save_Overwrites_WhenLabelAlreadyExists_AndOverwriteIsTrue()
    {
        var svc = CreateService();
        svc.Save(CreateConfig("Profilo1"));

        var updated = CreateConfig("Profilo1");
        updated.MaxNewRevs = 999;
        svc.Save(updated, overwrite: true);

        var all = svc.LoadAll();
        Assert.Equal(999, all["Profilo1"].MaxNewRevs);
    }

    [Fact]
    public void LabelExists_ReturnsTrue_OnlyAfterSaving()
    {
        var svc = CreateService();
        Assert.False(svc.LabelExists("Profilo1"));

        svc.Save(CreateConfig("Profilo1"));

        Assert.True(svc.LabelExists("Profilo1"));
    }

    [Fact]
    public void Save_And_LoadAll_RoundTrips_AllFields()
    {
        var svc = CreateService();
        var original = CreateConfig("Profilo1");
        svc.Save(original);

        var all = svc.LoadAll();
        var loaded = Assert.Single(all).Value;

        Assert.Equal(original.ConfigLabel, loaded.ConfigLabel);
        Assert.Equal(original.WorkingCopy, loaded.WorkingCopy);
        Assert.Equal(original.SourceRepository, loaded.SourceRepository);
        Assert.Equal(original.Issues, loaded.Issues);
        Assert.Equal(original.Revisions, loaded.Revisions);
        Assert.Equal(original.SkipRevisions, loaded.SkipRevisions);
        Assert.Equal(original.MaxNewRevs, loaded.MaxNewRevs);
        Assert.Equal(original.OutFile, loaded.OutFile);
        Assert.Equal(original.ResultType, loaded.ResultType);
        Assert.Equal(original.SvnTimeoutSeconds, loaded.SvnTimeoutSeconds);
    }

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenStoreFileDoesNotExist()
    {
        var svc = CreateService();

        var all = svc.LoadAll();

        Assert.Empty(all);
    }

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenStoreFileContainsInvalidJson()
    {
        var svc = CreateService();
        File.WriteAllText(svc.StorePath, "{ questo non è json valido");

        var all = svc.LoadAll();

        Assert.Empty(all);
    }
}
