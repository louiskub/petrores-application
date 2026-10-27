using System.Text.Json;
using System.Text.Json.Serialization;

namespace Petrores.Core;

public enum DepthOrder
{
    Empty,
    SinglePoint,
    Ascending,
    Descending,
    Mixed
}

public enum WellMeasurementType
{
    Depth,
    GammaRay,
    ResistivityDeep,
    ResistivityMedium,
    ResistivityShallow,
    Resistivity,
    BulkDensity,
    NeutronPorosity,
    DensityPorosity,
    SonicPorosity,
    CrossplotPorosity,
    Porosity,
    DensityCorrection,
    Photoelectric,
    Sonic,
    Caliper,
    SpontaneousPotential,
    Tension,
    Temperature,
    RateOfPenetration,
    MudWeight,
    GasTotal,
    Unknown
}

public enum UnitConversionStatus
{
    Converted,
    AlreadyStandard,
    NormalizedMnemonicOnly,
    UnknownUnitSkipped,
    NoConversionNeeded
}

public sealed record DuplicateDepthDetail(
    double Depth,
    int DuplicateRowCount,
    Dictionary<string, double?> MergedValues,
    double? ConditionedDepth = null
)
{
    [JsonIgnore]
    public int GroupRowCount => DuplicateRowCount;

    [JsonIgnore]
    public int MergedRowCount => Math.Max(0, DuplicateRowCount - 1);
}

public sealed record DepthConditioningReport(
    DepthOrder OriginalOrder,
    bool WasReordered,
    bool IsReversed,
    int OriginalRowCount,
    int ConditionedRowCount,
    int DuplicateRowCount,
    int DuplicateGroupCount,
    List<DuplicateDepthDetail> DuplicateDetails,
    string OriginalDepthUnit,
    string ConditionedDepthUnit,
    double DepthUnitConversionFactor,
    double? StartDepth,
    double? StopDepth,
    double? Step,
    double? MinStep,
    double? MaxStep,
    bool IsStepUniform,
    int GapCount
);

public sealed record CurveConditioningReport(
    string OriginalMnemonic,
    WellMeasurementType MappedMeasurement,
    string MeasurementDisplayName,
    string OriginalUnit,
    string ConditionedUnit,
    double UnitConversionFactor,
    UnitConversionStatus UnitConversionStatus,
    string? UnitConversionNote,
    int TotalCount,
    int NullCount,
    int ValidCount,
    double NullPercentage,
    double? FirstValidDepth,
    double? LastValidDepth,
    int ValuesConvertedCount
);

public sealed record PrimaryCurveSelection(
    string TrackName,
    string? SelectedMnemonic,
    WellMeasurementType Measurement,
    string Reason,
    int Priority,
    double CoveragePercentage,
    List<string> CandidateMnemonics
);

public sealed record WellConditioningReport(
    string SourcePath,
    string WellName,
    string LoadStatus,
    LasDiagnostic[] Diagnostics,
    DepthConditioningReport Depth,
    string MissingValuesPolicy,
    int TotalPoints,
    int TotalMissingPoints,
    double OverallMissingPercentage,
    List<CurveConditioningReport> Curves,
    Dictionary<string, PrimaryCurveSelection> PrimaryCurves,
    List<string> Warnings
)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, indented ? JsonOptions : new JsonSerializerOptions(JsonOptions) { WriteIndented = false });
}

public sealed record BatchConditioningReport(
    DateTime GeneratedAtUtc,
    int TotalFiles,
    int CleanCount,
    int WarningCount,
    int FailedCount,
    int TotalReversedFiles,
    int TotalDuplicateDepthFiles,
    int TotalDuplicateDepthRowsMerged,
    List<WellConditioningReport> Wells
)
{
    public string ToJson(bool indented = true) =>
        JsonSerializer.Serialize(this, indented ? WellConditioningReport.JsonOptions : new JsonSerializerOptions(WellConditioningReport.JsonOptions) { WriteIndented = false });

    public void SaveJson(string filePath, bool indented = true)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(filePath, ToJson(indented));
    }
}

public sealed record ConditioningOptions(
    bool StandardizeDepthToMeters = true,
    bool StandardizeDensity = true,
    bool StandardizePorosity = true,
    bool NormalizeUnits = true,
    bool SortDepthAscending = true,
    bool DeduplicateDepths = true,
    double DuplicateTolerance = 1e-6,
    double GapStepMultiplier = 2.0
);

public sealed record WellConditionResult(
    Well OriginalWell,
    Well ConditionedWell,
    WellConditioningReport Report
);
