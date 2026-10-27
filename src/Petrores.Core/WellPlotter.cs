using ScottPlot;
using ScottPlot.TickGenerators;
using SkiaSharp;

namespace Petrores.Core;

public sealed record WellPlotterOptions(
    int ImageWidth = 1350,
    int ImageHeight = 1000,
    double GammaRayMin = 0.0,
    double GammaRayMax = 150.0,
    double ResistivityMin = 0.2,
    double ResistivityMax = 2000.0,
    double DensityMin = 1.95,
    double DensityMax = 2.95,
    double NeutronMin = 0.45,
    double NeutronMax = -0.15
);

public static class WellPlotter
{
    private static readonly ScottPlot.Color ColorForestGreen = ScottPlot.Color.FromHex("#2E7D32");
    private static readonly ScottPlot.Color ColorCrimson = ScottPlot.Color.FromHex("#C62828");
    private static readonly ScottPlot.Color ColorDensityBlue = ScottPlot.Color.FromHex("#1565C0");
    private static readonly ScottPlot.Color ColorNeutronOrange = ScottPlot.Color.FromHex("#E65100");
    private static readonly ScottPlot.Color ColorGridGray = ScottPlot.Color.FromHex("#E0E0E0");

    /// <summary>
    /// Renders a full publication-quality 3-track well log plot to PNG image bytes.
    /// Tracks: Depth Ruler | Track 1 (Gamma Ray) | Track 2 (Resistivity Log) | Track 3 (Bulk Density + Neutron Porosity overlay).
    /// </summary>
    public static byte[] RenderCompositePlot(
        Well well,
        WellConditioningReport? report = null,
        WellPlotterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(well);
        options ??= new WellPlotterOptions();

        if (well.Depths.Length == 0)
        {
            return RenderEmptyPlot("No depth rows in well data.", options.ImageWidth, options.ImageHeight);
        }

        var minDepth = well.Depths.Min();
        var maxDepth = well.Depths.Max();
        if (Math.Abs(maxDepth - minDepth) < 1e-6)
        {
            maxDepth = minDepth + 1.0;
        }

        // Layout dimensions
        const int headerHeight = 90;
        var plotHeight = options.ImageHeight - headerHeight;
        const int depthTrackWidth = 110;
        var remainingWidth = options.ImageWidth - depthTrackWidth;
        var track1Width = (int)(remainingWidth * 0.31);
        var track2Width = (int)(remainingWidth * 0.31);
        var track3Width = remainingWidth - track1Width - track2Width;

        // Render subplots
        var depthPlot = CreateDepthTrackPlot(well, minDepth, maxDepth);
        var track1Plot = CreateTrack1Plot(well, report, minDepth, maxDepth, options);
        var track2Plot = CreateTrack2Plot(well, report, minDepth, maxDepth, options);
        var track3Plot = CreateTrack3Plot(well, report, minDepth, maxDepth, options);

        var depthBytes = depthPlot.GetImageBytes(depthTrackWidth, plotHeight, ImageFormat.Png);
        var t1Bytes = track1Plot.GetImageBytes(track1Width, plotHeight, ImageFormat.Png);
        var t2Bytes = track2Plot.GetImageBytes(track2Width, plotHeight, ImageFormat.Png);
        var t3Bytes = track3Plot.GetImageBytes(track3Width, plotHeight, ImageFormat.Png);

        using var bmpDepth = SKBitmap.Decode(depthBytes);
        using var bmpT1 = SKBitmap.Decode(t1Bytes);
        using var bmpT2 = SKBitmap.Decode(t2Bytes);
        using var bmpT3 = SKBitmap.Decode(t3Bytes);

        // Compose into single high-res canvas
        using var composite = new SKBitmap(options.ImageWidth, options.ImageHeight);
        using var canvas = new SKCanvas(composite);
        canvas.Clear(SKColors.White);

        // Draw header
        DrawHeader(canvas, well, report, minDepth, maxDepth, options.ImageWidth, headerHeight);

        // Draw tracks
        var currentX = 0f;
        canvas.DrawBitmap(bmpDepth, currentX, headerHeight);
        currentX += depthTrackWidth;

        canvas.DrawBitmap(bmpT1, currentX, headerHeight);
        currentX += track1Width;

        canvas.DrawBitmap(bmpT2, currentX, headerHeight);
        currentX += track2Width;

        canvas.DrawBitmap(bmpT3, currentX, headerHeight);

        // Draw subtle vertical dividers between tracks
        using var dividerPaint = new SKPaint
        {
            Color = new SKColor(180, 180, 180),
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        canvas.DrawLine(depthTrackWidth, headerHeight, depthTrackWidth, options.ImageHeight, dividerPaint);
        canvas.DrawLine(depthTrackWidth + track1Width, headerHeight, depthTrackWidth + track1Width, options.ImageHeight, dividerPaint);
        canvas.DrawLine(depthTrackWidth + track1Width + track2Width, headerHeight, depthTrackWidth + track1Width + track2Width, options.ImageHeight, dividerPaint);

        using var image = SKImage.FromBitmap(composite);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    /// <summary>
    /// Saves the composite well log plot directly to a file on disk.
    /// </summary>
    public static void SaveCompositePlot(
        Well well,
        string outputPath,
        WellConditioningReport? report = null,
        WellPlotterOptions? options = null)
    {
        var bytes = RenderCompositePlot(well, report, options);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllBytes(outputPath, bytes);
    }

    /// <summary>
    /// Creates Track 1: Gamma Ray (linear scale, depth increasing downward).
    /// </summary>
    public static Plot CreateTrack1Plot(
        Well well,
        WellConditioningReport? report,
        double minDepth,
        double maxDepth,
        WellPlotterOptions? options = null)
    {
        options ??= new WellPlotterOptions();
        var plot = new Plot();
        ConfigurePlotTheme(plot);

        // Depth axis inverted: shallowest on top, deepest on bottom
        plot.Axes.SetLimitsY(bottom: maxDepth, top: minDepth);
        plot.Axes.Left.TickGenerator = new NumericAutomatic();

        var curveMnemonic = report?.PrimaryCurves.GetValueOrDefault("Track1_GammaRay")?.SelectedMnemonic;
        var curve = FindCurve(well, curveMnemonic, WellMeasurementType.GammaRay);

        if (curve is null || !HasValidValues(curve))
        {
            plot.Axes.SetLimitsX(options.GammaRayMin, options.GammaRayMax);
            plot.Title("Track 1: Gamma Ray\n[No GR Curve Available]");
            AddCenteredNotice(plot, "No Gamma Ray data", options.GammaRayMin, options.GammaRayMax, minDepth, maxDepth);
            return plot;
        }

        var validMax = curve.Values.Where(v => v.HasValue).Max(v => v!.Value);
        var xMax = Math.Max(options.GammaRayMax, Math.Min(300.0, Math.Ceiling(validMax / 50.0) * 50.0));
        plot.Axes.SetLimitsX(options.GammaRayMin, xMax);

        plot.Title($"Track 1: {curve.Mnemonic} ({curve.Unit})");
        plot.Axes.Bottom.Label.Text = $"Gamma Ray ({curve.Unit})";

        AddContiguousSegments(plot, well.Depths, curve.Values, ColorForestGreen, 1.8f);

        return plot;
    }

    /// <summary>
    /// Creates Track 2: Resistivity on a logarithmic scale (depth increasing downward).
    /// </summary>
    public static Plot CreateTrack2Plot(
        Well well,
        WellConditioningReport? report,
        double minDepth,
        double maxDepth,
        WellPlotterOptions? options = null)
    {
        options ??= new WellPlotterOptions();
        var plot = new Plot();
        ConfigurePlotTheme(plot);

        plot.Axes.SetLimitsY(bottom: maxDepth, top: minDepth);

        var curveMnemonic = report?.PrimaryCurves.GetValueOrDefault("Track2_Resistivity")?.SelectedMnemonic;
        var curve = FindCurve(well, curveMnemonic, WellMeasurementType.ResistivityDeep, WellMeasurementType.Resistivity);

        var logMin = Math.Log10(options.ResistivityMin);
        var logMax = Math.Log10(options.ResistivityMax);
        plot.Axes.SetLimitsX(logMin, logMax);

        // Logarithmic ticks: 0.2, 1, 10, 100, 1000, 2000
        var logTicks = new NumericManual();
        logTicks.AddMajor(Math.Log10(0.2), "0.2");
        logTicks.AddMajor(Math.Log10(1.0), "1");
        logTicks.AddMajor(Math.Log10(10.0), "10");
        logTicks.AddMajor(Math.Log10(100.0), "100");
        logTicks.AddMajor(Math.Log10(1000.0), "1000");
        logTicks.AddMajor(Math.Log10(2000.0), "2000");

        // Minor ticks between decades
        double[] minorMultipliers = [2, 3, 4, 5, 6, 7, 8, 9];
        foreach (var m in minorMultipliers)
        {
            if (m * 0.1 >= 0.2) logTicks.AddMinor(Math.Log10(m * 0.1));
            logTicks.AddMinor(Math.Log10(m * 1.0));
            logTicks.AddMinor(Math.Log10(m * 10.0));
            logTicks.AddMinor(Math.Log10(m * 100.0));
        }
        plot.Axes.Bottom.TickGenerator = logTicks;

        if (curve is null || !HasValidValues(curve, v => v > 0))
        {
            plot.Title("Track 2: Resistivity (Log)\n[No Resistivity Curve Available]");
            AddCenteredNotice(plot, "No Resistivity data", logMin, logMax, minDepth, maxDepth);
            return plot;
        }

        plot.Title($"Track 2: {curve.Mnemonic} ({curve.Unit}) [Log]");
        plot.Axes.Bottom.Label.Text = $"Resistivity ({curve.Unit}) - Log Scale";

        // Filter and transform positive values to log10
        var logValues = new double?[curve.Values.Length];
        for (var i = 0; i < curve.Values.Length; i++)
        {
            var val = curve.Values[i];
            logValues[i] = (val is { } v && v > 0) ? Math.Log10(v) : null;
        }

        AddContiguousSegments(plot, well.Depths, logValues, ColorCrimson, 1.8f);

        return plot;
    }

    /// <summary>
    /// Creates Track 3: Bulk Density & Neutron Porosity on the same track.
    /// Bulk Density: 1.95 - 2.95 g/cm³ (solid blue)
    /// Neutron Porosity: 0.45 - -0.15 (dashed orange, inverted overlay)
    /// </summary>
    public static Plot CreateTrack3Plot(
        Well well,
        WellConditioningReport? report,
        double minDepth,
        double maxDepth,
        WellPlotterOptions? options = null)
    {
        options ??= new WellPlotterOptions();
        var plot = new Plot();
        ConfigurePlotTheme(plot);

        plot.Axes.SetLimitsY(bottom: maxDepth, top: minDepth);
        plot.Axes.SetLimitsX(options.DensityMin, options.DensityMax);

        // Density ticks on bottom axis
        var densityTicks = new NumericManual();
        densityTicks.AddMajor(1.95, "1.95");
        densityTicks.AddMajor(2.20, "2.20");
        densityTicks.AddMajor(2.45, "2.45");
        densityTicks.AddMajor(2.70, "2.70");
        densityTicks.AddMajor(2.95, "2.95");
        plot.Axes.Bottom.TickGenerator = densityTicks;

        var rhobMnemonic = report?.PrimaryCurves.GetValueOrDefault("Track3_BulkDensity")?.SelectedMnemonic;
        var nphiMnemonic = report?.PrimaryCurves.GetValueOrDefault("Track3_NeutronPorosity")?.SelectedMnemonic;

        var rhobCurve = FindCurve(well, rhobMnemonic, WellMeasurementType.BulkDensity);
        var nphiCurve = FindCurve(well, nphiMnemonic, WellMeasurementType.NeutronPorosity);

        var hasRhob = rhobCurve is not null && HasValidValues(rhobCurve);
        var hasNphi = nphiCurve is not null && HasValidValues(nphiCurve);

        if (!hasRhob && !hasNphi)
        {
            plot.Title("Track 3: Porosity Overlay\n[No Density / Neutron Curves]");
            AddCenteredNotice(plot, "No Porosity data", options.DensityMin, options.DensityMax, minDepth, maxDepth);
            return plot;
        }

        var titleParts = new List<string>();
        if (hasRhob) titleParts.Add($"{rhobCurve!.Mnemonic} (RHOB)");
        if (hasNphi) titleParts.Add($"{nphiCurve!.Mnemonic} (NPHI)");
        plot.Title($"Track 3: {string.Join(" + ", titleParts)}");
        plot.Axes.Bottom.Label.Text = "RHOB (1.95-2.95) | NPHI (0.45 - -0.15)";

        // Plot Bulk Density (1.95 to 2.95 g/cm³)
        if (hasRhob)
        {
            AddContiguousSegments(plot, well.Depths, rhobCurve!.Values, ColorDensityBlue, 1.8f);
        }

        // Plot Neutron Porosity (0.45 to -0.15 reversed mapped onto 1.95 - 2.95 range)
        if (hasNphi)
        {
            var mappedNphi = new double?[nphiCurve!.Values.Length];
            for (var i = 0; i < nphiCurve.Values.Length; i++)
            {
                var val = nphiCurve.Values[i];
                if (val is { } n)
                {
                    // Normalize percentage to decimal fraction if necessary (e.g. 45% -> 0.45)
                    var normN = n > 1.5 ? n / 100.0 : n;

                    // Linear mapping: 0.45 -> 1.95, -0.15 -> 2.95
                    var mappedX = 1.95 + ((0.45 - normN) / 0.60) * 1.0;
                    mappedNphi[i] = mappedX;
                }
                else
                {
                    mappedNphi[i] = null;
                }
            }

            AddContiguousSegments(plot, well.Depths, mappedNphi, ColorNeutronOrange, 1.8f, LinePattern.Dashed);
        }

        return plot;
    }

    /// <summary>
    /// Creates the Depth Ruler Track showing clean depth ticks and depth numbers.
    /// </summary>
    public static Plot CreateDepthTrackPlot(
        Well well,
        double minDepth,
        double maxDepth)
    {
        var plot = new Plot();
        ConfigurePlotTheme(plot);

        plot.Axes.SetLimitsY(bottom: maxDepth, top: minDepth);
        plot.Axes.SetLimitsX(0, 1);

        plot.Title($"Depth\n({well.DepthUnit})");
        plot.Axes.Bottom.TickGenerator = new NumericManual(); // No horizontal ticks
        plot.Axes.Bottom.Label.Text = well.DepthUnit;

        // Depth axis on left with clean grid
        plot.Axes.Left.TickGenerator = new NumericAutomatic();

        return plot;
    }

    private static void ConfigurePlotTheme(Plot plot)
    {
        plot.FigureBackground.Color = Colors.White;
        plot.DataBackground.Color = ScottPlot.Color.FromHex("#FAFAFA");
        plot.Grid.MajorLineColor = ColorGridGray;
        plot.Grid.MinorLineColor = ScottPlot.Color.FromHex("#F0F0F0");
    }

    private static void AddContiguousSegments(
        Plot plot,
        double[] depths,
        double?[] values,
        ScottPlot.Color color,
        float lineWidth,
        LinePattern? pattern = null)
    {
        var activePattern = pattern ?? LinePattern.Solid;
        var currentXs = new List<double>();
        var currentDepths = new List<double>();

        for (var i = 0; i < depths.Length; i++)
        {
            var v = values[i];
            if (v.HasValue && !double.IsNaN(v.Value) && double.IsFinite(v.Value))
            {
                currentXs.Add(v.Value);
                currentDepths.Add(depths[i]);
            }
            else
            {
                // Break contiguous segment
                if (currentXs.Count > 0)
                {
                    var line = plot.Add.ScatterLine(currentXs.ToArray(), currentDepths.ToArray());
                    line.Color = color;
                    line.LineWidth = lineWidth;
                    line.LinePattern = activePattern;
                    currentXs.Clear();
                    currentDepths.Clear();
                }
            }
        }

        if (currentXs.Count > 0)
        {
            var line = plot.Add.ScatterLine(currentXs.ToArray(), currentDepths.ToArray());
            line.Color = color;
            line.LineWidth = lineWidth;
            line.LinePattern = activePattern;
        }
    }

    private static void AddCenteredNotice(
        Plot plot,
        string message,
        double xMin,
        double xMax,
        double depthMin,
        double depthMax)
    {
        var midX = (xMin + xMax) / 2.0;
        var midY = (depthMin + depthMax) / 2.0;
        var text = plot.Add.Text(message, midX, midY);
        text.LabelFontColor = ScottPlot.Color.FromHex("#9E9E9E");
        text.LabelFontSize = 14;
        text.LabelBold = true;
        text.LabelAlignment = Alignment.MiddleCenter;
    }

    private static Curve? FindCurve(Well well, string? mnemonic, params WellMeasurementType[] fallbackTypes)
    {
        if (!string.IsNullOrWhiteSpace(mnemonic))
        {
            var exact = well.Curves.FirstOrDefault(c => c.Mnemonic.Equals(mnemonic, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact;
        }

        var aliasDict = CurveAliasDictionary.Default;
        foreach (var type in fallbackTypes)
        {
            var matched = well.Curves.FirstOrDefault(c => aliasDict.Resolve(c.Mnemonic) == type);
            if (matched is not null) return matched;
        }

        return null;
    }

    private static bool HasValidValues(Curve curve, Func<double, bool>? predicate = null)
    {
        predicate ??= (_ => true);
        return curve.Values.Any(v => v.HasValue && !double.IsNaN(v.Value) && double.IsFinite(v.Value) && predicate(v.Value));
    }

    private static void DrawHeader(
        SKCanvas canvas,
        Well well,
        WellConditioningReport? report,
        double minDepth,
        double maxDepth,
        int width,
        int height)
    {
        // Dark header banner
        using var bgPaint = new SKPaint
        {
            Color = new SKColor(24, 32, 44), // Dark Navy/Slate
            Style = SKPaintStyle.Fill
        };
        canvas.DrawRect(0, 0, width, height, bgPaint);

        // Header Title: Well Name
        var wellDisplayName = !string.IsNullOrWhiteSpace(well.Name) ? well.Name : "UNNAMED WELL";
        using var titlePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        using var titleFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 20);
        canvas.DrawText($"WELL: {wellDisplayName}", 20, 32, SKTextAlign.Left, titleFont, titlePaint);

        // Subtitle: Source file + Depth Range + Row count
        var sourceFile = !string.IsNullOrWhiteSpace(report?.SourcePath) ? Path.GetFileName(report.SourcePath) : "LAS Log";
        var depthSpan = maxDepth - minDepth;
        var subText = $"File: {sourceFile}   |   Depth: {minDepth:F1} - {maxDepth:F1} {well.DepthUnit} (Span: {depthSpan:F1} {well.DepthUnit})   |   Rows: {well.Depths.Length:N0}   |   Curves: {well.Curves.Length}";

        using var subPaint = new SKPaint
        {
            Color = new SKColor(180, 195, 210),
            IsAntialias = true
        };
        using var subFont = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal), 13);
        canvas.DrawText(subText, 20, 56, SKTextAlign.Left, subFont, subPaint);

        // Legend tags on the right
        DrawLegendPill(canvas, width - 480, 18, "Track 1: GR (gAPI)", new SKColor(46, 125, 50));
        DrawLegendPill(canvas, width - 330, 18, "Track 2: Res (Log)", new SKColor(198, 40, 40));
        DrawLegendPill(canvas, width - 180, 18, "Track 3: RHOB / NPHI", new SKColor(21, 101, 192));
    }

    private static void DrawLegendPill(SKCanvas canvas, float x, float y, string label, SKColor color)
    {
        using var pillPaint = new SKPaint
        {
            Color = color,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + 140, y + 26), 4), pillPaint);

        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 11);
        canvas.DrawText(label, x + 70, y + 17, SKTextAlign.Center, font, textPaint);
    }

    private static byte[] RenderEmptyPlot(string message, int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        using var paint = new SKPaint
        {
            Color = SKColors.DarkGray,
            IsAntialias = true
        };
        using var font = new SKFont(SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold), 20);
        canvas.DrawText(message, width / 2f, height / 2f, SKTextAlign.Center, font, paint);

        using var img = SKImage.FromBitmap(bitmap);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
