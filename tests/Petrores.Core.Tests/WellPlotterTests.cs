using ScottPlot;
using ScottPlot.TickGenerators;
using SkiaSharp;
using Xunit;

namespace Petrores.Core.Tests;

public class WellPlotterTests
{
    [Theory]
    [InlineData("GR", "%", 1, false)]
    [InlineData("GR", "%", 1, true)]
    [InlineData("RT", "API", 2, false)]
    [InlineData("RT", "API", 2, true)]
    [InlineData("RHOB", "CPS", 3, false)]
    [InlineData("RHOB", "CPS", 3, true)]
    [InlineData("NPHI", "CPS", 3, false)]
    [InlineData("NPHI", "CPS", 3, true)]
    public void TrackPlot_IncompatibleUnits_AreNotDrawn(string mnemonic, string unit, int track, bool includeReport)
    {
        var well = CreateSampleWell() with { Curves = [new Curve(mnemonic, unit, "", [50, 60, 70, 80, 90, 100])] };
        var conditioned = WellConditioner.Condition(well);
        var report = includeReport ? conditioned.Report : null;
        var plot = track switch
        {
            1 => WellPlotter.CreateTrack1Plot(conditioned.ConditionedWell, report, 1000, 1050),
            2 => WellPlotter.CreateTrack2Plot(conditioned.ConditionedWell, report, 1000, 1050),
            _ => WellPlotter.CreateTrack3Plot(conditioned.ConditionedWell, report, 1000, 1050)
        };

        Assert.Empty(plot.GetPlottables<ScottPlot.Plottables.Scatter>());
    }

    private static Well CreateSampleWell()
    {
        double[] depths = [1000.0, 1010.0, 1020.0, 1030.0, 1040.0, 1050.0];
        var curves = new[]
        {
            new Curve("GR", "GAPI", "Gamma Ray", [45.0, 55.0, 80.0, null, 120.0, 95.0]),
            new Curve("ILD", "OHMM", "Deep Induction", [2.5, 3.8, 15.0, 50.0, 120.0, 8.0]),
            new Curve("RHOB", "G/C3", "Bulk Density", [2.45, 2.50, 2.35, 2.20, 2.15, 2.40]),
            new Curve("NPHI", "V/V", "Neutron Porosity", [0.18, 0.15, 0.22, 0.35, 0.40, 0.20]),
        };

        var wellInfo = new[]
        {
            new LasHeaderEntry("WELL", "", "TEST-WELL-01", "Well Name", 1)
        };

        return new Well(
            VersionInformation: [],
            WellInformation: wellInfo,
            ParameterInformation: null,
            OtherInformation: null,
            Depths: depths,
            Curves: curves,
            DepthUnit: "M"
        );
    }

