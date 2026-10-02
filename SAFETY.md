# Safety, risks and disclaimer

**Please read this before using MOPAChroma Atlas.** The program shows it the first time it
starts, and asks you to accept it.

## MOPAChroma Atlas controls a Class 4 laser

MOPAChroma Atlas plans laser settings and sends engraving jobs to a WeCreat Lumos Ultra, a
Class 4 laser product. A Class 4 laser can blind instantly, including from reflections, burn
skin, and start fires. Working on metal adds hot spatter, reflective surfaces and metal-oxide
fumes.

**Safety is your responsibility.** By using MOPAChroma Atlas you accept that:

- You have read and follow the laser manufacturer's safety instructions for your machine.
- You stay with the machine the whole time it runs, and keep its emergency stop within reach.
- You and anyone nearby wear eyewear rated for the laser's wavelength (1064 nm for the
  Ultra's MOPA source) whenever the laser could be exposed.
- You use proper fume extraction, keep a suitable fire extinguisher at hand, and never leave
  a running job unattended.
- You check every job before you start it: where it will engrave, on what, and with what
  settings. Test settings can mark, cut through or damage the blank and the surface beneath
  it, and can damage the machine.

## What the software does to reduce risk, and what it cannot do

- It never starts the laser by itself: a job is only armed, and runs when you press Start.
- It sends a job only with the door closed and after you have seen where it will engrave.
- It refuses jobs whose heat per area exceeds its limits, and never plans settings above them.

These are aids, not guarantees. They depend on the machine reporting correctly, on the
calibration and settings being right, and on software that may contain mistakes. An early
test of this program planned settings hot enough to cut through a stainless sheet and mark
the machine's plate beneath it; the heat limits were added because of that. **Do not rely on
the software to keep you, your machine or your workspace safe.** You remain in control of, and
responsible for, everything the laser does.

## No warranty, no liability

MOPAChroma Atlas is free, open-source software provided **"as is", without warranty of any
kind**, under the MIT licence (see LICENSE). In no event shall the authors or contributors be
liable for any claim, damages or other liability, including injury, fire, damage to
property, materials or machines, or lost work, arising from the use of this software or its
results. If you do not accept this, do not use the software.

The colours and settings it records are measurements of test engravings, not promises: the
same settings can give a different result on another machine, lens, material batch, surface
finish, focus or temperature. Always test on scrap first.

## Unofficial software

MOPAChroma Atlas is not made, endorsed or supported by WeCreat, LightBurn Software or any
laser manufacturer. WeCreat, Lumos, Lumos Ultra and MakeIt are trademarks of their owners;
LightBurn is a trademark of LightBurn Software, LLC. Names are used only to say what the
program works with.

It talks to the laser through the machine's own local network interface, as WeCreat's software
does, and reads and writes only plain, unencrypted files that MakeIt itself imports and exports.
It does not bypass any encryption or protection. Using third-party software with your machine
may affect the manufacturer's warranty or support; check with the manufacturer if that matters
to you. A firmware or MakeIt update could change how the machine or MakeIt behaves; if anything
looks different after an update, stop and check before engraving.

## Your data

Atlas keeps everything on your own computer. Apart from talking to your laser, the only thing it
sends anywhere is a once-a-day check with GitHub for a newer version.
No personal data, atlas or pictures leave your computer.

---

This notice is a plain-language statement of the risks and of the licence's terms. It is not
legal advice.
