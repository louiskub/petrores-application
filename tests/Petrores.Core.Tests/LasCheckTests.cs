namespace Petrores.Core.Tests;

public sealed class LasCheckTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("petrores-check-").FullName;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Run_InvalidArguments_ReturnsUsageError(int argumentCount)
    {
        var code = Petrores.Check.Program.Run(Enumerable.Repeat(directory, argumentCount).ToArray(), output, error);

        Assert.Equal(2, code);
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void Run_MissingDirectory_ReturnsErrorInsteadOfThrowing()
    {
        var code = Petrores.Check.Program.Run([Path.Combine(directory, "missing")], output, error);

        Assert.Equal(2, code);
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public void Run_NoLasFiles_ReturnsError()
    {
        var code = Petrores.Check.Program.Run([directory], output, error);

        Assert.Equal(2, code);
        Assert.Contains("No LAS files", error.ToString());
    }

    [Fact]
    public void Run_FiftyFiles_ProcessesEveryFile()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal.las");
        for (var i = 0; i < 50; i++)
            File.Copy(fixture, Path.Combine(directory, $"well-{i:D2}.las"));

        var code = Petrores.Check.Program.Run([directory], output, error);

        Assert.Equal(0, code);
        Assert.Contains("Files=50; OK=50; WARN=0; FAIL=0", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void Run_BadFileBeforeGoodFile_ContinuesAndRecognizesUppercaseExtension()
    {
        File.WriteAllText(Path.Combine(directory, "01-bad.las"), "Not a LAS file");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal.las"), Path.Combine(directory, "02-good.LAS"));
        File.WriteAllText(Path.Combine(directory, "notes.txt"), "Ignore this file");

        var code = Petrores.Check.Program.Run([directory], output, error);

        Assert.Equal(1, code);
        Assert.Contains("[FAIL] 01-bad.las", output.ToString());
        Assert.Contains("[OK] 02-good.LAS", output.ToString());
        Assert.Contains("Files=2; OK=1; WARN=0; FAIL=1", output.ToString());
    }

    [Fact]
    public void Run_PartialLoad_IsReportedAsWarning()
    {
        var fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal.las"));
        File.WriteAllText(Path.Combine(directory, "damaged.las"), fixture.Replace("1000.0 50.0", "1000.0 broken"));

        var code = Petrores.Check.Program.Run([directory], output, error);

        Assert.Equal(0, code);
        Assert.Contains("[WARN] damaged.las", output.ToString());
        Assert.Contains("Files=1; OK=0; WARN=1; FAIL=0", output.ToString());
        Assert.Contains("Invalid value for GR", output.ToString());
    }

    [Fact]
    public void Run_UnwritableReportDestination_ReturnsExitCodeOne()
    {
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal.las");
        File.Copy(fixture, Path.Combine(directory, "well-01.las"));

        var invalidReportPath = directory;
        var code = Petrores.Check.Program.Run([directory, "--report", invalidReportPath], output, error);

        Assert.Equal(1, code);
        Assert.Contains("Failed to write conditioning report", error.ToString());
    }

    [Fact]
    public void Run_UnwritablePlotsDirectory_ReturnsErrorInsteadOfThrowing()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal.las"), Path.Combine(directory, "well.las"));
        var plotsPath = Path.Combine(directory, "blocked");
        File.WriteAllText(plotsPath, "This is a file, not a directory.");

        var code = Petrores.Check.Program.Run([directory, "--export-plots", plotsPath], output, error);

        Assert.NotEqual(0, code);
        Assert.Contains("Cannot create plots folder", error.ToString());
    }

    public void Dispose()
    {
        output.Dispose();
        error.Dispose();
        Directory.Delete(directory, recursive: true);
    }
}
