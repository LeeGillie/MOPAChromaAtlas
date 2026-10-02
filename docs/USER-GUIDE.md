# MOPAChroma Atlas — User Guide

MOPAChroma Atlas finds the laser settings that colour metal, and remembers them. You let
it plan small test grids, the laser engraves them, the laser's camera (or a scanner)
reads the colours back, and every result goes into your **atlas**: a growing catalogue of
"these settings, on this material, made this colour". Later you pick a colour and the
atlas tells you how to make it.

This guide has three parts: the **ideas** behind it (short), **recipes** for every job
you will do (step by step), and **tips and troubleshooting**.

Written for the WeCreat Lumos Ultra MOPA fiber laser, the machine Atlas is developed on.

---

## Part 1 — The ideas, simply

### How a laser colours metal
A MOPA fiber laser does not paint. It heats the surface so a very thin, clear oxide layer
grows on it, and the colour you see is light bouncing between the top and bottom of that
layer, like a soap bubble or an oil film. A thinner layer looks straw or gold; as it
gets thicker it goes brown, purple, blue, and on to further shades. So **every colour is
a thickness of oxide**, and the thickness comes from how much heat each spot gets and how
fast.

That heat is set by six knobs you will recognise from MakeIt: **power, speed, line
density, frequency, pulse width and passes**. Many different combinations give the same
heat, and most combinations give nothing useful (no mark, or a burnt surface). Testing
them one at a time wastes a lot of metal. Atlas plans the tests for you.

### Material, source and lens
A result only means something for the metal it was made on, the laser that made it, and
the lens. So everything in the atlas is filed under:
- **Material**: the metal *and* its finish and form: 304 mirror stainless sheet is a
  different material from brushed 304, and from a 304 business card (a big sheet sinks
  heat differently from a small card or a thick coin).
