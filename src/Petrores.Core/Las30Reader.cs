using System.Text.RegularExpressions;

namespace Petrores.Core;

internal static partial class Las30Reader
{
    [GeneratedRegex(@"\{(?<fmt>[^}]*)\}(?<array>\s*\[)?")]
    private static partial Regex Las30FormatRegex();

    internal static LasLoadResult Read(LasHeader header, TextReader reader)
    {
        if (!LasParsing.Setting(header.Version, "WRAP").Equals("NO", StringComparison.OrdinalIgnoreCase))
            return header.Failure("LAS 3.0 requires WRAP.NO; wrapped LAS 3.0 data is not supported.");

        var delimiter = LasParsing.Setting(header.Version, "DLM").ToUpperInvariant();
        if (delimiter is not ("SPACE" or "COMMA" or "TAB"))
            return header.Failure($"Unsupported or missing LAS 3.0 DLM value '{delimiter}'. Expected SPACE, COMMA or TAB.");

        foreach (var curve in header.Curves)
        {
            if (curve.Mnemonic.Contains('['))
                return header.Failure($"LAS 3.0 array curve {curve.Mnemonic} is not supported by the scalar curve model.", curve.LineNumber);

            var match = Las30FormatRegex().Match(curve.Description);
            if (!match.Success)
                continue;

            var format = match.Groups["fmt"].Value.Trim().ToUpperInvariant();
            if (format.Contains('[') || match.Groups["array"].Success)
                return header.Failure($"LAS 3.0 array format for {curve.Mnemonic} is not supported.", curve.LineNumber);
            if (format.Length == 0 || format[0] is not ('F' or 'E' or 'I'))
                return header.Failure($"Unsupported curve format for {curve.Mnemonic}: only scalar numeric F, E and I formats are supported.", curve.LineNumber);
        }

        return LasParsing.ReadData(header, reader, wrapped: false, delimiter: delimiter switch
        {
            "COMMA" => ',',
            "TAB" => '\t',
            _ => null
        });
    }
}
