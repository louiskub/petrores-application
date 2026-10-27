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

// Values[i] ต้องเป็นค่าที่ depth ตำแหน่ง i เดียวกัน; null หมายถึง missing value
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

// Well เป็น null เมื่ออ่านไม่ได้; ถ้ามี Well พร้อม Diagnostics หมายถึงอ่านได้พร้อมคำเตือน
public sealed record LasLoadResult(
    string SourcePath,
    Well? Well,
    LasDiagnostic[] Diagnostics
);
