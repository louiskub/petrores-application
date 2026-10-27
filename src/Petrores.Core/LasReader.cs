namespace Petrores.Core;

public sealed class LasReader
{
    public LasLoadResult Read(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        try
        {
            using var reader = new StreamReader(filePath);
            var header = LasParsing.ReadHeader(reader, filePath);
            if (!header.HasData || header.UnsupportedStructure)
                return header.Failure("No supported log data section and curve definition pair was found.");

            var version = LasParsing.Setting(header.Version, "VERS");
            if (!LasParsing.TryNumber(version, out var number))
                return header.Failure($"Invalid or missing VERS value '{version}'.");

            return number switch
            {
                1.2 => Las12Reader.Read(header, reader),
                2.0 => Las20Reader.Read(header, reader),
                3.0 => Las30Reader.Read(header, reader),
                _ => header.Failure($"Unsupported VERS value '{version}'; supported versions are 1.2, 2.0 and 3.0.")
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new LasLoadResult(
                filePath,
                null,
                [new LasDiagnostic(null, exception.Message)]);
        }
    }
}