- **Source**: the laser itself (e.g. the Ultra's 60 W or 100 W MOPA source).
- **Lens**: the lens fitted when it was engraved.

Results are never mixed between materials, sources or lenses.

### Grids and cells
A **grid** is one test card: a block of small squares (**cells**), each engraved with
different settings. Atlas plans three kinds:
- **Seed grid** — the first grid for a new material. Its cells are spread as far apart
  as possible across all the possible settings, to survey the whole map at once.
- **Refinement grid** — every grid after that. Atlas looks at what came back and puts
  cells where they teach the most: where colours change between neighbours, along the
  edge between "marks" and "no mark" or "burnt", in untested areas, and towards new
  colours (near the rarest colours so far, and a little hotter than any good cell yet).
  Each cell's reason is shown when you click it. It never repeats a setting already
  engraved, and skips settings it can already tell will leave no mark or burn.
- **Micro-grid** — a fine sweep around one cell you liked: power and speed varied a little
  either side of it (everything else kept), the nearest untested settings first. Its first
  cell repeats your chosen cell, to check the colour comes out the same twice; nothing else
  already engraved is repeated. It is only as big as its cells need (often a row or two),
  not the full test-grid size.

You can engrave a micro-grid and then go back to any earlier grid and plan another one
around a different cell: each grid stays in the list, and every new grid gets the next number.

Every grid gets a **number** (grid 1, 2, 3… in the order they were planned), shown in its
title and engraved on the card as a small row of dots (see *Corner crosses*).

### Planning ranges and the heat limit
Under **Planning ranges** (left of the main window) you set, **per material**, the span of
each setting the planner may use: power, frequency, pulse width, speed, line density, the
most passes (1, 2 or 3), and the least heat per area worth a cell. Every planned cell stays
inside them. The starting ranges (power 30–90 %, frequency 140–400 kHz, pulse width 4–7 ns)
keep tests in colour-making territory; **Starting ranges** puts them back.

To get something like MakeIt's own colour test, set pulse width 6 to 6, speed 800 to 800,
line density 5000 to 5000 and passes 1: only power and frequency are then varied.

On top of the ranges, the **heat limit** always applies (see *Safety*): no cell is planned
that would put enough heat into the metal to cut or burn it. A micro-grid around a good cell
that lies outside the ranges (an older grid, or ranges changed since) still works: the ranges
are stretched just enough to include that cell, never the heat limit.

### What each cell turned out as
Each cell gets an **outcome**:
- **Good** — an even colour.
- **No mark** — nothing happened (too little heat). This is useful: it tells the
  planner where not to look.
- **Uneven** — a mark, but blotchy or streaky.
- **Damaged** — burnt, rough or discoloured beyond the oxide colours (too much heat).

Atlas suggests each outcome from the picture; you can click to change any.

### Colour readings and how much to trust them
A colour is only as good as how it was measured, so each reading records its **method**:
- **Estimated** — from a photo: the laser's camera, a USB camera, a pasted picture.
  The laser's camera is good for a first look; its bright cabin light and a mirror finish
  wash colours out, so a blue may read as grey. A good USB camera, with the light and the
  card at an angle you choose, gives much truer colour (see *Recipe 5*).
- **Scanner** — from a flatbed scan. Every card is lit the same way, so scans compare
  with each other, but on brushed or polished metal a scanner's straight-on light can make
  oxide colours look washed out and flat. Try one card before relying on it.
- **Instrument** — from a colorimeter or spectrophotometer, if you have one.

A colour is **verified** when the same settings, engraved on another card and measured
the same way, came out the same (within ΔE00 3: barely distinguishable). Verified
colours are the ones to trust; Atlas lists them first.

### The laser camera and its calibration
The Ultra has a camera looking at the bed. Once it is **calibrated**, Atlas knows where
every point of the bed appears in the camera's picture. That lets it:
- draw a job on the picture *before* engraving, so you can drag it onto your blank;
- put the grid straight onto the engraved squares when the finished card is read.

You calibrate once (it engraves nine small + marks on a scrap and you click them). Do it
again only if the camera or lid is moved, or if you change how the picture is turned.

### Corner crosses
Atlas engraves three tiny crosses just outside each grid (above the top corners and below
the bottom-left; never at the bottom-right). The weakest cells often leave no mark at
all, so without the crosses a picture of the card could not show where the grid starts
or which way up it is. With them, Atlas finds the grid by itself in any scan or photo.

Between the two top crosses is a short row of dots: the grid's **number**, as a code Atlas
reads back. A dot at each end, the number, and a check so a misread is rejected rather than
taken for another grid. It is what lets Atlas recognise every grid on a scan of a whole
sheet. (Grids too narrow for the dots, and grids engraved before this, have no number on
the card; they are read one at a time.)

Beside the bottom-left cross the grid's name is engraved in small letters, such as
**Grid 29**, so you can tell the cards apart by eye. It only appears on grids that also carry
the dots. When a grid's first columns left no mark, Atlas uses the dots to be sure which
column the grid starts at, so the letters are never taken for a corner cross.

### The atlas file
Everything lives in one file, `atlas.json`, in your user folder
(`%APPDATA%\MOPAChromaAtlas` on Windows, `~/.config/MOPAChromaAtlas` on macOS and Linux),
never in the program's own folder, so updating or removing the program leaves it alone. **Start a new atlas…** (top of the main window)
puts the current one aside, renamed with today's date (`atlas-until-….json`, nothing is
deleted), and opens an empty one. Camera settings and the calibration are kept
separately beside it, in `capture-settings.json`. Pictures taken by Atlas are kept in
`captures`, and the exact job sent for every grid in `jobs`, so every result can be
traced back to a picture and a job.

---

## Part 2 — Recipes

### Recipe 0 — Install, update, and know your version
1. **Download** the zip for your computer from the project's *Releases* page on GitHub
   (`github.com/LeeGillie/MOPAChromaAtlas/releases/latest`): `win-x64` for an ordinary
   Windows PC, `osx-arm64` for a Mac with Apple silicon, `linux-x64` for an ordinary Linux PC
   (the README lists all seven).
