---

## Which file do I want?

| You are on | Download |
|---|---|
| Windows 10/11, ordinary PC | `MOPAChromaAtlas-*-win-x64.zip` |
| Windows on ARM (Surface Pro X, Snapdragon) | `MOPAChromaAtlas-*-win-arm64.zip` |
| Windows, 32-bit | `MOPAChromaAtlas-*-win-x86.zip` |
| Mac with Apple silicon (M1 and later) | `MOPAChromaAtlas-*-osx-arm64.zip` |
| Mac with an Intel processor | `MOPAChromaAtlas-*-osx-x64.zip` |
| Linux, ordinary PC | `MOPAChromaAtlas-*-linux-x64.zip` |
| Linux on ARM (Raspberry Pi 4/5) | `MOPAChromaAtlas-*-linux-arm64.zip` |

The **user guide** is in every zip, and on its own as `MOPAChroma-Atlas-User-Guide.pdf`.

Each zip holds one **MOPAChromaAtlas** folder: the program with the .NET runtime inside it,
the user guide, a `README.txt` that walks through starting, updating and removing it, and
the licence. On a Mac the program is a proper `MOPAChromaAtlas.app`; on Linux there is an
optional script that adds it to your application menu. No installer, no dependencies, no
administrator rights. Delete the folder and Atlas is gone; your atlas stays in your user
profile until you delete it too.

**Unzip it first**, somewhere you can write to: Documents or your home folder, not
`C:\Program Files`. On Windows, right-click the zip and choose *Extract All…*.

Supported platforms, and what has been confirmed on each:
[README](https://github.com/LeeGillie/MOPAChromaAtlas#supported-platforms).

## Updating

Atlas asks GitHub once a day whether a newer release exists and shows a green bar when there
is one. **Update now** downloads the zip for your computer, checks it against the checksum
GitHub publishes, starts the new copy once to be sure it runs, then replaces the program and
restarts. If any check fails, nothing is changed. If the check itself fails (no internet),
nothing happens at all.

## These builds are not code-signed

Atlas is a free tool and there is no certificate behind it, so your operating system will
treat it as software from an unidentified developer.

**Windows** shows a blue *"Windows protected your PC"* SmartScreen dialog. Click
**More info**, then **Run anyway**.

**macOS** refuses the first time. Open *System Settings → Privacy & Security*, scroll down
and click **Open Anyway** next to the message about MOPAChromaAtlas. If it instead claims the
app is damaged, that is how macOS words the quarantine on unsigned downloads:

```
xattr -dr com.apple.quarantine MOPAChromaAtlas.app
```

**Linux** does not object.

## Checking what you downloaded

`SHA256SUMS.txt` is attached. (The in-place updater does this check for you.)

```
sha256sum -c SHA256SUMS.txt --ignore-missing                # macOS and Linux
certutil -hashfile MOPAChromaAtlas-*-win-x64.zip SHA256     # Windows, compare by eye
```

## Safety

The Lumos Ultra is a Class 4 laser. Atlas never fires it by itself: a job is only armed, and
runs when you press Start on the machine. Unofficial software, not made, endorsed or supported
by WeCreat. Use at your own risk.
