# Resume here

## 2026-10-02: publish day (Lee's call)

State at the end of 2026-10-01 (all synced to Cortex, `-warnaserror` clean, 149 core tests
pass; nothing pushed): heat limits (planner 40 J/mm², job audit 45 J/mm², SafeSend the only
upload path) after the burn-through; per-material planning ranges (passes 1/2/3, least heat);
micro-grids sized to their cells, planned around good cells outside the ranges, and a window
when none can be planned; filled corner marks, number dots and "Grid N" label (visible on
titanium); whole-sheet reading confirmed on Lee's titanium scan (MakeIt card beside the grids,
grids not on one lattice, light/dark mixed cells); reading one grid from a picture of several
picks it by number; **MakeIt exports confirmed in MakeIt 3.0.6**: materials (`Export/
MakeItMaterials`, Import adds under Customization, every Fill setting applied, re-import
updates in place by id; ids must be well-formed UUIDs) and the palette project
(`Export/MakeItPalette`, plain 3.0.5 format, in colour order, fitted inside the lens field);
palette window counts, default export folder, Show in folder; console hidden for share builds.
User guide, README and CHANGELOG updated for all of it.

To publish (Lee):
1. Move `workflows-to-move\*.yml` into `.github\workflows\` (Claude's tools may not write there).
2. Delete `docs\images\micro-grid.png`, `docs\images\scan-import.png` and `docs\images\ScreenShot.png` (stale or unused; only main-demo.png is used, refreshed 2026-10-02).
3. Confirm the repo name `LeeGillie/MOPAChromaAtlas` (the updater and links use it).
4. Decide the absolute heat cap (40 J/mm² now; ~75 possible) or keep it.
5. Rebuild the guide PDF (`python docs/build_guide.py`), check it, then push and tag `v0.1.0`.

Open checks: engrave a shape from an exported MakeIt material beside its grid cell and compare;
LightBurn with the Ultra (T5–T7). Lee's draft Facebook announcement was rewritten on
2026-10-01 (no cabin-light control in Atlas: do not claim it).


## 2026-10-01 evening: publish candidate, waiting on Lee's tests

Built today, all on .NET 10 (`-warnaserror` clean; 108 core + 15 client tests pass on
Cortex): start a new atlas; SVG/CSV export removed; micro-grids are sendable Atlas grids;
no setting is ever repeated (except a micro-grid's one intended repeat); colour-seeking
refinement (rare hues, more heat; passes 1-3, line density to 3000/cm); palette window and
LightBurn `.clb` export; the colour finder uses a picker and swatches (no typed colours);
material form; grid numbers engraved as a dot code between the top crosses
(`Grids/CardId`); **Read every grid on a scanned sheet…** (`Scanning/SheetReader`,
`GridDetect.DetectEach` splits a shared lattice into islands); MIT licence;
raw-command button gone from the test harness; user guide updated.

Not built: reading the ~29 pre-numbering grids from a sheet scan (read them one at a time);
MakeIt project export (waits on T1). Sheet reading and the number dots are tested on
simulated sheets only: first real check is a scan of a sheet with two or three new grids.

Next: Lee's test list (TODO.md), fixes from it, then the first public push (Lee's call).
Test-only fix worth remembering: `GridDetect.Trace` is now AsyncLocal; a static hook set by
one test class was written to by others in parallel ("no currently active test").


## 2026-10-01 morning: where to pick up

**The working loop (confirmed on hardware):** plan a grid → Send to laser → the job is
drawn on the calibrated laser-camera picture, drag it onto the blank → Send and arm →
Start on the machine → the card is photographed and opens in the reader with the grid
already on the squares → Add to atlas. Lee has run it grid after grid on a large
stainless sheet (about 29 grids) and calls it "hugely better … pretty streamlined".

**Next:** the roadmap at the top of `TODO.md` (agreed with Lee): new-atlas reset and
removing SVG/CSV, sendable micro-grids with no repeats, card IDs, whole-sheet flatbed
scans replacing camera colours, colour-seeking planning, palette view, MakeIt/LightBurn
exports, material form. Then the "Before publishing" list there.

**Findings** from these sessions are in `docs/CALIBRATION-FINDINGS.md` (Real-hardware
findings, 2026-09-30 to 2026-10-01). Lessons worth keeping:
- Never let a job be sent without its placement shown on a calibrated picture: canvas
  (105, 105) is not where one might assume.
- A symmetric calibration target can be matched mirrored and still fit perfectly. The
  target is now uneven and matched automatically.
- Show one step at a time and never accept input that cannot mean anything yet (clicks
  before the target exists).
- Do not let software "snap" a grid the user placed: it locked onto the wrong squares.
- Dev environment: copying files to Cortex via the bridge needs a fresh staging path each
  time (re-sending the same path delivered a stale copy); verify by hash. Build and test
  in a copy under %TEMP%, never in D:\DevHome, so Lee's Visual Studio builds are not
  locked. Building over the share uses no .exe launchers (Bitdefender refuses copying
  new .exe files there; `Directory.Build.props`).


## 2026-10-01: Send to laser, then read the card back, in one flow

Not yet tried on the laser. **First job: a scrap blank, a small planned grid, Send to
laser…, press Start on the machine, and watch each step.** Built and tested off-machine:
74 core tests pass, app builds with `-warnaserror`.

- **Main window**: a planned Atlas grid (one with cells and no MakeIt definition) gets
  **Send to laser…** and **Read this grid from an image…**.
- **`Views/SendToLaserWindow`**: 1 find the laser and check the door → 2 build the job
  (`GcodeExport.ToGcode(grid, cornerMarks: true)`), keep a copy in
  `%APPDATA%\MOPAChromaAtlas\jobs`, `UploadAndArmJobAsync` → 3 wait for the machine's
  Start button (an armed job reports Idle 4; "Start from here" exists but needs a
  confirmation tick and is styled as dangerous) → 4 follow the job (Busy 2 seen, then two
  non-Busy polls = finished) → 5 wait 3 s, `TakePhotoAsync`, rotate, keep in captures,
  open the reader on it. Stop job calls `CancelJobAsync` (unconfirmed on the Ultra).
  **Unknown on hardware**: whether the physical Start button runs an uploaded armed job,
  and whether status really goes Idle → Busy → Idle as assumed.
- **Corner crosses** (`CornerMarks` in `GcodeExport.cs`): small crosses above the top-left
  and top-right cells and below the bottom-left cell (none at bottom-right), engraved with
  the strongest cell's settings, so the reader knows which way up the card is and where
  the grid ends even when the weakest cells leave nothing. `GridDetect.Detect(..., marks:
  CardMarks.AtlasCorners(layout))` looks for them; MakeIt cards use
  `CardMarks.MakeItLabels`.
- **Reader for an existing grid** (`ScanImportContext.Existing`): the definition section
  is hidden, rows/columns come from the grid, and Save goes through
  `Scanning/GridReadout.ToSamples`; `AtlasDocument.RecordResults` fills that grid's
  Pending samples and appends anything already measured (never overwrites).
- **Tests**: `AtlasCardTests` "engraves" the real G-code into an image, upright and
  turned 180°, with the two weakest columns invisible, and reads it back to < 2 px.
- **Dev**: `--read-demo <image>` opens the reader on an 8 × 3 demo grid.

Later the same night, after Lee found the reader clunky and the S3 USB camera black:
- **Reader**: scroll to zoom (about the pointer), drag the picture to pan, "Zoom to grid" /
  "Whole picture"; a small card in a big picture (laser camera) is zoomed to automatically.
  **Crop to card** (drag a box; Undo crop). The grid is moved by dragging it and reshaped by
  dragging its round corner handles, snapping to the squares on release; arrow keys move it
  one square. The ◀ ▶ ▲ ▼ buttons and the three-click placement are gone ("Place by hand"
  now drops a draggable grid in the view; `--scan-demo` clicks still work).
- **USB camera**: the preview no longer hands frames to Avalonia's decoder (a failure there
  was swallowed, leaving the preview black); frames go through `RgbImage.Decode`, like the
  capture, and the status line reports frames/s, "no frames", or a decode error. Cameras are
  listed per interface (`Capture/CameraSources`: DirectShow, then Media Foundation) with a
  time limit each. On Cortex over Remote Desktop the S3 (redirected) delivers 1080p frames
  that decode fine. **Not yet confirmed on Benchy**, where Lee saw the black preview.
- **Diagnostics**: `--list-cameras <txt>` and `--grab-camera <name> <folder>` (test capture,
  report and PNGs) - run them from a command line on the PC with the camera.

Later again (2 am), after Lee could not line the grid up and asked to see where a job will go:
- **Hand placement stays put**: dragging the grid or a corner no longer snaps (snapping on
  real camera pictures locked onto the wrong squares). "Snap to squares" does it on
  request, and refuses if it would move a corner more than a third of a square. **Undo**
  (Ctrl+Z) for every grid change; "Find automatically" is undoable.
- **View brightness**: Darker / Brighter / Stretch contrast change only how the picture is
  shown (washed-out laser-camera photos), never what is measured.
- **Laser camera orientation**: Lee saw the picture upside down. Default is now a quarter
  turn anticlockwise (`CaptureSettings.LumosQuarterTurns` = 3); "Turn picture" / "Mirror"
  in the reader and calibration window change it and are remembered. Not yet confirmed
  whether the picture is also mirrored.
- **Camera calibration** (`Scanning/BedCamera`, `GcodeExport.CalibrationTarget`,
  `Views/CameraCalibrationWindow`): engrave nine crosses (1-9 bars beside each) on a scrap,
  photograph, click them; homography + one radial lens term, 0.2 mm over the whole bed on a
  simulated tilted, barrel-distorted camera. Stored in capture settings with the picture
  orientation it belongs to. Only true at the calibrated height.
- **Send to laser shows the job on the camera picture** (`Views/PhotoView`, reusable
  zoom/pan view): drag it onto the blank, or type the job centre; the job is generated at
  that centre (`GcodeExport.ToGcode(grid, cx, cy, …)`, kept 2 mm inside the bed).
- **Light**: the cabin light's brightness may be settable through `/device/light/*`, shapes
  unknown. `--lumos-probe <txt>` GETs only status-style endpoints (light status/init value,
  `/camera/get_calibration`, peripheral status) and writes the answers - run it on Benchy,
  where the laser is. If `get_calibration` holds the factory camera calibration, the
  engraved target may not be needed.
- Dev: `--calib-demo <png> <clicks>` and `--send-demo <png>` (simulated pictures in the
  session's scratch `sim/render.py`). Building on Cortex now happens in a copy under
  %TEMP%, never in D:\DevHome, so Lee's Visual Studio builds are not locked out.

---

## 2026-09-30 evening: card images from anywhere, squares found without clicks

Not yet tried by Lee in the running app - **first job on Benchy: open the importer on a
real card from each source below and check the boxes land on the squares.** Built and
tested off-machine (Linux, SDK 8, packages copied from Benchy's NuGet cache):
69 core tests and 15 client tests pass, and the app builds with `-warnaserror`.

- **`Scanning/GridDetect`** finds the grid with no clicks: square-shaped blobs → a lattice
  (local spacing followed, so perspective photos work) → a homography → placement on the
  card. MakeIt's value labels (left of every row, under every column) decide which way up
  the card is and where the grid sits when faint low-power squares cannot be seen. Edges
  that run one way (the card outline) are not mistaken for labels. On Lee's two real
  scans it matches the three-click fit exactly (0.00 px) and recognises the 90° scan as
  turned 90° anticlockwise; a simulated angled photo of the same card lands within 1.6 px.
  An unlabelled card (an Atlas grid) whose edge squares are too faint is flagged
  "check the boxes" rather than guessed. Tests: `GridDetectTests` (real-scan tests skip
  when `samples/scans` is absent).
- **`GridGeometry` can now be projective** (`Warp`, a `Homography`). `GridFit` and
  `ScanMeasure` size their windows from each cell's own pitch. Affine grids behave as before.
- **Importer sources** (`ScanImportWindow`): open file, drag and drop, paste (Ctrl+V),
  **Scan…** (WIA on Windows, opens the scanner's own dialog; SANE `scanimage` on Linux),
  **scan folder** (any scanner's own software saving to a folder; new files open by
  themselves - `ScanFolderWatcher`), **USB camera…** (`CameraWindow`, FlashCap 1.12: live
  preview, the camera's own settings page to lock white balance/exposure, N frames
  averaged), and **Laser's camera** (`LumosUltraClient.TakePhotoAsync`, rotated upright).
  Squares are found as soon as an image arrives; "Place by hand" keeps the old three
  clicks; ◀ ▶ ▲ ▼ move the grid a whole square and "Turn 180°" flips it.
- **How readings are recorded**: a flatbed scan stays `MeasureMethod.Scanner`; any photo
  (camera, laser camera, pasted, or a file the user marks as a photo) is
  `MeasureMethod.Estimated`, with source, turn and angle in `ViewingCondition`. Captures
  that are not files are saved as PNG in `%APPDATA%\MOPAChromaAtlas\captures`.
- **New package**: FlashCap 1.12.0 (MIT). The app now also references `LumosUltra.Client`.
- **Not yet tried on hardware**: WIA scanning with the Xerox, a USB camera, the laser's
  camera through the importer. The laser camera's wide lens may bend straight rows near
  the edges of its picture; a homography cannot undo that, so keep the card near the
  middle of the bed and expect "check the boxes" if it is not.

Also found today (MakeIt 3.0.6, `D:\Temp\gcode`): a **dot-mode** job uses `G5T230` (per-dot
time) then `G5X..Y..S900` per dot - not yet in `docs/CALIBRATION-FINDINGS.md`. MakeIt's
framing file traces the outline with the red pointer (`M3S100`) and unpowered `G1 ... S0`
moves under its own `M107X-105Y-262.9` offset - worth comparing with what
`StartFramingPreviewAsync` sends, since framing still does not trace.

---

Earlier state follows.

Read this first when picking MOPAChroma Atlas back up. State as of 2026-09-27 morning
(major session: built `LumosUltra.Client`, confirmed it end-to-end on real hardware,
reverse-engineered MakeIt's gcode encoding, and built our own generator that bypasses
MakeIt entirely). `TODO.md` has the fuller backlog; `CLAUDE.md` the conventions.

## The big picture change this session

We can now go **Atlas grid → real laser mark** without MakeIt in the loop at all, for
grids whose cells vary power/speed/density/frequency/pulse-width independently -
exactly the kind MakeIt's own wizard can't represent (it only sweeps two parameters at
a time, linearly). MakeIt is still how framing/alignment and SVG-import-based coupon
cards work; the new path is for CSV-driven grids specifically.

## What's now CONFIRMED on real hardware (this session)

- **`src/LumosUltra.Client`**: standalone (no MOPAChroma.Core dependency) library for the
  Lumos Ultra's local HTTP+FTP API. Device info, job status, camera capture (with the
  90° rotation baked in), door status (confirmed `status: 1` = closed, `0` = open - the
  opposite of the original guess), and the full upload→arm→start job pipeline all
  CONFIRMED end-to-end: a job built entirely by our own code fired the laser and marked
  real material, no MakeIt UI or physical button involved. See
  `src/LumosUltra.Client/README.md` for the confidence table per method.
- **`src/LumosUltra.Client.TestHarness`**: Avalonia app, one tab per feature, for manual
  real-hardware bring-up. Camera tab has zoom/pan (cursor-anchored, fixed after several
  rounds of Avalonia RenderTransform gotchas - see git history/comments in
  `MainWindow.axaml.cs` if this regresses).
- **Framing preview is still broken**: status transitions correctly (Idle→Preview→Idle)
  but never visibly traces a boundary. **Confirmed this also fails in MakeIt's own
  software**, not just our code - this looks like a WeCreat firmware/product issue, not
  ours. A support ticket was going to be filed. **This is the reason the GitHub repo
  stays private for now** - doing a batch of real cards + flatbed scans without working
  framing/alignment is risky.
- **`docs/CALIBRATION-FINDINGS.md`**: the reverse-engineered encoding for MakeIt's
  "Color Test (Solid Color)" wizard gcode, confirmed via a controlled calibration run
  (known 3x3 grid, captured via FTP, cross-checked against the physical part - MakeIt
  engraves its own axis labels and fixed settings onto the coupon). In short: Power% ->
  `S = Power% * 10` on the burn stroke, Frequency(kHz) -> `M38F` directly, Speed(mm/s)
  -> `G1F = mm/s * 60`, Line density(/cm) -> pitch `10mm / density`, Pulse width(ns) ->
  `M39P` directly.
- **`src/MOPAChroma.Core/Grids/GcodeExport.cs`**: generates a real Lumos Ultra gcode.gc
  directly from a `TestGrid`, using the confirmed encoding above and a real, proven
  preamble/postamble (copied verbatim from a captured MakeIt job). 9 unit tests. Does
  **not** yet engrave cell labels onto the part (unlike `GridExport.ToSvg`) - this
  caused real friction identifying which physical square was which cell from a photo
  afterward. Worth fixing before the next real grid.
- **`src/GridCsvToGcode`**: small CLI that turns a `GridExport.ToCsv` file back into a
  gcode.gc via `GcodeExport`. Used to build and fire "grid 17" - Atlas's own seed grid,
  read straight off the settings CSV, with no MakeIt involvement. All 9 cells measured
  and saved in the atlas (4 Good with photo-estimated Lab, 3 Damaged, 2 NoMark).
- **Atlas gotcha (not a bug)**: `SaveResult()` in `MainWindow.axaml.cs` silently
  refuses to save a Good/Uneven result without L*/a*/b* filled in - by design, but easy
  to click through several cells thinking they saved when they didn't. Check
  `atlas.json` directly (or watch the status bar / cell border) if in doubt.