2. **Unzip it** somewhere you can write to, such as Documents. On Windows: right-click the zip,
   *Extract All…*. Avoid `C:\Program Files`: Atlas cannot update itself there.
3. **Start it**: `MOPAChromaAtlas.exe` on Windows, `MOPAChromaAtlas.app` on a Mac,
   `MOPAChromaAtlas` on Linux. It is free and not code-signed, so the first start asks once:
   on Windows click *More info* → *Run anyway*; on a Mac use *System Settings → Privacy &
   Security → Open Anyway*. The `README.txt` beside the program has the details.
4. **The first time it starts**, Atlas shows its safety notice and disclaimer: read it, tick
   the box and press **Accept** (or **Quit**). It is always under *Safety* at the top of the
   window.
5. **Your version** is shown under the title at the top of the window. **What's new** beside it
   shows the release notes for this version and earlier ones (they are built in, so this works
   offline). **User guide** opens this guide.
6. **Updates come to you.** Once a day Atlas asks GitHub whether a newer version exists. If
   there is one, a green bar appears: **Update now** downloads it, checks it, and restarts
   Atlas on the new version; **Skip this version** hides it. Your atlas is not touched. If
   the computer is offline, nothing happens. **Check for updates** asks straight away.

### Recipe 1 — First-time setup
1. **Connect the laser** to the PC by USB and switch it on. Atlas talks to it at
   `http://192.168.42.1:8080` (the USB address); this is filled in for you.
2. **Add your material** (left of the main window, *Material* → *Add a material*): name,
   grade, finish, **form** (sheet, card, coin, tag…), thickness, supplier and product
   code. One material per kind of blank.
3. **Check the laser and lens** under *Laser*: the source and the lens fitted.
4. **Set the test grid** under *Test grid*: the blank's width and height, cell size and
   gap. Atlas fits as many cells as the blank allows.
5. **Calibrate the camera** — Recipe 2. Atlas will not send jobs until this is done,
   because without it there is no way to show where a job will land.

### Recipe 2 — Calibrate the laser camera (once)
You need: a scrap that covers most of the bed. A sheet of cardboard is fine.
1. Open *Send to laser…* on any planned grid and press **Calibrate camera…** (or the
   button in the orange panel).
2. **Step 1 of 2.** Lay the cardboard over the bed at your normal engraving height and
   focus. Press **Send and arm**, then **Start** on the laser. Nine small + marks are
   engraved, each with 1 to 9 short bars beside it (about a minute).
3. When it finishes, Atlas takes a picture and moves to **Step 2 of 2**.
4. If the picture is upside down or mirrored, use **⟳** and **⇋** first: the calibration
   belongs to the way the picture is turned.
5. Click the **centre of each + mark**, in any order. Scroll to zoom in for precision;
   drag to move around. Each click gets a ring; once Atlas recognises a mark (after about
   five clicks) the ring turns **green** with that mark's number. A **red ring with ?**
   is a click that missed a mark — *Undo last click* and try again.
6. When all nine are green it says **Good**. Press **Save calibration**.

### Recipe 3 — The main loop: plan, engrave, read
You need: a blank of your material on the bed, and a calibrated camera.
1. **Plan a grid.** Pick the material, check its *Planning ranges*, then **Plan seed
   grid** (first grid for a material) or **Plan refinement grid** (every grid after that).
   The new grid appears under *Grids for this material*.
2. **Send to laser…** with the grid selected. The laser camera's picture appears with
   the job drawn on it, labelled **will engrave here**.
3. **Place it.** Drag the job onto your blank (or type the job centre X/Y). Press
   **Take picture** for a fresh view to check; it keeps your zoom and position. Keep it
   clear of earlier grids. The preview shows the whole job: cells, corner crosses, the
   number dots and the *Grid N* label, inside a dashed outline. The engraving time is shown
   too (hours, minutes and seconds).
