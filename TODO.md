# TODO

## Roadmap (agreed with Lee, 2026-10-01)

Where things stand: plan a grid, Send to laser (job placed on the calibrated camera
picture), press Start, the card is photographed and read back with the grid already on
the squares. Working well on hardware for a sheet of grids (about 29 so far). What is
missing is *finding vivid colour* and *measuring it well*. In order:

**Status 2026-10-01 (evening), ready for Lee's testing before going public:** roadmap
items 1-9 below are built (10 is a test for Lee). Item 4 is done for numbered grids;
the ~29 grids engraved before numbering are read one at a time as before (placing them
from saved jobs is not built). Licence MIT; .NET 10; raw-command button removed.

Done 2026-10-01: palette view (*Palette and exports…*: Good cells with a colour, one per
recipe, best reading, repeated colours first) and the LightBurn material library export
(`MOPAChroma.Export.LightBurnLibrary`, `.clb` + `.json`); SVG/CSV export removed from the
main window. Research behind it is in the author's private notes.

**User guide screenshots (next release; Lee: important).** Add a few small, current
screenshots: (1) the reader with the grid placed by hand, corner handles marked (Recipe 4);
(2) Send to laser with the job on the camera picture: outline, crosses, number dots, Grid N,
engraving time (Recipe 3); (3) the Planning ranges panel; (4) Palette and exports, counts and
save buttons; (5) MakeIt's Material list with an Atlas colour applied (Lee's screenshots,
cropped). 1-4 from the demo modes so they match the program; about half a page each.

**Laser cabin light while reading cards back** (Lee, 2026-10-01: dimming it helped when reading
colours with the laser camera). Atlas has never had this: no light control exists in the code or
its history (only the reader's display-only Darker/Brighter, and `--lumos-probe` reading
`/device/light/*`). To build it, capture what MakeIt sends when its light slider moves (MakeIt's
logs, or the probe before/after), then add a slider in Send to laser and the reader, set before
the picture and put back after.

