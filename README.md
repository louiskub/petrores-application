# Petrores Studio — Well Log Processing, Conditioning & 3-Track Plotting

**Software Engineering Intern Coding Assignment — Petrophysics Studio**  
Author: Louis | Target: **.NET 8 (C# 12)** | UI: **WPF**

---

## 1. How to Build & Run

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (v8.0.400 or later)
- **Desktop UI (`Petrores.App`)**: Windows 10/11 (WPF).
- **Core Engine, CLI Tool (`Petrores.Check`), and Tests**: 100% Cross-Platform (Windows, macOS, Linux).

---

### Commands

#### A. Run Unit Tests (109 Passing Tests)
```bash
# Cross-platform / Windows:
dotnet test tests/Petrores.Core.Tests
```

#### B. Run the Headless Batch CLI Processor (`Petrores.Check`)
Processes LAS files, applies data conditioning, outputs diagnostics, and exports plots:
```bash
# Process 50 wells, export PNG plots and write JSON conditioning report:
dotnet run --project tools/Petrores.Check -- TestData --limit 50 --export-plots output/plots --report output/conditioning-report.json

# Process all files in a folder without limit:
dotnet run --project tools/Petrores.Check -- TestData --export-plots output/plots
```

#### C. Launch the Desktop WPF Application (`Petrores.App`)
```powershell
dotnet run --project src/Petrores.App
```
*(Or press **F5** in VS Code, run `.\run.ps1` in PowerShell, or execute `run.cmd` on Windows.)*

**Interactive UI Features:**
- Select any directory of LAS files (defaults to `TestData/`).
- Filter wells by name or health badge (`OK`, `WARN`, `FAIL`).
- Interactive composite plot viewer with pan, zoom (+/-), and reset.
- **Optional Track 4 dynamic curve selector**: inspect any auxiliary curve (e.g. Caliper `CALI`, Sonic `DT`, `SP`, `PEF`) on the fly with automatic linear scaling and Deep Purple styling (`#7B1FA2`).
- Full LAS header search & inspector (`~W` Well Info, `~P` Parameters like BHT/Rmf, `~V` Version, `~O` Remarks).
- One-click buttons to export the current plot as PNG (including Track 4 if selected) or batch-export all plots and conditioning reports.

**Submitted Deliverables:**
- Pre-generated plots for 50 wells: [`output/plots/`](output/plots/) (`01_*.png` through `50_*.png`).
- Full batch conditioning audit report: [`output/conditioning-report.json`](output/conditioning-report.json).

---

## 2. System Design & Architecture

The solution is structured into four decoupled projects:
- **`src/Petrores.Core`**: Class library containing the custom zero-dependency LAS reader, data conditioner, unit converter, curve alias dictionary, and off-screen composite plotter.
- **`src/Petrores.App`**: Windows desktop WPF application providing an interactive GUI for log visualization and header inspection.
- **`tools/Petrores.Check`**: Headless CLI tool for batch processing, verification, and automated plot/report generation.
- **`tests/Petrores.Core.Tests`**: 109 automated unit tests covering parser robustness, conditioning edge cases, plot generation, and export failures.

### Pipeline Flow
```
Raw LAS File ──► Custom Streaming Parser ──► In-Memory Well Model ──► Conditioner & Unit Normalizer ──► 3-Track Plotter / WPF UI
```

### Performance & Resource Efficiency
The pipeline uses streaming input and array storage to reduce allocation overhead, while memory consumption remains proportional to the loaded well data retained in memory:
1. **Custom Streaming Reader**: Ingests files line-by-line via `StreamReader` instead of loading entire files into memory strings (`File.ReadAllText` / `File.ReadAllLines`), though parsed numeric values subsequently accumulate in memory arrays.
2. **Span-Based Tokenization**: Uses `ReadOnlySpan<char>` slicing and invariant `double.TryParse` to parse data tokens in place rather than calling `string.Split(' ')`, reducing short-lived string allocations during ASCII ingestion.
3. **Array Storage**: Stores numeric depth and curve measurements in contiguous arrays (`double[]`, `double?[]`) without per-cell objects.
4. **Structured Error Recovery**: Recovers from malformed rows and invalid measurements with structured diagnostics, converting corrupt numeric values to null or skipping invalid rows where appropriate.
5. **Headless Off-Screen Rendering**: Generates composite PNG bytes in memory using ScottPlot 5 and SkiaSharp without requiring a GUI thread.

---

## 3. Data-Conditioning Decisions & Rationale

| Area | Conditioning Decision | Rationale (Why) |
|---|---|---|
| **Missing Values** | Sentinels (`-999.25`, `-9999`, etc.), NaNs, and infinities converted to `null`. **Strictly no interpolation.** | In petrophysics, interpolating across missing intervals could fabricate false hydrocarbon shows or fake permeable sandstones. Curves break naturally across gaps. |
| **Depth Ordering** | Normalized to **Ascending (shallow-to-deep)**. Descending logs inverted in $O(N)$ time; mixed logs stably sorted. | Wireline logs are frequently logged bottom-up (descending depth). Standardizing to ascending ensures consistent depth alignment and multi-well comparison. |
| **Duplicate Depths** | Depths within $10^{-6}\text{ m}$ tolerance are merged via arithmetic mean of valid values. | Caused by tool stick/slip or overlapping passes. Averaging valid readings preserves authentic signal while removing duplicate depth rows. Valid readings are kept over nulls. |
| **Step Uniformity** | Computes median step and flags depth steps $> 2\times$ median as gaps. | Flags logging stalls and unrecorded depth intervals for petrophysical audit. |
| **Unit Normalization** | Depth standardized to **meters** ($0.3048\text{ m/ft}$). Porosity percentages ($15\%–45\%$) converted to **decimal fraction $v/v$** ($0.15–0.45$). | Ensures uniform SI depth measurement across mixed contractor logs, and standardizes porosity for mineral/fluid volume calculations. |
| **Curve Aliasing** | 120+ vendor mnemonics mapped to canonical tracks; scored by mnemonic tier & valid data count. | Disparate contractor names (e.g., `RT`, `ILD`, `LLD`, `RT_HRLT`) automatically resolve to the best available primary curve per track. |

Matrix-specific neutron curves keep their original lithology basis. Among `NPRL` (limestone), `NPRS` (sandstone), and `NPRD` (dolomite), limestone is preferred when available. Percentage-to-fraction scaling does not convert between lithology bases; the selected mnemonic and original description are retained.

Additional aliases were checked against the vendor-supplied Energistics PWLS catalogs for [SLB](https://curves.energistics.org/catalog/SLB/curves/), [Baker Hughes](https://curves.energistics.org/catalog/Baker_Hughes/curves/), and [Halliburton](https://curves.energistics.org/catalog/Halliburton/curves/). `HDRA` is a density correction. `RM_HRLT` is mud resistivity: its values are retained as `Unknown` and excluded from automatic formation-resistivity selection. Suffixed names use the longest known alias first, so `RM_HRLT_1` stays excluded rather than falling back to `RM`. Generic vendor-dependent names such as `RM` still require checking the source header.

### 3-Track (+ Optional 4th Track) Plot Layout
- **Track 1 (Lithology / Gamma Ray)**: Linear scale $0–150\text{ gAPI}$, forest green curve (`#2E7D32`).
- **Track 2 (Resistivity)**: 4-decade logarithmic scale $0.2–2000\ \Omega\cdot\text{m}$, crimson red curve (`#C62828`), with decade gridlines. Non-positive and null values are omitted.
- **Track 3 (Porosity Overlay — Density & Neutron)**: 
  - Bulk Density (`RHOB`): Linear scale $1.95–2.95\text{ g/cm}^3$ (left-to-right), blue curve.
  - Neutron Porosity (`NPHI`): Reversed scale $0.45\text{ to }-0.15\text{ v/v}$ (left-to-right), dashed orange curve.
  - *Gas Crossover*: In clean sands, curves track together; in gas zones, density drops (shifts left) while neutron drops (shifts right), highlighting hydrocarbon crossovers.
- **Track 4 (Optional Auxiliary Curve)**: Dynamic single-curve display with automatic linear scale (5% margin padding) rendered in deep purple (`#7B1FA2`). Activated by selecting any curve in the desktop UI or specifying `Track4CurveMnemonic`.
- **Missing Curves**: If a well lacks a channel, the track displays `[No Data]` while all available tracks continue to render cleanly.

---

## 4. Known Limitations

1. **Ambiguous Wrapped Records**: Wrapped depth values must appear on their own lines. A single-value continuation that could also be a depth within the declared `STRT`/`STOP` range is ambiguous; loading stops there and retains earlier complete records. Without a valid depth range, single-value continuations also stop loading. The reader does not guess record boundaries from `STEP`. Corruption that still looks like a complete numeric record cannot always be detected.
2. **LAS 3.0 Multidimensional Arrays**: Multidimensional array channels and indexed log sections (e.g., waveform arrays with `[n]` dimensions) are safely detected and rejected with clear diagnostic reporting, as scalar curve models are used for composite logging.
3. **Fixed 3-Track Default Batch Export**: Batch exports default to standardized 3-track composite views for consistency, while auxiliary curves (Caliper, PEF, Sonic DT) can be inspected on demand in the desktop UI via Track 4 or audited in the JSON report.
4. **GUI Platform Requirement**: The desktop UI (`Petrores.App`) is built with WPF for Windows. The Core library, CLI batch exporter, and unit test suite are fully cross-platform (Windows, macOS, Linux).

---

## 5. How AI Was Used

In accordance with the assignment guidelines, AI tools were used as interactive pair-programming partners in the following areas:

- **Domain & Specification Research**: Investigating CWLS LAS 1.2, 2.0, and 3.0 specifications, standard petrophysical 3-track plot conventions (e.g., logarithmic resistivity, density-neutron crossover), and real-world corrupted file patterns.
- **Mnemonic Taxonomy Compilation**: Cataloging and categorizing 120+ vendor-specific curve mnemonics (Schlumberger, Halliburton, Baker Hughes) into canonical measurement types and track priority tiers.
- **Architecture & Trade-off Brainstorming**: Evaluating technical trade-offs before implementation (e.g., arithmetic mean vs. first non-null for duplicate depths, streaming buffers vs. memory footprint).
- **Test Suite Scaffolding**: Drafting edge-case test matrices and synthetic LAS data fixtures for the 109 automated unit tests.
- **UI & Code Implementation**: Accelerating WPF XAML layout scaffolding and baseline C# logic.
- **Verification & Ownership**: Every line of code, parsing logic, and conditioning rule was reviewed, verified, and refined by the author to ensure correctness against the codebase.
