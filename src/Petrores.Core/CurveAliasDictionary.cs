using System.Text.RegularExpressions;

namespace Petrores.Core;

public sealed partial class CurveAliasDictionary
{
    private static readonly Lazy<CurveAliasDictionary> defaultInstance = new(() => new CurveAliasDictionary());
    public static CurveAliasDictionary Default => defaultInstance.Value;

    private readonly Dictionary<string, (WellMeasurementType Measurement, int Priority)> aliases =
        new(StringComparer.OrdinalIgnoreCase);

    public CurveAliasDictionary()
    {
        InitializeStandardAliases();
    }

    private void InitializeStandardAliases()
    {

        Add("DEPT", WellMeasurementType.Depth, 1);
        Add("DEPTH", WellMeasurementType.Depth, 2);
        Add("MD", WellMeasurementType.Depth, 3);
        Add("TVD", WellMeasurementType.Depth, 4);

        Add("GR", WellMeasurementType.GammaRay, 1);
        Add("GR_EDTC", WellMeasurementType.GammaRay, 2);
        Add("CGR", WellMeasurementType.GammaRay, 3);
        Add("SGR", WellMeasurementType.GammaRay, 4);
        Add("HGR", WellMeasurementType.GammaRay, 5);
        Add("ECGR", WellMeasurementType.GammaRay, 6);
        Add("GAM", WellMeasurementType.GammaRay, 7);
        Add("GAMMA", WellMeasurementType.GammaRay, 8);
        Add("RGR", WellMeasurementType.GammaRay, 9);
        Add("GRR", WellMeasurementType.GammaRay, 10);
        Add("GRD", WellMeasurementType.GammaRay, 11);
        Add("NGT", WellMeasurementType.GammaRay, 12);
        Add("GR_CDR", WellMeasurementType.GammaRay, 13);
        Add("GRCO", WellMeasurementType.GammaRay, 14);
        Add("HSGR", WellMeasurementType.GammaRay, 15);
        Add("HCGR", WellMeasurementType.GammaRay, 16);
        Add("PGR", WellMeasurementType.GammaRay, 17);
        Add("WGR", WellMeasurementType.GammaRay, 18);
        Add("GAPI", WellMeasurementType.GammaRay, 19);
        Add("GRS", WellMeasurementType.GammaRay, 20);
        Add("GRGC", WellMeasurementType.GammaRay, 21);
        Add("GRSG", WellMeasurementType.GammaRay, 22);
        Add("GCGR", WellMeasurementType.GammaRay, 23);
        Add("GRTO", WellMeasurementType.GammaRay, 24);
        Add("GRGM", WellMeasurementType.GammaRay, 25);
        Add("CSGR", WellMeasurementType.GammaRay, 26);
        Add("GRSLC", WellMeasurementType.GammaRay, 27);
        Add("GRSL", WellMeasurementType.GammaRay, 28);

        Add("RT", WellMeasurementType.ResistivityDeep, 1);
        Add("ILD", WellMeasurementType.ResistivityDeep, 2);
        Add("LLD", WellMeasurementType.ResistivityDeep, 3);
        Add("AT90", WellMeasurementType.ResistivityDeep, 4);
        Add("AIT90", WellMeasurementType.ResistivityDeep, 5);
        Add("AF90", WellMeasurementType.ResistivityDeep, 6);
        Add("AO90", WellMeasurementType.ResistivityDeep, 7);
        Add("HRLD", WellMeasurementType.ResistivityDeep, 8);
        Add("RD", WellMeasurementType.ResistivityDeep, 9);
        Add("RDEP", WellMeasurementType.ResistivityDeep, 10);
        Add("RT_HRLT", WellMeasurementType.ResistivityDeep, 11);
        Add("RLA5", WellMeasurementType.ResistivityDeep, 12);
        Add("RLA4", WellMeasurementType.ResistivityDeep, 13);
        Add("RLA3", WellMeasurementType.ResistivityDeep, 14);
        Add("RLA2", WellMeasurementType.ResistivityDeep, 15);
        Add("RLA1", WellMeasurementType.ResistivityDeep, 16);
        Add("RLA0", WellMeasurementType.ResistivityDeep, 17);
        Add("IDPH", WellMeasurementType.ResistivityDeep, 18);
        Add("M2RX", WellMeasurementType.ResistivityDeep, 19);
        Add("HDRS", WellMeasurementType.ResistivityDeep, 20);
        Add("RES_DEEP", WellMeasurementType.ResistivityDeep, 21);
        Add("RILD", WellMeasurementType.ResistivityDeep, 22);
        Add("RLLD", WellMeasurementType.ResistivityDeep, 23);
        Add("R90", WellMeasurementType.ResistivityDeep, 24);
        Add("AIT", WellMeasurementType.ResistivityDeep, 25);
        Add("HRLT", WellMeasurementType.ResistivityDeep, 26);
        Add("AT60", WellMeasurementType.ResistivityDeep, 27);
        Add("AF60", WellMeasurementType.ResistivityDeep, 28);
        Add("AO60", WellMeasurementType.ResistivityDeep, 29);
        Add("DDLL", WellMeasurementType.ResistivityDeep, 30);
        Add("DPRD", WellMeasurementType.ResistivityDeep, 31);
        Add("ATCO90", WellMeasurementType.ResistivityDeep, 32);
        Add("HLLD", WellMeasurementType.ResistivityDeep, 33);
        Add("RLAD", WellMeasurementType.ResistivityDeep, 34);
        Add("RO90", WellMeasurementType.ResistivityDeep, 35);

        Add("ILM", WellMeasurementType.ResistivityMedium, 51);
        Add("LLM", WellMeasurementType.ResistivityMedium, 52);
        Add("AT30", WellMeasurementType.ResistivityMedium, 53);
        Add("AIT30", WellMeasurementType.ResistivityMedium, 54);
        Add("AF30", WellMeasurementType.ResistivityMedium, 55);
        Add("AO30", WellMeasurementType.ResistivityMedium, 56);
        Add("IMPH", WellMeasurementType.ResistivityMedium, 57);
        // Mud resistivity must not fall back to the RM alias.
        Add("RM_HRLT", WellMeasurementType.Unknown, int.MaxValue);
        Add("RM", WellMeasurementType.ResistivityMedium, 59);
        Add("RMED", WellMeasurementType.ResistivityMedium, 60);
        Add("R30", WellMeasurementType.ResistivityMedium, 61);
        Add("RESM", WellMeasurementType.ResistivityMedium, 62);
        Add("RES_MED", WellMeasurementType.ResistivityMedium, 63);
        Add("DPRL", WellMeasurementType.ResistivityMedium, 64);
        Add("RO30", WellMeasurementType.ResistivityMedium, 65);

        Add("MSFL", WellMeasurementType.ResistivityShallow, 71);
        Add("SFLU", WellMeasurementType.ResistivityShallow, 72);
        Add("LLS", WellMeasurementType.ResistivityShallow, 73);
        Add("AT10", WellMeasurementType.ResistivityShallow, 74);
        Add("AIT10", WellMeasurementType.ResistivityShallow, 75);
        Add("AF10", WellMeasurementType.ResistivityShallow, 76);
        Add("AO10", WellMeasurementType.ResistivityShallow, 77);
        Add("AT20", WellMeasurementType.ResistivityShallow, 78);
        Add("AF20", WellMeasurementType.ResistivityShallow, 79);
        Add("AO20", WellMeasurementType.ResistivityShallow, 80);
        Add("RXO", WellMeasurementType.ResistivityShallow, 81);
        Add("RXOZ", WellMeasurementType.ResistivityShallow, 82);
        Add("RXO8", WellMeasurementType.ResistivityShallow, 83);
        Add("RXO_HRLT", WellMeasurementType.ResistivityShallow, 84);
        Add("SN", WellMeasurementType.ResistivityShallow, 85);
        Add("HMIN", WellMeasurementType.ResistivityShallow, 86);
        Add("HMNO", WellMeasurementType.ResistivityShallow, 87);
        Add("RS", WellMeasurementType.ResistivityShallow, 88);
        Add("RESS", WellMeasurementType.ResistivityShallow, 89);
        Add("RSHA", WellMeasurementType.ResistivityShallow, 90);
        Add("R10", WellMeasurementType.ResistivityShallow, 91);
        Add("MNOR", WellMeasurementType.ResistivityShallow, 92);
        Add("MINV", WellMeasurementType.ResistivityShallow, 93);
        Add("RMLL", WellMeasurementType.ResistivityShallow, 94);
        Add("DSLL", WellMeasurementType.ResistivityShallow, 95);
        Add("DPRS", WellMeasurementType.ResistivityShallow, 96);
        Add("RES_SHAL", WellMeasurementType.ResistivityShallow, 97);
        Add("HLLS", WellMeasurementType.ResistivityShallow, 98);
        Add("HRLS", WellMeasurementType.ResistivityShallow, 99);
        Add("RO10", WellMeasurementType.ResistivityShallow, 100);

        Add("RES", WellMeasurementType.Resistivity, 40);
        Add("RESISTIVITY", WellMeasurementType.Resistivity, 41);

        Add("RHOB", WellMeasurementType.BulkDensity, 1);
        Add("RHOZ", WellMeasurementType.BulkDensity, 2);
        Add("DEN", WellMeasurementType.BulkDensity, 3);
        Add("RHO8", WellMeasurementType.BulkDensity, 4);
        Add("DEN_COMP", WellMeasurementType.BulkDensity, 5);
        Add("BDEN", WellMeasurementType.BulkDensity, 6);
        Add("ZDEN", WellMeasurementType.BulkDensity, 7);
        Add("ZDNC", WellMeasurementType.BulkDensity, 8);
        Add("RHOL", WellMeasurementType.BulkDensity, 9);
        Add("RHOB_SHIFT", WellMeasurementType.BulkDensity, 10);
        Add("RHOM", WellMeasurementType.BulkDensity, 11);
        Add("RHO", WellMeasurementType.BulkDensity, 12);
        Add("DENCOMP", WellMeasurementType.BulkDensity, 13);
        Add("DEN_Z", WellMeasurementType.BulkDensity, 14);
        Add("RHOI", WellMeasurementType.BulkDensity, 15);
        Add("DNC", WellMeasurementType.BulkDensity, 16);
        Add("DENIC", WellMeasurementType.BulkDensity, 17);

        Add("NPHI", WellMeasurementType.NeutronPorosity, 1);
        Add("TNPH", WellMeasurementType.NeutronPorosity, 2);
        Add("CNL", WellMeasurementType.NeutronPorosity, 3);
        Add("NPOR", WellMeasurementType.NeutronPorosity, 4);
        Add("HTNP", WellMeasurementType.NeutronPorosity, 5);
        Add("DNPH", WellMeasurementType.NeutronPorosity, 6);
        Add("HNPO", WellMeasurementType.NeutronPorosity, 7);
        Add("NPOR_LS", WellMeasurementType.NeutronPorosity, 8);
        Add("NPOR_SS", WellMeasurementType.NeutronPorosity, 9);
        Add("PHIN", WellMeasurementType.NeutronPorosity, 10);
        Add("NPLS", WellMeasurementType.NeutronPorosity, 11);
        Add("NPSS", WellMeasurementType.NeutronPorosity, 12);
        Add("APHI", WellMeasurementType.NeutronPorosity, 13);
        Add("CNC", WellMeasurementType.NeutronPorosity, 14);
        Add("NPHC", WellMeasurementType.NeutronPorosity, 15);
        Add("NPHD", WellMeasurementType.NeutronPorosity, 16);
        Add("NPHL", WellMeasurementType.NeutronPorosity, 17);
        Add("NPHM", WellMeasurementType.NeutronPorosity, 18);
        Add("NPHS", WellMeasurementType.NeutronPorosity, 19);
        Add("NPL", WellMeasurementType.NeutronPorosity, 20);
        Add("CNLS", WellMeasurementType.NeutronPorosity, 21);
        Add("CNDL", WellMeasurementType.NeutronPorosity, 22);
        Add("CNSS", WellMeasurementType.NeutronPorosity, 23);
        Add("CNAD", WellMeasurementType.NeutronPorosity, 24);
        Add("CNPOR", WellMeasurementType.NeutronPorosity, 25);
        Add("NPDL", WellMeasurementType.NeutronPorosity, 26);
        Add("NPRL", WellMeasurementType.NeutronPorosity, 27);
        Add("NPRS", WellMeasurementType.NeutronPorosity, 28);
        Add("NPRD", WellMeasurementType.NeutronPorosity, 29);
        Add("APLC", WellMeasurementType.NeutronPorosity, 30);
        Add("ENLI", WellMeasurementType.NeutronPorosity, 31);
        Add("APSC", WellMeasurementType.NeutronPorosity, 32);
        Add("ENPS", WellMeasurementType.NeutronPorosity, 33);
        Add("APDC", WellMeasurementType.NeutronPorosity, 34);

        Add("DPHI", WellMeasurementType.DensityPorosity, 1);
        Add("DPHZ", WellMeasurementType.DensityPorosity, 2);
        Add("DPH8", WellMeasurementType.DensityPorosity, 3);
        Add("DPOR", WellMeasurementType.DensityPorosity, 4);
        Add("DPHD", WellMeasurementType.DensityPorosity, 5);
        Add("DPHL", WellMeasurementType.DensityPorosity, 6);
        Add("DPHS", WellMeasurementType.DensityPorosity, 7);
        Add("PORZ", WellMeasurementType.DensityPorosity, 8);
        Add("POR_DEN", WellMeasurementType.DensityPorosity, 9);
        Add("PORZC", WellMeasurementType.DensityPorosity, 10);
        Add("PHID", WellMeasurementType.DensityPorosity, 11);
        Add("DPDL", WellMeasurementType.DensityPorosity, 12);
        Add("HDPH_SAN", WellMeasurementType.DensityPorosity, 13);
        Add("PRZC", WellMeasurementType.DensityPorosity, 14);

        Add("SPHI", WellMeasurementType.SonicPorosity, 1);
        Add("SPHD", WellMeasurementType.SonicPorosity, 2);
        Add("SPHL", WellMeasurementType.SonicPorosity, 3);
        Add("SPHS", WellMeasurementType.SonicPorosity, 4);
        Add("SPDL", WellMeasurementType.SonicPorosity, 5);
        Add("PORA", WellMeasurementType.SonicPorosity, 6);
        Add("SPRL", WellMeasurementType.SonicPorosity, 7);
        Add("RPHI", WellMeasurementType.SonicPorosity, 8);

        Add("PXND", WellMeasurementType.CrossplotPorosity, 1);
        Add("PXND_HILT", WellMeasurementType.CrossplotPorosity, 2);
        Add("PHIX", WellMeasurementType.CrossplotPorosity, 3);
        Add("XPHI", WellMeasurementType.CrossplotPorosity, 4);
        Add("XPRC", WellMeasurementType.CrossplotPorosity, 5);
        Add("CPPZC", WellMeasurementType.CrossplotPorosity, 6);
        Add("CPHI", WellMeasurementType.CrossplotPorosity, 7);
        Add("TCMR", WellMeasurementType.CrossplotPorosity, 8);
        Add("CMRP_3MS", WellMeasurementType.CrossplotPorosity, 9);
        Add("XPOR", WellMeasurementType.CrossplotPorosity, 10);
        Add("CPZC", WellMeasurementType.CrossplotPorosity, 11);

        Add("POR", WellMeasurementType.Porosity, 1);
        Add("POROSITY", WellMeasurementType.Porosity, 2);
        Add("PHI", WellMeasurementType.Porosity, 3);
        Add("PHIE", WellMeasurementType.Porosity, 4);
        Add("PHIT", WellMeasurementType.Porosity, 5);

        Add("DRHO", WellMeasurementType.DensityCorrection, 1);
        Add("ZCOR", WellMeasurementType.DensityCorrection, 2);
        Add("DCOR", WellMeasurementType.DensityCorrection, 3);
        Add("HDRA", WellMeasurementType.DensityCorrection, 4);

        Add("PE", WellMeasurementType.Photoelectric, 1);
        Add("PEF", WellMeasurementType.Photoelectric, 2);
        Add("PEFZ", WellMeasurementType.Photoelectric, 3);
        Add("PE_HRLT", WellMeasurementType.Photoelectric, 4);

        Add("DT", WellMeasurementType.Sonic, 1);
        Add("DTC", WellMeasurementType.Sonic, 2);
        Add("DTCO", WellMeasurementType.Sonic, 3);
        Add("AC", WellMeasurementType.Sonic, 4);
        Add("ITT", WellMeasurementType.Sonic, 5);
        Add("DT24", WellMeasurementType.Sonic, 6);
        Add("DTS", WellMeasurementType.Sonic, 7);
        Add("DTSM", WellMeasurementType.Sonic, 8);

        Add("CALI", WellMeasurementType.Caliper, 1);
        Add("CAL", WellMeasurementType.Caliper, 2);
        Add("HCAL", WellMeasurementType.Caliper, 3);
        Add("CALS", WellMeasurementType.Caliper, 4);
        Add("DCAL", WellMeasurementType.Caliper, 5);
        Add("CLDC", WellMeasurementType.Caliper, 6);
        Add("C1", WellMeasurementType.Caliper, 8);
        Add("C2", WellMeasurementType.Caliper, 9);
        Add("CALX", WellMeasurementType.Caliper, 10);
        Add("CALY", WellMeasurementType.Caliper, 11);
        Add("DSOZ", WellMeasurementType.Caliper, 12);
        Add("IECX", WellMeasurementType.Caliper, 13);
        Add("IECY", WellMeasurementType.Caliper, 14);

        Add("SP", WellMeasurementType.SpontaneousPotential, 1);
        Add("SPC", WellMeasurementType.SpontaneousPotential, 2);

        Add("TENS", WellMeasurementType.Tension, 1);
        Add("CFTC", WellMeasurementType.Tension, 2);
        Add("TEN", WellMeasurementType.Tension, 3);

        Add("TEMP", WellMeasurementType.Temperature, 1);
        Add("GTEM", WellMeasurementType.Temperature, 2);
        Add("CTEM", WellMeasurementType.Temperature, 3);
        Add("BHT", WellMeasurementType.Temperature, 4);
        Add("AMBT", WellMeasurementType.Temperature, 5);

        Add("ROP", WellMeasurementType.RateOfPenetration, 1);
        Add("ROPA", WellMeasurementType.RateOfPenetration, 2);

        Add("MW", WellMeasurementType.MudWeight, 1);
        Add("MUDW", WellMeasurementType.MudWeight, 2);
        Add("MWI", WellMeasurementType.MudWeight, 3);

        Add("TGAS", WellMeasurementType.GasTotal, 1);
        Add("GAS", WellMeasurementType.GasTotal, 2);
        Add("TOTGAS", WellMeasurementType.GasTotal, 3);
    }

