using System.Globalization;
using System.Text.RegularExpressions;

namespace Petrores.Core;

internal sealed class LasHeader(string sourcePath)
{
    internal string SourcePath { get; } = sourcePath;
    internal int LineNumber;
    internal bool HasData;
    internal bool HasOther;
    internal bool UnsupportedStructure;
    internal string CurveSection = "";
    internal List<LasHeaderEntry> Version { get; } = [];
    internal List<LasHeaderEntry> Well { get; } = [];
    internal List<LasHeaderEntry> Parameters { get; } = [];
    internal List<LasHeaderEntry> Curves { get; } = [];
    internal List<string> OtherLines { get; } = [];
    private readonly List<LasDiagnostic> diagnostics = [];
    private int suppressedDiagnostics;

    internal void Report(LasDiagnostic diagnostic)
    {
        // shortcut: retain the first 1000 diagnostics; stream them when per-row audit exports are required.
        if (diagnostics.Count < 1000)
            diagnostics.Add(diagnostic);
        else
            suppressedDiagnostics++;
    }

    internal LasLoadResult Result(Well? well) => new(SourcePath, well, suppressedDiagnostics == 0
        ? diagnostics.ToArray()
        : [.. diagnostics, new(null, $"Suppressed {suppressedDiagnostics} additional diagnostics after the first 1000; file processing continued.")]);

    internal LasLoadResult Failure(string message, int? lineNumber = null)
    {
        diagnostics.Add(new(lineNumber, message));
        return Result(null);
    }
}

