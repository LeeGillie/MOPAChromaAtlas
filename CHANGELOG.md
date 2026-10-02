# Release notes

<!-- Newest first. One "## x.y.z — title" section per release, written for people who use
     Atlas, not for developers. The program shows these (What's new), and the release page
     leads with the newest section. Update before tagging: the build checks the newest
     section matches <Version> in Directory.Build.props. -->

## 0.1.0 — first public release

MOPAChroma Atlas plans colour test grids for MOPA fiber lasers, engraves them on the WeCreat
Lumos Ultra, reads the colours back, and builds a catalogue of "these settings made this
colour" for every material you test.

- **Plan, engrave, read.** Seed, refinement and micro-grids are planned for you and never
  repeat a setting. **Send to laser** draws the job on the laser camera's picture so you can
  drag it onto your blank; you press Start on the machine; the finished card is photographed
  and read automatically.
- **Planning for colour.** Every setting can vary (power, speed, frequency, pulse width, line
  density, passes), within **planning ranges** you set per material. Refinement grids put
  cells where colour changes, where the usable region ends, and towards colours you do not
  have yet. **Micro-grids** sweep around a cell you like, repeat it once, and are only as
  big as they need to be.
- **Numbered cards.** Every grid engraves its number as a small row of dots.
  **Read every grid on a scanned sheet** finds and reads all of them in one sharp picture
  (even beside other cards); on camera photos it often misses in this release, and placing a
  grid by hand on the picture always works. Better readings replace the laser camera's
  first estimates; a USB camera with angled light gave truer colour than a flatbed scanner. Each card also carries its name ("Grid 29") in the
  margin, for people, and the job preview shows both.
- **Hard heat limit.** Nothing is planned or sent with more heat per area than Atlas allows
  (40 J/mm² at most, and no more than half again past the hottest cell that came out well,
  or past the first damaged one). Send to laser refuses any grid with a cell above it.
- **Use your colours in MakeIt.** **Save MakeIt materials** turns every colour into a MakeIt
  material: import it once, then pick a colour by name from MakeIt's Material list and every
  setting is filled in. Importing again updates them. A **MakeIt palette** project (a
  labelled square per colour, in colour order) is the visual key. Also: pick a colour to get
  the settings that made it, and a **LightBurn material library** (not yet tried with the Ultra).
- **Keeps itself current.** The version is shown in the main window, release notes are one
  click away, and when a new release is out Atlas offers to update itself in place.
- **User guide** included as a PDF, and in the program under *User guide*.

Tested on the authors' Lumos Ultra on Windows 11. See *Supported platforms* in the README for
what is confirmed elsewhere.