4. Leave **Engrave corner crosses** and **photograph … and read it** ticked.
5. **Send and arm.** Atlas checks the door is closed and sends the job. Nothing fires
   yet.
6. Press **Start** on the laser. (A *Start from here* button exists, behind a safety
   tick; the machine's own button is better, because you are standing at the laser.)
7. Atlas follows the job. When it finishes it photographs the card and opens the
   **reader** with the grid already on the engraved squares.
8. **Check.** Each box should sit on its square; zoom in if unsure. The panel on the
   right shows each cell's colour and suggested outcome — click a swatch to change it
   (Good → No mark → Uneven → Damaged).
9. **Add to atlas.** Back in the main window, plan the next refinement grid and repeat.

A large sheet works well: one grid after another, each placed beside the last. Leave at
least a cell's width between one grid's crosses and the next grid, so a scan of the sheet
can tell them apart (Recipe 5b).

### Recipe 4 — When the grid is not on the squares
The grid is the set of boxes Atlas draws over the picture, one per cell: each box is where
that cell's colour is read, so every box has to sit on its engraved square. When Atlas
cannot find the card by itself, it puts the grid in the middle of the picture and you place it:
1. **Move it:** press inside the boxes (not on a round handle) and drag, until the boxes
   roughly cover the card's squares.
2. **Fit it:** each corner box has a round yellow handle. Drag the top-left handle onto the
   middle of the card's top-left square, then the top-right and bottom-left ones onto
   theirs; the bottom-right follows. A corner cell that left no mark still counts: put the
   handle where that square would be (the corner crosses show where the grid ends).
3. **Check:** scroll to zoom in; every box should sit on a square.

Also in the reader:
- **Arrow keys** move it one square at a time. **Turn 180°** flips it if the card is the
  other way up.
- **Snap to squares** nudges the boxes onto the square centres nearby, but only if that
  keeps them close to where you put them.
- **Find automatically** lets Atlas search the picture by itself.
- **Undo** (Ctrl+Z) reverses any of these.
- **Crop to card** — drag a box around the card to ignore the rest of the picture.
- **Darker / Brighter / Stretch contrast** — make a washed-out picture easier to see.
  These change only what you see, never what is measured.
- **Place by hand** — a fresh grid in the middle of the view, to drag into place.

### Recipe 5 — Better colour: read a card from a camera or a scan
Oxide colours change with the angle of the light and of your view, so the best picture is
the one where the card looks the way you see it. On Lee's stainless and titanium a **USB
camera** (a YoloLiv S3) gave far better colour than a flatbed scanner, whose straight-on
light made the colours washed out and flat: set up a lamp to one side, tilt the card or the
camera until the colours look right, lock the camera's white balance and exposure, and keep
that set-up for every card. A flatbed scan (600 dpi, colour, PNG or TIFF) is worth trying on
your metal, but check it against the real card.
1. Select the grid in the main window and press **Read this grid from an image…**.
2. Bring the picture in any of these ways: **Open file…**, drag and drop, **Paste**,
   **Scan…** (your scanner's own window opens), **USB camera…** (live preview; lock white
   balance and exposure in *Camera settings…*), **Laser's camera**, or **Scan folder…**
   (pick the folder your scanner software saves into, and new scans open by themselves).
3. Set **This image is a** to *Flatbed scan* or *Photo*.
4. The grid is found automatically thanks to the corner crosses and its number (in a
   picture of a whole sheet, Atlas picks the grid whose number matches); check it as in
   Recipe 4, then **Add to atlas**. If it is not found, **Crop to card** round just this
   card and press **Find automatically**, or place it by hand (Recipe 4).

### Recipe 5b — Read a whole sheet in one picture
The laser camera gives a quick first reading; a better picture gives the colours to trust.
When a sheet is full (or whenever you like), take one picture of it and Atlas looks for every
grid on it.

**Known limitation (0.1):** finding grids by themselves works on a sharp, high-resolution
picture where the crosses and number dots show clearly (it was proven on a scan of a titanium
sheet), but on Lee's camera photos it did not find them. When that happens, read each grid
with Recipe 5 and place the grid by hand (Recipe 4): that works reliably. Better recognition
of camera photos is planned for a later release.

1. Scan the whole sheet on a flatbed (600 dpi, colour, PNG or TIFF, any way up), or take a
   sharp photo of it with a camera held square to the sheet. Other cards on the sheet, such
   as a MakeIt colour test, do not get in the way.
2. In the main window press **Read every grid on a scanned sheet…** and open the scan.
   (It does not matter which material or grid is selected.)
3. Atlas finds each grid, reads its number from the dots, and outlines it on the picture
   with its number. The list on the right shows what was found; grids whose number could
   not be read, or that belong to another atlas, are skipped and counted.
4. **This picture is a flatbed scan** should be ticked for a scan (it is, for most scanner
   files) and unticked for a photo, so photo colours are kept as estimates.
5. **Save readings.** Each cell is recorded as the reader judged it. Earlier readings are
   kept; the palette and exports use the latest one (an instrument reading, if any, comes
   first), so a better picture taken later replaces an earlier one. To correct a cell, click it in the main window and use *Correct by hand*.

Grids engraved before numbering existed, or without corner crosses, are not found this
way: read them one at a time with Recipe 5.

### Recipe 6 — Zoom in on a colour you like: micro-grid
1. In the main window, click the cell you like in its grid (it must have a colour).
2. Press **Plan micro-grid around this cell**. A new grid appears with the next number and
   is selected: check its title says *Micro-grid around …* before sending.
3. Engrave and read it like any other grid (Recipe 3). It is sized to its cells, so it
   fits in a corner of the sheet.

If nothing can be planned (every setting around the cell is engraved already, or over the
heat limit), a window says so and why, and the grid you came from stays selected: do not
press *Send to laser* expecting a micro-grid.

### Recipe 7 — Find the settings for a colour
*Find settings for a colour* (bottom of the main window):
1. **Pick the colour** you want with the colour picker, or click one of the swatches of
   colours you have already made on this material.
2. Press **Find closest measured colours**. The list shows the measured cells closest to
   it, for the selected material, source and lens: verified colours first, and only cells
   that actually have a colour reading (no-mark cells never appear).

If you have a colour as numbers from a colorimeter, open *Exact L\*a\*b\* target*.

### Recipe 8 — Your palette, and using your colours in MakeIt or LightBurn
1. Pick the material (and laser and lens) in the main window and press **Palette and
   exports…**. Every colour you have made appears as a swatch with its recipe (only cells
   read as **Good**, each with its latest reading, or an instrument reading if there is one). **✓** marks a
   colour that came out the same on two cards.
2. Choose what to export. **Repeated (verified) colours only** shows how many colours that
   is (for example *3 of 187*), and is ticked to begin with only when there are some. A bold
   line says how many colours the exports will hold; the save buttons are greyed out when
   that would be none.
3. Every save asks where to put the file. The Save box starts in
   *Documents\MOPAChroma Atlas* (then wherever you saved last); afterwards the window shows
   the full path and a **Show the file in its folder** button.

**In MakeIt: your colours as materials (the main way).**
1. **Save MakeIt materials…** writes every colour as a MakeIt material, in one `.json`
   file. Each is named after its square in the palette ("Stainless steel 1.3 Muted blue"),
   carries the colour's recipe on the Fill tab (power, speed, passes, pulse width,
   frequency, line density), and shows the colour as its picture.
