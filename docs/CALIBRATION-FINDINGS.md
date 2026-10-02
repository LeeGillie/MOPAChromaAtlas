# WeCreat MakeIt "Color Test (Solid Color)" gcode encoding

CONFIRMED via a controlled real-hardware calibration run on the authors' own Lumos
Ultra: a known 3x3 grid was configured in MakeIt's own "Color Test (Solid Color)"
wizard, the resulting job was pulled via FTP before it ran
(`calibration-3x3-power-freq.gc`, 1,078,527 bytes, kept local under `samples/gcode/` -
see .gitignore), and the marked result was independently read back off the physical
stainless coupon (MakeIt engraves the axis labels and fixed settings onto the part
itself). The gcode analysis and the physical photo agree exactly on every value below.

## Wizard input

| Field | Value |
|---|---|
| Coloring Scheme | Fine Color Marking |
| Column axis | Power, 30-90%, 3 steps (30/60/90) |
| Row axis | Frequency, 30-90, 3 steps (30/60/90) |
| Pulse Width (fixed) | 6 |
| Speed (fixed) | 800 |
| Line density (fixed) | 5000 |
| Column/Row spacing | 1mm / 1mm |
| Cell height/width | 5mm / 5mm |

Note: MakeIt's wizard UI showed "mm/s" as the unit suffix on the Row (Frequency)
min/max fields. That is a cosmetic label bug, not a real unit mismatch - the engraved
part itself labels this axis "Frequency(k)" (kHz), and the gcode confirms the values
flow into the `M38F` frequency M-code unchanged. Do not read "mm/s" as meaning the row
axis actually controls speed.

## Confirmed per-cell encoding

Each cell is a short horizontal raster fill: a `M38F<freq>` / `M39P<pulse-width>`
header, then repeated scanlines of the form
`G0Y<line> G0X<start> G1X<mid>S0 G1X<end>S<power*10> G1X<margin>S0` - i.e. a
laser-off fly-in, a laser-on burn stroke at a fixed `S`, and a laser-off fly-out,
one line per Y increment at the configured line-density pitch.

- **Power (%) -> `S` value on the G1 burn stroke: `S = Power% * 10`.**
  Confirmed exactly across all three tested values (30% -> `S300`, 60% -> `S600`,
  90% -> `S900`), constant across every row for a given column. `M39P` does **not**
  carry power in this scheme.
- **Frequency (row axis) -> `M38F`, identity mapping.** Confirmed exactly across all
  three tested values (30 -> `M38F30`, 60 -> `M38F60`, 90 -> `M38F90`), constant
  across every column for a given row.
- **`M39P` = Pulse Width, held fixed at the wizard's Pulse Width value (6 in this
  run), unrelated to the swept axes.** Independently confirmed by the physical part's
  own "Pulse Width: 6" label. (Earlier, larger captures showed `M39P100` in their
  header/logo-drawing preamble only - that is unrelated setup/branding content, not a
  cell value; see "Not yet covered" below.)
- **Speed (fixed, 800) -> `G1F<mm/min>` on the fill segments = `Speed(mm/s) * 60`.**
  Confirmed: `G1F48000` in every cell = 800 * 60.
- **Line density (fixed, 5000) -> raster line pitch = `10mm / LineDensity`.**
  Confirmed: observed 0.002mm Y-increment per scanline = 10mm / 5000. (Wizard label
  reads "per cm" going by Atlas's own CSV column naming; 1cm / 5000 = 0.002mm agrees.)

## Used in `GcodeExport`

`src/MOPAChroma.Core/Grids/GcodeExport.cs` implements exactly this mapping to generate
gcode directly from a `TestGrid`, bypassing MakeIt's wizard (which can only sweep two
parameters at a time) entirely. First real-hardware validation: "grid 17" (Atlas's own
seed grid for stainless steel 304), fired directly with no MakeIt involvement, all 9
cells recorded in the atlas.

## Not yet covered / open questions

- Cell traversal order in the file is **not** simple row-major or column-major - the
  9 cells appear in a scrambled order (confirmed by cross-referencing each block's X
  range and M38F against a hand-built table; do not assume file order implies grid
  position, always read the actual `G0X`/`G0Y` and `M38F` values per block).