    private void Add(string mnemonic, WellMeasurementType type, int priority) =>
        aliases[mnemonic] = (type, priority);

    public void RegisterAlias(string mnemonic, WellMeasurementType type, int priority = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mnemonic);
        aliases[mnemonic.Trim()] = (type, priority);
    }

    [GeneratedRegex(@"[:_.\-\[\(]|\d+$")]
    private static partial Regex SuffixRegex();

    public (WellMeasurementType Measurement, int Priority) ResolveWithPriority(string? mnemonic)
    {
        if (string.IsNullOrWhiteSpace(mnemonic))
            return (WellMeasurementType.Unknown, int.MaxValue);

        var cleaned = mnemonic.Trim();
        if (aliases.TryGetValue(cleaned, out var match))
            return match;

        var suffixes = SuffixRegex().Matches(cleaned);
        for (var i = suffixes.Count - 1; i >= 0; i--)
        {
            var stripped = cleaned[..suffixes[i].Index].Trim();
            if (stripped.Length > 0 && aliases.TryGetValue(stripped, out var strippedMatch))
                return (strippedMatch.Measurement, strippedMatch.Priority == int.MaxValue ? int.MaxValue : strippedMatch.Priority + 50);
        }

        return (WellMeasurementType.Unknown, int.MaxValue);
    }

    public static bool IsUnitCompatible(WellMeasurementType measurement, string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return true;

        var u = unit.Trim().ToUpperInvariant();

        return measurement switch
        {
            WellMeasurementType.GammaRay =>
                u is not ("%" or "PERC" or "PERCENT" or "PCT" or "PCNT" or "V/V" or "DEC" or "FRAC" or "FRACTION" or "OHMM" or "OHM-M" or "OHM.M" or "G/CC" or "G/CM3"),

            WellMeasurementType.NeutronPorosity or WellMeasurementType.DensityPorosity or WellMeasurementType.SonicPorosity or WellMeasurementType.Porosity =>
                u is not ("CPS" or "C/S" or "COUNTS" or "CPS/S" or "OHMM" or "OHM-M" or "OHM.M" or "G/CC" or "G/CM3" or "US/FT" or "US/M"),

            WellMeasurementType.ResistivityDeep or WellMeasurementType.ResistivityMedium or
            WellMeasurementType.ResistivityShallow or WellMeasurementType.Resistivity =>
                u is not ("%" or "PERC" or "PERCENT" or "PCT" or "G/CC" or "G/CM3" or "CPS" or "API" or "GAPI" or "US/FT" or "US/M"),

            WellMeasurementType.BulkDensity =>
                u is not ("%" or "PERC" or "PERCENT" or "PCT" or "OHMM" or "OHM-M" or "OHM.M" or "CPS" or "API" or "GAPI" or "US/FT" or "US/M"),

            _ => true
        };
    }

    public WellMeasurementType Resolve(string? mnemonic) => ResolveWithPriority(mnemonic).Measurement;

    public int GetPriority(string? mnemonic) => ResolveWithPriority(mnemonic).Priority;

    public static string GetDisplayName(WellMeasurementType type) => type switch
    {
        WellMeasurementType.Depth => "Depth",
        WellMeasurementType.GammaRay => "Gamma Ray",
        WellMeasurementType.ResistivityDeep => "Deep Resistivity",
        WellMeasurementType.ResistivityMedium => "Medium Resistivity",
        WellMeasurementType.ResistivityShallow => "Shallow Resistivity",
        WellMeasurementType.Resistivity => "Resistivity",
        WellMeasurementType.BulkDensity => "Bulk Density",
        WellMeasurementType.NeutronPorosity => "Neutron Porosity",
        WellMeasurementType.DensityPorosity => "Density Porosity",
        WellMeasurementType.SonicPorosity => "Sonic Porosity",
        WellMeasurementType.CrossplotPorosity => "Crossplot Porosity",
        WellMeasurementType.Porosity => "Porosity",
        WellMeasurementType.DensityCorrection => "Density Correction (DRHO)",
        WellMeasurementType.Photoelectric => "Photoelectric Factor",
        WellMeasurementType.Sonic => "Sonic Compressional (DT)",
        WellMeasurementType.Caliper => "Caliper",
        WellMeasurementType.SpontaneousPotential => "Spontaneous Potential",
        WellMeasurementType.Tension => "Cable Tension",
        WellMeasurementType.Temperature => "Temperature",
        WellMeasurementType.RateOfPenetration => "Rate of Penetration",
        WellMeasurementType.MudWeight => "Mud Weight",
        WellMeasurementType.GasTotal => "Total Gas",
        _ => "Unknown"
    };
}