2. In MakeIt: **Material preview** → **Import** → choose the file (a large file takes a
   little while). The colours appear under **Customization**, beside your own materials,
   which are left alone.
3. Select your artwork and pick the colour from the **Material** list: every Fill setting
   is filled in, and the colour shows under *Preview*. The pen icon on a material opens
   MakeIt's editor for it.
4. Export and import again whenever your atlas grows: colours already in MakeIt are updated
   in place (never duplicated), new ones are added. Colours you dropped from the atlas are
   not removed from MakeIt; delete those there by hand.

**In MakeIt: a palette to look at.** **Save MakeIt palette…** writes a MakeIt project
(`.wws`) with one square per colour, filled in that colour, labelled under it (row.column),
and already set to its recipe; plus a `.txt` list of every square with its recipe. Squares
are in colour order (greys and silvers first, then reds round to purples, light to dark in
each); they are 10 mm, smaller beyond 100 colours so all fit inside the lens field, and past
225 colours further pages go in `…_2.wws` and so on. Open it with *File* → *Open*, and choose
the Lumos Ultra and its **MOPA** module at the top left first (without them MakeIt crosses
out speed and line density and hides frequency and pulse width). It is a colour key, and
each square can also be copied into a design. The labels are set to 0 % power; delete them,
or tick *Ignore for processing* on them, before engraving the palette itself.

