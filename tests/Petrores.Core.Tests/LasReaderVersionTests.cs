using System.Globalization;

namespace Petrores.Core.Tests;

public class LasReaderVersionTests
{
    [Theory]
    [InlineData("1.2")]
    [InlineData("2.0")]
    [InlineData("2.00")]
    [InlineData("3.0")]
    [InlineData("3.00")]
    public void Read_SupportedVersions_PreservesMetadataAndReadsNumericCurves(string version)
    {
        var result = ReadText(Las(version));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        Assert.Equal("DEMO WELL", result.Well.Name);
        Assert.Equal("M", result.Well.DepthUnit);
        var versionEntry = Assert.Single(result.Well.VersionInformation, entry => entry.Mnemonic == "VERS");
        Assert.Equal(version, versionEntry.Value);
        Assert.Equal(2, versionEntry.LineNumber);
        var startEntry = Assert.Single(result.Well.WellInformation, entry => entry.Mnemonic == "STRT");
        Assert.Equal("M", startEntry.Unit);
        Assert.Equal(6, startEntry.LineNumber);
        Assert.Equal(new[] { 1000.0 }, result.Well.Depths);
        Assert.Equal(2, result.Well.Curves.Length);
        Assert.Null(result.Well.Curves[0].Values[0]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[0]);
    }

