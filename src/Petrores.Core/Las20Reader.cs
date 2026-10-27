namespace Petrores.Core;

internal static class Las20Reader
{
    internal static LasLoadResult Read(LasHeader header, TextReader reader)
    {
        var wrap = LasParsing.Setting(header.Version, "WRAP").ToUpperInvariant();
        if (wrap is not ("YES" or "NO"))
            return header.Failure($"Invalid or missing WRAP value '{wrap}'. Expected YES or NO.");

        return LasParsing.ReadData(header, reader, wrapped: wrap == "YES", delimiter: null);
    }
}
