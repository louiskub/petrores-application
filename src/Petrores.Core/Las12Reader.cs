namespace Petrores.Core;

internal static class Las12Reader
{
    internal static LasLoadResult Read(LasHeader header, TextReader reader)
    {
        for (var i = 0; i < header.Well.Count; i++)
        {
            var entry = header.Well[i];
            var value = LasParsing.HeaderValue(entry);
            if (value != entry.Value)
            {
                header.Well[i] = entry with { Value = value };
                header.Report(new(entry.LineNumber, $"Used the description as the legacy value for {entry.Mnemonic}."));
            }
        }

        return Las20Reader.Read(header, reader);
    }
}
