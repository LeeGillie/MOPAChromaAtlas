# Third-party notices

MOPAChroma Atlas is MIT-licensed (see LICENSE). Its release builds include the following
open-source components, each under its own licence. Their copyright notices and licence texts
are in each package, available from NuGet (nuget.org) and the projects' own sites.

| Component | Used for | Licence |
|---|---|---|
| .NET runtime and libraries (Microsoft) | the program's runtime, built into the release | MIT |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Controls.ColorPicker (AvaloniaUI) | windows and controls | MIT |
| Avalonia.Fonts.Inter (AvaloniaUI), with the Inter typeface (Rasmus Andersson) | the user-interface font | MIT; the Inter font itself under the SIL Open Font License 1.1 |
| SkiaSharp, HarfBuzzSharp (Microsoft / .NET Foundation), with Skia (Google) and HarfBuzz | drawing and text, used by Avalonia | MIT (SkiaSharp, HarfBuzzSharp); BSD-3-Clause (Skia); "Old MIT" (HarfBuzz) |
| MicroCom.Runtime, Tmds.DBus.Protocol | platform plumbing, used by Avalonia | MIT |
| SixLabors.ImageSharp (Six Labors) | reading and writing pictures | Six Labors Split License 1.0: used here under the Apache License 2.0, because MOPAChroma Atlas is open-source software |
| FlashCap (Kouji Matsui) | USB cameras | Apache License 2.0 |
| FluentFTP (Robin Rodricks and contributors) | sending jobs to the laser | MIT |

If you build a product of your own from this source, check these licences for your use; in
particular ImageSharp's Split License sets conditions for closed-source commercial use.
