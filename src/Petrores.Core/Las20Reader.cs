namespace Petrores.Core;

internal static class Las20Reader
{
    internal static LasLoadResult Read(LasHeader header, TextReader reader)
    {
        var wrapEntry = header.Version.FirstOrDefault(e => e.Mnemonic.Equals("WRAP", StringComparison.OrdinalIgnoreCase));
        var wrap = wrapEntry is not null ? LasParsing.HeaderValue(wrapEntry).ToUpperInvariant() : "";
        if (wrap is not ("YES" or "NO"))
            return header.Failure($"Invalid or missing WRAP value '{wrap}'. Expected YES or NO.");

        var isWrapped = wrap == "YES";
        if (!isWrapped && wrapEntry?.Description.Contains("MANY LINES", StringComparison.OrdinalIgnoreCase) == true)
        {
            header.Report(new(wrapEntry.LineNumber, "WRAP was specified as 'NO' but description indicates 'MANY LINES PER DEPTH STEP'; treated as wrapped data."));
            isWrapped = true;
        }

        return LasParsing.ReadData(header, reader, wrapped: isWrapped, delimiter: null);
    }
}