**Tests for Lee** (from that report; each harmless to MakeIt's own files):
- T1 PASSED 2026-10-01 (MakeIt 3.0.6 opened the plain 3.0.5 project). Next, T2: open samples\makeit-palette-format-test.wws and check each square's settings against the .txt; then engrave one square and compare with Atlas. Was: copy the plain 3.0.5 project (`%APPDATA%\Wecreat MakeIt!\LocalProject\project-5bce1719-….wws`)
  to `D:\Temp\plain-test.wws` and open it in MakeIt 3.0.6. Opens → Atlas can write MakeIt
  palette projects; fails → MakeIt stays served by Send to laser.
- T3: note every pulse width MakeIt offers for the Ultra, and the frequency limits at a few
  widths (for the source profile; exports already round to the profile's list).
- T5/T6: in LightBurn with the Ultra profile, Load the exported `.clb`, Assign an entry to a
  layer, check power/speed/interval (and frequency/pulse width if shown); also save one
  entry made by hand and compare its XML with ours.
- T7: save G-code from LightBurn for that layer and look for `M38` (frequency) and `M39`
  (pulse width): present → the library is a real colour route for the Ultra.
- T8/T9: Merge the same library twice; re-export after a change and check linked layers
  follow.
- T10: if MakeIt can save a custom material, look at the first bytes of the new file in
  `LocalMaterials` ("WWS2" = encrypted, out of reach).

1. [x] **Start a new atlas** (archive the current file with a date, open an empty one; delete
   nothing) and **remove SVG/CSV export** (and probably the GridCsvToGcode tool): Send to
   laser replaced them, and the atlas file holds the data.
2. [x] **Micro-grids as sendable Atlas grids**, never repeating an engraved setting except
   the one intended repeat of the centre cell. Also make refinement refuse settings
   *physically almost identical* to tested ones, not only exact repeats.
3. [x] **An ID on every card**: the grid number as a row of 18 dots between the top
   corner crosses (end dots, 12-bit number, 4-bit check). `MOPAChroma.Grids.CardId`.
4. [~] **Read a whole-sheet flatbed scan** (numbered grids done: *Read every grid on a
   scanned sheet…*, `SheetReader`; legacy grids not) (and USB-camera photos of single cards): find every
   grid on it by its ID, read all cells, and let the better reading (Scanner, or a
   good camera) replace the laser camera's Estimated one wherever colours are looked up.
   For the ~29 grids engraved without IDs: place them from their saved jobs
   (`%APPDATA%\MOPAChromaAtlas\jobs`, which hold each job's bed position) if the sheet has
   not moved; otherwise click a grid and pick its number.
5. [x] **Plan for colour, not just coverage**: a "find new colours" refinement that rewards
   hues the atlas does not have yet and pushes towards more heat (thicker oxide: purples,
   blues and beyond). Widen the space: passes 1-3, line density beyond 1000 /cm. Today the
   planner rewards colour *change* and novelty but never missing hues, and is held to one
   pass, which is why the atlas is golds, browns and silvers.
6. [x] **Palette view**: only Good cells with a colour, repeat-verified first; no-mark, uneven
   and damaged left out.
7. [~] **Exports for using the colours** (LightBurn library done; MakeIt palette written 2026-10-01, `MOPAChroma.Export.MakeItPalette`, settings check T2 pending): a MakeIt palette project (`.wws`, one labelled swatch
   per confirmed colour with its settings; format partly known from William110x110), a
   LightBurn material library (`.clb`, check against one exported from Lee's LightBurn),
   and investigate the laser's own material list (`/device/material/{upload,download}`).
   Every export states the source and lens: recipes transfer exactly only to the same
   source and lens.
8. [x] **No more typing colour numbers.** The main window still has L\*/a\*/b\* boxes for a
   cell's result and for "find settings for a colour" (from the first version). Readings
   from pictures are automatic; replace the result boxes with "enter a colorimeter
   reading" (tucked away), and the colour finder's boxes with picking from a palette of
   colours actually made.
9. [x] **Material "form"** field (sheet / card / coin / tag): a big sheet sinks heat unlike a
   small card or a thick coin, so they should be separate materials even for the same
   steel.
10. Run `--lumos-probe` on Benchy (cabin light control; factory camera calibration), and
   wrap the light if its request shape is safe and clear.

## Vision (Lee, 2026-10-01): beyond what MakeIt lets you reach

- **Settings MakeIt does not offer.** MakeIt's colour tools sweep two settings at a time
  over fixed ranges; Atlas writes the G-code itself, so every cell can have its own power,
  speed, frequency, pulse width, line spacing, passes and hatch angle, in combinations no
  MakeIt preset reaches. That is where unfamiliar colours may be found. Keep the
  planner's space wide (roadmap 5) and say so in the guide.
- **Iridescent / rainbow finishes.** Some of these colours change with viewing angle
  because the surface becomes a fine grating (very tight line spacing, cross-hatching,
  periodic ripples formed by the pulses), not just an oxide layer. Plan: a separate
  "structural colour" experiment type sweeping line spacing near the spot size, hatch
  angle and cross-hatch at low heat; record results as an *effect* (iridescent, with the
  angle seen) as well as a colour; photograph at two or three angles. Experimental: no
  promise of what the Ultra's spot size allows.
- **Colour pictures and fine detail ("micro printing").** With a palette of reliable
  colours, a picture can be engraved in colour: reduce it to the palette's colours
  (with dithering, so mixtures of nearby dots read as in-between shades), then engrave one
  pass per colour with that colour's recipe, each only where its dots are. Needs: the
  smallest dot and spacing each recipe holds without bleeding into its neighbours (heat
  spreads), registration between passes, and an order (low heat before high). A natural
  later stage once the palette is verified; start with a few large-area colours, then
  finer dots.

- **Research: what striking effects are people getting with MOPA fiber lasers** (not
  primary; Lee, 2026-10-01). A separate deep-research pass on effects beyond flat oxide
  colour: rainbow/holographic diffraction finishes, colour photo engraving, black
  annealing, frosted or textured surfaces, layered or multi-pass colour, colour on
  titanium, aluminium and other metals, 2.5D relief combined with colour, and what has
  (or has not) been shown on a Lumos Ultra. Goal: ideas no one has shown on the Ultra
  yet, each judged for whether Atlas's direct G-code control makes it reachable.

## Before publishing (opening the project to others)

- [x] **Licence.** MIT (Lee, 2026-10-01). Still to check: ImageSharp's Six Labors split
  licence terms are acceptable for users (free for open-source use as far as we know).
- **Laser safety.** Atlas arms a Class 4 laser. Keep: nothing fires without Start on the
  machine (or the confirmed Start-from-here), door check before arming, no sending
  without seeing the placement. [x] Test harness raw-command button removed; the
  library's `SendRawCommandAsync` stays, behind an explicit acknowledgement. [x]
  Disclaimer in the README.
- **Provenance.** `LumosUltra.Client/PROVENANCE.md` already records sources and what is
  *not* used (no WeCreat-private material). marcoreps/lumos_RE and 2ktsch/weburn have no
  licence files: use facts learned from them, never their code (as now). The G-code
  preamble/postamble are copied from MakeIt's own output: keep that noted. Check WeCreat's
  terms on reverse engineering / third-party control (not legal advice).
- **Private data.** Scans (`samples/`), the atlas, captures and jobs stay out of the repo
  (gitignored or in %APPDATA%). Check the git history for anything private before the
  first public push; William110x110 stays separate.
- [x] **.NET 10** (done 2026-10-01).
- **User guide** (`docs/USER-GUIDE.md`) kept in step with the app: update its "coming
  soon" notes as roadmap items land, and add screenshots.
- **What is confirmed vs not** stated plainly (README table like the client's): not yet
  verified on hardware: WIA scanning, USB camera on Benchy, Stop job, cabin light,
  framing.
- **Machine-specific setup** explained for new users: camera calibration per machine,
  colours per material, source and lens.
- [x] Packaging: per-platform zips (packaging/make_bundle.py), in-place updater, version shown, release notes in the program (CHANGELOG.md), user guide PDF (docs/build_guide.py), release workflow. Still to do: the first tagged release on GitHub, and trying the non-Windows zips.


## Earlier state (MakeIt colour-test era, 2026-09-26)

Done so far: Core engine, 33 passing tests, first app window, `--demo`, `--screenshot`,
and **scan import for MakeIt test cards** (type the card's settings, open a
flatbed scan, click three corner squares, check the suggested results, add to atlas).

Added 2026-09-26 from the Lumos Ultra MOPA Color Engraving Field Guide:
- Importer takes either card kind: Ultra colour test (power × frequency) or Material
  Testing Array (power × speed) - any two of power/speed/frequency, either direction.
- Every card records card ID, focus/Z (also in each cell's settings), stock lot,
  cleaning and MakeIt version; materials record thickness, supplier and SKU.
- *Plan micro-grid around this cell*: power ±5 points, speed ±22.5 %, frequency ±20 %
  (the last is ours, not the guide's), rest locked; odd counts so the centre cell
  repeats the seed. Saved as a planned card that the importer offers.
- Automatic repeatability: a colour is *verified* when the same settings (power ±1 pt,
  speed/frequency ±2 %, rest identical) on another card, measured the same way, come
  out within ΔE00 3. The colour finder ranks verified colours first.
- Not yet clicked through end to end in the main window (import → micro-grid → import
  the planned card → verified). Do that with the first real micro-grid card.
Verified on Lee's first card: mirror stainless, τ 6 ns, 800 mm/s, density 5000,
150-350 kHz x 10 rows, 40-90 % x 20 columns - grid fitted to 1.0 px, tilt handled.

The direction changed on 2026-09-26: **MakeIt's own colour test is the engraving
route** (LightBurn cannot be relied on with the Ultra yet, and per-cell typing in MakeIt
is unacceptable). The Ultra's colour test varies frequency (rows) and power (columns)
with pulse width, speed and line density fixed per card, and engraves its settings on
the card. The atlas's job becomes choosing the next card: its pulse width, speed, line
density and its frequency/power ranges.

Next, in order (several need Benchy):

1. **MakeIt colour-test dialog on Benchy:** which fields it allows, maximum rows and
   columns, and whether frequency/power ranges are free or fixed.
2. **Staged G-code of a colour test** (`%APPDATA%\Wecreat MakeIt!\gcode\gcode.gc`):
   - line density units - is 5000 per cm (a 0.002 mm pitch) or something else?
   - are the per-square power and frequency the exact linear steps or the rounded labels?
3. **Xerox scan settings:** white paper scans as pure 255 - turn off background
   suppression / auto exposure so whites stop clipping.
4. **Planner rewrite for colour-test cards:** propose the next card (τ, speed, density,
   frequency range, power range, rows, columns) instead of free cells. Show it as a
   card of numbers to type into MakeIt.
5. Check the source presets against MakeIt (frequency range, pulse-width list, speed).
6. Measure the red lens spot size.

## Findings to keep (scans, 2026-09-26)

- **Scan orientation changes the colour.** Same card at 0° vs 90°: median ΔE00 2.0 on
  dark squares, 4.9 on coloured ones, up to 8.8. Standard orientation is therefore
  fixed: labels readable, power increasing left to right. The importer assumes it.
- **Mirror finish scans dark and dull:** L* up to ~40, C* up to ~15, far below what the
  eye sees when the card is tilted. Colours are "as scanned" values, comparable only
  with scans made the same way. Stored with method `Scanner`.
- The bare metal between squares is measured automatically and used for "no mark".

## Planned features

- [ ] **Try Send to laser on hardware** (2026-10-01): scrap blank first (framing does not
      trace). Confirm the Start button runs an armed upload, the Idle → Busy → Idle status
      sequence, Stop job, and that the laser-camera photo reads the corner crosses.
- [ ] Engraved human-readable labels on Atlas grids (corner crosses already give placement).

- [ ] Scan colour calibration (IT8 or grey card) once whites stop clipping.
- [ ] Read the card's engraved settings from the scan (OCR) so nothing is typed.
- [x] Auto-detect the grid without clicks (the labels give orientation and extent). Done 2026-09-30: `GridDetect`.
- [ ] Brightened swatch view for dark mirror-finish scans.
- [~] Keep a copy of each imported scan beside the atlas. Camera, laser-camera and pasted captures are now kept in
      `%APPDATA%\MOPAChromaAtlas\captures`; scans opened from a file are referenced by path only.
- [ ] ~~LightBurn project export with per-cell settings~~ superseded by roadmap 7 (LightBurn material library).
- [ ] Gaussian-process surrogate per Lab channel + outcome classifier; expected
      information gain acquisition; predicted-settings suggestions clearly labelled.
- [ ] Atlas browser: plot every measured colour in a*b* and L*C* views, click through
      to the settings; per-material gamut outline.
- [ ] Repeatability cells: re-engrave a few settings on every coupon to catch drift.
- [ ] Open / Save As / merge atlases; share one material's results as a file.
- [ ] Editable parameter space and source/lens profiles in the UI.
- [ ] SQLite storage when JSON gets slow.
- [x] Packaging: DepthView-style per-platform zips, in-place updater, GitHub release
      workflow (2026-10-01). Banner from `Art/` still to do.
- [x] Move to the next .NET LTS (.NET 10, 2026-10-01).
