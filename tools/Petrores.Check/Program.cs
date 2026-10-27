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
        foreach (var file in files)
        {
            var result = reader.Read(file);
            string status;
            if (result.Well is null)
            {
                status = "FAIL";
                failed++;
            }
            else if (result.Diagnostics.Length > 0)
            {
                status = "WARN";
                warnings++;
            }
            else
            {
                status = "OK";
                clean++;
            }

            output.WriteLine($"[{status}] {Path.GetFileName(file)} | rows={result.Well?.Depths.Length ?? 0}; curves={result.Well?.Curves.Length ?? 0}; diagnostics={result.Diagnostics.Length}");
            foreach (var diagnostic in result.Diagnostics)
                output.WriteLine($"  {(diagnostic.LineNumber is { } line ? $"line {line}" : "file")}: {diagnostic.Message}");
        }

        output.WriteLine($"Files={files.Length}; OK={clean}; WARN={warnings}; FAIL={failed}");
        return failed > 0 ? 1 : 0;
    }
}
