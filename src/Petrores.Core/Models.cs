namespace Petrores.Core;

public sealed record Well(
    LasHeaderEntry[] VersionInformation,
    LasHeaderEntry[] WellInformation,
    LasHeaderEntry[]? ParameterInformation,
    string? OtherInformation,
    double[] Depths,
    Curve[] Curves,
    string DepthUnit
)
{
    public string Name => WellInformation
        .FirstOrDefault(entry => entry.Mnemonic.Equals("WELL", StringComparison.OrdinalIgnoreCase))
        ?.Value ?? string.Empty;
}

public sealed record LasHeaderEntry(
    string Mnemonic,
    string Unit,
    string Value,
    string Description,
    int LineNumber
);

public sealed record Curve(
    string Mnemonic,
    string Unit,
    string Description,
    double?[] Values
);

public sealed record LasDiagnostic(
    int? LineNumber,
    string Message
);

public sealed record LasLoadResult(
    string SourcePath,
    Well? Well,
    LasDiagnostic[] Diagnostics
);