    [Theory]
    [InlineData("-999.25")]
    [InlineData("-9999")]
    [InlineData("-999999.0000")]
    [InlineData("-.9999")]
    [InlineData("-999.20")]
    [InlineData("0")]
    public void Read_NullSentinel_ComesFromEachFilesWellInformation(string nullValue)
    {
        var result = ReadText(Las(nullValue: nullValue, data: $"1000 {nullValue} 12"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        Assert.Null(result.Well.Curves[0].Values[0]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[0]);
    }

    [Fact]
    public void Read_ZeroNull_DoesNotEraseNearbyMeasurementsOrAssumeAnotherSentinel()
    {
        var result = ReadText(Las(nullValue: "0", data: "1000 0 -999.25\n1000.5 0.00005 -0.00005"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        Assert.Equal(new double?[] { null, 0.00005 }, result.Well.Curves[0].Values);
        Assert.Equal(new double?[] { -999.25, -0.00005 }, result.Well.Curves[1].Values);
    }

    [Fact]
    public void Read_NullSentinel_MatchesWithinEpsilon()
    {
        var data = "1000 -999.25004 12\n1000.5 -999.2502 12";
        var result = ReadText(Las(nullValue: "-999.25", data: data));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        Assert.Equal(2, result.Well.Depths.Length);
        Assert.Null(result.Well.Curves[0].Values[0]);
        Assert.Equal(-999.2502, result.Well.Curves[0].Values[1]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[0]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[1]);
    }

    [Fact]
    public void Read_Las12_UsesLegacyDescriptionValues()
    {
        var text = Las("1.2", nullValue: "-1234", data: "1000 -1234 12")
            .Replace("WELL. DEMO WELL : Well name", "WELL. WELL : LEGACY WELL")
            .Replace("NULL. -1234 : Missing value", "NULL. NULL : -1234");

        var result = ReadText(text);

        Assert.NotNull(result.Well);
        Assert.Equal("LEGACY WELL", result.Well.Name);
        Assert.Null(result.Well.Curves[0].Values[0]);
        Assert.Contains(result.Well.WellInformation, entry => entry.Mnemonic == "NULL" && entry.Value == "-1234" && entry.LineNumber == 9);
    }

    [Fact]
    public void Read_Las12_DoesNotTurnAnEmptyOptionalFieldIntoItsDescription()
    {
        var result = ReadText(Las("1.2").Replace("~Curve Information", "LONG. : Longitude\n~Curve Information"));

        Assert.NotNull(result.Well);
        Assert.Contains(result.Well.WellInformation, entry => entry.Mnemonic == "LONG" && entry.Value == "" && entry.Description == "Longitude");
    }

    [Theory]
    [InlineData(".GR.API : Gamma ray", "GR")]
    [InlineData(".API : Missing mnemonic", "UNNAMED_2")]
    public void Read_DamagedCurveMnemonic_PreservesItsColumnPosition(string definition, string expectedName)
    {
        var result = ReadText(Las(data: "1000 50 12").Replace("GR.API : Gamma ray", definition));

        Assert.NotNull(result.Well);
        Assert.Equal(2, result.Well.Curves.Length);
        Assert.Equal(expectedName, result.Well.Curves[0].Mnemonic);
        Assert.Equal(50.0, result.Well.Curves[0].Values[0]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[0]);
        Assert.Contains(result.Diagnostics, entry => entry.LineNumber.HasValue && entry.Message.Contains("curve"));
    }

    [Fact]
    public void Read_PreservesRepeatedParametersUnitsAndOtherText()
    {
        var text = Las().Replace("~Curve Information", """
            ~Parameter Information
            BHT.DEGC 60 : Bottom hole temperature
            BHT.DEGC 65 : Second run temperature
            NULL. -12 : A parameter, not the file null sentinel
            ~Curve Information
            """).Replace("~ASCII", """
            ~Other Information
            # Keep this comment
            Operator note: retain this text.
            ~ASCII
            """);

        var result = ReadText(text);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        Assert.NotNull(result.Well.ParameterInformation);
        Assert.Equal(3, result.Well.ParameterInformation.Length);
        Assert.Equal("DEGC", result.Well.ParameterInformation[0].Unit);
        Assert.Equal("60", result.Well.ParameterInformation[0].Value);
        Assert.Equal("65", result.Well.ParameterInformation[1].Value);
        Assert.Equal(12, result.Well.ParameterInformation[0].LineNumber);
        Assert.Equal(13, result.Well.ParameterInformation[1].LineNumber);
        Assert.Contains("# Keep this comment", result.Well.OtherInformation);
        Assert.Contains("Operator note: retain this text.", result.Well.OtherInformation);
        Assert.Null(result.Well.Curves[0].Values[0]);
    }

    [Fact]
    public void Read_UnwrappedShortRow_DoesNotShiftFollowingColumns()
    {
        var result = ReadText(Las(data: "1000 50 12\n1000.5 60\n1001 70 15"));

        Assert.NotNull(result.Well);
        Assert.Equal(new[] { 1000.0, 1001.0 }, result.Well.Depths);
        Assert.Equal(new double?[] { 50, 70 }, result.Well.Curves[0].Values);
        Assert.Equal(new double?[] { 12, 15 }, result.Well.Curves[1].Values);
        Assert.Contains(result.Diagnostics, entry => entry.LineNumber.HasValue && entry.Message.Contains("3 values"));
    }

    [Theory]
    [InlineData("1.2")]
    [InlineData("2.0")]
    public void Read_WrappedData_AccumulatesOneRecordAcrossLines(string version)
    {
        var result = ReadText(Las(version, wrap: "YES", data: "1000\n50\n12\n# Comment\n1000.5\n-9999 15\n1001"));

        Assert.NotNull(result.Well);
        Assert.Equal(new[] { 1000.0, 1000.5 }, result.Well.Depths);
        Assert.Equal(new double?[] { 50, null }, result.Well.Curves[0].Values);
        Assert.Contains(result.Diagnostics, entry => entry.LineNumber.HasValue && entry.Message.Contains("Incomplete wrapped"));
    }

    [Fact]
    public void Read_WrappedData_AmbiguousSingleValue_StopsWithoutShiftingDepths()
    {

        var data = "1000\n1001\n60\n1002";
        var text = Las("2.0", wrap: "YES", data: data)
            .Replace("STEP.M 0.5", "STEP.M 1.0")
            .Replace("GR.API : Gamma ray\n", "");
        var result = ReadText(text);

        Assert.Null(result.Well);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("Ambiguous wrapped record"));
    }

    [Theory]
    [InlineData("1000\n1100 10\n1001\n1101 20")]
    [InlineData("1000\n1100\n10\n1001\n1101\n20")]
    public void Read_WrappedData_LargeMeasurements_AreNotTreatedAsDepths(string data)
    {
        var result = ReadText(Las(wrap: "YES", data: data).Replace("STEP.M 0.5", "STEP.M 1.0"));

        Assert.NotNull(result.Well);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(new[] { 1000.0, 1001.0 }, result.Well.Depths);
        Assert.Equal(new double?[] { 1100, 1101 }, result.Well.Curves[0].Values);
        Assert.Equal(new double?[] { 10, 20 }, result.Well.Curves[1].Values);
    }

    [Fact]
    public void Read_WrappedData_TruncatedWithoutDepthMetadata_DoesNotInventARecord()
    {
        var text = Las(wrap: "YES", data: "1000\n50\n1001\n60 10\n1002\n70 20")
            .Replace("STRT.M 1000 : Start depth", "")
            .Replace("STOP.M 1001 : Stop depth", "")
            .Replace("STEP.M 0.5 : Step", "");

        var result = ReadText(text);

        Assert.Null(result.Well);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("Ambiguous wrapped record"));
    }

    [Fact]
    public void Read_WrappedData_AmbiguityAfterValidRecord_PreservesOnlyCompletedRecords()
    {
        var result = ReadText(Las(wrap: "YES", data: "1000\n50 12\n1000.5\n60\n1001\n70 20"));

        Assert.NotNull(result.Well);
        Assert.Equal(new[] { 1000.0 }, result.Well.Depths);
        Assert.Equal(new double?[] { 50 }, result.Well.Curves[0].Values);
        Assert.Equal(new double?[] { 12 }, result.Well.Curves[1].Values);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("Ambiguous wrapped record"));
    }

    [Fact]
    public void Read_InvalidMeasurement_PreservesTheDepthAndOtherMeasurements()
    {
        var result = ReadText(Las(data: "1000 broken 12"));

        Assert.NotNull(result.Well);
        Assert.Equal(new[] { 1000.0 }, result.Well.Depths);
        Assert.Null(result.Well.Curves[0].Values[0]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[0]);
        Assert.Contains(result.Diagnostics, entry => entry.LineNumber.HasValue && entry.Message.Contains("GR"));
    }

    [Fact]
    public void Read_ManyDamagedRows_BoundsDiagnosticsWithoutStoppingDataLoading()
    {
        var data = string.Join('\n', Enumerable.Repeat("1000 broken 12", 1005));

        var result = ReadText(Las(data: data));

        Assert.NotNull(result.Well);
        Assert.Equal(1005, result.Well.Depths.Length);
        Assert.InRange(result.Diagnostics.Length, 2, 1001);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("Suppressed 5 additional diagnostics"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("broken")]
    [InlineData("-9999")]
    public void Read_InvalidOrMissingDepth_SkipsTheRow(string depth)
    {
        var result = ReadText(Las(data: $"{depth} 50 12\n1001 70 15"));

        Assert.NotNull(result.Well);
        Assert.Equal(new[] { 1001.0 }, result.Well.Depths);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Read_UsesInvariantNumbersEvenWithFrenchCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var result = ReadText(Las(data: "1000.5 1.25D+01 0.125"));

            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Well);
            Assert.Equal(1000.5, result.Well.Depths[0]);
            Assert.Equal(12.5, result.Well.Curves[0].Values[0]);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("1.3")]
    [InlineData("4.0")]
    [InlineData("broken")]
    public void Read_UnsupportedOrInvalidVersion_ReturnsFailure(string version)
    {
        var result = ReadText(Las(version));

        Assert.Null(result.Well);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("VERS"));
    }

