using LumosUltra.Jobs;
namespace MOPAChroma.Grids;

/// <summary>The settings a MakeIt test card can sweep across its rows or columns.</summary>
public enum CardAxis { Power, Speed, Frequency }

/// <summary>
/// One swept axis of a card. <paramref name="Descending"/> means the first row (top) or
/// first column (left) holds <paramref name="Max"/>; otherwise it holds <paramref name="Min"/>.
/// </summary>
public sealed record AxisSweep(CardAxis Axis, double Min, double Max, int Count, bool Descending)
{
    public double At(int i)
    {
        if (Count <= 1) return Descending ? Max : Min;
        double t = i / (Count - 1.0);
        return Descending ? Max - t * (Max - Min) : Min + t * (Max - Min);
    }

    public double First => At(0);
    public double Last => At(Count - 1);
}

/// <summary>
/// A test card made with one of MakeIt's built-in tests, which sweep two settings across
/// the card and hold the rest fixed:
/// <list type="bullet">
/// <item>the Lumos Ultra MOPA colour test: frequency down the card (top = highest), power
/// across it (left = lowest), engraved labels included;</item>
/// <item>the Material Testing Array: power and speed.</item>
/// </list>
/// Any two of power, speed and frequency may be the axes; the third is fixed.
///
/// Standard scanning orientation: labels readable. Row 0 is the top row and column 0 the
/// left column, whatever values they hold.
///
/// Values between the ends are the exact linear steps. Confirmed from a real colour-test
/// G-code (2026-09-27): MakeIt sends frequency (M38F) essentially at full precision,
/// rounded only to the nearest whole kHz; power (S) is rounded coarser, to the nearest
/// whole percent. <see cref="SettingsAt"/> rounds to match. Speed's rounding step is not
/// yet confirmed (no Material Testing Array G-code checked).
/// </summary>
public sealed record ColorTestDefinition
{
    public AxisSweep RowSweep { get; init; } = new(CardAxis.Frequency, 150, 350, 10, Descending: true);
    public AxisSweep ColumnSweep { get; init; } = new(CardAxis.Power, 40, 90, 20, Descending: false);

    // Fixed for the whole card. The two that are swept are ignored.
    public double PowerPercent { get; init; } = 50;
    public double SpeedMmPerS { get; init; } = 800;
    public double FrequencyKHz { get; init; } = 200;

    public double PulseWidthNs { get; init; } = 6;
    /// <summary>As typed in MakeIt. Units still to confirm for the colour test (see TODO).</summary>
    public double LineDensity { get; init; } = 5000;
    public int Passes { get; init; } = 1;
    /// <summary>Focus / Z as MakeIt reports it, mm. Part of every cell's settings.</summary>
    public double FocusZMm { get; init; }

    public int Rows => RowSweep.Count;
    public int Columns => ColumnSweep.Count;

    /// <summary>The Lumos Ultra MOPA colour test, as on Lee's first card.</summary>
    public static ColorTestDefinition UltraColourTest() => new();

    /// <summary>MakeIt's Material Testing Array: speed down the card, power across.</summary>
    public static ColorTestDefinition MaterialTestArray() => new()
    {
        RowSweep = new(CardAxis.Speed, 400, 2000, 10, Descending: false),
        ColumnSweep = new(CardAxis.Power, 20, 80, 20, Descending: false),
        FrequencyKHz = 200,
    };

    public bool IsSwept(CardAxis a) => RowSweep.Axis == a || ColumnSweep.Axis == a;

    public double ValueAt(CardAxis axis, int row, int col) =>
        RowSweep.Axis == axis ? RowSweep.At(row)
        : ColumnSweep.Axis == axis ? ColumnSweep.At(col)
        : axis switch
        {
            CardAxis.Power => PowerPercent,
            CardAxis.Speed => SpeedMmPerS,
            _ => FrequencyKHz,
        };

    /// <summary>Null if valid, otherwise what is wrong, in words for the user.</summary>
    public string? Problem() =>
        RowSweep.Axis == ColumnSweep.Axis ? "Rows and columns must sweep different settings."
        : Rows < 2 || Columns < 2 ? "A card needs at least 2 rows and 2 columns."
        : RowSweep.Min >= RowSweep.Max || ColumnSweep.Min >= ColumnSweep.Max ? "Each range needs its lowest value below its highest."
        : null;

    public MopaSettings SettingsAt(int row, int column) => new()
    {
        // Power and frequency rounded to match what MakeIt actually sends to the laser
        // (nearest whole percent and nearest whole kHz respectively); speed's step is
        // unconfirmed, so it keeps the finer rounding.
        PowerPercent = Math.Round(ValueAt(CardAxis.Power, row, column)),
        SpeedMmPerS = Math.Round(ValueAt(CardAxis.Speed, row, column), 3),
        FrequencyKHz = Math.Round(ValueAt(CardAxis.Frequency, row, column)),
        LineDensityPerCm = LineDensity,
        PulseWidthNs = PulseWidthNs,
        Passes = Passes,
        FocusOffsetMm = FocusZMm,
    };

    public static string Name(CardAxis a) => a switch
    {
        CardAxis.Power => "Power",
        CardAxis.Speed => "Speed",
        _ => "Frequency",
    };

    public static string Unit(CardAxis a) => a switch
    {
        CardAxis.Power => "%",
        CardAxis.Speed => "mm/s",
        _ => "kHz",
    };

    public static string Format(CardAxis a, double v) => $"{v:0.##} {Unit(a)}";

    public string DescribeCell(int row, int col) =>
        $"{Format(RowSweep.Axis, RowSweep.At(row))}, {Format(ColumnSweep.Axis, ColumnSweep.At(col))}";

    public string Describe()
    {
        var fixedParts = new List<string> { $"τ {PulseWidthNs:0.#} ns" };
        foreach (var a in Enum.GetValues<CardAxis>())
            if (!IsSwept(a)) fixedParts.Add(Format(a, ValueAt(a, 0, 0)));
        fixedParts.Add($"density {LineDensity:0.#}");
        if (Passes != 1) fixedParts.Add($"{Passes} passes");
        fixedParts.Add($"Z {FocusZMm:+0.0#;-0.0#;0}");
        return $"{string.Join(", ", fixedParts)}; rows {Sweep(RowSweep)}; columns {Sweep(ColumnSweep)}";

        static string Sweep(AxisSweep s) =>
            $"{Name(s.Axis).ToLowerInvariant()} {s.First:0.##}→{s.Last:0.##} {Unit(s.Axis)} × {s.Count}";
    }

    /// <summary>The card as a grid, cells in reading order (row by row from the top).</summary>
    public TestGrid ToGrid(string title, string materialId, SourceProfile source, LensProfile lens,
                           double cellPitchMm = 3.5, CardInfo? card = null, string purpose = "MakeIt test card")
    {
        var layout = new GridLayout
        {
            Columns = Columns,
            Rows = Rows,
            CellSizeMm = cellPitchMm * 0.8,
            GapMm = cellPitchMm * 0.2,
        };
        var cells = new List<GridCell>(Rows * Columns);
        for (int r = 0; r < Rows; r++)
            for (int c = 0; c < Columns; c++)
                cells.Add(new GridCell(r, c, GridLayout.Label(r, c), SettingsAt(r, c), $"Card cell: {DescribeCell(r, c)}"));

        return new TestGrid
        {
            Title = title,
            Purpose = purpose,
            MaterialId = materialId,
            SourceId = source.Id,
            LensId = lens.Id,
            Layout = layout,
            Cells = cells,
            Card = card,
            Definition = this,
        };
    }
}
