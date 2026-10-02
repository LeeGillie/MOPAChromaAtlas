# Working on MOPAChroma Atlas

Context for anyone — human or model — picking this up cold. `README.md` is for users,
`TODO.md` is what to do next, `docs/DESIGN.md` is why it works the way it does.

## Layout

```
Art/                         source artwork (icon, splash, banners) - do not edit, derive from it
src/MOPAChroma.Core/         UI-free engine: Color, Laser, Planning, Grids, Atlas
src/MOPAChromaAtlas/         Avalonia desktop app (code-behind, no MVVM framework)
src/MOPAChromaAtlas/Capture/ scanner (WIA / SANE) and capture settings; the camera window is in Views/
tests/MOPAChroma.Core.Tests/ xUnit tests for the engine
docs/                        DESIGN.md, images/
```

## Build, run, test

```powershell
Get-Process MOPAChromaAtlas -EA SilentlyContinue | Stop-Process -Force   # a running exe locks the build
dotnet build MOPAChromaAtlas.slnx -warnaserror --nologo -v minimal
dotnet test  MOPAChromaAtlas.slnx --nologo
dotnet run --project src\MOPAChromaAtlas -- --demo
# README screenshot:
src\MOPAChromaAtlas\bin\Debug\net10.0\MOPAChromaAtlas.exe --demo --screenshot docs\images\main-demo.png
```

The user's atlas lives at `%APPDATA%\MOPAChromaAtlas\atlas.json` (per-user app data on
macOS/Linux). `--atlas <file>` points elsewhere. `--demo` writes to a temp file only.

## Conventions that are load-bearing

- **Plan in physics space, emit MakeIt units.** `Physics.PlanningVector` defines the
  space; `MopaSettings` is always in MakeIt's UI units (line density per CENTIMETRE).
- **Answers are measured, never predicted.** `ColorFinder` returns engraved cells only.
- **Never pool across material, source or lens.** Use `AtlasDocument.SamplesFor`.
- **Samples are immutable once measured.** Replace a Pending placeholder; append a
  re-measure; never edit.
- **Simulated data is always labelled.** Demo samples carry "SIMULATED (demo)" and the
  window title says DEMO. Never let simulated results reach a real atlas.
- **Evidence grades are honest.** Presets are Nominal until checked on the machine.
- **One scan orientation.** Labels readable, power increasing left to right, frequency
  increasing upward (row 0 = top = highest frequency). Scan colour depends on
  orientation by up to ~9 ΔE00, so never mix orientations.
- **Photos are estimates.** Only a flatbed scan is recorded as `Scanner`; anything taken
  with a camera is `Estimated` and says so in its viewing condition. Never relabel one as the other.
- **Scans are never committed** (`samples/scans/` is Lee's). `tools/scan_probe.py` is
  the Python prototype the C# scan reader was built from.
- **Tooltips on every UI element.**
- **Test locally before GitHub.** Nothing is pushed until Lee says so.

## Editing files on Windows

Windows PowerShell 5 `Get-Content`/`Set-Content` mangle UTF-8 without a BOM (·, ², ΔE).
Use an editor tool or `[IO.File]::ReadAllText`/`WriteAllText` with UTF-8.