**In LightBurn.** **Save LightBurn library…** writes a material library (`.clb`), one entry
per colour named by colour (e.g. "Deep blue · 250k/6ns · A1B2C3"), with a `.json` file
beside it holding every colour's full record. In LightBurn: *Library* → *Load* (or *Merge*
into your own library), then **Assign** an entry to a layer, or **Link** it so the layer
follows the library. Entry names never change once made, so linked layers keep working when
a colour is re-measured. *Not yet tried with the Ultra:* whether LightBurn sends pulse width
and frequency to it.

Focus (Z) is not part of any export: set it yourself when a colour needs it. Recipes repeat
only on the same material, laser source and lens. MakeIt 3.0.6 saves its own projects
encrypted, and Atlas never touches that: it writes MakeIt's plain material file and the
older plain project format, both of which MakeIt 3.0.6 opens.

### Recipe 9 — Reading a MakeIt colour-test card
For cards made with MakeIt's own colour test (the ones with value labels along the
edges): **Import MakeIt test-card scan…**, choose *Ultra colour test* or *Material test
array* and type the card's settings as shown on it, bring in the picture as in Recipe 5,
check, and add to the atlas.

---

## Part 3 — Tips, troubleshooting and safety

### Good habits
- **One material per kind of blank**, even for the same steel: a sheet, a card and a coin
  colour differently with the same settings.
- **Engrave at the height you calibrated at.** A thicker blank sits closer to the camera
  and shifts slightly in the picture, more towards the edges of the bed.
- **Take pictures with the lid closed**, as during calibration.
- **Use the laser camera for placing, a good USB camera (or a scan, if it suits your metal)
  for colour**, with the same light and angle for every card.
- **Let the planner work.** No-mark and damaged cells are not failures: they teach the
  planner where the useful region ends.
- **Keep your cards**, labelled with their grid number. Every colour in the atlas should
  trace back to a real piece of metal.

