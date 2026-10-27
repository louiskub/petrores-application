namespace Petrores.Core;

public sealed record UnitConversionResult(
    string OriginalUnit,
    string ConditionedUnit,
    double Factor,
    UnitConversionStatus Status,
    string? Note,
    int ConvertedCount,
    double?[] ConvertedValues
);

public static class UnitConverter
{
    public static (double[] Depths, string NewUnit, double Factor, UnitConversionStatus Status, string? Note)
        ConvertDepth(double[] depths, string originalUnit, ConditioningOptions options)
    {
        var unitClean = (originalUnit ?? string.Empty).Trim();
        var unitUpper = unitClean.ToUpperInvariant();

        if (!options.StandardizeDepthToMeters)
        {
            return (depths, unitClean, 1.0, UnitConversionStatus.NoConversionNeeded, null);
        }

        if (unitUpper is "FT" or "F" or "FEET" or "FOOT" or ".FT" or "FTS" or "FT.")
        {
            const double factor = 0.3048;
            var converted = new double[depths.Length];
            for (var i = 0; i < depths.Length; i++)
                converted[i] = depths[i] * factor;

            return (converted, "m", factor, UnitConversionStatus.Converted, $"Converted depth from {unitClean} to m (factor 0.3048)");
        }

        if (unitUpper is "M" or "METER" or "METERS" or "METRE" or "METRES" or "MTR" or "M.")
        {
            return (depths, "m", 1.0, UnitConversionStatus.AlreadyStandard, "Depth already in meters; normalized unit symbol to m");
        }

        if (string.IsNullOrEmpty(unitClean))
        {
            return (depths, string.Empty, 1.0, UnitConversionStatus.UnknownUnitSkipped, "Depth unit is blank; retained original depth values without scaling");
        }

        return (depths, unitClean, 1.0, UnitConversionStatus.UnknownUnitSkipped, $"Unknown depth unit '{unitClean}'; retained original depth values without scaling");
    }