## Dev environment gotchas (recurring all session)

- `\\cortex\D\DevHome\MOPAChroma Atlas` intermittently fails `dotnet build` with
  `MSB3021` (file locked) even with no visible process holding it - fix is
  `Restart-Service LanmanWorkstation -Force` then retry. Happened ~15+ times this
  session, always fixed by this.
- Separately, the **loopback SMB share itself** (`\\cortex\D`) can lag behind the real
  local filesystem state (server-side cache, not the client-side issue above) - a
  freshly-created file/folder was invisible via `\\cortex\D\...` but immediately visible
  via the local `D:\...` path on the same machine (hostname `BENCHY`). If something
  looks missing right after Atlas (running locally) writes it, try the local `D:\` path
  before assuming it's really gone.
- `gh` CLI is **not installed** on Benchy - needed (or the GitHub web UI) to actually
  create and push the private repo.

## Opening it on Benchy

- The folder is reachable from Benchy as `\\cortex\D\DevHome\MOPAChroma Atlas` (or just
  `D:\DevHome\MOPAChroma Atlas` if already on Benchy - see the SMB gotcha above).
- Build/test: `dotnet build MOPAChromaAtlas.slnx -warnaserror` /
  `dotnet test MOPAChromaAtlas.slnx`.
- Run the main app: `dotnet run --project src\MOPAChromaAtlas`.
- Run the hardware test harness: `dotnet run --project src\LumosUltra.Client.TestHarness`
  (or launch the built exe directly - see gotcha above about stale builds).
- **The atlas file is per machine** (`%APPDATA%\MOPAChromaAtlas\atlas.json`).
- **The repo is now (or about to be) a git repository** - see `TODO.md` for the
  push-to-GitHub status; it should say private-until-framing-works.

## Next, in order

1. **Push to GitHub as a private repo** (was about to happen as of this note - check
   `git remote -v` / `gh repo view` to see if it's done).
2. **Framing**: waiting on WeCreat (support ticket). Revisit `StartFramingPreviewAsync`
   if/when there's new information; don't re-litigate the investigation already done
   (see the method's own doc comment in `LumosUltra.Client/LumosUltraClient.cs` and the
   README's Framing row) unless something material has changed.
3. **Add label engraving to `GcodeExport`** (mirror `GridExport.ToSvg`'s lettering
   group) so future real parts are self-identifying. Partly covered 2026-10-01: corner
   crosses let the reader place the grid automatically; human-readable labels are still
   worth adding.
4. Everything in the old MakeIt-scan-import backlog in `TODO.md` is still valid and
   untouched this session (scan import, micro-grid end-to-end click-through, Xerox scan
   whites clipping, etc.) - the new gcode-generation path is additive, not a
   replacement for that workflow.
5. Once there's more measured data (beyond grid 17), revisit whether `GcodeExport`'s
   margin/fly-in-out convention and raster approach hold up against a wider variety of
   settings (only tested at fairly low line densities so far - the huge 899K-line
   MakeIt-native captures were never round-tripped through our own generator).

## Parked idea: a Lumos Ultra device library

Done - see `src/LumosUltra.Client`. the private note `docs/LUMOS-LIBRARY-IDEA.md` is the original
feasibility note that kicked this off.