- Only Power and Frequency have been swept and confirmed. Speed, Line density, and
  Pulse Width were only ever observed as held-fixed constants in this run - their
  own gcode encoding (if varied as an axis instead) is not yet confirmed.
- `M39P`'s role outside "Fine Color Marking" mode, and the meaning of the repeated
  `M38F300`/`M39P100` header blocks seen before real cell data in every captured
  file (11 repetitions in this file, presumably a logo/text-drawing preamble unrelated
  to grid cells - each is followed by `S400F18000`-style vector strokes, not raster
  fill), are still unconfirmed.
- This mapping is specific to the "Fine Color Marking" coloring scheme. Other
  schemes in the same wizard dropdown have not been tested and may use different
  conventions.
- `GcodeExport` does not yet engrave cell labels onto the part (unlike
  `GridExport.ToSvg`'s lettering group) - identifying which physical square was which
  cell after the fact required reverse-engineering from photos. Worth fixing.

## Real-hardware findings, 2026-09-30 to 2026-10-01 (Send to laser, camera)

From Lee running the integrated Send to laser flow on a large stainless sheet, about a
dozen grids (grids ~17-29), and from MakeIt 3.0.6 captures in `D:\Temp\gcode`.

CONFIRMED
- **Upload, arm, then the machine's own Start button** runs an Atlas-generated job
  (`UploadAndArmJobAsync`, then Lee pressing Start). No MakeIt involved.
- **Following the job by status** works well enough to photograph and read the card
  automatically when it finishes (Busy seen, then two polls not Busy).
- **Job position.** Canvas (105, 105) is *not* reliably under the red dot or the middle
  of the bed: an uncalibrated job at 105, 105 landed "somewhere unexpected". Never assume
  where a canvas position lands; place jobs on a calibrated camera picture.
- **Laser camera orientation.** `TakePhotoAsync`'s 4032 × 3024 JPEG turned a quarter turn
  *clockwise* displays upside down. A quarter turn anticlockwise is now the default;
  whether the picture is also mirrored is not confirmed (the app has turn/mirror buttons,
  remembered per user).
- **Camera calibration from an engraved target works**, and placing jobs by dragging them
  on the calibrated picture put them where shown (Lee, 04:27: "I place the will engrave
  here, and it does so").
- **The first calibration target was a trap.** Nine crosses on a regular 3 × 3 layout:
  clicked in the wrong order (cross 1 taken for cross 4, 2 for 3), a top-to-bottom mirror
  of the bed fits *perfectly*, and every job then lands on the wrong side. It did, twice,
  including one job engraved over an earlier grid. Fixed with an uneven layout
  (`BedCameraTarget`, version 2) whose only good match is the right one, matched
  automatically from clicks in any order.
- **The cabin light washes out colour** in laser-camera pictures of mirror stainless:
  fine for placing grids, poor for measuring colour. Readings from it are stored as
  `Estimated`.

FOUND IN MAKEIT OUTPUT (not exercised by Atlas)
- **Dot mode:** `G5T230` (time per dot) then `G5X..Y..S900` per dot.
- **Framing file:** traces the outline with the red pointer (`M3S100`) using unpowered
  `G1 … S0` moves, under its own `M107X-105Y-262.9` offset (jobs use `M107X-105Y-105`).
  Compare with what `StartFramingPreviewAsync` sends; framing still does not trace.

NOT YET KNOWN
- **Cabin light brightness:** `/device/light/*` exists, request shapes unknown.
  `--lumos-probe` (read-only GETs) collects them; not yet run on Benchy.
- **`/camera/get_calibration`:** may hold the factory camera calibration; same probe.
- Whether the camera moves with the lid (if so, pictures must be taken with the lid in
  the calibration position, i.e. closed).
- `CancelJobAsync` (Stop job) on the Ultra; physical stop button always works.

## Source data (kept local, not committed - see `samples/` in .gitignore)

- `calibration-3x3-power-freq.gc` - the full captured job for this run.
- `colour-test-2026-09-27.gc`, `colour-test-2026-09-26_1826.gc` - earlier, larger
  captures from MakeIt's own wizard, not yet cross-checked against this mapping in
  detail.
- `minimal-test-segment.gc` - the proven-good preamble/postamble `GcodeExport` copies
  verbatim.
- `grid-17-generated.gc` - `GcodeExport`'s output for grid 17, the first grid fired
  with no MakeIt involvement.
