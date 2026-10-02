# MOPAChroma Atlas — Design

Status: draft, 2026-09-26. Describes version 0.1 as built and the direction after it.

## 1. The problem

A MOPA fiber laser colours metal by growing a thin oxide film; the colour is the
interference colour of that film, and its thickness is set by how much heat each spot
receives and how quickly. MakeIt exposes six or more knobs that all feed that heat:
power, speed, line density, frequency, pulse width and passes.

The usual way to find colours is a sweep grid — power across, speed down, everything
else fixed — repeated for every combination of the other knobs. That wastes most of
its cells, in three ways:

1. **Equivalent cells.** Double the power, the speed and the frequency and every
   physical quantity is unchanged. A knob-by-knob sweep engraves such pairs as if they
   were different experiments.
2. **Dead cells.** Large corners of any sweep leave no mark or burn the surface. Once
   one cell has shown that, every cell that delivers less (or more) of both energy per
   area and energy per pulse is certain to do the same.
3. **Already-mapped cells.** Where neighbouring cells came out the same colour, cells
   between them add nothing.

The atlas exists to spend each cell where it can teach something, and to remember
everything learned, per material, forever.

## 2. Principles

- **Plan in physics space, engrave in MakeIt units.** Distances between candidate
  settings are measured on four physical axes (below). Every output is in the units
  MakeIt shows, so a cell can be typed in without conversion.
- **Measured, never predicted, answers.** "What settings give this colour" answers
  only from engraved and measured cells. Predictions only decide *where to look next*.
- **Material identity includes grade and finish.** 304 and 316, brushed and mirror,
  are different materials. Results are never pooled across them, nor across sources
  or lenses.
- **Evidence on every number.** Source limits and spot size carry an evidence grade
  (Nominal, VendorStated, Measured). The shipped presets are Nominal.
- **Samples are immutable.** A measured cell is never edited; a re-measure is a new
  sample. Every colour in the atlas traces to a physical coupon.

## 3. Data model (Core, `MOPAChroma.*`)

| Type | Holds |
|---|---|
| `Material` | name, family, grade, finish, thickness, supplier |
| `SourceProfile` | rated power, frequency range, discrete pulse widths, max speed, evidence |
| `LensProfile` | field size, spot diameter, evidence |
| `MopaSettings` | power %, speed mm/s, line density /cm, frequency kHz, pulse width ns, passes, hatch angle, focus offset |
| `Physics` | derived: average power, pulse energy, peak power, fluence, pulse pitch, pulse/line overlap, areal energy |
| `TestGrid` | layout (cols, rows, cell mm, gap mm) + cells, each with settings and the planner's reason |
| `Sample` | one cell's result: outcome, Lab colour, method, viewing condition, time |
| `AtlasDocument` | all of the above, saved as one JSON file |

Outcomes: `Pending`, `Good`, `NoMark`, `Uneven`, `Damaged`.
Measure methods, most to least trusted: `Instrument`, `CalibratedImage`, `Estimated`.

MakeIt unit facts relied on (decoded from its staged G-code in the Lumos Ultra
LightBurn config project): speed mm/s in the UI (G1F is mm/min), power 0–100 %
(S 0–1000), line density in lines per **centimetre**, frequency kHz (M38F), pulse width
ns (M39P).

## 4. The planning space

For each candidate the planner computes

| Axis | Formula | Scale |
|---|---|---|
| Areal energy, J/mm² | passes × P ÷ (speed × line pitch) | log |
| Pulse energy, mJ | P ÷ f | log |
| Pulse width, ns | as set | log |
| Pulse overlap | 1 − (speed ÷ f) ÷ spot | linear |

and normalises each axis to 0..1 over the candidate pool. P is rated power × power %.
Candidates are a Halton low-discrepancy sample of the settings space, snapped to
sensible steps and filtered for validity (duty cycle ≤ 0.5, gaps no wider than half a
spot).

Known simplifications, to revisit with real data:
- Output power is assumed linear in the power percentage at every pulse width and
  frequency. Real MOPA sources derate at short pulses and high frequency.
- Line overlap is not a planning axis (it is folded into areal energy). If coupons
  show line density matters beyond energy, it becomes a fifth axis.
- Hatch angle and focus offset are carried but not yet planned over.

## 5. Choosing cells

**Seed grid.** Farthest-point sampling from the centre of the space: each new cell is
the candidate farthest from every cell already chosen or engraved. The first coupon is
a survey of the whole map, with no two cells physically equivalent. Cells are placed
on the coupon in order of increasing areal energy so it reads faint to strong.

**Refinement grid.** From the measured results:
1. *Dominance pruning* — skip any candidate with less areal and pulse energy than a
   no-mark cell, or more of both than a damaged cell.
2. *Dead-ground estimate* — distance-weighted share of no-mark/damaged among the five
   nearest measured cells (pDead).
3. *Score* = distance to nearest tested cell × (1 + colour spread among neighbours ÷ 2
   ΔE00 + 3 × edge) × (1 − 0.9 pDead²), where edge = 4 pDead (1 − pDead) peaks on the
   boundary of the usable region.
4. *Greedy pick* with a spacing penalty so one hot spot cannot take every cell.

Every cell carries a one-line reason ("Colour changes nearby (ΔE00 40.1)", "Edge of the
usable region", "Untested region") shown in the app.

On a simulated material the refinement grid put 21 % of its cells in dead ground
against the seed's 50 %; the unit test holds it to at most 60 % of the seed's share.

**Planned next: a proper surrogate model.** Replace the neighbour heuristics with a
Gaussian-process model per Lab channel (and a classifier for outcome) over the same
four axes. Acquisition then becomes expected information gain about the colour map,
and "find settings for a colour" can propose an untested setting with a stated
confidence — clearly labelled as a prediction to be engraved, never as an answer.

## 6. Measuring colour

Colour is stored as CIELAB (D65, 2°) and compared with CIEDE2000. Two cells closer than
ΔE00 2 are treated as the same colour.

Ways to get Lab, in order of trust:
1. **Instrument** — a colorimeter or spectrophotometer reading per cell. Best.
2. **Calibrated image** — a flatbed scan or fixed-lighting photo with a colour
   reference (ColorChecker or similar) in frame; the app registers the grid, samples
   the centre of each cell, and corrects against the reference. *Not built yet.*
3. **Estimated** — typed in or picked from an uncorrected photo.

Oxide colours shift strongly with viewing angle and light. Every sample records its
viewing condition; the scan workflow will fix it by construction.

## 7. Output for engraving

- **SVG** in millimetres: one square per cell with its label as id, plus a separate
  labels group to engrave at a single known setting or delete.
- **CSV** settings sheet in MakeIt units with the derived physics beside each cell.

Next: a LightBurn project export carrying each cell's settings directly, building on
the Lumos Ultra LightBurn configuration work.

## 8. Platform and packaging

C# on .NET 10 with Avalonia 11, the same stack as DepthView, published self-contained
for Windows, macOS and Linux as per-platform zips with the same in-place updater.
Moved from .NET 8 to .NET 10 (the current LTS) in October 2026; the target framework is
one line in `Directory.Build.props`.