    [Fact]
    public void Read_MissingNull_DoesNotGuessAMissingValue()
    {
        var result = ReadText(Las(data: "1000 -999.25 12").Replace("NULL. -9999 : Missing value", ""));

        Assert.NotNull(result.Well);
        Assert.Equal(-999.25, result.Well.Curves[0].Values[0]);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("NULL"));
    }

    [Fact]
    public void Read_InvalidNull_ReturnsFailure()
    {
        var result = ReadText(Las(nullValue: "broken"));

        Assert.Null(result.Well);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("NULL"));
    }

    [Fact]
    public void Read_ConflictingNullDeclarations_ReturnsFailure()
    {
        var result = ReadText(Las().Replace("NULL. -9999 : Missing value", "NULL. -9999 : Missing value\nNULL. 0 : Conflicting value"));

        Assert.Null(result.Well);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("NULL"));
    }

    [Theory]
    [InlineData("COMMA", "1000,-9999,12")]
    [InlineData("TAB", "1000\t-9999\t12")]
    public void Read_Las30_UsesTheDeclaredDelimiter(string delimiter, string data)
    {
        var result = ReadText(Las("3.0", delimiter: delimiter, data: data));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        Assert.Null(result.Well.Curves[0].Values[0]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[0]);
    }

    [Fact]
    public void Read_Las30_EmptyCommaCell_DoesNotShiftColumns()
    {
        var result = ReadText(Las("3.0", delimiter: "COMMA", data: "1000,,12"));

        Assert.NotNull(result.Well);
        Assert.Null(result.Well.Curves[0].Values[0]);
        Assert.Equal(12.0, result.Well.Curves[1].Values[0]);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Read_Las30_GroupedLogSections_UseTheirCurveDefinitions()
    {
        var text = Las("3.0")
            .Replace("~Curve Information", "~Log_Definition")
            .Replace("~ASCII", "~Log_Data | Log_Definition");

        var result = ReadText(text);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
        Assert.Equal(2, result.Well.Curves.Length);
    }

    [Fact]
    public void Read_Las30_MismatchedDefinitionReference_ReturnsFailure()
    {
        var result = ReadText(Las("3.0").Replace("~ASCII", "~ASCII | Core_Definition"));

        Assert.Null(result.Well);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Theory]
    [InlineData("YES", "SPACE")]
    [InlineData("NO", "SEMICOLON")]
    public void Read_Las30_UnsupportedSettings_ReturnFailure(string wrap, string delimiter)
    {
        var result = ReadText(Las("3.0", wrap: wrap, delimiter: delimiter));

        Assert.Null(result.Well);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Read_Las30_NonNumericCurveFormat_ReturnsAnExplicitLimitation()
    {
        var result = ReadText(Las("3.0").Replace("GR.API : Gamma ray", "GR.API : Label {S}"));

        Assert.Null(result.Well);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_Las30_ArrayCurve_ReturnsAnExplicitLimitation()
    {
        var result = ReadText(Las("3.0").Replace("GR.API : Gamma ray", "GR[1].API : Gamma ray {F}"));

        Assert.Null(result.Well);
        Assert.Contains(result.Diagnostics, entry => entry.Message.Contains("array", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Read_AdditionalDataSection_IsNotMixedIntoTheLog()
    {
        var result = ReadText(Las("3.0") + "\n~Core_Data | Core_Definition\n1001 70 15");

        Assert.NotNull(result.Well);
        Assert.Single(result.Well.Depths);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public void Read_ShortLowercaseSectionNames_AreRecognized()
    {
        var text = Las().Replace("~Version Information", "~v")
            .Replace("~Well Information", "~w")
            .Replace("~Curve Information", "~c")
            .Replace("~ASCII", "~a");

        var result = ReadText(text);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Well);
    }

    [Fact]
    public void Read_NoDataSection_ReturnsFailure()
    {
        var result = ReadText(Las().Split("~ASCII")[0]);

        Assert.Null(result.Well);
        Assert.NotEmpty(result.Diagnostics);
    }

    private static string Las(
        string version = "2.0",
        string wrap = "NO",
        string nullValue = "-9999",
        string delimiter = "SPACE",
        string data = "1000 -9999 12") => $"""
        ~Version Information
        VERS. {version} : LAS version
        WRAP. {wrap} : Wrapped data
        DLM. {delimiter} : Data delimiter
        ~Well Information
        STRT.M 1000 : Start depth
        STOP.M 1001 : Stop depth
        STEP.M 0.5 : Step
        NULL. {nullValue} : Missing value
        WELL. DEMO WELL : Well name
        ~Curve Information
        DEPT.M : Depth
        GR.API : Gamma ray
        RT.OHMM : Resistivity
        ~ASCII
        {data}
        """;

    private static LasLoadResult ReadText(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"petrores-{Guid.NewGuid():N}.las");
        File.WriteAllText(path, contents);
        try
        {
            return new LasReader().Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
