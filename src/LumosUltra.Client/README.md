# LumosUltra.Client

An unofficial .NET client for the WeCreat Lumos Ultra's local network API - the same
API MakeIt! itself uses. Not affiliated with, endorsed by, or supported by WeCreat.
See [PROVENANCE.md](PROVENANCE.md) for exactly what this is built from (and explicitly
not built from) before trusting any of it against real hardware.

**Most of this library has not been run against a physical Lumos Ultra by its authors
yet.** `GetJobStatusAsync`, `StartFramingPreviewAsync`, and `StopFramingPreviewAsync`
have been - the rest is built from direct observation of the device's own logs plus two
independent community reverse-engineering projects, not from a vendor spec or hardware
testing. Test carefully, start with read-only calls, and read [Safety](#safety) below
first.

One confirmed gotcha worth knowing before you add another body-bearing POST call: the
device's embedded web server does not handle `Transfer-Encoding: chunked` request
bodies, and `System.Net.Http.Json.JsonContent` doesn't always send an upfront
`Content-Length`. Use the library's internal `JsonBody` helper (a fully-buffered
`StringContent`), not `JsonContent.Create`, or the request will fail with a generic
"the response ended prematurely" error that has nothing obviously to do with the cause.

## Why a separate library

This is deliberately standalone - no dependency on any other project in this
solution or elsewhere - so it can be referenced directly from other repositories
(for example, DepthView) as well as from MOPAChromaAtlas here. Cross-platform by
construction: it's plain `HttpClient` and FTP, nothing Windows-specific.

## Using it from another repository

Add a project reference by relative path:

```xml
<ProjectReference Include="..\..\MOPAChroma Atlas\src\LumosUltra.Client\LumosUltra.Client.csproj" />
```

## What's implemented

| Area | Methods | Confidence |
|---|---|---|
| Device | `GetDeviceInfoAsync`, `IsReachableAsync` | Device info CONFIRMED from the authors' own device |
| Job status | `GetJobStatusAsync` | CONFIRMED (status codes 4, 6 observed directly; 2, 3 per marcoreps/lumos_RE) |
| Job control | `PauseJobAsync`, `CancelJobAsync` | Per 2ktsch/weburn (Vision 20W); not yet re-confirmed on the Ultra |
| Job upload | `UploadAndArmJobAsync`, `StartArmedJobAsync` | CONFIRMED end-to-end on the authors' own Ultra: upload, arm, and start actually fired the laser and marked real material with no MakeIt UI or physical button involved |
| Framing | `StartFramingPreviewAsync`, `StopFramingPreviewAsync` | Status transition (Idle→Preview→Idle) CONFIRMED end-to-end; actually drawing a boundary trace is NOT confirmed - see the method's own doc comment |
| Camera | `TakePhotoAsync` | CONFIRMED end-to-end on the Ultra (4032x3024 JPEG). Image is delivered on its side: a quarter turn *anticlockwise* puts it upright on the authors' machine (clockwise shows it upside down); whether it is also mirrored is not yet confirmed |
| Safety | `TryIsDoorClosedAsync` | Response shape CONFIRMED on the authors' own device (`status: 1` closed, `0` open) via two direct real-hardware observations |
| Anything else | `GetRawAsync`, `PostRawJsonAsync`, `SendRawCommandAsync` | Escape hatches - see below |

## Known but not yet wrapped

Named by `marcoreps/lumos_RE` but with no documented request/response shape, so not
worth a typed method yet: `/device/set_config`, `/device/set_device_type`,
`/device/set_wlan0_mac`, `/device/set_factory_sn`, `/device/sys_restart`,
`/device/set_inverse_dis`, `/device/laser_led_switch`,
`/device/global/cfg_peripheral_status`, `/camera/measure_distance`,
`/camera/get_calibration`, `/camera/measure_material_thick`,
`/device/camera/{upload,download,factory_upload,factory_download}`,
`/lumos/camera/{start,stop}_autofocus`, `/device/material/{upload,download}`,
`/process/upload`, `/process/multipart`, `/device/light/{status,progress,focus_light,logo_light,get_init_value}`,
`/test/cmd/rs485`. Call these with `GetRawAsync`/`PostRawJsonAsync` and inspect what
comes back before committing to a typed wrapper.

**No native fan/fume-extraction endpoint was found anywhere in the documented API.**
The one fan-related community tool (`shelly_fume_plug.py` in `marcoreps/lumos_RE`)
controls a third-party Shelly smart plug, not the machine itself - if you want
programmatic fan control, that's a separate integration.

## Safety

- The device enforces no authentication and, per 2ktsch/weburn, no lid interlock at
  the API layer - `SendRawCommandAsync` and the arm/start job path will do exactly
  what you tell them to, door open or not.
- `UploadAndArmJobAsync` defaults to refusing unless `TryIsDoorClosedAsync` can
  positively confirm the door is closed - because the API itself won't stop you, this
  library tries to. That check's response shape is now confirmed on the authors' own
  device, but a machine with a different firmware/field layout will still make it
  conservatively refuse; pass `requireDoorConfirmedClosed: false` only once you've
  verified safety some other way for that specific call.
- `SendRawCommandAsync` requires an explicit `iUnderstandTheRisk: true` - there is no
  safe default for "run arbitrary text on a Class 4 laser." No app in this repository
  exposes it, and the test harness's raw request box refuses `/test/cmd/*`.
- This is a 100 W-class MOPA fiber laser. Follow ANSI Z136.1 practice: appropriate
  eyewear, no defeating physical interlocks, and don't run commands you can't predict
  the effect of.
