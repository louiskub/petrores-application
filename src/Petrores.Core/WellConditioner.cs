namespace Petrores.Core;

public static class WellConditioner
{
    public static WellConditionResult SortByDepth(Well well) =>
        Condition(well, options: new ConditioningOptions(
            StandardizeDepthToMeters: false,
            StandardizeDensity: false,
            StandardizePorosity: false,
            NormalizeUnits: false));

    public static WellConditionResult Condition(
        Well well,
        string sourcePath = "",
        ConditioningOptions? options = null,
        LasDiagnostic[]? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(well);
        options ??= new ConditioningOptions();
        diagnostics ??= [];

        var warnings = new List<string>();
        foreach (var diag in diagnostics)
        {
            if (diag.Message.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                diag.Message.Contains("skipped", StringComparison.OrdinalIgnoreCase) ||
                diag.Message.Contains("missing", StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(diag.LineNumber is { } ln ? $"Line {ln}: {diag.Message}" : diag.Message);
            }
        }

        var rawDepthCount = well.Depths.Length;
        foreach (var curve in well.Curves)
        {
            if (curve.Values.Length != rawDepthCount)
            {
                throw new InvalidOperationException(
                    $"Curve '{curve.Mnemonic}' has {curve.Values.Length} values, but well has {rawDepthCount} depth rows.");
            }
        }

        var nullSentinel = ExtractNullSentinel(well);

        var sanitizedCurves = new Curve[well.Curves.Length];
        for (var c = 0; c < well.Curves.Length; c++)
        {
            var rawCurve = well.Curves[c];
            var cleanVals = new double?[rawCurve.Values.Length];
            for (var i = 0; i < rawCurve.Values.Length; i++)
            {
                cleanVals[i] = IsMissingValue(rawCurve.Values[i], nullSentinel) ? null : rawCurve.Values[i];
            }
            sanitizedCurves[c] = new Curve(rawCurve.Mnemonic, rawCurve.Unit, rawCurve.Description, cleanVals);
        }

        var originalOrder = AnalyzeDepthOrder(well.Depths);

        var (sortedDepths, sortedCurves, wasReordered, isReversed, dupRowCount, dupGroupCount, dupDetails) =
            SortAndDeduplicate(well.Depths, sanitizedCurves, originalOrder, options);

        if (isReversed)
        {
            warnings.Add("Depth values were originally descending; reversed to ascending (shallow-to-deep).");
        }
        else if (wasReordered)
        {
            warnings.Add($"Depth values were originally {originalOrder}; sorted to ascending (shallow-to-deep).");
        }

        if (dupGroupCount > 0)
        {
            warnings.Add($"Found {dupGroupCount} duplicate depth points ({dupRowCount} duplicate rows merged via averaging).");
        }

        var (step, minStep, maxStep, isUniform, gapCount) = AnalyzeSampling(sortedDepths, options.GapStepMultiplier);
        if (gapCount > 0)
        {
            warnings.Add($"Detected {gapCount} depth gap(s) exceeding {options.GapStepMultiplier}x median step.");
        }

        var (finalDepths, depthUnit, depthFactor, depthStatus, depthNote) =
            UnitConverter.ConvertDepth(sortedDepths, well.DepthUnit, options);

        if (depthStatus == UnitConversionStatus.Converted)
        {
            warnings.Add(depthNote ?? $"Converted depth unit from {well.DepthUnit} to {depthUnit}");
            if (step.HasValue) step *= depthFactor;
            if (minStep.HasValue) minStep *= depthFactor;
            if (maxStep.HasValue) maxStep *= depthFactor;

            dupDetails = dupDetails.Select(d => d with { ConditionedDepth = Math.Round(d.Depth * depthFactor, 4) }).ToList();
        }

        var aliasDict = CurveAliasDictionary.Default;
        var conditionedCurves = new List<Curve>();
        var curveReports = new List<CurveConditioningReport>();

        var totalPoints = finalDepths.Length * sortedCurves.Length;
        var totalMissing = 0;

        foreach (var curve in sortedCurves)
        {
            var (measurement, priority) = aliasDict.ResolveWithPriority(curve.Mnemonic);
            var convResult = UnitConverter.ConvertCurve(curve.Values, curve.Unit, measurement, options);

            if (convResult.Status == UnitConversionStatus.Converted)
            {
                warnings.Add(convResult.Note ?? $"Converted curve '{curve.Mnemonic}' unit from {curve.Unit} to {convResult.ConditionedUnit}");
            }
            else if (convResult.Status == UnitConversionStatus.UnknownUnitSkipped && !string.IsNullOrWhiteSpace(curve.Unit))
            {
                warnings.Add(convResult.Note ?? $"Retained unknown unit '{curve.Unit}' for curve '{curve.Mnemonic}'");
            }

            var conditionedCurve = new Curve(
                curve.Mnemonic,
                convResult.ConditionedUnit,
                curve.Description,
                convResult.ConvertedValues);
            conditionedCurves.Add(conditionedCurve);

            var nullCount = 0;
            double? firstValid = null;
            double? lastValid = null;

            for (var i = 0; i < conditionedCurve.Values.Length; i++)
            {
                if (conditionedCurve.Values[i] is { } val && !double.IsNaN(val) && double.IsFinite(val))
                {
                    firstValid ??= finalDepths[i];
                    lastValid = finalDepths[i];
                }
                else
                {
                    nullCount++;
                }
            }

            totalMissing += nullCount;
            var validCount = conditionedCurve.Values.Length - nullCount;
            var nullPct = conditionedCurve.Values.Length > 0
                ? (nullCount * 100.0) / conditionedCurve.Values.Length
                : 0.0;

            curveReports.Add(new CurveConditioningReport(
                OriginalMnemonic: curve.Mnemonic,
                MappedMeasurement: measurement,
                MeasurementDisplayName: CurveAliasDictionary.GetDisplayName(measurement),
                OriginalUnit: curve.Unit,
                ConditionedUnit: convResult.ConditionedUnit,
                UnitConversionFactor: convResult.Factor,
                UnitConversionStatus: convResult.Status,
                UnitConversionNote: convResult.Note,
                TotalCount: conditionedCurve.Values.Length,
                NullCount: nullCount,
                ValidCount: validCount,
                NullPercentage: Math.Round(nullPct, 2),
                FirstValidDepth: firstValid,
                LastValidDepth: lastValid,
                ValuesConvertedCount: convResult.ConvertedCount));
        }

        var overallNullPct = totalPoints > 0 ? (totalMissing * 100.0) / totalPoints : 0.0;

        var primaryCurves = SelectPrimaryCurves(conditionedCurves, curveReports, aliasDict);

        var depthReport = new DepthConditioningReport(
            OriginalOrder: originalOrder,
            WasReordered: wasReordered,
            IsReversed: isReversed,
            OriginalRowCount: rawDepthCount,
            ConditionedRowCount: finalDepths.Length,
            DuplicateRowCount: dupRowCount,
            DuplicateGroupCount: dupGroupCount,
            DuplicateDetails: dupDetails,
            OriginalDepthUnit: well.DepthUnit,
            ConditionedDepthUnit: depthUnit,
            DepthUnitConversionFactor: depthFactor,
            StartDepth: finalDepths.Length > 0 ? finalDepths[0] : null,
            StopDepth: finalDepths.Length > 0 ? finalDepths[^1] : null,
            Step: step.HasValue ? Math.Round(step.Value, 4) : null,
            MinStep: minStep.HasValue ? Math.Round(minStep.Value, 4) : null,
            MaxStep: maxStep.HasValue ? Math.Round(maxStep.Value, 4) : null,
            IsStepUniform: isUniform,
            GapCount: gapCount);

        var loadStatus = diagnostics.Length == 0 ? "OK" : "WARN";

        var wellReport = new WellConditioningReport(
            SourcePath: sourcePath,
            WellName: well.Name,
            LoadStatus: loadStatus,
            Diagnostics: diagnostics,
            Depth: depthReport,
            MissingValuesPolicy: "Kept as null; no interpolation or imputation applied",
            TotalPoints: totalPoints,
            TotalMissingPoints: totalMissing,
            OverallMissingPercentage: Math.Round(overallNullPct, 2),
            Curves: curveReports,
            PrimaryCurves: primaryCurves,
            Warnings: warnings);

        var conditionedWell = new Well(
            VersionInformation: well.VersionInformation,
            WellInformation: well.WellInformation,
            ParameterInformation: well.ParameterInformation,
            OtherInformation: well.OtherInformation,
            Depths: finalDepths,
            Curves: conditionedCurves.ToArray(),
            DepthUnit: depthUnit);

        return new WellConditionResult(well, conditionedWell, wellReport);
    }

    private static DepthOrder AnalyzeDepthOrder(double[] depths)
    {
        if (depths.Length == 0) return DepthOrder.Empty;
        if (depths.Length == 1) return DepthOrder.SinglePoint;

        var isNonDecreasing = true;
        var isNonIncreasing = true;

        for (var i = 0; i < depths.Length - 1; i++)
        {
            if (depths[i] > depths[i + 1])
                isNonDecreasing = false;
            if (depths[i] < depths[i + 1])
                isNonIncreasing = false;
        }

        if (isNonDecreasing) return DepthOrder.Ascending;
        if (isNonIncreasing) return DepthOrder.Descending;
        return DepthOrder.Mixed;
    }

    private static (
        double[] Depths,
        Curve[] Curves,
        bool WasReordered,
        bool IsReversed,
        int DuplicateRowCount,
        int DuplicateGroupCount,
        List<DuplicateDepthDetail> DuplicateDetails)
    SortAndDeduplicate(
        double[] rawDepths,
        Curve[] rawCurves,
        DepthOrder originalOrder,
        ConditioningOptions options)
    {
        var n = rawDepths.Length;
        if (n == 0)
        {
            return (rawDepths, rawCurves, false, false, 0, 0, []);
        }

        var indices = new int[n];
        for (var i = 0; i < n; i++) indices[i] = i;

        var isReversed = originalOrder == DepthOrder.Descending && options.SortDepthAscending;

        if (options.SortDepthAscending)
        {

            Array.Sort(indices, (a, b) =>
            {
                var cmp = rawDepths[a].CompareTo(rawDepths[b]);
                return cmp != 0 ? cmp : a.CompareTo(b);
            });
        }

        var wasReordered = false;
        for (var i = 0; i < n; i++)
        {
            if (indices[i] != i)
            {
                wasReordered = true;
                break;
            }
        }

        if (!options.DeduplicateDepths)
        {
            var sDepths = new double[n];
            for (var i = 0; i < n; i++) sDepths[i] = rawDepths[indices[i]];

            var sCurves = new Curve[rawCurves.Length];
            for (var c = 0; c < rawCurves.Length; c++)
            {
                var vals = new double?[n];
                for (var i = 0; i < n; i++) vals[i] = rawCurves[c].Values[indices[i]];
                sCurves[c] = new Curve(rawCurves[c].Mnemonic, rawCurves[c].Unit, rawCurves[c].Description, vals);
            }
            return (sDepths, sCurves, wasReordered, isReversed, 0, 0, []);
        }

        var outDepths = new List<double>();
        var outCurvesValues = rawCurves.Select(_ => new List<double?>()).ToArray();
        var dupDetails = new List<DuplicateDepthDetail>();
        var dupRowCount = 0;
        var dupGroupCount = 0;

        var start = 0;
        while (start < n)
        {
            var end = start + 1;
            while (end < n && Math.Abs(rawDepths[indices[end]] - rawDepths[indices[start]]) <= options.DuplicateTolerance)
            {
                end++;
            }

            var groupSize = end - start;
            double depthVal;
            if (groupSize == 1)
            {
                depthVal = rawDepths[indices[start]];
            }
            else
            {
                var sum = 0.0;
                for (var g = start; g < end; g++)
                    sum += rawDepths[indices[g]];
                depthVal = sum / groupSize;
            }
            outDepths.Add(depthVal);

            if (groupSize == 1)
            {
                var idx = indices[start];
                for (var c = 0; c < rawCurves.Length; c++)
                {
                    outCurvesValues[c].Add(rawCurves[c].Values[idx]);
                }
            }
            else
            {
                dupGroupCount++;
                dupRowCount += (groupSize - 1);
                var mergedValues = new Dictionary<string, double?>();

                for (var c = 0; c < rawCurves.Length; c++)
                {
                    var curve = rawCurves[c];
                    var validValues = new List<double>();

                    for (var g = start; g < end; g++)
                    {
                        var v = curve.Values[indices[g]];
                        if (v is { } val && !double.IsNaN(val) && double.IsFinite(val))
                        {
                            validValues.Add(val);
                        }
                    }

                    double? merged;
                    if (validValues.Count == 0)
                    {
                        merged = null;
                    }
                    else
                    {
                        merged = validValues.Average();
                    }

                    outCurvesValues[c].Add(merged);
                    mergedValues[curve.Mnemonic] = merged;
                }

                dupDetails.Add(new DuplicateDepthDetail(depthVal, groupSize, mergedValues));
            }

            start = end;
        }

        var resultCurves = new Curve[rawCurves.Length];
        for (var c = 0; c < rawCurves.Length; c++)
        {
            resultCurves[c] = new Curve(
                rawCurves[c].Mnemonic,
                rawCurves[c].Unit,
                rawCurves[c].Description,
                outCurvesValues[c].ToArray());
        }

        return (
            outDepths.ToArray(),
            resultCurves,
            wasReordered,
            isReversed,
            dupRowCount,
            dupGroupCount,
            dupDetails);
    }

    private static (double? Step, double? MinStep, double? MaxStep, bool IsUniform, int GapCount)
        AnalyzeSampling(double[] depths, double gapMultiplier)
    {
        if (depths.Length < 2)
            return (null, null, null, true, 0);

        var deltas = new List<double>(depths.Length - 1);
        for (var i = 0; i < depths.Length - 1; i++)
        {
            var d = depths[i + 1] - depths[i];
            if (d > 0) deltas.Add(d);
        }

        if (deltas.Count == 0)
            return (null, null, null, true, 0);

        deltas.Sort();
        var median = deltas[deltas.Count / 2];
        var min = deltas[0];
        var max = deltas[^1];

        var tolerance = Math.Max(median * 0.05, 1e-5);
        var isUniform = deltas.All(d => Math.Abs(d - median) <= tolerance);

        var gapThreshold = median * gapMultiplier;
        var gapCount = deltas.Count(d => d > gapThreshold);

        return (median, min, max, isUniform, gapCount);
    }

    private static Dictionary<string, PrimaryCurveSelection> SelectPrimaryCurves(
        List<Curve> curves,
        List<CurveConditioningReport> curveReports,
        CurveAliasDictionary aliasDict)
    {
        var selections = new Dictionary<string, PrimaryCurveSelection>();

        selections["Track1_GammaRay"] = SelectCandidate(
            "Track 1 (Gamma Ray)",
            WellMeasurementType.GammaRay,
            curves, curveReports, aliasDict,
            m => m == WellMeasurementType.GammaRay);

        selections["Track2_Resistivity"] = SelectCandidate(
            "Track 2 (Resistivity)",
            WellMeasurementType.ResistivityDeep,
            curves, curveReports, aliasDict,
            m => m is WellMeasurementType.ResistivityDeep
                 or WellMeasurementType.Resistivity
                 or WellMeasurementType.ResistivityMedium
                 or WellMeasurementType.ResistivityShallow);

        selections["Track3_BulkDensity"] = SelectCandidate(
            "Track 3 (Bulk Density)",
            WellMeasurementType.BulkDensity,
            curves, curveReports, aliasDict,
            m => m == WellMeasurementType.BulkDensity);

        selections["Track3_NeutronPorosity"] = SelectCandidate(
            "Track 3 (Neutron Porosity)",
            WellMeasurementType.NeutronPorosity,
            curves, curveReports, aliasDict,
            m => m == WellMeasurementType.NeutronPorosity);

        return selections;
    }

    private static PrimaryCurveSelection SelectCandidate(
        string trackName,
        WellMeasurementType targetType,
        List<Curve> curves,
        List<CurveConditioningReport> curveReports,
        CurveAliasDictionary aliasDict,
        Func<WellMeasurementType, bool> predicate)
    {
        var candidates = new List<(string Mnemonic, int Priority, double Coverage, int ValidCount)>();

        for (var i = 0; i < curves.Count; i++)
        {
            var report = curveReports[i];
            if (predicate(report.MappedMeasurement)
                && CurveAliasDictionary.IsUnitCompatible(targetType, report.OriginalUnit)
                && CurveAliasDictionary.IsUnitCompatible(targetType, report.ConditionedUnit))
            {
                var priority = aliasDict.GetPriority(report.OriginalMnemonic);
                var cov = report.TotalCount > 0 ? (report.ValidCount * 100.0) / report.TotalCount : 0.0;
                candidates.Add((report.OriginalMnemonic, priority, cov, report.ValidCount));
            }
        }

        if (candidates.Count == 0)
        {
            return new PrimaryCurveSelection(
                TrackName: trackName,
                SelectedMnemonic: null,
                Measurement: targetType,
                Reason: "No matching curve found for this measurement",
                Priority: int.MaxValue,
                CoveragePercentage: 0.0,
                CandidateMnemonics: []);
        }

        var candidateNames = candidates.Select(c => c.Mnemonic).ToList();

        var usableCandidates = candidates.Where(c => c.ValidCount > 0).ToList();
        var pool = usableCandidates.Count > 0 ? usableCandidates : candidates;

        pool.Sort((a, b) =>
        {
            var pCmp = a.Priority.CompareTo(b.Priority);
            if (pCmp != 0) return pCmp;
            var covCmp = b.Coverage.CompareTo(a.Coverage);
            if (covCmp != 0) return covCmp;
            return b.ValidCount.CompareTo(a.ValidCount);
        });

        var chosen = pool[0];
        var reason = candidates.Count == 1
            ? $"Only candidate matching {targetType} with {chosen.Coverage:F1}% valid points"
            : (usableCandidates.Count > 0 && usableCandidates.Count < candidates.Count)
                ? $"Selected '{chosen.Mnemonic}' with {chosen.Coverage:F1}% usable data over empty alternative(s)"
                : $"Selected among [{string.Join(", ", candidateNames)}] by highest priority (rank {chosen.Priority}) and {chosen.Coverage:F1}% coverage";

        return new PrimaryCurveSelection(
            TrackName: trackName,
            SelectedMnemonic: chosen.Mnemonic,
            Measurement: targetType,
            Reason: reason,
            Priority: chosen.Priority,
            CoveragePercentage: Math.Round(chosen.Coverage, 2),
            CandidateMnemonics: candidateNames);
    }

    public static double? ExtractNullSentinel(Well well)
    {
        if (well.WellInformation is not null)
        {
            foreach (var entry in well.WellInformation)
            {
                if (entry.Mnemonic.Equals("NULL", StringComparison.OrdinalIgnoreCase))
                {
                    var valStr = entry.Value;
                    if (string.IsNullOrWhiteSpace(valStr))
                        valStr = entry.Description;
                    if (double.TryParse(valStr.Replace('D', 'E').Replace('d', 'e'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nullVal) && double.IsFinite(nullVal))
                    {
                        return nullVal;
                    }
                }
            }
        }
        return -999.25;
    }

    public static bool IsMissingValue(double? val, double? nullSentinel, double epsilon = 1e-4)
    {
        if (!val.HasValue) return true;
        var v = val.Value;
        if (double.IsNaN(v) || !double.IsFinite(v)) return true;
        if (nullSentinel.HasValue)
        {
            if (v == nullSentinel.Value) return true;
            if (nullSentinel.Value != 0 && Math.Abs(v - nullSentinel.Value) <= epsilon) return true;
        }
        if (Math.Abs(v - (-999.25)) <= epsilon) return true;
        return false;
    }
}