    public static UnitConversionResult ConvertCurve(
        double?[] values,
        string originalUnit,
        WellMeasurementType measurement,
        ConditioningOptions options)
    {
        var unitClean = (originalUnit ?? string.Empty).Trim();
        var unitUpper = unitClean.ToUpperInvariant();

        switch (measurement)
        {
            case WellMeasurementType.BulkDensity:
            case WellMeasurementType.DensityCorrection:
                if (options.StandardizeDensity)
                {

                    if (unitUpper is "KG/M3" or "KG/M^3" or "KGM3" or "K/M3" or "KG/M³" or "K/M^3")
                    {
                        const double factor = 0.001;
                        var (newVals, count) = ScaleValues(values, factor);
                        return new(unitClean, "g/cm3", factor, UnitConversionStatus.Converted,
                            $"Converted bulk density from {unitClean} to g/cm3 (divided by 1000)", count, newVals);
                    }

                    if (unitUpper is "G/C3" or "G/CC" or "G/CM3" or "GM/CC" or "GM/C3" or "G/CM³" or "G/C" or "GR/CC" or "GM/CM3" or "GM/CM^3" or "G/CUBIC CM")
                    {
                        return new(unitClean, "g/cm3", 1.0, UnitConversionStatus.AlreadyStandard,
                            $"Bulk density already in {unitClean}; normalized unit symbol to g/cm3", 0, values);
                    }

                    if (string.IsNullOrEmpty(unitClean))
                    {
                        return new(unitClean, string.Empty, 1.0, UnitConversionStatus.UnknownUnitSkipped,
                            "Bulk density unit is blank; kept original values without scaling", 0, values);
                    }

                    return new(unitClean, unitClean, 1.0, UnitConversionStatus.UnknownUnitSkipped,
                        $"Unknown bulk density unit '{unitClean}'; kept original values without scaling", 0, values);
                }
                break;

            case WellMeasurementType.NeutronPorosity:
            case WellMeasurementType.DensityPorosity:
            case WellMeasurementType.SonicPorosity:
            case WellMeasurementType.CrossplotPorosity:
            case WellMeasurementType.Porosity:
                if (options.StandardizePorosity)
                {

                    if (unitUpper is "%" or "PERC" or "PERCENT" or "PCT" or "PCNT" or "PU" or "P.U." or "LPU" or "SPU")
                    {
                        var displayName = CurveAliasDictionary.GetDisplayName(measurement).ToLowerInvariant();
                        const double factor = 0.01;
                        var (newVals, count) = ScaleValues(values, factor);
                        return new(unitClean, "v/v", factor, UnitConversionStatus.Converted,
                            $"Converted {displayName} from {unitClean} to volume fraction v/v (divided by 100)", count, newVals);
                    }

                    if (unitUpper is "V/V" or "DEC" or "DECP" or "DECI" or "FRAC" or "FRACTION" or "CFCF" or "FT3/FT3" or "M3/M3" or "C3/C3")
                    {
                        var displayName = CurveAliasDictionary.GetDisplayName(measurement).ToLowerInvariant();
                        return new(unitClean, "v/v", 1.0, UnitConversionStatus.AlreadyStandard,
                            $"{displayName} already in volume fraction {unitClean}; normalized unit symbol to v/v", 0, values);
                    }

                    if (string.IsNullOrEmpty(unitClean))
                    {
                        return new(unitClean, string.Empty, 1.0, UnitConversionStatus.UnknownUnitSkipped,
                            "Porosity unit is blank; kept original values without scaling", 0, values);
                    }

                    return new(unitClean, unitClean, 1.0, UnitConversionStatus.UnknownUnitSkipped,
                        $"Unknown porosity unit '{unitClean}'; kept original values without scaling", 0, values);
                }
                break;

            case WellMeasurementType.GammaRay:
                if (options.NormalizeUnits)
                {
                    if (unitUpper is "GAPI" or "API" or "GAP" or "AAPI")
                    {
                        return new(unitClean, "gAPI", 1.0, UnitConversionStatus.NormalizedMnemonicOnly,
                            $"Gamma ray unit normalized to gAPI", 0, values);
                    }
                }
                break;

            case WellMeasurementType.ResistivityDeep:
            case WellMeasurementType.ResistivityMedium:
            case WellMeasurementType.ResistivityShallow:
            case WellMeasurementType.Resistivity:
                if (options.NormalizeUnits)
                {
                    if (unitUpper is "OHMM" or "OHM-M" or "OHM.M" or "OHMS" or "OHMMETER" or "OHM")
                    {
                        return new(unitClean, "ohm.m", 1.0, UnitConversionStatus.NormalizedMnemonicOnly,
                            $"Resistivity unit normalized to ohm.m", 0, values);
                    }
                }
                break;

            case WellMeasurementType.Caliper:
                if (options.NormalizeUnits)
                {
                    if (unitUpper is "IN" or "INCH" or "INCHES" or "\"")
                    {
                        return new(unitClean, "in", 1.0, UnitConversionStatus.NormalizedMnemonicOnly,
                            $"Caliper unit normalized to in", 0, values);
                    }
                    if (unitUpper is "MM")
                    {
                        const double factor = 1.0 / 25.4;
                        var (newVals, count) = ScaleValues(values, factor);
                        return new(unitClean, "in", factor, UnitConversionStatus.Converted,
                            $"Converted caliper from mm to in", count, newVals);
                    }
                }
                break;

            case WellMeasurementType.Sonic:
                if (options.NormalizeUnits)
                {
                    if (unitUpper is "US/F" or "US/FT" or "USEC/FT" or "USEC/F")
                    {
                        return new(unitClean, "us/ft", 1.0, UnitConversionStatus.NormalizedMnemonicOnly,
                            $"Sonic unit normalized to us/ft", 0, values);
                    }
                    if (unitUpper is "US/M" or "US/METER" or "USEC/M")
                    {
                        const double factor = 0.3048;
                        var (newVals, count) = ScaleValues(values, factor);
                        return new(unitClean, "us/ft", factor, UnitConversionStatus.Converted,
                            $"Converted sonic from us/m to us/ft", count, newVals);
                    }
                }
                break;
        }

        return new(unitClean, unitClean, 1.0, UnitConversionStatus.NoConversionNeeded, null, 0, values);
    }

    private static (double?[] Values, int ConvertedCount) ScaleValues(double?[] values, double factor)
    {
        var count = 0;
        var scaled = new double?[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            if (values[i] is { } v && !double.IsNaN(v) && double.IsFinite(v))
            {
                scaled[i] = v * factor;
                count++;
            }
            else
            {
                scaled[i] = null;
            }
        }
        return (scaled, count);
    }
}
