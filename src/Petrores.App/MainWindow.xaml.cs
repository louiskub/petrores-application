using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
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
    private LasHeaderEntry[] currentWellEntries = [];
    private LasHeaderEntry[] currentParamEntries = [];
    private LasHeaderEntry[] currentVersionEntries = [];
    private string currentOtherText = string.Empty;
    private string? _selectedTrack4Mnemonic;
    private bool _isUpdatingCmbTrack4;

    private CancellationTokenSource? backgroundCts;

    private CancellationTokenSource? currentPlotCts;

    public MainWindow()
    {
        InitializeComponent();
        ImgPlot.LayoutTransform = plotScaleTransform;

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
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

    private void LoadFolder(string folderPath)
    {
        backgroundCts?.Cancel();
        backgroundCts?.Dispose();
        backgroundCts = new CancellationTokenSource();
        var ct = backgroundCts.Token;

        allWells.Clear();
        LstWells.ItemsSource = null;

        var files = Directory.GetFiles(folderPath, "*.las", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(folderPath, "*.LAS", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var list = new List<WellItemViewModel>(files.Length);
        foreach (var file in files)
        {
            list.Add(new WellItemViewModel(file));
        }

        allWells.AddRange(list);
        ApplySearchFilter();

        TxtWellCount.Text = $"{allWells.Count} files";
        TxtStatus.Text = $"Found {allWells.Count} files. Processing in background...";

        if (LstWells.Items.Count > 0)
        {
            LstWells.SelectedIndex = 0;
        }

        _ = ProcessWellsInBackgroundAsync(allWells, ct);
    }

    private async Task ProcessWellsInBackgroundAsync(List<WellItemViewModel> items, CancellationToken ct)
    {
        PbProgress.Visibility = Visibility.Visible;
        PbProgress.IsIndeterminate = false;
        PbProgress.Minimum = 0;
        PbProgress.Maximum = items.Count;
        PbProgress.Value = 0;

        var processedCount = 0;

        var bgParallelism = Math.Max(1, Environment.ProcessorCount / 2);

        await Task.Run(() =>
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = bgParallelism,
                CancellationToken = ct
            };

            try
            {
                Parallel.ForEach(items, options, item =>
                {
                    if (ct.IsCancellationRequested) return;

                    var localReader = new LasReader();
                    item.EnsureLoaded(localReader);

                    var count = Interlocked.Increment(ref processedCount);

                    if (count % 3 == 0 || count == items.Count)
                    {
                        Dispatcher.InvokeAsync(() =>
                        {
                            if (ct.IsCancellationRequested) return;
                            PbProgress.Value = count;
                            var ok = items.Count(w => w.StatusText == "OK");
                            var warn = items.Count(w => w.StatusText == "WARN");
                            var fail = items.Count(w => w.StatusText == "FAIL");
                            TxtStatus.Text = $"Processed {count}/{items.Count} wells (OK={ok}, WARN={warn}, FAIL={fail})";
                        });
                    }
                });
            }
            catch (OperationCanceledException) { }
        }, ct).ContinueWith(t =>
        {
            if (ct.IsCancellationRequested) return;

            Dispatcher.InvokeAsync(() =>
            {
                PbProgress.Visibility = Visibility.Collapsed;
                var ok = items.Count(w => w.StatusText == "OK");
                var warn = items.Count(w => w.StatusText == "WARN");
                var fail = items.Count(w => w.StatusText == "FAIL");
                TxtStatus.Text = $"Ready. All {items.Count} wells processed. OK={ok}, WARN={warn}, FAIL={fail}";
            });
        }, TaskScheduler.FromCurrentSynchronizationContext());
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
            UpdateMetadata(null);
            _isUpdatingCmbTrack4 = true;
            CmbTrack4.Items.Clear();
            _isUpdatingCmbTrack4 = false;
            return;
        }

        currentPlotCts?.Cancel();
        currentPlotCts?.Dispose();
        currentPlotCts = new CancellationTokenSource();
        var plotCt = currentPlotCts.Token;

        try
        {
            TxtSelectedWellTitle.Text = $"WELL: {item.DisplayName}";
            TxtSelectedWellDetails.Text = $"File: {item.FileName}  |  Status: {item.StatusText}  |  {item.SummaryText}";

            if (!item.IsLoaded)
            {
                TxtStatus.Text = $"Loading '{item.FileName}' (Priority)...";
                await Task.Run(() =>
                {
                    var dedicatedReader = new LasReader();
                    item.EnsureLoaded(dedicatedReader);
                }, plotCt);
            }

            if (plotCt.IsCancellationRequested) return;

            TxtSelectedWellTitle.Text = $"WELL: {item.DisplayName}";
            TxtSelectedWellDetails.Text = $"File: {item.FileName}  |  Status: {item.StatusText}  |  {item.SummaryText}";

            UpdateMetadata(item.LoadResult?.Well);

            if (item.ConditionResult is null)
            {
                ImgPlot.Source = null;
                TxtNoPlot.Text = "Cannot plot: LAS file could not be read.\nSee diagnostics tab for details.";
                TxtNoPlot.Visibility = Visibility.Visible;
                TxtConditioningReport.Text = "File failed to load.\n" + (item.LoadResult is not null ? string.Join(Environment.NewLine, item.LoadResult.Diagnostics.Select(d => d.Message)) : "");
                LvCurves.ItemsSource = null;
                _isUpdatingCmbTrack4 = true;
                CmbTrack4.Items.Clear();
                _isUpdatingCmbTrack4 = false;
                return;
            }

            var report = item.ConditionResult.Report;
            TxtConditioningReport.Text = report.ToJson(indented: true);
            LvCurves.ItemsSource = report.Curves;

            PopulateTrack4ComboBox(item);

            await RenderPlotAsync(item, plotCt);
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error rendering plot for '{item.DisplayName}': {ex.Message}";
            TxtNoPlot.Text = $"Failed to render plot: {ex.Message}";
            TxtNoPlot.Visibility = Visibility.Visible;
            ImgPlot.Source = null;
        }
    }

    private void PopulateTrack4ComboBox(WellItemViewModel item)
    {
        _isUpdatingCmbTrack4 = true;
        try
        {
            CmbTrack4.Items.Clear();
            CmbTrack4.Items.Add("None (Standard 3-Track)");

            if (item.ConditionResult is not null)
            {
                foreach (var c in item.ConditionResult.ConditionedWell.Curves)
                {
                    var unitStr = !string.IsNullOrWhiteSpace(c.Unit) ? $" ({c.Unit})" : "";
                    CmbTrack4.Items.Add($"{c.Mnemonic}{unitStr}");
                }
            }

            var selectedIdx = 0;
            if (!string.IsNullOrEmpty(_selectedTrack4Mnemonic))
            {
                for (var i = 1; i < CmbTrack4.Items.Count; i++)
                {
                    var text = CmbTrack4.Items[i]?.ToString() ?? "";
                    if (text.StartsWith(_selectedTrack4Mnemonic + " (", StringComparison.OrdinalIgnoreCase) ||
                        text.Equals(_selectedTrack4Mnemonic, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIdx = i;
                        break;
                    }
                }
            }

            CmbTrack4.SelectedIndex = selectedIdx;
            if (selectedIdx == 0)
            {
                _selectedTrack4Mnemonic = null;
            }
        }
        finally
        {
            _isUpdatingCmbTrack4 = false;
        }
    }

    private async void CmbTrack4_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingCmbTrack4) return;
        if (LstWells.SelectedItem is not WellItemViewModel item || item.ConditionResult is null) return;

        if (CmbTrack4.SelectedIndex <= 0)
        {
            _selectedTrack4Mnemonic = null;
        }
        else
        {
            var selectedText = CmbTrack4.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedText))
            {
                var idx = selectedText.IndexOf(" (", StringComparison.Ordinal);
                _selectedTrack4Mnemonic = idx > 0 ? selectedText[..idx].Trim() : selectedText.Trim();
            }
            else
            {
                _selectedTrack4Mnemonic = null;
            }
        }

        currentPlotCts?.Cancel();
        currentPlotCts?.Dispose();
        currentPlotCts = new CancellationTokenSource();
        var plotCt = currentPlotCts.Token;

        try
        {
            await RenderPlotAsync(item, plotCt);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Error rendering plot for '{item.DisplayName}': {ex.Message}";
        }
    }

    private async Task RenderPlotAsync(WellItemViewModel item, CancellationToken plotCt)
    {
        if (item.ConditionResult is null) return;

        TxtNoPlot.Visibility = Visibility.Collapsed;
        TxtStatus.Text = $"Rendering log plot for '{item.DisplayName}'...";

        byte[]? plotBytes = null;
        if (_selectedTrack4Mnemonic is null)
        {
            plotBytes = item.CachedPlotBytes;
        }

        if (plotBytes is null)
        {
            var options = new WellPlotterOptions(Track4CurveMnemonic: _selectedTrack4Mnemonic);
            plotBytes = await Task.Run(() => WellPlotter.RenderCompositePlot(
                item.ConditionResult.ConditionedWell,
                item.ConditionResult.Report,
                options), plotCt);

            if (plotCt.IsCancellationRequested) return;

            if (_selectedTrack4Mnemonic is null)
            {
                item.CachedPlotBytes = plotBytes;
            }
        }

        if (!plotCt.IsCancellationRequested)
        {
            ImgPlot.Source = LoadBitmapFromBytes(plotBytes);
            TxtStatus.Text = $"Plot rendered for '{item.DisplayName}'. ({plotBytes.Length / 1024} KB)";
        }
    }

    private void BtnExportCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (LstWells.SelectedItem is not WellItemViewModel item)
        {
            MessageBox.Show("Please select a well to export.", "No Well Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!item.IsLoaded)
        {
            var dedicatedReader = new LasReader();
            item.EnsureLoaded(dedicatedReader);
        }

        if (item.ConditionResult is null)
        {
            MessageBox.Show("Selected well cannot be plotted because it failed to load.", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                var options = new WellPlotterOptions(Track4CurveMnemonic: _selectedTrack4Mnemonic);
                var bytes = (_selectedTrack4Mnemonic is null ? item.CachedPlotBytes : null) ?? WellPlotter.RenderCompositePlot(
                    item.ConditionResult.ConditionedWell,
                    item.ConditionResult.Report,
                    options);

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

        var snapshot = allWells.ToArray();
        var total = snapshot.Length;
        if (total == 0) return;

        BtnBatchExport.IsEnabled = false;
        PbProgress.Visibility = Visibility.Visible;
        PbProgress.IsIndeterminate = false;
        PbProgress.Minimum = 0;
        PbProgress.Maximum = total;
        PbProgress.Value = 0;

        var wellReports = new List<WellConditioningReport>();

        try
        {
            Directory.CreateDirectory(plotsDir);
            await Task.Run(() =>
            {
                for (var i = 0; i < total; i++)
                {
                    var wellItem = snapshot[i];
                    if (!wellItem.IsLoaded)
                    {
                        var reader = new LasReader();
                        wellItem.EnsureLoaded(reader);
                    }

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

                var totalReversed = wellReports.Count(r => r.Depth.IsReversed);
                var totalDupFiles = wellReports.Count(r => r.Depth.DuplicateRowCount > 0);
                var totalDupRows = wellReports.Sum(r => r.Depth.DuplicateRowCount);

                var batchReport = new BatchConditioningReport(
                    GeneratedAtUtc: DateTime.UtcNow,
                    TotalFiles: total,
                    CleanCount: snapshot.Count(w => w.StatusText == "OK"),
                    WarningCount: snapshot.Count(w => w.StatusText == "WARN"),
                    FailedCount: snapshot.Count(w => w.StatusText == "FAIL"),
                    TotalReversedFiles: totalReversed,
                    TotalDuplicateDepthFiles: totalDupFiles,
                    TotalDuplicateDepthRowsMerged: totalDupRows,
                    Wells: wellReports);

                batchReport.SaveJson(reportPath, indented: true);
            });

            TxtStatus.Text = $"Batch export complete! {wellReports.Count} plots saved to '{plotsDir}'.";
            MessageBox.Show(
                $"Batch Export Complete!\n\n• Plots: {wellReports.Count} saved to:\n  {plotsDir}\n\n• Conditioning Report saved to:\n  {reportPath}",
                "Batch Export Succeeded",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"Batch export failed: {ex.Message}";
            MessageBox.Show($"Batch export encountered an error:\n{ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnBatchExport.IsEnabled = true;
            PbProgress.Visibility = Visibility.Collapsed;
        }
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

    private void TxtMetaSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyMetadataFilter();
    }

    private void UpdateMetadata(Well? well)
    {
        if (well is null)
        {
            currentWellEntries = [];
            currentParamEntries = [];
            currentVersionEntries = [];
            currentOtherText = string.Empty;
        }
        else
        {
            currentWellEntries = well.WellInformation;
            currentParamEntries = well.ParameterInformation ?? [];
            currentVersionEntries = well.VersionInformation;
            currentOtherText = !string.IsNullOrWhiteSpace(well.OtherInformation)
                ? well.OtherInformation
                : "(No ~Other Information remarks found in this LAS file)";
        }

        ApplyMetadataFilter();
    }

    private void ApplyMetadataFilter()
    {
        var filter = TxtMetaSearch?.Text?.Trim() ?? string.Empty;

        bool Match(LasHeaderEntry entry)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            return entry.Mnemonic.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Value.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || entry.Unit.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        var filteredWell = currentWellEntries.Where(Match).ToList();
        var filteredParam = currentParamEntries.Where(Match).ToList();
        var filteredVersion = currentVersionEntries.Where(Match).ToList();

        if (LvWellInfo is not null) LvWellInfo.ItemsSource = filteredWell;
        if (LvParameters is not null) LvParameters.ItemsSource = filteredParam;
        if (LvVersionInfo is not null) LvVersionInfo.ItemsSource = filteredVersion;
        if (TxtOtherInfo is not null) TxtOtherInfo.Text = currentOtherText;

        if (TabWellInfo is not null) TabWellInfo.Header = $"  Well Info (~W) ({filteredWell.Count})  ";
        if (TabParameters is not null) TabParameters.Header = $"  Parameters (~P) ({filteredParam.Count})  ";
        if (TabVersion is not null) TabVersion.Header = $"  Version (~V) ({filteredVersion.Count})  ";
        if (TabOther is not null) TabOther.Header = string.IsNullOrWhiteSpace(currentOtherText) || currentOtherText.StartsWith("(")
            ? "  Remarks / Other (~O)  "
            : "  Remarks / Other (~O) •  ";
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

public sealed class WellItemViewModel : INotifyPropertyChanged
{
    private static readonly Brush WaitingBrush = CreateFrozenBrush(Color.FromRgb(148, 163, 184));
    private static readonly Brush OkBrush = CreateFrozenBrush(Color.FromRgb(46, 125, 50));
    private static readonly Brush WarnBrush = CreateFrozenBrush(Color.FromRgb(230, 81, 0));
    private static readonly Brush FailBrush = CreateFrozenBrush(Color.FromRgb(198, 40, 40));

    private static Brush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private readonly object syncLock = new();

    public string FilePath { get; }
    public string FileName => Path.GetFileName(FilePath);

    private string displayName;
    public string DisplayName
    {
        get => displayName;
        private set { displayName = value; OnPropertyChanged(); }
    }

    private string statusText = "WAITING";
    public string StatusText
    {
        get => statusText;
        private set { statusText = value; OnPropertyChanged(); }
    }

    private Brush statusBadgeColor = WaitingBrush;
    public Brush StatusBadgeColor
    {
        get => statusBadgeColor;
        private set { statusBadgeColor = value; OnPropertyChanged(); }
    }

    private string summaryText = "Waiting in queue to process...";
    public string SummaryText
    {
        get => summaryText;
        private set { summaryText = value; OnPropertyChanged(); }
    }

    public bool IsLoaded { get; private set; }
    public LasLoadResult? LoadResult { get; private set; }
    public WellConditionResult? ConditionResult { get; private set; }
    public byte[]? CachedPlotBytes { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WellItemViewModel(string filePath)
    {
        FilePath = filePath;
        displayName = Path.GetFileNameWithoutExtension(filePath);
    }

    public void EnsureLoaded(LasReader reader)
    {
        if (IsLoaded) return;

        lock (syncLock)
        {
            if (IsLoaded) return;

            var load = reader.Read(FilePath);
            LoadResult = load;

            if (load.Well is null)
            {
                StatusText = "FAIL";
                StatusBadgeColor = FailBrush;
                SummaryText = "Load failed: bad or unreadable file.";
            }
            else
            {
                var cond = WellConditioner.Condition(load.Well, FilePath, diagnostics: load.Diagnostics);
                ConditionResult = cond;

                if (load.Diagnostics.Length > 0)
                {
                    StatusText = "WARN";
                    StatusBadgeColor = WarnBrush;
                }
                else
                {
                    StatusText = "OK";
                    StatusBadgeColor = OkBrush;
                }

                var well = cond.ConditionedWell;
                if (!string.IsNullOrWhiteSpace(well.Name))
                {
                    DisplayName = well.Name;
                }

                var minD = well.Depths.Length > 0 ? well.Depths.Min() : 0;
                var maxD = well.Depths.Length > 0 ? well.Depths.Max() : 0;
                SummaryText = $"{well.Depths.Length:N0} rows | {well.Curves.Length} curves | {minD:F1} - {maxD:F1} {well.DepthUnit}";
            }

            IsLoaded = true;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
