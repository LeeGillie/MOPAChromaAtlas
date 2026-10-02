# Provenance

This library talks to a WeCreat Lumos Ultra's local network API. It is unofficial,
built entirely from the sources below, and is not affiliated with, endorsed by, or
supported by WeCreat or LightBurn Software. Every method's doc comment says which of
these sources backs it, and whether that claim is CONFIRMED or INFERRED.

## What this library is built from

1. **Direct observation of the authors' own, purchased Lumos Ultra**, running WeCreat's
   officially released production firmware and the officially released MakeIt!
   application - specifically, MakeIt's own local application log
   (`%APPDATA%\Wecreat MakeIt!\logs\main.log`) and a staged `gcode.gc` job file, both
   read from the authors' own machine on the authors' own local network. This is the
   source for the exact JSON shape of `/process/fileupload_via_ftp`, the `status` codes
   `4` (idle/armed) and `6` (framing preview) on `/process/status`, and the framing
   preview endpoints.
2. **[marcoreps/lumos_RE](https://github.com/marcoreps/lumos_RE)** - a public,
   independent, third-party reverse-engineering project specifically on the Lumos Ultra.
   Source for the broader endpoint list, the documented upload-arm-start sequence, and
   the default FTP credentials.
3. **[2ktsch/weburn](https://github.com/2ktsch/weburn)** - a public, independent,
   third-party project reverse-engineering the same API family on WeCreat's earlier
   Vision 20W (2024). Source for request/response body shapes not yet independently
   re-confirmed on the Ultra (camera endpoints, `/process/control`), and for the "no
   interlock is enforced at this layer" finding.

## What this library is explicitly NOT built from

- **No WeCreat-private material.** WeCreat Support (specifically Allen, WeCreat's
  point of contact for the authors' beta participation) shared a private LightBurn
  development build and its configuration file in August 2026, on the explicit
  condition it stay private. Nothing about that build, its link, or its configuration
  file appears anywhere in this library, its tests, its documentation, or its commit
  history - not summarized, not paraphrased, not referenced. That material lives only
  in the authors' own private, gitignored notes for an unrelated LightBurn-profile
  project.
- **No quoted private correspondence.** Nothing in this library quotes WeCreat support
  email text. Where WeCreat confirmed a fact (e.g. that Ver. 000240 is the production
  firmware shipped to all Kickstarter backers), it is stated as a fact, not as a quote,
  consistent with WeCreat's own stated preference for how findings get shared publicly.
- **No LightBurn-internal information.** Nothing here comes from LightBurn's private
  beta channels, their forum staff's private messages, or anything beyond their public
  forum posts.

## A known gap

Several endpoints are referenced in this library's XML doc comments (and in its
README's "known but not yet wrapped" list) purely because `marcoreps/lumos_RE` names
them - their request/response shapes are not documented anywhere public, including
there. This library does not guess at undocumented shapes; where a shape isn't
confirmed, the doc comment says so and the raw escape hatch (`GetRawAsync`,
`PostRawJsonAsync`) is the only way to call it.
