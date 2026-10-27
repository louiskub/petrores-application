using Petrores.Core;

namespace Petrores.Check;

public static class Program
{
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length != 1)
        {
            error.WriteLine("Usage: dotnet run --project tools/Petrores.Check -- <LAS-folder>");
            return 2;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(args[0])
                .Where(path => Path.GetExtension(path).Equals(".las", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine($"Cannot read LAS folder: {exception.Message}");
            return 2;
        }

        if (files.Length == 0)
        {
            error.WriteLine("No LAS files found directly in the supplied folder.");
            return 2;
        }

        output.WriteLine($"Found {files.Length} LAS files.");
        var reader = new LasReader();
        var clean = 0;
        var warnings = 0;
        var failed = 0;
        var wellReports = new List<WellConditioningReport>();

        foreach (var file in files)
        {
            var result = reader.Read(file);
            string status;
            if (result.Well is null)
            {
                status = "FAIL";
                failed++;

                var failedDepth = new DepthConditioningReport(
                    OriginalOrder: DepthOrder.Empty,
                    WasReordered: false,
                    IsReversed: false,
                    OriginalRowCount: 0,
                    ConditionedRowCount: 0,
                    DuplicateRowCount: 0,
                    DuplicateGroupCount: 0,
                    DuplicateDetails: [],
                    OriginalDepthUnit: string.Empty,
                    ConditionedDepthUnit: string.Empty,
                    DepthUnitConversionFactor: 1.0,
                    StartDepth: null,
                    StopDepth: null,
                    Step: null,
                    MinStep: null,
                    MaxStep: null,
                    IsStepUniform: true,
                    GapCount: 0);

                wellReports.Add(new WellConditioningReport(
                    SourcePath: file,
                    WellName: string.Empty,
                    LoadStatus: "FAIL",
                    Diagnostics: result.Diagnostics,
                    Depth: failedDepth,
                    MissingValuesPolicy: "Kept as null; no interpolation or imputation applied",
                    TotalPoints: 0,
                    TotalMissingPoints: 0,
                    OverallMissingPercentage: 0.0,
                    Curves: [],
                    PrimaryCurves: new Dictionary<string, PrimaryCurveSelection>(),
                    Warnings: result.Diagnostics.Select(d => d.Message).ToList()));
            }
            else if (result.Diagnostics.Length > 0)
            {
                status = "WARN";
                warnings++;
                var conditioned = WellConditioner.Condition(result.Well, file, diagnostics: result.Diagnostics);
                wellReports.Add(conditioned.Report);
            }
            else
            {
                status = "OK";
                clean++;
                var conditioned = WellConditioner.Condition(result.Well, file, diagnostics: result.Diagnostics);
                wellReports.Add(conditioned.Report);
            }

            output.WriteLine($"[{status}] {Path.GetFileName(file)} | rows={result.Well?.Depths.Length ?? 0}; curves={result.Well?.Curves.Length ?? 0}; diagnostics={result.Diagnostics.Length}");
            foreach (var diagnostic in result.Diagnostics)
                output.WriteLine($"  {(diagnostic.LineNumber is { } line ? $"line {line}" : "file")}: {diagnostic.Message}");

            var report = wellReports[^1];
            if (result.Well is not null)
            {
                if (report.Depth.IsReversed)
                    output.WriteLine("  conditioning: depth reversed from descending to ascending (shallow-to-deep)");
                else if (report.Depth.WasReordered)
                    output.WriteLine($"  conditioning: depth order was {report.Depth.OriginalOrder}; sorted ascending");

                if (report.Depth.DuplicateRowCount > 0)
                    output.WriteLine($"  conditioning: merged {report.Depth.DuplicateRowCount} duplicate depth rows across {report.Depth.DuplicateGroupCount} depth levels via averaging");

                if (report.Depth.DepthUnitConversionFactor != 1.0)
                    output.WriteLine($"  units: depth converted from {report.Depth.OriginalDepthUnit} to {report.Depth.ConditionedDepthUnit} (factor {report.Depth.DepthUnitConversionFactor})");

                var convertedCurves = report.Curves.Where(c => c.UnitConversionStatus == UnitConversionStatus.Converted).ToList();
                if (convertedCurves.Count > 0)
                {
                    output.WriteLine($"  units: converted {convertedCurves.Count} curve(s): {string.Join(", ", convertedCurves.Select(c => $"{c.OriginalMnemonic} ({c.OriginalUnit}->{c.ConditionedUnit})"))}");
                }

                var mappedCurves = report.Curves.Where(c => c.MappedMeasurement != WellMeasurementType.Unknown).ToList();
                if (mappedCurves.Count > 0)
                {
                    output.WriteLine($"  curve aliases: mapped {mappedCurves.Count}/{report.Curves.Count} curve(s): {string.Join(", ", mappedCurves.Take(6).Select(c => $"{c.OriginalMnemonic}->{c.MeasurementDisplayName}"))}{(mappedCurves.Count > 6 ? $", +{mappedCurves.Count - 6} more" : "")}");
                }

                output.WriteLine($"  primary curves: Track 1 (GR)='{report.PrimaryCurves.GetValueOrDefault("Track1_GammaRay")?.SelectedMnemonic ?? "none"}'; Track 2 (Res)='{report.PrimaryCurves.GetValueOrDefault("Track2_Resistivity")?.SelectedMnemonic ?? "none"}'; Track 3 (Density)='{report.PrimaryCurves.GetValueOrDefault("Track3_BulkDensity")?.SelectedMnemonic ?? "none"}'; Track 3 (Neutron)='{report.PrimaryCurves.GetValueOrDefault("Track3_NeutronPorosity")?.SelectedMnemonic ?? "none"}'");

                output.WriteLine($"  missing values: policy='{report.MissingValuesPolicy}'; {report.OverallMissingPercentage:F1}% missing ({report.TotalMissingPoints}/{report.TotalPoints} points)");
            }
        }

        output.WriteLine($"Files={files.Length}; OK={clean}; WARN={warnings}; FAIL={failed}");

        // Save batch conditioning report
        var totalReversed = wellReports.Count(r => r.Depth.IsReversed);
        var totalDupFiles = wellReports.Count(r => r.Depth.DuplicateRowCount > 0);
        var totalDupRows = wellReports.Sum(r => r.Depth.DuplicateRowCount);

        var batchReport = new BatchConditioningReport(
            GeneratedAtUtc: DateTime.UtcNow,
            TotalFiles: files.Length,
            CleanCount: clean,
            WarningCount: warnings,
            FailedCount: failed,
            TotalReversedFiles: totalReversed,
            TotalDuplicateDepthFiles: totalDupFiles,
            TotalDuplicateDepthRowsMerged: totalDupRows,
            Wells: wellReports);

        try
        {
            var reportPath = Path.Combine(Directory.GetCurrentDirectory(), "conditioning-report.json");
            batchReport.SaveJson(reportPath, indented: true);
            output.WriteLine($"Conditioning report saved to: {reportPath}");
        }
        catch
        {
            // Non-fatal if report file cannot be written to current directory
        }

        return failed > 0 ? 1 : 0;
    }
}
