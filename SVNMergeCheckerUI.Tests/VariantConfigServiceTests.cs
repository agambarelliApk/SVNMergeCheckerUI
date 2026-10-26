namespace SVNMergeCheckerUI.Tests;

public class VariantConfigServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "variants_" + Guid.NewGuid().ToString("N"));

    public VariantConfigServiceTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private IReadOnlyList<VariantDefinition> Load(string? json)
    {
        var path = Path.Combine(_dir, "parallel_modules.json");
        if (json is not null) File.WriteAllText(path, json);
        return new JsonVariantConfigService(path).LoadAll();
    }

    [Fact]
    public void LoadAll_ReturnsOnlyGenerale_WhenFileMissing()
    {
        var r = Load(null);
        Assert.Equal(new[] { "Generale" }, r.Select(v => v.Label));
    }

    [Fact]
    public void LoadAll_ReturnsOnlyGenerale_WhenJsonInvalid()
    {
        var r = Load("{ non valido");
        Assert.Equal(new[] { "Generale" }, r.Select(v => v.Label));
    }

    [Fact]
    public void LoadAll_AddsGenerale_WhenSingleEntryDifferent()
    {
        var r = Load("[{\"Label\":\"TributiNet\",\"FolderName\":\"tributinet\"}]");
        Assert.Equal(new[] { "TributiNet", "Generale" }, r.Select(v => v.Label));
    }

    [Fact]
    public void LoadAll_DoesNotDuplicateGenerale_WhenSingleEntryIsGenerale()
    {
        var r = Load("[{\"Label\":\"Generale\",\"FolderName\":\"generale\"}]");
        Assert.Single(r);
    }

    [Fact]
    public void LoadAll_RemovesDuplicates_IgnoringTuttoAndEmpty_AndKeepsGeneraleLast()
    {
        var r = Load("[{\"Label\":\"Generale\",\"FolderName\":\"generale\"},{\"Label\":\"A\",\"FolderName\":\"a\"},{\"Label\":\"a\",\"FolderName\":\"x\"},{\"Label\":\"Tutto\",\"FolderName\":\"t\"},{\"Label\":\"\",\"FolderName\":\"e\"}]");
        Assert.Equal(new[] { "A", "Generale" }, r.Select(v => v.Label));
        Assert.Equal("a", r[0].FolderName);
    }

    [Fact]
    public void LoadAll_UsesLowercaseLabel_WhenFolderNameMissing()
    {
        var r = Load("[{\"Label\":\"Foo\"}]");
        Assert.Equal("foo", r[0].FolderName);
    }

    [Fact]
    public void LoadAll_CapitalizesFirstLetterOfLabel_KeepingRestUnchanged()
    {
        var r = Load("[{\"Label\":\"tributiNet\",\"FolderName\":\"tributinet\"},{\"Label\":\"generale\",\"FolderName\":\"generale\"}]");
        Assert.Equal(new[] { "TributiNet", "Generale" }, r.Select(v => v.Label));
    }

    [Fact]
    public void Save_WritesCapitalizedLabelToFile()
    {
        var path = Path.Combine(_dir, "parallel_modules.json");
        new JsonVariantConfigService(path).Save(new[] { new VariantDefinition("foo", "foo") });
        Assert.Contains("\"Foo\"", File.ReadAllText(path));
    }

    [Fact]
    public void Save_Then_LoadAll_RoundTrips_AndKeepsGeneraleLast()
    {
        var path = Path.Combine(_dir, "parallel_modules.json");
        var svc = new JsonVariantConfigService(path);
        svc.Save(new[] { new VariantDefinition("Generale", "generale"), new VariantDefinition("X", "x") });

        var r = svc.LoadAll();
        Assert.Equal(new[] { "X", "Generale" }, r.Select(v => v.Label));
        Assert.Equal("x", r[0].FolderName);
    }

    [Theory]
    [InlineData("https://svn.example.com/repo/branches/tributinet", "tributinet")]
    [InlineData("https://svn.example.com/repo/branches/generale/", "generale")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ExtractFolderName_ReturnsLastSegmentOrNull(string? url, string? expected)
    {
        Assert.Equal(expected, JsonVariantConfigService.ExtractFolderName(url));
    }
}
