using ScottPlot;
using ScottPlot.TickGenerators;
using SkiaSharp;
using Xunit;

namespace Petrores.Core.Tests;

public class WellPlotterTests
{
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

        // Verify PNG magic bytes: 0x89 0x50 0x4E 0x47
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
        Assert.Equal(0x4E, pngBytes[2]);
        Assert.Equal(0x47, pngBytes[3]);
    }

    [Fact]
    public void RenderCompositePlot_WithMissingCurves_DoesNotCrashAndPlotsRemaining()
    {
        // Well with ONLY Gamma Ray (no Resistivity, no Density, no Neutron)
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
        // Depth increasing downward: bottom axis is 1050 (deep), top axis is 1000 (shallow)
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
}
