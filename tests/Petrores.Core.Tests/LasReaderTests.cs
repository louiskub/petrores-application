namespace Petrores.Core.Tests;

public class LasReaderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Read_MissingPath_RejectsTheArgument(string? filePath)
    {
        var reader = new LasReader();

        Assert.ThrowsAny<ArgumentException>(() => reader.Read(filePath!));
    }

    [Fact]
    public void Read_MissingFile_ReturnsDiagnosticInsteadOfThrowing()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"petrores-{Guid.NewGuid():N}.las");
        var reader = new LasReader();

        var result = reader.Read(filePath);

        Assert.Equal(filePath, result.SourcePath);
        Assert.Null(result.Well);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message));
    }

    [Fact]
    public void Read_UnwrappedLas20_ReturnsCurvesAndMissingValues()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal.las");
        var reader = new LasReader();

        var result = reader.Read(filePath);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        var well = result.Well;
        Assert.Equal("DEMO WELL", well.Name);
        Assert.Equal(new[] { 1000.0, 1000.5 }, well.Depths);
        Assert.Equal(4, well.Curves.Length);
        var gammaRay = Assert.Single(well.Curves, curve => curve.Mnemonic == "GR");
        Assert.Equal("API", gammaRay.Unit);
        Assert.Equal(new double?[] { 50.0, null }, gammaRay.Values);
    }
}
