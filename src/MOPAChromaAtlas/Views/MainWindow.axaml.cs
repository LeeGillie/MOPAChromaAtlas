using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MOPAChroma.Atlas;
using MOPAChroma.Color;
using MOPAChroma.Grids;
using MOPAChroma.Laser;
using MOPAChroma.Planning;
using MOPAChroma.Scanning;

namespace MOPAChromaAtlas.Views;

public partial class MainWindow : Window
{
    private readonly string _atlasPath;
    private AtlasDocument _atlas;
    private TestGrid? _grid;
    private GridCell? _cell;
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();

        var opts = Program.Options;
        string loadNote;
        if (opts.Demo)
        {
            // Never touches the real atlas: a fresh temp file every run.
            _atlasPath = opts.AtlasPath ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MOPAChromaAtlas-demo.json");
            _atlas = DemoAtlas.Create();
            _atlas.Save(_atlasPath);
            loadNote = "DEMO atlas - the results in it are simulated, not engraved or measured.";
            Title += "  [DEMO - simulated results]";
        }
        else
        {
            _atlasPath = opts.AtlasPath ?? DefaultAtlasPath();
            _atlas = LoadOrCreate(_atlasPath, out loadNote);
        }
        AtlasPathText.Text = _atlasPath;

        _loading = true;
        NewMatFamily.ItemsSource = Enum.GetValues<MaterialFamily>().Select(Material.FamilyLabel).ToList();
        NewMatFamily.SelectedIndex = 0;
        NewMatForm.ItemsSource = new[] { "Form: not set", "Sheet", "Card (e.g. business card)", "Coin", "Tag", "Other" };
        NewMatForm.SelectedIndex = 0;
        OutcomeBox.ItemsSource = Enum.GetValues<Outcome>();
        OutcomeBox.SelectedItem = Outcome.Good;
        MethodBox.ItemsSource = Enum.GetValues<MeasureMethod>().Where(m => m != MeasureMethod.None).ToList();
        MethodBox.SelectedItem = MeasureMethod.Instrument;
        RefreshMaterials(_atlas.Materials.FirstOrDefault()?.Id);
        LoadCouponBoxesFromMaterial();
        SourceBox.ItemsSource = _atlas.Sources.Select(s => s.Name).ToList();
        SourceBox.SelectedIndex = 0;
        LensBox.ItemsSource = _atlas.Lenses.Select(l => l.Name).ToList();
        LensBox.SelectedIndex = 0;
        _loading = false;
        OnLaserChanged();

        if (opts.Demo && _atlas.Grids.Count > 0)
        {
            // Open on the simulated seed grid with a coloured cell selected, so the demo
            // shows a finished grid rather than an empty one.
            RefreshGrids(_atlas.Grids[0].Id);
            _cell = _grid?.Cells.FirstOrDefault(c => LatestFor(_grid, c)?.HasColor == true);
            ShowCell(_cell);
            FindColour();
        }

        MaterialBox.SelectionChanged += (_, _) => { if (!_loading) { LoadCouponBoxesFromMaterial(); RefreshGrids(); } };
        SourceBox.SelectionChanged += (_, _) => { if (!_loading) OnLaserChanged(); };
        LensBox.SelectionChanged += (_, _) => { if (!_loading) OnLaserChanged(); };
        SpotBox.ValueChanged += (_, _) => OnSpotChanged();
        CouponWidthBox.ValueChanged += (_, _) => OnGridLayoutBoxChanged();
        CouponHeightBox.ValueChanged += (_, _) => OnGridLayoutBoxChanged();
        CellBox.ValueChanged += (_, _) => OnGridLayoutBoxChanged();
        GapBox.ValueChanged += (_, _) => OnGridLayoutBoxChanged();
        CornerRadiusBox.ValueChanged += (_, _) => OnGridLayoutBoxChanged();
        GridsBox.SelectionChanged += (_, _) => { if (!_loading) ShowGrid(SelectedGridFromBox()); };
        AddMaterialButton.Click += (_, _) => AddMaterial();
        NewAtlasButton.Click += async (_, _) => await StartNewAtlasAsync();
        WireUpdates();
        foreach (var b in RangeBoxes) b.ValueChanged += (_, _) => OnRangesChanged();
        RngPasses.SelectionChanged += (_, _) => OnRangesChanged();
        RangesResetButton.Click += (_, _) => { ShowRanges(PlanningRanges.Default); OnRangesChanged(); };
        SeedButton.Click += (_, _) => Plan(refine: false);
        RefineButton.Click += (_, _) => Plan(refine: true);
        PaletteButton.Click += async (_, _) =>
        {
            if (CurrentMaterial is not { } m) return;
            await new PaletteWindow(m, CurrentSource, CurrentLens, _atlas.Samples).ShowDialog(this);
        };
        ImportScanButton.Click += async (_, _) => await ImportScanAsync();
        SendButton.Click += async (_, _) => await SendToLaserAsync();
        ReadGridButton.Click += async (_, _) => await ReadGridAsync();
        ReadSheetButton.Click += async (_, _) => await ReadSheetAsync();
        MicroGridButton.Click += async (_, _) => await PlanMicroGridAsync();
        SaveResultButton.Click += (_, _) => SaveResult();
        FindButton.Click += (_, _) => FindColour();
        foreach (var box in new[] { TLBox, TABox, TBBox })
            box.ValueChanged += (_, _) => { if (!_settingTarget) FindColour(); };
        TargetPicker.ColorChanged += (_, e) => SetTarget(ColorConvert.ToLab(new Srgb8(e.NewColor.R, e.NewColor.G, e.NewColor.B)), fromPicker: true);
        LBox.ValueChanged += (_, _) => UpdateSwatch();
        ABox.ValueChanged += (_, _) => UpdateSwatch();
        BBox.ValueChanged += (_, _) => UpdateSwatch();
        CanvasHost.SizeChanged += (_, _) => DrawGrid();