### Troubleshooting
| What you see | What to do |
|---|---|
| *Send and arm* is greyed out | Take a picture (top right of *Send to laser*) so you can see where the job will go. Calibrate the camera first if asked. |
| The job engraved somewhere other than shown | Recalibrate the camera (Recipe 2). Check the lid was closed and the camera had not moved. |
| The camera picture is upside down or mirrored | **⟳ Turn picture** / **⇋ Mirror** in the reader or calibration window; it is remembered. Then recalibrate. |
| The reader's grid is off the squares | Recipe 4. |
| Colours look washed out | Use *Stretch contrast* to see them; for real colour readings, use a USB camera with the light at an angle (Recipe 5). A flatbed scanner can wash oxide colours out too. |
| USB camera preview stays black | The status line says why: no frames (close other apps using the camera, try another format) or frames that cannot be decoded (try another format). |
| A sheet picture finds no grids, or misses some | Only Atlas grids with corner crosses and number dots are found, and only when they show clearly; camera photos often miss (a known limitation in 0.1). Read each grid with Recipe 5 and place it by hand (Recipe 4). |
| Finding the grid in a photo fails | Place it by hand (Recipe 4): drag the boxes over the squares and the corner handles onto the corner squares. *Crop to card* first sometimes lets *Find automatically* work. |
| *Send to laser* shows the old grid, full size, after planning a micro-grid | The micro-grid was not planned: the window that appeared says why. Check the grid title before sending. |
| The export buttons in *Palette and exports* are greyed out | Nothing to export with the current choice: usually *Repeated colours only* is ticked and no colour has been repeated yet. Untick it. |
| MakeIt shows the palette's speeds and line densities crossed out, and no frequency or pulse width | Choose the Lumos Ultra and its MOPA module at the top left of MakeIt. |
| Imported MakeIt materials do not show | Look under *Material preview* → *Customization*, at the end of the list. |
| *Update now* says the copy cannot replace itself | It was moved out of its folder, renamed, or unzipped somewhere read-only. Use *Download page* and unzip the new version over the old folder. |
| *Stop job* does not stop the laser | Use the stop button on the machine; it always works. |

### Safety
**Read the safety notice** (*Safety* at the top of the main window; `SAFETY.txt` beside the
program; shown and accepted on first start). Atlas controls a Class 4 laser: safety is your
responsibility, and the software comes as is, without warranty.

The Ultra is a Class 4 laser. Atlas never fires it by itself:
- a job is only **armed**; it runs when you press **Start** on the machine (or tick the
  safety box and press *Start from here*);
- the **door must be closed** before a job is sent;
- a job cannot be sent without first **seeing where it will engrave** on the camera
  picture.
- **no spot may get too much heat.** Before any job is sent, Atlas reads the job itself and
  adds up the energy every square millimetre of the bed would receive, all passes included.
  If any spot would get more than 45 J/mm², the job is refused and nothing is sent. The
  planner also never plans a cell above 40 J/mm², or more than half again past the hottest
  cell that came out Good, or past the first Damaged one. Colour never needs the heat that
  cuts metal.

These are aids, not guarantees: they depend on the machine, the calibration and software that
may contain mistakes. Stay with the machine while it runs, use eyewear rated for 1064 nm and
proper extraction, keep a fire extinguisher at hand, and keep the machine's own stop button
within reach. Test new settings on scrap first.

### Feedback, help and bug reports
- **Comments, discussion, questions and ideas:** the Facebook group *Lumos Ultra Owners &
  Makers — Independent User Group*.
- **Bug reports and feature requests:** an issue on GitHub
  (`github.com/LeeGillie/MOPAChromaAtlas/issues`, or **Report a problem** at the top of the
  main window). Say what you did, what happened, your computer and Atlas version, and attach
  a picture if it helps.

---

## Glossary
- **Atlas** — your catalogue of results; one file per user.
- **Cell** — one test square on a grid.
- **Grid** — one test card of cells (seed, refinement or micro), numbered in order.
- **Grid number dots** — the short row of dots between the top crosses: the grid's number,
  read back from scans.
- **Corner crosses** — the three marks beside every Atlas grid that let pictures of it be
  read automatically.
- **Calibration (camera)** — the link between bed positions and the laser camera's
  picture.
- **Outcome** — Good, No mark, Uneven or Damaged.
- **Planning ranges** — per material, the span of each setting the planner may use.
- **Micro-grid** — a small grid of settings close to one cell you liked, plus a repeat of it.
- **MakeIt material** — a named set of MakeIt settings with a picture; Atlas exports each
  colour as one.
- **Method** — how a colour was read: Estimated (photo), Scanner, Instrument.
- **Verified** — the same settings gave the same colour on another card.
- **ΔE00** — the standard measure of colour difference; about 1 is just noticeable,
  3 is a small but visible difference.
- **L\*a\*b\*** — a standard way of writing a colour as three numbers: lightness (L\*),
  green–red (a\*) and blue–yellow (b\*).
