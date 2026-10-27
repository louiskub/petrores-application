using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Petrores.Core;

namespace Petrores.App;

public partial class MainWindow : Window
{
    private readonly List<WellItemViewModel> allWells = [];
    private double currentZoom = 1.0;
    private readonly ScaleTransform plotScaleTransform = new(1.0, 1.0);

    public MainWindow()
    {
        InitializeComponent();
        ImgPlot.LayoutTransform = plotScaleTransform;

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Check for TestData directory in common relative locations
        var candidates = new[]
        {
            Path.GetFullPath("TestData"),
            Path.GetFullPath("../../../../../TestData"),
            Path.GetFullPath("../../../../TestData"),
            Path.GetFullPath("../../../TestData"),
            Path.GetFullPath("../../TestData")
        };

        foreach (var dir in candidates)
        {
            if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*.las", SearchOption.TopDirectoryOnly).Any())
            {
                TxtFolderPath.Text = dir;
                LoadFolder(dir);
                break;
            }
        }
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select LAS Files Folder",
            InitialDirectory = Directory.Exists(TxtFolderPath.Text) ? TxtFolderPath.Text : Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog() == true)
        {
            TxtFolderPath.Text = dialog.FolderName;
            LoadFolder(dialog.FolderName);
        }
    }

    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(TxtFolderPath.Text))
        {
            LoadFolder(TxtFolderPath.Text);
        }
        else
        {
            MessageBox.Show("Selected directory does not exist.", "Invalid Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void LoadFolder(string folderPath)
    {
        BtnLoad.IsEnabled = false;
        PbProgress.Visibility = Visibility.Visible;
        PbProgress.IsIndeterminate = true;
        TxtStatus.Text = $"Scanning folder '{folderPath}'...";

        allWells.Clear();
        LstWells.ItemsSource = null;

        var items = await Task.Run(() =>
        {
            var files = Directory.GetFiles(folderPath, "*.las", SearchOption.TopDirectoryOnly)
                .Concat(Directory.GetFiles(folderPath, "*.LAS", SearchOption.TopDirectoryOnly))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var reader = new LasReader();
            var list = new List<WellItemViewModel>();

            foreach (var file in files)
            {
                var loadResult = reader.Read(file);
                WellConditionResult? condResult = null;
                string statusText;
                Brush statusBrush;

                if (loadResult.Well is null)
                {
                    statusText = "FAIL";
                    statusBrush = new SolidColorBrush(Color.FromRgb(198, 40, 40));
                }
                else
                {
                    condResult = WellConditioner.Condition(loadResult.Well, file, diagnostics: loadResult.Diagnostics);
                    if (loadResult.Diagnostics.Length > 0)
                    {
                        statusText = "WARN";
                        statusBrush = new SolidColorBrush(Color.FromRgb(230, 81, 0));
                    }
                    else
                    {
                        statusText = "OK";
                        statusBrush = new SolidColorBrush(Color.FromRgb(46, 125, 50));
                    }
                }

                statusBrush.Freeze();
                list.Add(new WellItemViewModel(file, loadResult, condResult, statusText, statusBrush));
            }

            return list;
        });

        allWells.AddRange(items);
        ApplySearchFilter();

        BtnLoad.IsEnabled = true;
        PbProgress.Visibility = Visibility.Collapsed;
        TxtWellCount.Text = $"{allWells.Count} files";
        TxtStatus.Text = $"Loaded {allWells.Count} wells. OK={allWells.Count(w => w.StatusText == "OK")}, WARN={allWells.Count(w => w.StatusText == "WARN")}, FAIL={allWells.Count(w => w.StatusText == "FAIL")}";

        if (LstWells.Items.Count > 0)
        {
            LstWells.SelectedIndex = 0;
        }
    }

    private void TxtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplySearchFilter();
    }

    private void ApplySearchFilter()
    {
        var filter = TxtSearchFilter.Text.Trim();
        if (string.IsNullOrEmpty(filter))
        {
            LstWells.ItemsSource = allWells;
        }
        else
        {
            LstWells.ItemsSource = allWells.Where(w =>
                w.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.FileName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                w.StatusText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    private async void LstWells_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstWells.SelectedItem is not WellItemViewModel item)
        {
            TxtSelectedWellTitle.Text = "Select a well from the list to view log plot";
            TxtSelectedWellDetails.Text = "";
            ImgPlot.Source = null;
            TxtNoPlot.Visibility = Visibility.Visible;
            TxtConditioningReport.Text = "";
            LvCurves.ItemsSource = null;
            return;
        }

        TxtSelectedWellTitle.Text = $"WELL: {item.DisplayName}";
        TxtSelectedWellDetails.Text = $"File: {item.FileName}  |  Status: {item.StatusText}  |  {item.SummaryText}";

        if (item.ConditionResult is null)
        {
            ImgPlot.Source = null;
            TxtNoPlot.Text = "Cannot plot: LAS file could not be read.\nSee diagnostics tab for details.";
            TxtNoPlot.Visibility = Visibility.Visible;
            TxtConditioningReport.Text = "File failed to load.\n" + string.Join(Environment.NewLine, item.LoadResult.Diagnostics.Select(d => d.Message));
            LvCurves.ItemsSource = null;
            return;
        }

        var report = item.ConditionResult.Report;
        TxtConditioningReport.Text = report.ToJson(indented: true);
        LvCurves.ItemsSource = report.Curves;

        // Render plot
        TxtNoPlot.Visibility = Visibility.Collapsed;
        TxtStatus.Text = $"Rendering log plot for '{item.DisplayName}'...";

        byte[]? plotBytes = item.CachedPlotBytes;
        if (plotBytes is null)
        {
            plotBytes = await Task.Run(() => WellPlotter.RenderCompositePlot(
                item.ConditionResult.ConditionedWell,
                item.ConditionResult.Report));
            item.CachedPlotBytes = plotBytes;
        }

        ImgPlot.Source = LoadBitmapFromBytes(plotBytes);
        TxtStatus.Text = $"Plot rendered for '{item.DisplayName}'. ({plotBytes.Length / 1024} KB)";
    }

    private void BtnExportCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (LstWells.SelectedItem is not WellItemViewModel item || item.ConditionResult is null)
        {
            MessageBox.Show("Please select a valid loaded well to export its plot.", "No Well Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var sfd = new SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png",
            FileName = $"{Path.GetFileNameWithoutExtension(item.FileName)}_Plot.png",
            Title = "Save Well Log Plot"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                var bytes = item.CachedPlotBytes ?? WellPlotter.RenderCompositePlot(
                    item.ConditionResult.ConditionedWell,
                    item.ConditionResult.Report);

                File.WriteAllBytes(sfd.FileName, bytes);
                MessageBox.Show($"Plot exported successfully:\n{sfd.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save plot: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void BtnBatchExport_Click(object sender, RoutedEventArgs e)
    {
        if (allWells.Count == 0)
        {
            MessageBox.Show("Please load a folder of LAS files first.", "No Files Loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Select Destination Folder for Exported Plots & Report",
            InitialDirectory = Directory.GetCurrentDirectory()
        };

        if (dialog.ShowDialog() != true) return;

        var targetFolder = dialog.FolderName;
        var plotsDir = Path.Combine(targetFolder, "plots");
        var reportPath = Path.Combine(targetFolder, "conditioning-report.json");
        Directory.CreateDirectory(plotsDir);

        BtnBatchExport.IsEnabled = false;
        PbProgress.Visibility = Visibility.Visible;
        PbProgress.IsIndeterminate = false;
        PbProgress.Minimum = 0;
        PbProgress.Maximum = allWells.Count;
        PbProgress.Value = 0;

        var wellReports = new List<WellConditioningReport>();
        var total = allWells.Count;

        await Task.Run(() =>
        {
            for (var i = 0; i < total; i++)
            {
                var wellItem = allWells[i];
                if (wellItem.ConditionResult is not null)
                {
                    wellReports.Add(wellItem.ConditionResult.Report);

                    var plotName = $"{i + 1:D2}_{Path.GetFileNameWithoutExtension(wellItem.FileName)}.png";
                    var plotFile = Path.Combine(plotsDir, plotName);

                    var bytes = wellItem.CachedPlotBytes ?? WellPlotter.RenderCompositePlot(
                        wellItem.ConditionResult.ConditionedWell,
                        wellItem.ConditionResult.Report);

                    File.WriteAllBytes(plotFile, bytes);
                }

                Dispatcher.Invoke(() =>
                {
                    PbProgress.Value = i + 1;
                    TxtStatus.Text = $"Exporting [{i + 1}/{total}]: {wellItem.FileName}...";
                });
            }

            // Save batch report
            var totalReversed = wellReports.Count(r => r.Depth.IsReversed);
            var totalDupFiles = wellReports.Count(r => r.Depth.DuplicateRowCount > 0);
            var totalDupRows = wellReports.Sum(r => r.Depth.DuplicateRowCount);

            var batchReport = new BatchConditioningReport(
                GeneratedAtUtc: DateTime.UtcNow,
                TotalFiles: total,
                CleanCount: allWells.Count(w => w.StatusText == "OK"),
                WarningCount: allWells.Count(w => w.StatusText == "WARN"),
                FailedCount: allWells.Count(w => w.StatusText == "FAIL"),
                TotalReversedFiles: totalReversed,
                TotalDuplicateDepthFiles: totalDupFiles,
                TotalDuplicateDepthRowsMerged: totalDupRows,
                Wells: wellReports);

            batchReport.SaveJson(reportPath, indented: true);
        });

        BtnBatchExport.IsEnabled = true;
        PbProgress.Visibility = Visibility.Collapsed;
        TxtStatus.Text = $"Batch export complete! {wellReports.Count} plots saved to '{plotsDir}'.";

        MessageBox.Show(
            $"Batch Export Complete!\n\n• Plots: {wellReports.Count} saved to:\n  {plotsDir}\n\n• Conditioning Report saved to:\n  {reportPath}",
            "Batch Export Succeeded",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
    {
        currentZoom = Math.Min(3.0, currentZoom + 0.2);
        ApplyZoom();
    }

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
    {
        currentZoom = Math.Max(0.4, currentZoom - 0.2);
        ApplyZoom();
    }

    private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
    {
        currentZoom = 1.0;
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        plotScaleTransform.ScaleX = currentZoom;
        plotScaleTransform.ScaleY = currentZoom;
        BtnResetZoom.Content = $"{Math.Round(currentZoom * 100)}%";
    }

    private static BitmapImage LoadBitmapFromBytes(byte[] bytes)
    {
        var image = new BitmapImage();
        using var stream = new MemoryStream(bytes);
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public sealed class WellItemViewModel
{
    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);
    public string DisplayName { get; }
    public LasLoadResult LoadResult { get; }
    public WellConditionResult? ConditionResult { get; }
    public string StatusText { get; }
    public Brush StatusBadgeColor { get; }
    public string SummaryText { get; }
    public byte[]? CachedPlotBytes { get; set; }

    public WellItemViewModel(
        string filePath,
        LasLoadResult loadResult,
        WellConditionResult? conditionResult,
        string statusText,
        Brush statusBadgeColor)
    {
        FilePath = filePath;
        LoadResult = loadResult;
        ConditionResult = conditionResult;
        StatusText = statusText;
        StatusBadgeColor = statusBadgeColor;

        if (conditionResult is not null)
        {
            var well = conditionResult.ConditionedWell;
            DisplayName = !string.IsNullOrWhiteSpace(well.Name) ? well.Name : Path.GetFileNameWithoutExtension(filePath);
            var minD = well.Depths.Length > 0 ? well.Depths.Min() : 0;
            var maxD = well.Depths.Length > 0 ? well.Depths.Max() : 0;
            SummaryText = $"{well.Depths.Length:N0} rows | {well.Curves.Length} curves | {minD:F1} - {maxD:F1} {well.DepthUnit}";
        }
        else
        {
            DisplayName = Path.GetFileNameWithoutExtension(filePath);
            SummaryText = "Load failed: bad or unreadable file.";
        }
    }
}