        Status(loadNote);
    }

    // ---------------------------------------------------------------- state helpers

    private Material? CurrentMaterial => MaterialBox.SelectedIndex >= 0 && MaterialBox.SelectedIndex < _atlas.Materials.Count
        ? _atlas.Materials[MaterialBox.SelectedIndex] : null;
    private SourceProfile CurrentSource => _atlas.Sources[Math.Max(0, SourceBox.SelectedIndex)];
    private LensProfile CurrentLens => _atlas.Lenses[Math.Max(0, LensBox.SelectedIndex)];

    private IReadOnlyList<Sample> CurrentSamples() =>
        CurrentMaterial is { } m ? _atlas.SamplesFor(m.Id, CurrentSource.Id, CurrentLens.Id) : [];

    /// <summary>The latest sample recorded for a cell of a grid.</summary>
    private Sample? LatestFor(TestGrid g, GridCell c) =>
        _atlas.Samples.LastOrDefault(s => s.GridId == g.Id && s.CellLabel == c.Label);

    private static string DefaultAtlasPath()
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MOPAChromaAtlas");
        Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "atlas.json");
    }

    private static AtlasDocument LoadOrCreate(string path, out string note)
    {
        if (File.Exists(path))
        {
            try
            {
                var doc = AtlasDocument.Load(path);
                note = $"Opened atlas: {doc.Materials.Count} materials, {doc.Grids.Count} grids, {doc.Samples.Count(s => s.Outcome != Outcome.Pending)} measured cells.";
                return doc;
            }
            catch (Exception ex)
            {
                // Never overwrite an atlas we could not read: set it aside and start fresh.
                var aside = path + $".unreadable-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(path, aside);
                note = $"Could not read the atlas ({ex.Message}). It was kept as {System.IO.Path.GetFileName(aside)}; a new one was started.";
            }
        }
        else note = "New atlas created.";

        var fresh = AtlasDocument.CreateDefault();
        fresh.Save(path);
        return fresh;
    }

    private void Save() => _atlas.Save(_atlasPath);

    /// <summary>
    /// Puts the current atlas aside, renamed with the date and time (nothing is deleted),
    /// and opens a fresh one in a new main window. Calibration and capture settings live
    /// in a separate file and are kept.
    /// </summary>
    private async Task StartNewAtlasAsync()
    {
        if (Program.Options.Demo) { Status("This is the demo atlas: nothing to put aside."); return; }
        int grids = _atlas.Grids.Count, measured = _atlas.Samples.Count(x => x.Outcome != Outcome.Pending);
        var aside = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_atlasPath)!,
            $"{System.IO.Path.GetFileNameWithoutExtension(_atlasPath)}-until-{DateTime.Now:yyyy-MM-dd-HHmm}.json");
        bool ok = await ConfirmWindow.AskAsync(this, "Start a new atlas?",
            $"This atlas ({_atlas.Materials.Count} materials, {grids} grids, {measured} measured cells) will be kept as\n" +
            $"{aside}\n\nand an empty atlas started. Nothing is deleted: to go back, rename that file to {System.IO.Path.GetFileName(_atlasPath)}.\n" +
            "Camera calibration and capture settings are kept.", "Start a new atlas");
        if (!ok) return;
        try
        {
            File.Move(_atlasPath, aside);
            var next = new MainWindow();
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d)
                d.MainWindow = next;
            next.Show();
            Close();
        }
        catch (Exception ex) { Status($"Could not put the atlas aside: {ex.Message}"); }
    }

    private void Status(string text) => StatusText.Text = text;

    // ---------------------------------------------------------------- materials and laser

    private void RefreshMaterials(string? selectId)
    {
        bool was = _loading;
        _loading = true;
        MaterialBox.ItemsSource = _atlas.Materials.Select(m => $"{m.DisplayName}  ({Material.FamilyLabel(m.Family)})").ToList();
        MaterialBox.SelectedIndex = Math.Max(0, _atlas.Materials.FindIndex(m => m.Id == selectId));
        _loading = was;
        if (!_loading) RefreshGrids();
    }

    private void AddMaterial()
    {
        var name = NewMatName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) { Status("A material needs a name."); return; }

        var family = Enum.GetValues<MaterialFamily>()[Math.Max(0, NewMatFamily.SelectedIndex)];
        var grade = NewMatGrade.Text?.Trim() ?? "";
        var finish = NewMatFinish.Text?.Trim() ?? "";
        double thickness = (double)(NewMatThickness.Value ?? 0);
        var form = (MaterialForm)Math.Max(0, NewMatForm.SelectedIndex);
        var id = Slug($"{name}-{grade}-{finish}-{(thickness > 0 ? thickness.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "mm" : "")}" +
                      (form == MaterialForm.Unspecified ? "" : "-" + form));
        if (_atlas.Materials.Any(m => m.Id == id)) { Status("That material is already in the atlas."); return; }

        _atlas.Materials.Add(new Material
        {
            Id = id, Name = name, Family = family, Grade = grade, Finish = finish, ThicknessMm = thickness, Form = form,
            Supplier = NewMatSupplier.Text?.Trim() ?? "", Sku = NewMatSku.Text?.Trim() ?? "",
        });
        Save();
        NewMatName.Text = NewMatGrade.Text = NewMatFinish.Text = NewMatSupplier.Text = NewMatSku.Text = "";
        NewMatThickness.Value = null;
        RefreshMaterials(id);
        Status($"Added {name}.");
    }

    private static string Slug(string s) =>
        string.Join("-", new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private void OnLaserChanged()
    {
        _loading = true;
        SpotBox.Value = (decimal)CurrentLens.SpotDiameterUm;
        _loading = false;
        var src = CurrentSource;
        SourceNote.Text = $"{src.RatedPowerW:0} W · {src.FrequencyMinKHz:0}–{src.FrequencyMaxKHz:0} kHz · " +
                          $"{src.PulseWidthsNs.Count} pulse widths · evidence: {src.Evidence} · spot evidence: {CurrentLens.Evidence}" +
                          (string.IsNullOrEmpty(src.Notes) ? "" : $"\n{src.Notes}");
        RefreshGrids();
    }

    private void OnSpotChanged()
    {
        if (_loading || SpotBox.Value is not { } v) return;
        int i = Math.Max(0, LensBox.SelectedIndex);
        if (Math.Abs(_atlas.Lenses[i].SpotDiameterUm - (double)v) < 1e-9) return;
        // Typing a spot size is a measurement claim; the preset's Nominal tag no longer applies.
        _atlas.Lenses[i] = _atlas.Lenses[i] with { SpotDiameterUm = (double)v, Evidence = Evidence.Measured };
        Save();
        OnLaserChanged();
    }

    /// <summary>Fills the coupon size, cell size and gap boxes from the selected material,
    /// without persisting (a mere selection change should not overwrite a material's
    /// stored values).</summary>
    private void LoadCouponBoxesFromMaterial()
    {
        bool was = _loading;
        _loading = true;
        var m = CurrentMaterial;
        CouponWidthBox.Value = (decimal)(m?.CouponWidthMm ?? 86);
        CouponHeightBox.Value = (decimal)(m?.CouponHeightMm ?? 54);
        CellBox.Value = (decimal)(m?.CellSizeMm ?? 8);
        GapBox.Value = (decimal)(m?.GapMm ?? 2);
        CornerRadiusBox.Value = (decimal)(m?.CornerRadiusMm ?? 3);
        ShowRanges(m?.Ranges ?? PlanningRanges.Default);
        _loading = was;
        UpdateGridFit();
    }

    private NumericUpDown[] RangeBoxes => [RngPowerMin, RngPowerMax, RngFreqMin, RngFreqMax, RngPwMin, RngPwMax,
                                           RngSpeedMin, RngSpeedMax, RngDensMin, RngDensMax, RngHeatMin];

    private void ShowRanges(PlanningRanges r)
    {
        bool was = _loading;
        _loading = true;
        RngPowerMin.Value = (decimal)r.PowerMinPercent; RngPowerMax.Value = (decimal)r.PowerMaxPercent;
        RngFreqMin.Value = (decimal)r.FrequencyMinKHz; RngFreqMax.Value = (decimal)r.FrequencyMaxKHz;
        RngPwMin.Value = (decimal)r.PulseWidthMinNs; RngPwMax.Value = (decimal)r.PulseWidthMaxNs;
        RngSpeedMin.Value = (decimal)r.SpeedMinMmPerS; RngSpeedMax.Value = (decimal)r.SpeedMaxMmPerS;
        RngDensMin.Value = (decimal)r.LineDensityMinPerCm; RngDensMax.Value = (decimal)r.LineDensityMaxPerCm;
        RngPasses.ItemsSource ??= Enumerable.Range(1, PlanningRanges.MostPasses).Select(n => n.ToString()).ToList();
        RngPasses.SelectedIndex = Math.Clamp(r.MaxPasses, 1, PlanningRanges.MostPasses) - 1;
        RngHeatMin.Value = (decimal)r.HeatMinJPerMm2;
        _loading = was;
    }

    private PlanningRanges RangesFromBoxes()
    {
        double V(NumericUpDown b, double d) => b.Value is { } v ? (double)v : d;
        var d = PlanningRanges.Default;
        return new PlanningRanges
        {
            PowerMinPercent = V(RngPowerMin, d.PowerMinPercent), PowerMaxPercent = V(RngPowerMax, d.PowerMaxPercent),
            FrequencyMinKHz = V(RngFreqMin, d.FrequencyMinKHz), FrequencyMaxKHz = V(RngFreqMax, d.FrequencyMaxKHz),
            PulseWidthMinNs = V(RngPwMin, d.PulseWidthMinNs), PulseWidthMaxNs = V(RngPwMax, d.PulseWidthMaxNs),
            SpeedMinMmPerS = V(RngSpeedMin, d.SpeedMinMmPerS), SpeedMaxMmPerS = V(RngSpeedMax, d.SpeedMaxMmPerS),
            LineDensityMinPerCm = V(RngDensMin, d.LineDensityMinPerCm), LineDensityMaxPerCm = V(RngDensMax, d.LineDensityMaxPerCm),
            MaxPasses = RngPasses.SelectedIndex is >= 0 and var k ? k + 1 : d.MaxPasses,
            HeatMinJPerMm2 = V(RngHeatMin, d.HeatMinJPerMm2),
        };
    }

    private void OnRangesChanged()
    {
        if (_loading) return;
        int i = MaterialBox.SelectedIndex;
        if (i < 0 || i >= _atlas.Materials.Count) return;
        _atlas.Materials[i] = _atlas.Materials[i] with { Ranges = RangesFromBoxes() };
        Save();
    }

    private ParameterSpace CurrentSpace() => ParameterSpace.For(CurrentSource, CurrentMaterial?.Ranges);

    private void OnGridLayoutBoxChanged()
    {
        if (_loading) return;
        UpdateGridFit();
        int i = MaterialBox.SelectedIndex;
        if (i < 0 || i >= _atlas.Materials.Count) return;
        double w = (double)(CouponWidthBox.Value ?? 86);
        double h = (double)(CouponHeightBox.Value ?? 54);
        double cell = (double)(CellBox.Value ?? 8);
        double gap = (double)(GapBox.Value ?? 2);
        double radius = (double)(CornerRadiusBox.Value ?? 3);
        var mat = _atlas.Materials[i];
        if (mat.CouponWidthMm == w && mat.CouponHeightMm == h && mat.CellSizeMm == cell
            && mat.GapMm == gap && mat.CornerRadiusMm == radius) return;
        // Typing any of these is worth remembering per material - different stock pieces
        // and cell layouts are different from one material to the next.
        _atlas.Materials[i] = mat with { CouponWidthMm = w, CouponHeightMm = h, CellSizeMm = cell, GapMm = gap, CornerRadiusMm = radius };
        Save();
    }

    /// <summary>Columns and rows are never typed directly: they are the most that fit the
    /// coupon size, cell size and gap, including the label band each row reserves.</summary>
    private GridLayout CurrentLayout()
    {
        double couponW = (double)(CouponWidthBox.Value ?? 86);
        double couponH = (double)(CouponHeightBox.Value ?? 54);
        double cell = (double)(CellBox.Value ?? 8);
        double gap = (double)(GapBox.Value ?? 2);
        var (cols, rows) = GridLayout.FitToCoupon(couponW, couponH, cell, gap);
        return new GridLayout { Columns = cols, Rows = rows, CellSizeMm = cell, GapMm = gap };
    }

    private void UpdateGridFit()
    {
        var l = CurrentLayout();
        double margin = 2 * (CornerMarks.OffsetMm(l) + CornerMarks.SizeMm(l) / 2) - l.CellSizeMm;
        GridFitText.Text = $"Fits {l.Columns} × {l.Rows} cells ({l.Capacity} total) · cells {l.WidthMm:0.#} × {l.HeightMm:0.#} mm, " +
                           $"{l.WidthMm:0.#} × {l.HeightMm + margin:0.#} mm with corner crosses.";
    }

    // ---------------------------------------------------------------- grids

    private List<TestGrid> GridsForCurrent() =>
        CurrentMaterial is { } m
            ? _atlas.Grids.Where(g => g.MaterialId == m.Id && g.SourceId == CurrentSource.Id && g.LensId == CurrentLens.Id).ToList()
            : [];

    private void RefreshGrids(Guid? select = null)
    {
        FillPaletteStrip();
        var grids = GridsForCurrent();
        _loading = true;
        GridsBox.ItemsSource = grids.Select(g => $"{g.Title}  ·  {g.CreatedUtc.LocalDateTime:yyyy-MM-dd HH:mm}").ToList();
        int idx = select is { } id ? grids.FindIndex(g => g.Id == id) : grids.Count - 1;
        GridsBox.SelectedIndex = idx;
        _loading = false;

        var samples = CurrentSamples();
        int measured = samples.Count(s => s.Outcome != Outcome.Pending);
        int colours = samples.Count(s => s.HasColor);
        ProgressText.Text = grids.Count == 0
            ? "No grids yet. Start with a seed grid."
            : $"{grids.Count} grid(s), {measured} of {samples.Count} cells measured, {colours} with colour.";
        RefineButton.IsEnabled = measured > 0;

        var planned = PlannedForCurrent();
        PlannedText.Text = planned.Count == 0 ? ""
            : $"Waiting to be engraved and scanned: {string.Join(", ", planned.Select(p => p.CardId))}.";

        ShowGrid(idx >= 0 ? grids[idx] : null);
    }

    private List<PlannedCard> PlannedForCurrent() =>
        CurrentMaterial is { } m
            ? _atlas.PlannedCards.Where(p => p.CompletedGridId is null && p.MaterialId == m.Id
                                             && p.SourceId == CurrentSource.Id && p.LensId == CurrentLens.Id).ToList()
            : [];

    /// <summary>Next free card ID for the current material, counting cards and planned cards.</summary>
    private string NextCardId() =>
        CardInfo.NextId(CurrentMaterial?.Id ?? "card",
            _atlas.Grids.Select(g => g.Card?.CardId ?? "").Concat(_atlas.PlannedCards.Select(p => p.CardId)));

    private TestGrid? SelectedGridFromBox()
    {
        var grids = GridsForCurrent();
        return GridsBox.SelectedIndex >= 0 && GridsBox.SelectedIndex < grids.Count ? grids[GridsBox.SelectedIndex] : null;
    }

    private void Plan(bool refine)
    {
        if (CurrentMaterial is not { } mat) { Status("Choose or add a material first."); return; }

        var layout = CurrentLayout();

        var planner = new GridPlanner(CurrentSpace(), CurrentSource, CurrentLens);
        var samples = CurrentSamples();
        var picks = refine
            ? planner.Refine(layout.Capacity, samples)
            : planner.Seed(layout.Capacity, samples.Select(s => s.Settings), HeatLimits.Ceiling(samples, CurrentSource, CurrentLens));

        if (picks.Count == 0) { Status("The planner found nothing new to test within the current limits."); return; }

        int n = GridsForCurrent().Count + 1;
        var purpose = refine ? $"Refine #{n - 1}" : (n == 1 ? "Seed" : $"Seed top-up #{n}");
        var grid = TestGrid.Arrange($"{mat.Name} {mat.Grade} — grid {_atlas.NextGridNumber()}".Replace("  ", " "), purpose, layout,
            picks.Select(p => (p.Settings, p.Reason)), CurrentSource, CurrentLens, mat.Id);

        grid = _atlas.AddGrid(grid);
        foreach (var c in grid.Cells)
            _atlas.Samples.Add(new Sample
            {
                MaterialId = mat.Id, SourceId = CurrentSource.Id, LensId = CurrentLens.Id,
                GridId = grid.Id, CellLabel = c.Label, Settings = c.Settings,
            });
        Save();
        RefreshGrids(grid.Id);
        Status($"Planned {grid.Title}: {grid.Cells.Count} cells ({purpose}).");
    }

    private void ShowGrid(TestGrid? g)
    {
        _grid = g;
        _cell = null;
        GridTitle.Text = g?.Title ?? "No grid";
        GridSubtitle.Text = g is null ? "" :
            $"{g.Purpose} · {g.Layout.Columns}×{g.Layout.Rows} cells of {g.Layout.CellSizeMm:0.#} mm, gap {g.Layout.GapMm:0.#} mm · coupon {g.Layout.WidthMm:0.#} × {g.Layout.HeightMm:0.#} mm";
        // Sending and reading back are for Atlas's own grids; a MakeIt card came from MakeIt.
        SendButton.IsEnabled = g is { Definition: null } && g.Cells.Count > 0;
        ReadGridButton.IsEnabled = g is { Definition: null } && g.Cells.Count > 0;
        GridHint.Text = g?.Definition is { } d
            ? $"Laid out as on the card: {d.Describe()}. " +
              (g.Card is { } card ? $"Lot {Or(card.Lot)}, cleaning {Or(card.Cleaning)}, MakeIt {Or(card.MakeItVersion)}. " : "") +
              "Hover a cell for its settings; click it for details."
            : "Cells run from least to most energy per area. Grey: not yet measured. Hover a cell for its settings; click it to record a result.";
        ShowCell(null);
        DrawGrid();
    }

    private static string Or(string s) => string.IsNullOrWhiteSpace(s) ? "not recorded" : s;

    private void DrawGrid()
    {
        GridCanvas.Children.Clear();
        if (_grid is not { } g) return;

        double w = CanvasHost.Bounds.Width, h = CanvasHost.Bounds.Height;
        if (w < 10 || h < 10) return;

        var l = g.Layout;
        double scale = Math.Min(w / l.WidthMm, h / l.HeightMm);
        double cellPx = l.CellSizeMm * scale, gapPx = l.GapMm * scale;
        double ox = (w - l.WidthMm * scale) / 2, oy = (h - l.HeightMm * scale) / 2;

        int count = g.Cells.Count;
        for (int i = 0; i < count; i++)
        {
            var c = g.Cells[i];
            var sample = LatestFor(g, c);
            var p = Physics.From(c.Settings, CurrentSource, CurrentLens);

            IBrush fill;
            string mark = "";
            if (sample is { HasColor: true, Color: { } lab })
            {
                var rgb = ColorConvert.ToSrgb(lab);
                fill = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
            }
            else
            {
                // Unmeasured cells: grey ramp by energy order, faint to strong.
                byte v = (byte)(70 + 90 * (count > 1 ? i / (double)(count - 1) : 0));
                fill = new SolidColorBrush(Color.FromRgb(v, v, (byte)Math.Min(255, v + 6)));
                mark = sample?.Outcome switch
                {
                    Outcome.NoMark => "no mark",
                    Outcome.Damaged => "damaged",
                    _ => "",
                };
            }

            bool selected = ReferenceEquals(c, _cell);
            var box = new Border
            {
                Width = cellPx, Height = cellPx,
                Background = fill,
                CornerRadius = new CornerRadius(Math.Min(4, cellPx / 10)),
                BorderBrush = selected ? Brushes.White : sample?.Outcome == Outcome.Damaged ? Brushes.OrangeRed : new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(selected ? 3 : 1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = c.Label, FontWeight = FontWeight.SemiBold, FontSize = Math.Clamp(cellPx / 5, 9, 16),
                                        HorizontalAlignment = HorizontalAlignment.Center, Foreground = LabelBrush(fill) },
                        new TextBlock { Text = mark, FontSize = Math.Clamp(cellPx / 9, 8, 11),
                                        HorizontalAlignment = HorizontalAlignment.Center, Foreground = LabelBrush(fill) },
                    },
                },
            };
            ToolTip.SetTip(box, $"{c.Label}: {c.Settings.ToShortString()}\n{PhysicsLine(p)}\n{c.Reason}");
            box.PointerPressed += (_, _) => { _cell = c; ShowCell(c); DrawGrid(); };

            Canvas.SetLeft(box, ox + c.Column * (cellPx + gapPx));
            Canvas.SetTop(box, oy + c.Row * (cellPx + gapPx));
            GridCanvas.Children.Add(box);
        }
    }

    private static IBrush LabelBrush(IBrush fill)
    {
        if (fill is ISolidColorBrush b)
        {
            var c = b.Color;
            double lum = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
            return lum > 140 ? Brushes.Black : Brushes.White;
        }
        return Brushes.White;
    }

    private static string PhysicsLine(Physics p) =>
        $"E/area {p.ArealEnergyJPerMm2:0.###} J/mm² · pulse {p.PulseEnergyMJ:0.####} mJ · peak {p.PeakPowerKW:0.##} kW · overlap {p.PulseOverlap:P0} / {p.LineOverlap:P0}";

    // ---------------------------------------------------------------- selected cell

    private void ShowCell(GridCell? c)
    {
        bool on = c is not null && _grid is not null;
        if (!on) ShowResult(null);
        SaveResultButton.IsEnabled = on;
        OutcomeBox.IsEnabled = LBox.IsEnabled = ABox.IsEnabled = BBox.IsEnabled = MethodBox.IsEnabled = ViewingBox.IsEnabled = on;
        MicroGridButton.IsEnabled = false;
        if (!on)
        {
            CellTitle.Text = "—";
            CellSettings.Text = "Click a cell in the grid.";
            CellReason.Text = "";
            RepeatText.Text = "";
            return;
        }

        var s = c!.Settings;
        var p = Physics.From(s, CurrentSource, CurrentLens);
        CellTitle.Text = $"Cell {c.Label}";
        CellSettings.Text =
            $"Power        {s.PowerPercent,8:0.#} %\n" +
            $"Speed        {s.SpeedMmPerS,8:0.#} mm/s\n" +
            $"Line density {s.LineDensityPerCm,8:0.#} /cm\n" +
            $"Frequency    {s.FrequencyKHz,8:0.#} kHz\n" +
            $"Pulse width  {s.PulseWidthNs,8:0.#} ns\n" +
            $"Passes       {s.Passes,8}\n" +
            $"Focus / Z    {s.FocusOffsetMm,8:+0.##;-0.##;0} mm\n\n" +
            PhysicsLine(p).Replace(" · ", "\n");
        CellReason.Text = string.IsNullOrEmpty(c.Reason) ? "" : $"Why: {c.Reason}";

        var latest = LatestFor(_grid!, c);
        MicroGridButton.IsEnabled = latest?.HasColor == true;
        ShowResult(latest);
        if (latest?.HasColor == true)
        {
            var rc = Repeatability.Check(latest, CurrentSamples());
            RepeatText.Text = rc.Status switch
            {
                RepeatStatus.Verified => $"✔ Verified: repeated on another card within ΔE00 {rc.BestDeltaE00:0.0}.",
                RepeatStatus.RepeatDiffers => $"✖ Repeat differs: closest repeat is ΔE00 {rc.BestDeltaE00:0.0} away. Not a dependable colour yet.",
                _ => "Not yet repeated. A micro-grid around it repeats it as its first cell.",
            };
            RepeatText.Foreground = rc.Status switch
            {
                RepeatStatus.Verified => Brushes.LightGreen,
                RepeatStatus.RepeatDiffers => Brushes.OrangeRed,
                _ => Brushes.Gray,
            };
        }
        else RepeatText.Text = "";
        OutcomeBox.SelectedItem = latest is { Outcome: not Outcome.Pending } ? latest.Outcome : Outcome.Good;
        if (latest?.Color is { } lab)
        {
            LBox.Value = (decimal)lab.L; ABox.Value = (decimal)lab.A; BBox.Value = (decimal)lab.B;
        }
        else LBox.Value = ABox.Value = BBox.Value = null;
        if (latest is { Method: not MeasureMethod.None }) MethodBox.SelectedItem = latest.Method;
        ViewingBox.Text = latest?.ViewingCondition ?? ViewingBox.Text;
        UpdateSwatch();
    }

    private Lab? EnteredLab() =>
        LBox.Value is { } l && ABox.Value is { } a && BBox.Value is { } b ? new Lab((double)l, (double)a, (double)b) : null;

    private void UpdateSwatch()
    {
        if (EnteredLab() is { } lab)
        {
            var rgb = ColorConvert.ToSrgb(lab, out bool inGamut);
            Swatch.Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
            Swatch.BorderBrush = inGamut ? new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)) : Brushes.Red;
        }
        else
        {
            Swatch.Background = Brushes.Transparent;
            Swatch.BorderBrush = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
        }
    }

    private void SaveResult()
    {
        if (_grid is not { } g || _cell is not { } c || CurrentMaterial is not { } mat) return;

        var outcome = OutcomeBox.SelectedItem is Outcome o ? o : Outcome.Good;
        var lab = EnteredLab();
        if (outcome is Outcome.Good or Outcome.Uneven && lab is null)
        {
            Status("A Good or Uneven result needs L*, a* and b*.");
            return;
        }

        var sample = new Sample
        {
            MaterialId = mat.Id, SourceId = g.SourceId, LensId = g.LensId,
            GridId = g.Id, CellLabel = c.Label, Settings = c.Settings,
            Outcome = outcome,
            Color = outcome is Outcome.Good or Outcome.Uneven ? lab : null,
            Method = outcome is Outcome.Good or Outcome.Uneven ? (MethodBox.SelectedItem as MeasureMethod? ?? MeasureMethod.Estimated) : MeasureMethod.None,
            ViewingCondition = ViewingBox.Text?.Trim() ?? "",
            MeasuredUtc = DateTimeOffset.UtcNow,
        };

        // A pending placeholder is replaced; a measured sample is never edited - a
        // re-measure is appended, and the newest one is what the grid shows.
        var existing = _atlas.Samples.FindIndex(s => s.GridId == g.Id && s.CellLabel == c.Label && s.Outcome == Outcome.Pending);
        if (existing >= 0) _atlas.Samples[existing] = sample;
        else _atlas.Samples.Add(sample);
        Save();

        // Step to the next unmeasured cell, so a whole coupon can be entered quickly.
        var next = g.Cells.FirstOrDefault(x => LatestFor(g, x)?.Outcome is null or Outcome.Pending);
        var keep = g.Id;
        RefreshGrids(keep);
        if (next is not null)
        {
            _cell = _grid!.Cells.First(x => x.Label == next.Label);
            ShowCell(_cell);
            DrawGrid();
        }
        Status($"Saved {c.Label}: {outcome}{(sample.Color is { } col ? $" {col}" : "")}.");
    }

    // ---------------------------------------------------------------- colour search

    /// <summary>The cell's latest result in words and a swatch (readings come from reading the card's picture).</summary>
    private void ShowResult(Sample? latest)
    {
        if (latest is null || latest.Outcome == Outcome.Pending)
        {
            ResultText.Text = latest is null ? "" : "Not read yet: send the grid to the laser, or read it from a picture of the card.";
            ResultSwatch.Background = Brushes.Transparent;
            return;
        }
        string how = latest.Method switch
        {
            MeasureMethod.Estimated => "photo",
            MeasureMethod.Scanner => "scan",
            MeasureMethod.CalibratedImage => "calibrated image",
            MeasureMethod.Instrument => "instrument",
            _ => "by eye",
        };
        if (latest.Color is { } lab)
        {
            var rgb = ColorConvert.ToSrgb(lab);
            ResultSwatch.Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
            ResultText.Text = $"{latest.Outcome}: {ColorNames.Name(lab).ToLowerInvariant()} ({how}{(latest.MeasuredUtc is { } t ? $", {t.ToLocalTime():d MMM}" : "")})";
        }
        else
        {
            ResultSwatch.Background = Brushes.Transparent;
            ResultText.Text = $"{latest.Outcome} ({how})";
        }
    }

    private bool _settingTarget;

    /// <summary>Sets the colour-finder target (from the picker, a palette swatch, or exact L*a*b*) and searches.</summary>
    private void SetTarget(Lab target, bool fromPicker = false)
    {
        if (_settingTarget) return;
        _settingTarget = true;
        TLBox.Value = (decimal)Math.Round(target.L, 1);
        TABox.Value = (decimal)Math.Round(target.A, 1);
        TBBox.Value = (decimal)Math.Round(target.B, 1);
        if (!fromPicker)
        {
            var rgb = ColorConvert.ToSrgb(target);
            TargetPicker.Color = Color.FromRgb(rgb.R, rgb.G, rgb.B);
        }
        _settingTarget = false;
        FindColour();
    }

    /// <summary>Small swatches of the colours made on this material: click one to find its settings and its neighbours.</summary>
    private void FillPaletteStrip()
    {
        PaletteStrip.Children.Clear();
        if (CurrentMaterial is not { } m) return;
        foreach (var e in Palette.Build(_atlas.Samples, m.Id, CurrentSource.Id, CurrentLens.Id).Take(40))
        {
            var rgb = ColorConvert.ToSrgb(e.Color);
            var b = new Border
            {
                Width = 20, Height = 20, Margin = new Thickness(0, 0, 3, 3), CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B)),
                BorderBrush = e.Verified ? Brushes.White : null, BorderThickness = new Thickness(e.Verified ? 1.5 : 0),
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            };
            ToolTip.SetTip(b, $"{(e.Verified ? "✓ " : "")}{e.Name}\n{e.Settings.ToShortString()}");
            var target = e.Color;
            b.PointerPressed += (_, _) => SetTarget(target);
            PaletteStrip.Children.Add(b);
        }
    }

    private void FindColour()
    {
        var target = new Lab((double)(TLBox.Value ?? 50), (double)(TABox.Value ?? 0), (double)(TBBox.Value ?? 0));
        var matches = ColorFinder.Find(CurrentSamples(), target, 8);
        var items = new List<Control>();
        if (matches.Count == 0)
            items.Add(new TextBlock { Classes = { "dim" }, Text = "No measured colours for this material, source and lens yet." });

        foreach (var m in matches)
        {
            var rgb = ColorConvert.ToSrgb(m.Sample.Color!.Value);
            var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            var sw = new Border
            {
                Width = 28, Height = 28, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B)),
            };
            DockPanel.SetDock(sw, Dock.Left);
            row.Children.Add(sw);
            row.Children.Add(new TextBlock
            {
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Text = $"ΔE00 {m.DeltaE00:0.0} · cell {m.Sample.CellLabel} · {m.Sample.Outcome} · {Repeatability.Label(m.Repeat.Status)}\n{m.Sample.Settings.ToShortString()}",
            });
            ToolTip.SetTip(row, $"{m.Sample.Color} measured by {m.Sample.Method}");
            items.Add(row);
        }
        FindResults.ItemsSource = items;
        ColorConvert.ToSrgb(target, out bool inGamut);
        Status($"Target {target}{(inGamut ? "" : " (outside sRGB)")}: {matches.Count} match(es).");
    }

    // ---------------------------------------------------------------- send and read back

    private ScanImportContext? ReadContext()
    {
        if (CurrentMaterial is not { } mat) { Status("Choose or add a material first."); return null; }
        var previous = GridsForCurrent().Where(g => g.Definition is not null).MaxBy(g => g.CreatedUtc);
        return new ScanImportContext(mat, CurrentSource, CurrentLens, PlannedForCurrent(), previous, NextCardId());
    }

    private async Task SendToLaserAsync()
    {
        if (_grid is not { Definition: null } g || ReadContext() is not { } ctx) return;
        // A grid planned before grids were shaped to their cells still has the full coupon's
        // layout. If nothing of it has been read yet, it has not been engraved: trim it now,
        // so the job, its crosses and the preview are the size of the cells.
        if (g.Trimmed() is var trimmed && !ReferenceEquals(trimmed, g)
            && _atlas.Samples.Where(x => x.GridId == g.Id).All(x => x.Outcome == Outcome.Pending))
        {
            int gi = _atlas.Grids.FindIndex(x => x.Id == g.Id);
            if (gi >= 0)
            {
                _atlas.Grids[gi] = trimmed;
                Save();
                RefreshGrids(trimmed.Id);
                g = trimmed;
                Status($"{g.Title} was shrunk to its {g.Layout.Columns} × {g.Layout.Rows} cells before sending.");
            }
        }
        // Hard heat limit (HeatLimits): a grid planned before the limit, or with results since
        // that lowered it, is not sent with any cell above it.
        double ceiling = HeatLimits.Ceiling(CurrentSamples(), CurrentSource, CurrentLens);
        var tooHot = HeatLimits.TooHot(g, ceiling, CurrentSource, CurrentLens);
        if (tooHot.Count > 0)
        {
            string list = string.Join(", ", tooHot.Take(12).Select(t => $"{t.Label} ({t.JPerMm2:0.#})")) + (tooHot.Count > 12 ? $" and {tooHot.Count - 12} more" : "");
            bool replan = await ConfirmWindow.AskAsync(this, "Too hot to send",
                $"{tooHot.Count} cell(s) of {g.Title} would put more heat into the metal than Atlas allows " +
                $"({ceiling:0.#} J/mm² for this material so far), enough to cut into or through it:\n\n{list}\n\n" +
                "This grid will not be sent. Plan a new grid instead: it stays within the limit.",
                "Plan a new grid");
            if (replan) Plan(refine: true);
            return;
        }
        var result = await new SendToLaserWindow(g, ctx).ShowDialog<ScanImportResult?>(this);
        if (result is not null) RecordReading(result);
    }

    private async Task ReadGridAsync()
    {
        if (_grid is not { Definition: null } g || ReadContext() is not { } ctx) return;
        var result = await new ScanImportWindow(ctx with { Existing = g }).ShowDialog<ScanImportResult?>(this);
        if (result is not null) RecordReading(result);
    }

    /// <summary>Every numbered grid on one scan of a sheet, whatever material or grid is selected.</summary>
    private async Task ReadSheetAsync()
    {
        if (!_atlas.Grids.Any(g => g.Number > 0 && g.Definition is null))
        {
            Status("This atlas has no Atlas grids yet: plan one and send it to the laser first.");
            return;
        }
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open a scan of the whole sheet",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Scans and photos") { Patterns = ["*.png", "*.tif", "*.tiff", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
        RgbImage img;
        try
        {
            Status("Opening the scan…");
            img = await Task.Run(() => RgbImage.Load(path));
        }
        catch (Exception ex)
        {
            Status("Could not open that picture: " + ex.Message);
            return;
        }
        var readings = await new SheetReadWindow(_atlas, img, System.IO.Path.GetFileName(path)).ShowDialog<List<Sample>?>(this);
        if (readings is null || readings.Count == 0) { Status("Sheet not recorded."); return; }
        int filled = _atlas.RecordResults(readings);
        Save();
        RefreshGrids(_grid?.Id);
        int grids = readings.Select(r => r.GridId).Distinct().Count();
        Status($"Read {grids} grid(s) from the sheet: {readings.Count} cells, {readings.Count(r => r.HasColor)} with colour" +
               (readings.Count - filled > 0 ? $"; {readings.Count - filled} already had a result and gained a re-measure." : "."));
    }

    /// <summary>Results read back for one of this atlas's grids: fill its pending cells, never overwrite a measurement.</summary>
    private void RecordReading(ScanImportResult result)
    {
        int filled = _atlas.RecordResults(result.Samples);
        Save();
        RefreshGrids(result.Grid.Id);
        int remeasured = result.Samples.Count - filled;
        Status($"Read {result.Grid.Title}: {result.Samples.Count} cells, {result.Samples.Count(s => s.HasColor)} with colour" +
               (remeasured > 0 ? $"; {remeasured} already had a result and gained a re-measure." : "."));
    }

    // ---------------------------------------------------------------- scan import

    private async Task ImportScanAsync()
    {
        // A MakeIt card describes itself; an Atlas grid is read back with "Read this grid".
        if (CurrentMaterial is not { } mat) { Status("Choose or add a material first."); return; }
        var previous = GridsForCurrent().Where(g => g.Definition is not null).MaxBy(g => g.CreatedUtc);
        var ctx = new ScanImportContext(mat, CurrentSource, CurrentLens, PlannedForCurrent(), previous, NextCardId());
        var dlg = new ScanImportWindow(ctx);
        var result = await dlg.ShowDialog<ScanImportResult?>(this);
        if (result is null) return;

        _atlas.AddGrid(result.Grid);
        _atlas.Samples.AddRange(result.Samples);
        if (result.PlannedCardId is { } pid)
        {
            int i = _atlas.PlannedCards.FindIndex(p => p.Id == pid);
            if (i >= 0) _atlas.PlannedCards[i] = _atlas.PlannedCards[i] with { CompletedGridId = result.Grid.Id };
        }
        Save();
        RefreshGrids(result.Grid.Id);
        Status($"Added {result.Grid.Title}: {result.Samples.Count} squares, " +
               $"{result.Samples.Count(s => s.HasColor)} with colour.");
    }

    /// <summary>
    /// A micro-grid around the selected cell, as an Atlas grid ready for Send to laser:
    /// power and speed swept around the cell's settings, everything else the same, the cell
    /// itself repeated once (repeatability check), nothing else that was engraved before.
    /// </summary>
    private Task PlanMicroGridAsync()
    {
        if (_grid is not { } g || _cell is not { } c || LatestFor(g, c) is not { HasColor: true } seed) return Task.CompletedTask;
        if (CurrentMaterial is not { } mat) return Task.CompletedTask;
        var layout = CurrentLayout();
        double ceiling = HeatLimits.Ceiling(CurrentSamples(), CurrentSource, CurrentLens);
        string still = $"No micro-grid was planned, so {g.Title} is still the selected grid (Send to laser would send it again).";
        if (HeatLimits.ArealEnergy(seed.Settings, CurrentSource, CurrentLens) > ceiling)
            return NoMicroGridAsync($"{c.Label}'s settings are above the safe heat limit ({ceiling:0.#} J/mm²), so nothing is planned around it.\n\n{still}");
        // The material's planning ranges, stretched to hold the chosen cell: it made colour, so
        // the sweep may start from it even when it lies outside them (or the ranges were changed
        // after its grid was planned). The heat limit is never stretched.
        var ranges = (mat.Ranges ?? PlanningRanges.Default).Including(seed.Settings);
        int tooHot = 0, outside = 0;
        var picks = MicroGrid.AtlasCells(seed.Settings, CurrentSource, CurrentSamples().Select(x => x.Settings), layout.Capacity,
            allowed: s =>
            {
                if (HeatLimits.ArealEnergy(s, CurrentSource, CurrentLens) > ceiling) { tooHot++; return false; }
                if (!ranges.Contains(s)) { outside++; return false; }
                return true;
            });
        if (picks.Count <= 1)
        {
            var why = new List<string>();
            if (tooHot > 0) why.Add($"{tooHot} would be over the safe heat limit ({ceiling:0.#} J/mm²)");
            if (outside > 0) why.Add($"{outside} would be outside {mat.Name}'s planning ranges");
            string reason = why.Count > 0
                ? $"Of the settings around {c.Label}, {string.Join(" and ", why)}, and the rest have been engraved already."
                : $"Every setting around {c.Label} has been engraved already.";
            return NoMicroGridAsync($"{reason}\n\n{still}");
        }
        var grid = TestGrid.Arrange($"{mat.Name} {mat.Grade} — grid {_atlas.NextGridNumber()}".Replace("  ", " "), $"Micro-grid around {g.Title} {c.Label}",
            layout, picks, CurrentSource, CurrentLens, mat.Id);
        grid = _atlas.AddGrid(grid);
        foreach (var cell in grid.Cells)
            _atlas.Samples.Add(new Sample
            {
                MaterialId = mat.Id, SourceId = CurrentSource.Id, LensId = CurrentLens.Id,
                GridId = grid.Id, CellLabel = cell.Label, Settings = cell.Settings,
            });
        Save();
        RefreshGrids(grid.Id);
        Status($"Planned {grid.Title}: {grid.Cells.Count} cells around {c.Label} (one repeats it). Send it to the laser like any grid.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// A micro-grid that could not be planned is said in a window, not only the status line:
    /// otherwise the grid the cell came from stays selected and Send to laser sends that again.
    /// </summary>
    private Task NoMicroGridAsync(string text)
    {
        Status("No micro-grid planned.");
        return ConfirmWindow.TellAsync(this, "No micro-grid planned", text);
    }
}
