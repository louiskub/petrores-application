using System.Text.Json;
using Xunit;

namespace Petrores.Core.Tests;

public class WellConditionerTests
{
    private static Well CreateSampleWell(
        double[] depths,
        string depthUnit = "FT",
        Curve[]? curves = null,
        string wellName = "TEST WELL")
    {
        var wellInfo = new List<LasHeaderEntry>
        {
            new("WELL", "", wellName, "Well Name", 1),
            new("NULL", "", "-999.25", "Null Value", 2)
        };

        curves ??=
        [
            new Curve("GR", "GAPI", "Gamma Ray", new double?[depths.Length]),
            new Curve("RHOB", "G/C3", "Bulk Density", new double?[depths.Length])
        ];

        return new Well(
            VersionInformation: [new("VERS", "", "2.0", "CWLS LAS Version", 1)],
            WellInformation: wellInfo.ToArray(),
            ParameterInformation: null,
            OtherInformation: null,
            Depths: depths,
            Curves: curves,
            DepthUnit: depthUnit
        );
    }

    [Fact]
    public void Condition_EmptyWell_ReturnsEmptyConditionedWell()
    {
        var well = CreateSampleWell([], "FT", [new Curve("GR", "API", "", [])]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(DepthOrder.Empty, result.Report.Depth.OriginalOrder);
        Assert.False(result.Report.Depth.WasReordered);
        Assert.False(result.Report.Depth.IsReversed);
        Assert.Empty(result.ConditionedWell.Depths);
        Assert.Single(result.ConditionedWell.Curves);
    }

    [Fact]
    public void Condition_SinglePoint_PreservesSinglePoint()
    {
        var well = CreateSampleWell([1000.0], "FT", [new Curve("GR", "API", "", [55.0])]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(DepthOrder.SinglePoint, result.Report.Depth.OriginalOrder);
        Assert.False(result.Report.Depth.WasReordered);
        Assert.False(result.Report.Depth.IsReversed);
        Assert.Single(result.ConditionedWell.Depths);
        Assert.Equal(304.8, result.ConditionedWell.Depths[0], precision: 4); // 1000 ft * 0.3048 = 304.8 m
        Assert.Equal("m", result.ConditionedWell.DepthUnit);
    }

    [Fact]
    public void Condition_AscendingOrder_PreservesDepthOrderWithoutReversal()
    {
        var depths = new[] { 1000.0, 1000.5, 1001.0, 1001.5 };
        var grValues = new double?[] { 45.0, 50.0, 55.0, 60.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(DepthOrder.Ascending, result.Report.Depth.OriginalOrder);
        Assert.False(result.Report.Depth.WasReordered);
        Assert.False(result.Report.Depth.IsReversed);
        Assert.Equal(depths, result.ConditionedWell.Depths);
        Assert.Equal(grValues, result.ConditionedWell.Curves[0].Values);
    }

    [Fact]
    public void Condition_DescendingOrder_ReversesToAscendingAndReportsReversal()
    {
        var depths = new[] { 1005.0, 1004.0, 1003.0, 1002.0 };
        var grValues = new double?[] { 70.0, 65.0, 60.0, 55.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(DepthOrder.Descending, result.Report.Depth.OriginalOrder);
        Assert.True(result.Report.Depth.WasReordered);
        Assert.True(result.Report.Depth.IsReversed);
        Assert.Equal(new[] { 1002.0, 1003.0, 1004.0, 1005.0 }, result.ConditionedWell.Depths);
        Assert.Equal(new double?[] { 55.0, 60.0, 65.0, 70.0 }, result.ConditionedWell.Curves[0].Values);
        Assert.Contains(result.Report.Warnings, w => w.Contains("reversed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Condition_MixedOrder_SortsAscendingAndReportsReordering()
    {
        var depths = new[] { 1002.0, 1000.0, 1003.0, 1001.0 };
        var grValues = new double?[] { 20.0, 10.0, 30.0, 15.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(DepthOrder.Mixed, result.Report.Depth.OriginalOrder);
        Assert.True(result.Report.Depth.WasReordered);
        Assert.False(result.Report.Depth.IsReversed); // was mixed, not purely descending
        Assert.Equal(new[] { 1000.0, 1001.0, 1002.0, 1003.0 }, result.ConditionedWell.Depths);
        Assert.Equal(new double?[] { 10.0, 15.0, 20.0, 30.0 }, result.ConditionedWell.Curves[0].Values);
    }

    [Fact]
    public void Condition_DuplicateDepths_AveragesValidValues()
    {
        // Depths with duplicates: 1000.0 (unique), 1001.0 (duplicate x2), 1002.0 (unique)
        var depths = new[] { 1000.0, 1001.0, 1001.0, 1002.0 };
        // At 1001.0: GR has 40.0 and 60.0 -> average is 50.0
        var grValues = new double?[] { 30.0, 40.0, 60.0, 70.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(3, result.ConditionedWell.Depths.Length);
        Assert.Equal(new[] { 1000.0, 1001.0, 1002.0 }, result.ConditionedWell.Depths);
        Assert.Equal(new double?[] { 30.0, 50.0, 70.0 }, result.ConditionedWell.Curves[0].Values);

        Assert.Equal(1, result.Report.Depth.DuplicateGroupCount);
        Assert.Equal(1, result.Report.Depth.DuplicateRowCount);
        Assert.Single(result.Report.Depth.DuplicateDetails);
        Assert.Equal(1001.0, result.Report.Depth.DuplicateDetails[0].Depth);
        Assert.Equal(2, result.Report.Depth.DuplicateDetails[0].DuplicateRowCount);
    }

    [Fact]
    public void Condition_DuplicateDepths_ChoosesValidValueWhenOneIsNull()
    {
        // Depths: 1001.0 appears twice
        var depths = new[] { 1000.0, 1001.0, 1001.0, 1002.0 };
        // Row 1 at 1001.0: null, Row 2 at 1001.0: 85.0 -> should choose 85.0
        var grValues = new double?[] { 50.0, null, 85.0, 60.0 };
        // For RHOB: Row 1 is 2.50, Row 2 is null -> should choose 2.50
        var rhobValues = new double?[] { 2.45, 2.50, null, 2.55 };

        var well = CreateSampleWell(depths, "M",
        [
            new Curve("GR", "API", "", grValues),
            new Curve("RHOB", "G/C3", "", rhobValues)
        ]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(new[] { 1000.0, 1001.0, 1002.0 }, result.ConditionedWell.Depths);
        Assert.Equal(new double?[] { 50.0, 85.0, 60.0 }, result.ConditionedWell.Curves[0].Values);
        Assert.Equal(new double?[] { 2.45, 2.50, 2.55 }, result.ConditionedWell.Curves[1].Values);
    }

    [Fact]
    public void Condition_DuplicateDepths_KeepsNullWhenAllAreNull()
    {
        var depths = new[] { 1000.0, 1001.0, 1001.0, 1002.0 };
        // At 1001.0: both are null -> should remain null
        var grValues = new double?[] { 50.0, null, null, 60.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(new[] { 1000.0, 1001.0, 1002.0 }, result.ConditionedWell.Depths);
        Assert.Equal(new double?[] { 50.0, null, 60.0 }, result.ConditionedWell.Curves[0].Values);
    }

    [Fact]
    public void Condition_DuplicateDepths_HandlesMultipleDuplicatesAndThreeRows()
    {
        // 1000.0 (3 rows), 1001.0 (2 rows)
        var depths = new[] { 1000.0, 1000.0, 1000.0, 1001.0, 1001.0 };
        // 1000.0 has 10.0, null, 20.0 -> average is 15.0
        // 1001.0 has 30.0, 40.0 -> average is 35.0
        var grValues = new double?[] { 10.0, null, 20.0, 30.0, 40.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(new[] { 1000.0, 1001.0 }, result.ConditionedWell.Depths);
        Assert.Equal(new double?[] { 15.0, 35.0 }, result.ConditionedWell.Curves[0].Values);
        Assert.Equal(2, result.Report.Depth.DuplicateGroupCount);
        Assert.Equal(3, result.Report.Depth.DuplicateRowCount); // (3-1) + (2-1) = 3
    }

    [Fact]
    public void Condition_MissingValues_KeepsNullsAndComputesStatistics()
    {
        var depths = new[] { 1000.0, 1001.0, 1002.0, 1003.0 };
        var grValues = new double?[] { 40.0, null, null, 80.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Equal("Kept as null; no interpolation or imputation applied", result.Report.MissingValuesPolicy);
        var grReport = Assert.Single(result.Report.Curves);
        Assert.Equal(4, grReport.TotalCount);
        Assert.Equal(2, grReport.NullCount);
        Assert.Equal(2, grReport.ValidCount);
        Assert.Equal(50.0, grReport.NullPercentage);
        Assert.Equal(1000.0, grReport.FirstValidDepth);
        Assert.Equal(1003.0, grReport.LastValidDepth);

        // Make sure no values were imputed
        Assert.Null(result.ConditionedWell.Curves[0].Values[1]);
        Assert.Null(result.ConditionedWell.Curves[0].Values[2]);
    }

    [Fact]
    public void Condition_UnitConversion_DepthFeetToMeters()
    {
        var depths = new[] { 1000.0, 2000.0 };
        var well = CreateSampleWell(depths, "FT", [new Curve("GR", "GAPI", "", [50.0, 60.0])]);

        var result = WellConditioner.Condition(well);

        Assert.Equal("m", result.ConditionedWell.DepthUnit);
        Assert.Equal(0.3048, result.Report.Depth.DepthUnitConversionFactor);
        Assert.Equal(304.8, result.ConditionedWell.Depths[0], precision: 4);
        Assert.Equal(609.6, result.ConditionedWell.Depths[1], precision: 4);
    }

    [Fact]
    public void Condition_UnitConversion_DensityKgM3ToGCm3()
    {
        var depths = new[] { 100.0, 101.0 };
        var rhobValues = new double?[] { 2650.0, 2710.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("RHOB", "kg/m3", "", rhobValues)]);

        var result = WellConditioner.Condition(well);

        var rhobCurve = result.ConditionedWell.Curves[0];
        Assert.Equal("g/cm3", rhobCurve.Unit);
        Assert.Equal(2.65, rhobCurve.Values[0]!.Value, precision: 4);
        Assert.Equal(2.71, rhobCurve.Values[1]!.Value, precision: 4);

        var report = Assert.Single(result.Report.Curves);
        Assert.Equal(UnitConversionStatus.Converted, report.UnitConversionStatus);
        Assert.Equal(0.001, report.UnitConversionFactor);
        Assert.Equal(2, report.ValuesConvertedCount);
    }

    [Fact]
    public void Condition_UnitConversion_NeutronPorosityPercentToFraction()
    {
        var depths = new[] { 100.0, 101.0 };
        var nphiValues = new double?[] { 15.0, 22.5 };
        var well = CreateSampleWell(depths, "M", [new Curve("NPHI", "%", "", nphiValues)]);

        var result = WellConditioner.Condition(well);

        var nphiCurve = result.ConditionedWell.Curves[0];
        Assert.Equal("v/v", nphiCurve.Unit);
        Assert.Equal(0.15, nphiCurve.Values[0]!.Value, precision: 4);
        Assert.Equal(0.225, nphiCurve.Values[1]!.Value, precision: 4);

        var report = Assert.Single(result.Report.Curves);
        Assert.Equal(UnitConversionStatus.Converted, report.UnitConversionStatus);
        Assert.Equal(0.01, report.UnitConversionFactor);
        Assert.Equal(2, report.ValuesConvertedCount);
    }

    [Fact]
    public void Condition_UnitConversion_UnknownUnit_RetainedWithoutConversion()
    {
        var depths = new[] { 100.0, 101.0 };
        var values = new double?[] { 1.5, 2.5 };
        var well = CreateSampleWell(depths, "M",
        [
            new Curve("RHOB", "XYZ_UNKNOWN", "", values),
            new Curve("CUSTOM", "ANY_UNIT", "", values)
        ]);

        var result = WellConditioner.Condition(well);

        var rhobCurve = result.ConditionedWell.Curves[0];
        Assert.Equal("XYZ_UNKNOWN", rhobCurve.Unit);
        Assert.Equal(1.5, rhobCurve.Values[0]);
        Assert.Equal(2.5, rhobCurve.Values[1]);
        Assert.Equal(UnitConversionStatus.UnknownUnitSkipped, result.Report.Curves[0].UnitConversionStatus);
        Assert.Equal(0, result.Report.Curves[0].ValuesConvertedCount);

        var customCurve = result.ConditionedWell.Curves[1];
        Assert.Equal("ANY_UNIT", customCurve.Unit);
        Assert.Equal(UnitConversionStatus.NoConversionNeeded, result.Report.Curves[1].UnitConversionStatus);
    }

    [Fact]
    public void CurveAliasDictionary_ResolvesCommonMnemonics()
    {
        var dict = CurveAliasDictionary.Default;

        Assert.Equal(WellMeasurementType.GammaRay, dict.Resolve("GR"));
        Assert.Equal(WellMeasurementType.GammaRay, dict.Resolve("GAM"));
        Assert.Equal(WellMeasurementType.GammaRay, dict.Resolve("CGR"));
        Assert.Equal(WellMeasurementType.GammaRay, dict.Resolve("gr_edtc"));

        Assert.Equal(WellMeasurementType.ResistivityDeep, dict.Resolve("ILD"));
        Assert.Equal(WellMeasurementType.ResistivityDeep, dict.Resolve("LLD"));
        Assert.Equal(WellMeasurementType.ResistivityDeep, dict.Resolve("RT"));
        Assert.Equal(WellMeasurementType.ResistivityDeep, dict.Resolve("AT90"));

        Assert.Equal(WellMeasurementType.BulkDensity, dict.Resolve("RHOB"));
        Assert.Equal(WellMeasurementType.BulkDensity, dict.Resolve("RHOZ"));
        Assert.Equal(WellMeasurementType.BulkDensity, dict.Resolve("DEN"));

        Assert.Equal(WellMeasurementType.NeutronPorosity, dict.Resolve("NPHI"));
        Assert.Equal(WellMeasurementType.NeutronPorosity, dict.Resolve("TNPH"));
        Assert.Equal(WellMeasurementType.NeutronPorosity, dict.Resolve("CNL"));
        Assert.Equal(WellMeasurementType.NeutronPorosity, dict.Resolve("NPOR"));

        Assert.Equal(WellMeasurementType.Caliper, dict.Resolve("CALI"));
        Assert.Equal(WellMeasurementType.Caliper, dict.Resolve("DCAL"));
        Assert.Equal(WellMeasurementType.Sonic, dict.Resolve("DT"));
    }

    [Fact]
    public void Condition_SelectsPrimaryCurvesForTracks()
    {
        var depths = new[] { 100.0, 101.0 };
        var curves = new[]
        {
            new Curve("GR_EDTC", "GAPI", "EDTC Gamma Ray", [40.0, 45.0]),
            new Curve("GR", "GAPI", "Standard Gamma Ray", [42.0, 47.0]),
            new Curve("ILD", "OHMM", "Deep Induction", [10.0, 12.0]),
            new Curve("RT", "OHMM", "True Resistivity", [11.0, 13.0]),
            new Curve("RHOZ", "G/C3", "Density Z", [2.5, 2.6]),
            new Curve("RHOB", "G/C3", "Bulk Density", [2.51, 2.61]),
            new Curve("CNL", "%", "Neutron", [18.0, 20.0]),
            new Curve("NPHI", "%", "Neutron Porosity", [0.18, 0.20])
        };

        var well = CreateSampleWell(depths, "M", curves);
        var result = WellConditioner.Condition(well);

        // GR has higher priority than GR_EDTC
        Assert.Equal("GR", result.Report.PrimaryCurves["Track1_GammaRay"].SelectedMnemonic);
        // RT has higher priority than ILD
        Assert.Equal("RT", result.Report.PrimaryCurves["Track2_Resistivity"].SelectedMnemonic);
        // RHOB has higher priority than RHOZ
        Assert.Equal("RHOB", result.Report.PrimaryCurves["Track3_BulkDensity"].SelectedMnemonic);
        // NPHI has higher priority than CNL
        Assert.Equal("NPHI", result.Report.PrimaryCurves["Track3_NeutronPorosity"].SelectedMnemonic);
    }

    [Fact]
    public void Condition_ReportSerializesToValidJson()
    {
        var depths = new[] { 1000.0, 1000.0, 999.0 }; // Descending + duplicate
        var grValues = new double?[] { 50.0, null, 60.0 };
        var well = CreateSampleWell(depths, "FT", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.Condition(well, sourcePath: "sample.las");
        var json = result.Report.ToJson(indented: true);

        Assert.NotEmpty(json);
        Assert.Contains("\"isReversed\": true", json);
        Assert.Contains("\"duplicateRowCount\": 1", json);
        Assert.Contains("Kept as null; no interpolation or imputation applied", json);

        // Parse back using System.Text.Json Document
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("sample.las", root.GetProperty("sourcePath").GetString());
        Assert.True(root.GetProperty("depth").GetProperty("isReversed").GetBoolean());
    }

    [Fact]
    public void Condition_SortByDepth_ConvenienceMethodMaintainsBackwardCompatibility()
    {
        var depths = new[] { 1005.0, 1000.0 };
        var grValues = new double?[] { 50.0, 40.0 };
        var well = CreateSampleWell(depths, "FT", [new Curve("GR", "API", "", grValues)]);

        var result = WellConditioner.SortByDepth(well);

        // SortByDepth should sort depths without converting units to meters
        Assert.Equal(new[] { 1000.0, 1005.0 }, result.ConditionedWell.Depths);
        Assert.Equal(new double?[] { 40.0, 50.0 }, result.ConditionedWell.Curves[0].Values);
        Assert.Equal("FT", result.ConditionedWell.DepthUnit); // kept FT
    }

    [Fact]
    public void Condition_RealDescendingFile_SuccessfullyReversesToAscending()
    {
        var filePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../TestData/4200340740PnrUniv7-23_1.las"));
        if (!File.Exists(filePath)) return;

        var loadResult = new LasReader().Read(filePath);
        Assert.NotNull(loadResult.Well);

        var result = WellConditioner.Condition(loadResult.Well, filePath, diagnostics: loadResult.Diagnostics);

        Assert.Equal(DepthOrder.Descending, result.Report.Depth.OriginalOrder);
        Assert.True(result.Report.Depth.IsReversed);
        Assert.True(result.Report.Depth.WasReordered);
        Assert.True(result.ConditionedWell.Depths.Length > 0);

        // Verify strictly ascending
        for (var i = 0; i < result.ConditionedWell.Depths.Length - 1; i++)
        {
            Assert.True(result.ConditionedWell.Depths[i] <= result.ConditionedWell.Depths[i + 1]);
        }
    }

    [Fact]
    public void Condition_DuplicateDepths_DeduplicatesAndAveragesEndToEnd()
    {
        var rawDepths = new[] { 1000.0, 1000.0, 1000.5, 1001.0, 1001.0, 1001.5 };
        var grValues = new double?[] { 40.0, 60.0, 55.0, null, 70.0, 80.0 };
        var rhobValues = new double?[] { 2.40, null, 2.50, 2.60, 2.70, 2.55 };

        var well = CreateSampleWell(rawDepths, "M",
        [
            new Curve("GR", "API", "Gamma Ray", grValues),
            new Curve("RHOB", "g/cm3", "Bulk Density", rhobValues)
        ]);

        var result = WellConditioner.Condition(well);

        // Raw was 6 rows, duplicates: 1000.0 (x2), 1001.0 (x2) -> 2 duplicate rows merged
        Assert.Equal(2, result.Report.Depth.DuplicateRowCount);
        Assert.Equal(2, result.Report.Depth.DuplicateGroupCount);
        Assert.Equal(4, result.ConditionedWell.Depths.Length);
        Assert.Equal(new[] { 1000.0, 1000.5, 1001.0, 1001.5 }, result.ConditionedWell.Depths);

        // GR at 1000.0: (40 + 60) / 2 = 50.0
        // GR at 1001.0: null and 70.0 -> 70.0 (picks valid value)
        Assert.Equal(new double?[] { 50.0, 55.0, 70.0, 80.0 }, result.ConditionedWell.Curves[0].Values);

        // RHOB at 1000.0: 2.40 and null -> 2.40 (picks valid value)
        // RHOB at 1001.0: (2.60 + 2.70) / 2 = 2.65
        Assert.Equal(2.40, result.ConditionedWell.Curves[1].Values[0]!.Value, precision: 4);
        Assert.Equal(2.50, result.ConditionedWell.Curves[1].Values[1]!.Value, precision: 4);
        Assert.Equal(2.65, result.ConditionedWell.Curves[1].Values[2]!.Value, precision: 4);
        Assert.Equal(2.55, result.ConditionedWell.Curves[1].Values[3]!.Value, precision: 4);

        // Verify strictly increasing
        for (var i = 0; i < result.ConditionedWell.Depths.Length - 1; i++)
        {
            Assert.True(result.ConditionedWell.Depths[i] < result.ConditionedWell.Depths[i + 1]);
        }
    }

    [Fact]
    public void Condition_MissingValues_SentinelValuesFromWellInfo_TreatedAsNull()
    {
        var wellInfo = new List<LasHeaderEntry>
        {
            new("WELL", "", "SENTINEL TEST", "Well Name", 1),
            new("NULL", "", "-9999.0", "Null Value", 2)
        };

        var depths = new[] { 1000.0, 1001.0, 1002.0 };
        // -9999.0 matches custom NULL; -999.25 matches standard default NULL
        var grValues = new double?[] { -9999.0, 55.0, -999.25 };
        var well = new Well(
            VersionInformation: [new("VERS", "", "2.0", "", 1)],
            WellInformation: wellInfo.ToArray(),
            ParameterInformation: null,
            OtherInformation: null,
            Depths: depths,
            Curves: [new Curve("GR", "GAPI", "", grValues)],
            DepthUnit: "M");

        var result = WellConditioner.Condition(well);

        Assert.Null(result.ConditionedWell.Curves[0].Values[0]);
        Assert.Equal(55.0, result.ConditionedWell.Curves[0].Values[1]);
        Assert.Null(result.ConditionedWell.Curves[0].Values[2]);

        var rep = Assert.Single(result.Report.Curves);
        Assert.Equal(2, rep.NullCount);
        Assert.Equal(1, rep.ValidCount);
        Assert.Equal(1001.0, rep.FirstValidDepth);
        Assert.Equal(1001.0, rep.LastValidDepth);
    }

    [Fact]
    public void Condition_MissingValues_NaNAndNonFinite_TreatedAsNull()
    {
        var depths = new[] { 1000.0, 1001.0, 1002.0 };
        var grValues = new double?[] { double.NaN, 65.0, double.PositiveInfinity };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "GAPI", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Null(result.ConditionedWell.Curves[0].Values[0]);
        Assert.Equal(65.0, result.ConditionedWell.Curves[0].Values[1]);
        Assert.Null(result.ConditionedWell.Curves[0].Values[2]);

        var rep = Assert.Single(result.Report.Curves);
        Assert.Equal(2, rep.NullCount);
        Assert.Equal(1, rep.ValidCount);
        Assert.Equal(1001.0, rep.FirstValidDepth);
    }

    [Fact]
    public void Condition_DuplicateDepths_WithSentinelAndValid_PicksValidValue()
    {
        var depths = new[] { 1000.0, 1000.0 };
        // One row has -999.25 (sentinel), second has 45.0 -> must pick 45.0, NOT average to -477
        var grValues = new double?[] { -999.25, 45.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "GAPI", "", grValues)]);

        var result = WellConditioner.Condition(well);

        Assert.Single(result.ConditionedWell.Depths);
        Assert.Equal(45.0, result.ConditionedWell.Curves[0].Values[0]);
    }

    [Fact]
    public void Condition_DensityPorosity_ConvertedFromPercentToFraction()
    {
        var depths = new[] { 1000.0, 1001.0 };
        var dphiValues = new double?[] { 12.5, 18.0 };
        var well = CreateSampleWell(depths, "M",
        [
            new Curve("DPHI", "%", "Density Porosity", dphiValues),
            new Curve("DPOR", "%", "Density Porosity", dphiValues)
        ]);

        var result = WellConditioner.Condition(well);

        Assert.Equal("v/v", result.ConditionedWell.Curves[0].Unit);
        Assert.Equal(0.125, result.ConditionedWell.Curves[0].Values[0]!.Value, precision: 4);
        Assert.Equal(0.18, result.ConditionedWell.Curves[0].Values[1]!.Value, precision: 4);

        Assert.Equal(WellMeasurementType.DensityPorosity, result.Report.Curves[0].MappedMeasurement);
        Assert.Equal(WellMeasurementType.DensityPorosity, result.Report.Curves[1].MappedMeasurement);
    }

    [Fact]
    public void Condition_Porosity_CFCF_ft3ft3_Deci_NormalizedToVV()
    {
        var depths = new[] { 1000.0, 1001.0 };
        var vals = new double?[] { 0.15, 0.20 };
        var well = CreateSampleWell(depths, "M",
        [
            new Curve("NPHI", "CFCF", "", vals),
            new Curve("TNPH", "ft3/ft3", "", vals),
            new Curve("DPHI", "Deci", "", vals)
        ]);

        var result = WellConditioner.Condition(well);

        Assert.All(result.ConditionedWell.Curves, c => Assert.Equal("v/v", c.Unit));
        Assert.All(result.Report.Curves, r => Assert.Equal(UnitConversionStatus.AlreadyStandard, r.UnitConversionStatus));
    }

    [Fact]
    public void Condition_BulkDensity_ZDNC_MappedAndSelectedForTrack3()
    {
        var depths = new[] { 1000.0, 1001.0 };
        var rhobVals = new double?[] { 2.65, 2.70 };
        var well = CreateSampleWell(depths, "M",
        [
            new Curve("ZDNC", "G/C3", "Z-Density Normalized Compensated", rhobVals),
            new Curve("CNC", "PU", "Compensated Neutron", [15.0, 20.0])
        ]);

        var result = WellConditioner.Condition(well);

        Assert.Equal(WellMeasurementType.BulkDensity, result.Report.Curves[0].MappedMeasurement);
        Assert.Equal("ZDNC", result.Report.PrimaryCurves["Track3_BulkDensity"].SelectedMnemonic);
        Assert.Equal("CNC", result.Report.PrimaryCurves["Track3_NeutronPorosity"].SelectedMnemonic);
    }

    [Fact]
    public void Condition_ResistivityFallback_SelectsMediumWhenDeepMissing()
    {
        var depths = new[] { 1000.0, 1001.0 };
        var well = CreateSampleWell(depths, "M",
        [
            new Curve("ILM", "OHMM", "Medium Induction", [10.0, 12.0])
        ]);

        var result = WellConditioner.Condition(well);

        Assert.Equal("ILM", result.Report.PrimaryCurves["Track2_Resistivity"].SelectedMnemonic);
    }

    [Fact]
    public void Condition_MixedOrder_ReordersAndLogsWarning()
    {
        var depths = new[] { 1002.0, 1000.0, 1001.0 };
        var well = CreateSampleWell(depths, "M", [new Curve("GR", "API", "", [20.0, 10.0, 15.0])]);

        var result = WellConditioner.Condition(well);

        Assert.True(result.Report.Depth.WasReordered);
        Assert.Contains(result.Report.Warnings, w => w.Contains("Mixed") && w.Contains("sorted to ascending"));
    }
}
