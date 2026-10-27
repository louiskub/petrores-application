using System.Text.Json;
using Xunit.Abstractions;

namespace Petrores.Core.Tests;

public class LasReaderTests
{
    private readonly ITestOutputHelper _output;

    public LasReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

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

    [Fact]
    public void Save_LasResult_AsJsonFile()
    {
        // 1. ระบุไฟล์ LAS ที่ต้องการอ่าน
        var filePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../TestData/4200340125MAIN.las"));
        var reader = new LasReader();
        var result = reader.Read(filePath);

        // 2. แปลงเป็น JSON แบบจัดหน้าให้อ่านง่าย
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true 
        };
        string json = JsonSerializer.Serialize(result, options);

        // 3. เซฟลงในโฟลเดอร์ TestData (ชื่อ result_4200340125MAIN.json)
        var outputPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../TestData/result_4200340125MAIN.json"));
        File.WriteAllText(outputPath, json);

        _output.WriteLine($"Saved JSON result to: {outputPath}");
        Assert.True(File.Exists(outputPath));
    }
}
    