internal static partial class LasParsing
{
    internal static LasHeader ReadHeader(TextReader reader, string sourcePath)
    {
        var header = new LasHeader(sourcePath);
        var section = '\0';
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            header.LineNumber++;
            var trimmed = line.Trim();
            if (trimmed.StartsWith('~'))
            {
                var name = SectionName(trimmed);
                if (name.StartsWith("LOG_", StringComparison.Ordinal) && name.Contains('['))
                {
                    header.UnsupportedStructure = true;
                    header.Report(new(header.LineNumber, "LAS 3.0 indexed log section arrays are not supported."));
                }

                section = name switch
                {
                    "V" or "VERSION" => 'V',
                    "W" or "WELL" => 'W',
                    "C" or "CURVE" or "LOG_DEFINITION" => 'C',
                    "P" or "PARAMETER" or "PARAMETERS" or "LOG_PARAMETER" => 'P',
                    "O" or "OTHER" => 'O',
                    "A" or "ASCII" or "LOG_DATA" => 'A',
                    _ => '?'
                };

                if (section == 'A')
                {
                    header.HasData = true;
                    var separator = trimmed.IndexOf('|');
                    if (separator >= 0 && SectionName(trimmed[(separator + 1)..]) != header.CurveSection)
                    {
                        header.UnsupportedStructure = true;
                        header.Report(new(header.LineNumber, "Data section references a different curve definition section."));
                    }
                    break;
                }

                if (section == 'C')
                {
                    if (header.CurveSection.Length > 0)
                    {
                        header.UnsupportedStructure = true;
                        header.Report(new(header.LineNumber, "Multiple curve definition sections are not supported."));
                    }
                    header.CurveSection = name;
                }
                if (section == 'O')
                    header.HasOther = true;
                if (section == '?')
                    header.Report(new(header.LineNumber, $"Skipped unsupported section {name}."));
                continue;
            }

            if (section == 'O')
            {
                header.OtherLines.Add(line);
                continue;
            }
            if (trimmed.Length == 0 || trimmed.StartsWith('#') || section == '?')
                continue;

            var entry = ParseHeaderEntry(trimmed, header.LineNumber);
            if (entry is null && section == 'C' && trimmed.StartsWith('.'))
            {
                // Keep the column slot even when a supplier adds a leading dot or omits its mnemonic.
                entry = ParseHeaderEntry(trimmed[1..], header.LineNumber)
                    ?? ParseHeaderEntry($"UNNAMED_{header.Curves.Count + 1}{trimmed}", header.LineNumber);
                if (entry is not null)
                    header.Report(new(header.LineNumber, $"Recovered damaged curve definition as {entry.Mnemonic}: {trimmed}"));
            }
            if (entry is null)
            {
                header.Report(new(header.LineNumber, $"Could not read header entry: {trimmed}"));
                continue;
            }

            switch (section)
            {
                case 'V': header.Version.Add(entry); break;
                case 'W': header.Well.Add(entry); break;
                case 'P': header.Parameters.Add(entry); break;
                case 'C': header.Curves.Add(entry); break;
                default: header.Report(new(header.LineNumber, "Header entry occurs outside a recognized section.")); break;
            }
        }
        return header;
    }

    [GeneratedRegex(@"^~?\s*(?<name>[^\s|]+)")]
    private static partial Regex SectionNameRegex();

    [GeneratedRegex(@"^(?<mnemonic>[^.]+?)\s*\.(?<unit>\S*)\s*(?<value>[^:]*?)(?:\s*:\s*(?<desc>.*))?$")]
    private static partial Regex HeaderEntryRegex();

    private static string SectionName(string text)
    {
        var match = SectionNameRegex().Match(text);
        return match.Success ? match.Groups["name"].Value.ToUpperInvariant() : "";
    }

    private static LasHeaderEntry? ParseHeaderEntry(string text, int lineNumber)
    {
        var match = HeaderEntryRegex().Match(text);
        if (!match.Success)
            return null;

        var mnemonic = match.Groups["mnemonic"].Value.Trim();
        if (mnemonic.Length == 0)
            return null;

        return new(
            mnemonic,
            match.Groups["unit"].Value,
            match.Groups["value"].Value.Trim(),
            match.Groups["desc"].Value.Trim(),
            lineNumber);
    }

    internal static string HeaderValue(LasHeaderEntry entry)
    {
        var value = entry.Value.Trim();
        if (value.Length == 0 && entry.Mnemonic.ToUpperInvariant() is not
            ("VERS" or "WRAP" or "DLM" or "NULL" or "WELL" or "API" or "UWI" or "STRT" or "STOP" or "STEP"))
            return value;

        var label = entry.Mnemonic.ToUpperInvariant() switch
        {
            "API" => "API NUMBER",
            "UWI" => "UNIQUE WELL ID",
            "COMP" => "COMPANY",
            "NULL" => "NULL VALUE",
            "STRT" => "START DEPTH",
            "STOP" => "STOP DEPTH",
            _ => entry.Mnemonic
        };
        return value.Length == 0 || value.Equals(entry.Mnemonic, StringComparison.OrdinalIgnoreCase)
            || value.Equals(label, StringComparison.OrdinalIgnoreCase) ? entry.Description.Trim() : value;
    }

    internal static string Setting(List<LasHeaderEntry> entries, string mnemonic)
    {
        var entry = entries.LastOrDefault(item => item.Mnemonic.Equals(mnemonic, StringComparison.OrdinalIgnoreCase));
        return entry is null ? "" : HeaderValue(entry);
    }

    internal static bool TryNumber(string text, out double value) =>
        double.TryParse(text.Replace('D', 'E').Replace('d', 'e'), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value);

    private static bool IsNullValue(double value, double? nullValue, double epsilon = 1e-4)
    {
        if (!nullValue.HasValue)
            return false;
        if (value == nullValue.Value)
            return true;
        if (nullValue.Value == 0)
            return false;
        return Math.Abs(value - nullValue.Value) <= epsilon;
    }

    internal static LasLoadResult ReadData(LasHeader header, TextReader reader, bool wrapped, char? delimiter)
    {
        if (header.Well.Count == 0 || header.Curves.Count == 0)
            return header.Failure("Missing well information or curve definitions.");

        double? nullValue = null;
        foreach (var entry in header.Well.Where(item => item.Mnemonic.Equals("NULL", StringComparison.OrdinalIgnoreCase)))
        {
            if (!TryNumber(HeaderValue(entry), out var number))
                return header.Failure("Invalid NULL value in Well Information.", entry.LineNumber);
            if (nullValue.HasValue && !IsNullValue(number, nullValue))
                return header.Failure("Conflicting NULL values in Well Information.", entry.LineNumber);
            nullValue = number;
        }
        if (!nullValue.HasValue)
            header.Report(new(null, "No NULL value in Well Information; numeric values were retained without sentinel replacement."));

        var depths = new List<double>();
        var values = header.Curves.Skip(1).Select(_ => new List<double?>()).ToArray();
        var pending = new List<string>();
        var recordLine = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            header.LineNumber++;
            var trimmed = line.Trim();
            if (trimmed.StartsWith('~'))
            {
                header.Report(new(header.LineNumber, "Additional sections after the first log data set are not loaded."));
                break;
            }
            var comment = line.IndexOf('#');
            var data = comment >= 0 ? line[..comment] : line;
            if (string.IsNullOrWhiteSpace(data))
                continue;

            var tokens = delimiter.HasValue
                ? data.Split(delimiter.Value, StringSplitOptions.TrimEntries)
                : data.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (!wrapped)
            {
                if (tokens.Length != header.Curves.Count)
                {
                    header.Report(new(header.LineNumber, $"Expected {header.Curves.Count} values but found {tokens.Length}; skipped row."));
                    continue;
                }
                AddRow(tokens, header.LineNumber);
                continue;
            }

            if (pending.Count == 0)
                recordLine = header.LineNumber;
            pending.AddRange(tokens);
            if (pending.Count > header.Curves.Count)
            {
                header.Report(new(recordLine, $"Expected {header.Curves.Count} values in a wrapped record but found {pending.Count}; discarded record."));
                pending.Clear();
            }
            else if (pending.Count == header.Curves.Count)
            {
                AddRow(pending.ToArray(), recordLine);
                pending.Clear();
            }
        }

        if (pending.Count > 0)
            header.Report(new(recordLine, $"Incomplete wrapped record: expected {header.Curves.Count} values but found {pending.Count}."));
        if (depths.Count == 0)
            return header.Failure("No valid numeric depth rows were read.");

        var well = new Well(
            header.Version.ToArray(),
            header.Well.ToArray(),
            header.Parameters.ToArray(),
            header.HasOther ? string.Join(Environment.NewLine, header.OtherLines) : null,
            depths.ToArray(),
            header.Curves.Skip(1).Select((entry, index) => new Curve(entry.Mnemonic, entry.Unit, entry.Description, values[index].ToArray())).ToArray(),
            header.Curves[0].Unit);
        return header.Result(well);

        void AddRow(string[] tokens, int lineNumber)
        {
            if (!TryNumber(tokens[0], out var depth) || IsNullValue(depth, nullValue))
            {
                header.Report(new(lineNumber, "Invalid or missing depth; skipped row."));
                return;
            }
            depths.Add(depth);
            for (var i = 1; i < tokens.Length; i++)
            {
                if (!TryNumber(tokens[i], out var number))
                {
                    values[i - 1].Add(null);
                    header.Report(new(lineNumber, $"Invalid value for {header.Curves[i].Mnemonic}; kept as missing."));
                }
                else
                {
                    values[i - 1].Add(IsNullValue(number, nullValue) ? null : number);
                }
            }
        }
    }
}
