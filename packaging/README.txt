MOPAChroma Atlas
================

An Isotope NW tool. Free, open source (MIT), from
https://github.com/LeeGillie/MOPAChromaAtlas

MOPAChroma Atlas finds the laser settings that colour metal on a MOPA fiber
laser, and remembers them. It plans small test grids, sends them to a
WeCreat Lumos Ultra, reads the colours back from a camera picture or a
scan, and builds a catalogue of "these settings made this colour"
for every material you test.

It runs entirely on your own computer. Apart from talking to your laser, the
only thing it ever sends is a once-a-day question to GitHub asking whether a
newer version exists. Nothing about you, your atlas or your files goes
anywhere. If that question fails (no internet), nothing happens.

READ THE USER GUIDE FIRST: MOPAChroma-Atlas-User-Guide.pdf in this folder,
or "User guide" at the top of the program's window.


SAFETY - READ SAFETY.txt FIRST
------------------------------

MOPAChroma Atlas controls a Class 4 laser. Safety is your responsibility:
read SAFETY.txt in this folder before you start. The program shows it the
first time it runs and asks you to accept it.

Atlas never fires the laser by itself: a job is only armed, and runs when you
press Start on the machine. Stay with the machine while it runs, wear eyewear
rated for 1064 nm, use extraction, keep a fire extinguisher at hand, and keep
the machine's stop button within reach. Unofficial software, not made,
endorsed or supported by WeCreat. Provided as is, without warranty; use at
your own risk (see SAFETY.txt and LICENSE.txt).


FEEDBACK AND BUG REPORTS
------------------------

Comments, discussion and ideas: the Facebook group "Lumos Ultra Owners &
Makers - Independent User Group". Bug reports and feature requests: open an
issue at https://github.com/LeeGillie/MOPAChromaAtlas/issues (or "Report a
problem" at the top of the program's window).


WHAT'S IN THIS FOLDER
---------------------

  Windows   MOPAChromaAtlas.exe               the program
  Mac       MOPAChromaAtlas.app               the program
  Linux     MOPAChromaAtlas                   the program
            mopachroma-atlas.png              its icon
            install-menu-entry.sh             adds Atlas to your application menu
  All       MOPAChroma-Atlas-User-Guide.pdf   the user guide
            README.txt                        this file
            SAFETY.txt                        safety, risks and disclaimer
            LICENSE.txt                       the MIT licence
            THIRD-PARTY-NOTICES.txt           open-source components inside

Nothing else is needed. The .NET runtime is built into the program, so there
is nothing to install first.

Keep the files together in this MOPAChromaAtlas folder, somewhere you can
write to: Documents, your home folder, or a tools folder all work. Avoid
C:\Program Files on Windows: Atlas cannot update itself there.

UNZIP FIRST. On Windows, right-click the zip, choose "Extract All...", and
pick where the folder should go. Double-clicking a zip only shows what is
inside it, and the program cannot run from there.


STARTING IT
-----------

Windows   Double-click MOPAChromaAtlas.exe. The first time, Windows may show
          a blue "Windows protected your PC" box, because the program is free
          and not code-signed. Click "More info", then "Run anyway".
          For a desktop shortcut: right-click MOPAChromaAtlas.exe, choose
          Show more options > Send to > Desktop (create shortcut).

Mac       Drag MOPAChromaAtlas.app into Applications if you like (or leave
          it in this folder), then double-click it. The first time, macOS
          will refuse because it is from an unidentified developer. Open
          System Settings > Privacy & Security, scroll down, click
          "Open Anyway" next to the message about MOPAChromaAtlas, and
          confirm. If macOS instead says the app "is damaged", open Terminal
          and run
              xattr -dr com.apple.quarantine /path/to/MOPAChromaAtlas.app
          then double-click it again. It is not damaged; that is how macOS
          words the quarantine on unsigned downloads. macOS asks once before
          Atlas may use a camera or reach the laser on your network: allow
          both.

Linux     Double-click MOPAChromaAtlas in your file manager, or in a
          terminal in this folder run   ./MOPAChromaAtlas
          To add it to your application menu, run   ./install-menu-entry.sh
          (./install-menu-entry.sh --remove takes it out again).
          Needs a desktop with X11 or XWayland, and the fontconfig, libX11,
          libICE and libSM libraries, which almost every desktop already has.


THE LASER
---------

Connect the Lumos Ultra by USB (Atlas uses the laser's USB address,
192.168.42.1, filled in for you) or give Atlas the laser's Wi-Fi address.
Close MakeIt while Atlas is talking to the laser.

Before the first job, calibrate the laser camera (User guide, Recipe 2).


UPDATES
-------

When Atlas starts it asks GitHub, at most once a day, whether a newer
version has been released. If there is one, a green bar at the top says so:

  - "Update now" downloads the new version, checks it against the checksum
    GitHub publishes for it, starts it once to be sure it runs, then
    replaces the program in this folder and restarts. If any check fails,
    nothing is changed. Your atlas is kept in your user profile, not in this
    folder, so an update never touches it.
  - "What's new" opens the release notes.
  - "Skip this version" hides the bar until the next version comes out.

"Check for updates" at the top of the window asks straight away. "What's new"
there shows the release notes for this version and earlier ones at any time.

To update by hand instead, download the zip for your computer from
https://github.com/LeeGillie/MOPAChromaAtlas/releases/latest
and unzip it over this folder, replacing the files.

Which zip is yours:

  Windows 10/11, ordinary PC                  MOPAChromaAtlas-...-win-x64.zip
  Windows on ARM (Surface Pro X, Snapdragon)  MOPAChromaAtlas-...-win-arm64.zip
  Windows, 32-bit                             MOPAChromaAtlas-...-win-x86.zip
  Mac with Apple silicon (M1 and later)       MOPAChromaAtlas-...-osx-arm64.zip
  Mac with an Intel processor                 MOPAChromaAtlas-...-osx-x64.zip
  Linux, ordinary PC                          MOPAChromaAtlas-...-linux-x64.zip
  Linux on ARM (Raspberry Pi 4/5)             MOPAChromaAtlas-...-linux-arm64.zip


CHECKING A DOWNLOAD
-------------------

Atlas is not code-signed, so the checksums are how you confirm a download is
the file the release actually built. SHA256SUMS.txt is attached to every
release:

  macOS / Linux   sha256sum -c SHA256SUMS.txt --ignore-missing
  Windows         certutil -hashfile MOPAChromaAtlas-...-win-x64.zip SHA256
                  (compare with the line for that file by eye)

The in-place update does this check for you.


WHERE YOUR WORK IS KEPT
-----------------------

The atlas, camera calibration, preferences, and the pictures and jobs Atlas
keeps for every grid:

  Windows       %APPDATA%\MOPAChromaAtlas
  Mac, Linux    ~/.config/MOPAChromaAtlas

Back that folder up: it is your whole catalogue.


REMOVING IT
-----------

Delete this folder (on a Mac, MOPAChromaAtlas.app). Your atlas stays in the
folder above until you delete that too. On Linux, run
./install-menu-entry.sh --remove first if you added the menu entry.


LICENCE
-------

MIT - see LICENSE.txt. Free to use, copy and share, with no warranty. The
program includes third-party components under their own licences (Avalonia,
ImageSharp, FlashCap, FluentFTP); see the project page.