    [Fact]
    public void RenderCompositePlot_WithValidWell_GeneratesNonEmptyPng()
    {
        var well = CreateSampleWell();
        var conditionResult = WellConditioner.Condition(well);

        var pngBytes = WellPlotter.RenderCompositePlot(conditionResult.ConditionedWell, conditionResult.Report);

        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 1000);

        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
        Assert.Equal(0x4E, pngBytes[2]);
        Assert.Equal(0x47, pngBytes[3]);
    }

    [Fact]
    public void RenderCompositePlot_WithMissingCurves_DoesNotCrashAndPlotsRemaining()
    {

        double[] depths = [500.0, 510.0, 520.0];
        var curves = new[]
        {
            new Curve("GR", "GAPI", "Gamma Ray", [50.0, 75.0, 60.0])
        };

        var well = new Well(
            VersionInformation: [],
            WellInformation: [new LasHeaderEntry("WELL", "", "WELL-ONLY-GR", "", 1)],
            ParameterInformation: null,
            OtherInformation: null,
            Depths: depths,
            Curves: curves,
            DepthUnit: "M"
        );

        var conditionResult = WellConditioner.Condition(well);
        var pngBytes = WellPlotter.RenderCompositePlot(conditionResult.ConditionedWell, conditionResult.Report);

        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 1000);
    }

    [Fact]
    public void SaveCompositePlot_WritesValidFileToDisk()
    {
        var well = CreateSampleWell();
        var conditionResult = WellConditioner.Condition(well);
        var tempFile = Path.Combine(Path.GetTempPath(), $"well_test_{Guid.NewGuid():N}.png");

        try
        {
            WellPlotter.SaveCompositePlot(conditionResult.ConditionedWell, tempFile, conditionResult.Report);

            Assert.True(File.Exists(tempFile));
            var fileInfo = new FileInfo(tempFile);
            Assert.True(fileInfo.Length > 1000);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Track1Plot_HasInvertedDepthLimits()
    {
        var well = CreateSampleWell();
        var plot = WellPlotter.CreateTrack1Plot(well, null, 1000.0, 1050.0);

        Assert.NotNull(plot);

        Assert.Equal(1050.0, plot.Axes.GetLimits().Bottom);
        Assert.Equal(1000.0, plot.Axes.GetLimits().Top);
    }

    [Fact]
    public void Track2Plot_ConfiguredWithLogScaleLimits()
    {
        var well = CreateSampleWell();
        var plot = WellPlotter.CreateTrack2Plot(well, null, 1000.0, 1050.0);

        Assert.NotNull(plot);
        Assert.Equal(1050.0, plot.Axes.GetLimits().Bottom);
        Assert.Equal(1000.0, plot.Axes.GetLimits().Top);
        Assert.Equal(Math.Log10(0.2), plot.Axes.GetLimits().Left, 3);
        Assert.Equal(Math.Log10(2000.0), plot.Axes.GetLimits().Right, 3);
    }

    [Fact]
    public void Track3Plot_ConfiguredWithPorosityOverlayLimits()
    {
        var well = CreateSampleWell();
        var plot = WellPlotter.CreateTrack3Plot(well, null, 1000.0, 1050.0);

        Assert.NotNull(plot);
        Assert.Equal(1050.0, plot.Axes.GetLimits().Bottom);
        Assert.Equal(1000.0, plot.Axes.GetLimits().Top);
        Assert.Equal(1.95, plot.Axes.GetLimits().Left, 3);
        Assert.Equal(2.95, plot.Axes.GetLimits().Right, 3);
    }

    [Fact]
    public void RenderCompositePlot_WithDepthGaps_RendersCleanly()
    {

        double[] depths = [1000.0, 1001.0, 1002.0, 1100.0, 1101.0];
        var curves = new[]
        {
            new Curve("GR", "API", "Gamma Ray", [45.0, 50.0, 55.0, 80.0, 85.0]),
            new Curve("ILD", "OHMM", "Resistivity", [10.0, 12.0, 11.0, 25.0, 30.0]),
            new Curve("RHOB", "G/C3", "Bulk Density", [2.5, 2.52, 2.48, 2.3, 2.32]),
            new Curve("NPHI", "V/V", "Neutron Porosity", [0.15, 0.16, 0.14, 0.3, 0.28])
        };

        var well = new Well(
            VersionInformation: [],
            WellInformation: [new LasHeaderEntry("WELL", "", "GAP-WELL", "", 1)],
            ParameterInformation: null,
            OtherInformation: null,
            Depths: depths,
            Curves: curves,
            DepthUnit: "M"
        );

        var conditionResult = WellConditioner.Condition(well);
        var bytes = WellPlotter.RenderCompositePlot(conditionResult.ConditionedWell, conditionResult.Report);

        Assert.NotNull(bytes);
        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public void RenderCompositePlot_WithValidTrack4_GeneratesExpandedPlotAndNonEmptyPng()
    {
        var well = CreateSampleWell();
        var caliCurve = new Curve("CALI", "IN", "Caliper", [8.5, 8.6, 9.0, 9.2, 8.8, 8.5]);
        var wellWithCali = well with { Curves = [.. well.Curves, caliCurve] };
        var conditionResult = WellConditioner.Condition(wellWithCali);

        var options = new WellPlotterOptions(Track4CurveMnemonic: "CALI");
        var pngBytes = WellPlotter.RenderCompositePlot(conditionResult.ConditionedWell, conditionResult.Report, options);

        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 1000);
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
        Assert.Equal(0x4E, pngBytes[2]);
        Assert.Equal(0x47, pngBytes[3]);

        using var bmp = SKBitmap.Decode(pngBytes);
        Assert.NotNull(bmp);
        Assert.True(bmp.Width > 1350);
    }

    [Fact]
    public void RenderCompositePlot_WithNonExistentTrack4_GracefullyFallsBackTo3Tracks()
    {
        var well = CreateSampleWell();
        var conditionResult = WellConditioner.Condition(well);

        var options = new WellPlotterOptions(Track4CurveMnemonic: "NON_EXISTENT_CURVE");
        var pngBytes = WellPlotter.RenderCompositePlot(conditionResult.ConditionedWell, conditionResult.Report, options);

        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 1000);

        using var bmp = SKBitmap.Decode(pngBytes);
        Assert.NotNull(bmp);
        Assert.Equal(1350, bmp.Width);
    }

    [Fact]
    public void Track4Plot_SingleValue_ExpandsLimitsWithoutError()
    {
        var curve = new Curve("CALI", "IN", "Caliper", [8.5, 8.5, 8.5]);
        var well = CreateSampleWell() with { Depths = [1000.0, 1010.0, 1020.0], Curves = [curve] };
        var plot = WellPlotter.CreateTrack4Plot(well, null, 1000.0, 1020.0, curve);

        Assert.NotNull(plot);
        var limits = plot.Axes.GetLimits();
        Assert.True(limits.Left < limits.Right);
        Assert.True(limits.Left < 8.5);
        Assert.True(limits.Right > 8.5);
    }
}
