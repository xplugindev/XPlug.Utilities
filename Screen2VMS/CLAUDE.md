# Screen2VMS — working notes

Turns a Windows webcam into an ONVIF Profile-S IP camera so Genetec Security
Center and Milestone XProtect can discover it, stream it and record it over
standard ONVIF + RTSP, with no VMS plugin.

The full requirements are in `PROJECT_INSTRUCTIONS.md`. Section numbers below
(spec 9, spec 22, …) refer to it. **Read this file before that one** — it
records decisions and corrections that override the spec where the spec is
wrong.

---

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Camera enumeration + live WPF preview | **Done, verified on hardware** |
| 2 | H.264 encoding (Media Foundation MFT) | Not started |
| 3 | RTSP server, VLC plays the stream | Not started |
| 4 | ONVIF device/media services + WS-Discovery | Not started |
| 5 | Genetec discovers, adds, displays, records | Not started |
| 6 | XProtect discovers, adds, displays, records | Not started |
| 7 | 24-hour stability, reconnect, sleep/resume | Not started |

Do not start a phase before the previous one passes its acceptance test.

---

## Decisions already made

These were settled with the project owner. Do not relitigate them.

- **Encoder: Windows Media Foundation H.264 MFT.** Hardware-accelerated where
  available, H.264 royalties covered by the Windows licence, no bundled
  binaries. Not ffmpeg.
- **RTSP: an in-process C# server** (SharpRTSPServer or equivalent). Not
  MediaMTX, not an external process. The point is precise control over SDP,
  `sprop-parameter-sets` and keyframe timing, which is what VMS compatibility
  actually turns on.
- **ONVIF: hand-rolled minimal SOAP on Kestrel.** See the correction below.
- **Licensing: internal use only.** GPL is acceptable, so ffmpeg and ffprobe are
  fine as *debugging* tools. They must not become shipping dependencies.
- **Target framework: `net8.0`** (`net8.0-windows` where Windows APIs are used).
- **Test targets: both Genetec and XProtect are available** on the owner's
  network, so phases 5 and 6 are real gates.

---

## Corrections to the spec

The spec is the contract, but these parts of it are wrong and following them
would produce a device neither VMS will accept.

**Spec 66 assumes a mature .NET ONVIF server library exists. It does not.**
Every well-known .NET ONVIF package is a *client*. The ONVIF service layer is
the one place the "use a library" rule has to be broken. Hand-roll the SOAP
responses from XML templates; the surface is about a dozen operations.

**Spec 22's operation list is incomplete.** These are also required:

- `GetSystemDateAndTime` — the first call both VMSes make, and it **must answer
  unauthenticated**. Clients use it to compute clock skew for the WS-Security
  timestamp. If it 401s, every later authentication fails with a misleading
  error.
- `GetScopes`, plus correct WS-Discovery scopes
  (`onvif://www.onvif.org/type/video_encoder`,
  `onvif://www.onvif.org/Profile/Streaming`, `/name/…`, `/hardware/…`). Without
  the `NetworkVideoTransmitter` type and the Streaming scope, a VMS finds the
  device but will not classify it as a camera.
- `GetNetworkInterfaces` — Milestone reads the MAC from here and uses it as the
  hardware identity key. See "Device identity" below.
- `GetServiceCapabilities` on device and media; `GetServices` with
  `IncludeCapability`. Some clients read only `GetCapabilities`, others only
  `GetServices`; the two must agree.
- The four operations spec 22 lists as "prefer" (`GetVideoSources`,
  `GetVideoSourceConfigurations`, `GetVideoEncoderConfigurations`,
  `GetVideoEncoderConfigurationOptions`) are required.
- `GetSnapshotUri` **and a working JPEG-over-HTTP endpoint**. Both VMSes pull
  snapshots for thumbnails; without one the camera looks broken in the
  management client even when RTSP is perfect.
- `SetVideoEncoderConfiguration` earlier than spec 22's "later" — XProtect
  writes resolution, fps and bitrate when the unit is added. It may clamp and
  ignore, but it must not SOAP-fault.

**Spec 24 has the authentication priority backwards.** ONVIF clients lead with
WS-Security UsernameToken (digest with nonce and created); HTTP Digest is the
fallback. Implement both; neither is optional.

**Spec 23 and 26 pick the IP address the wrong way.** `GetStreamUri` must return
the address of the local interface that routes to *the requesting client*, not a
configured or first-found one. With a VPN or several NICs, returning the wrong
address is the most common reason a unit adds cleanly and then shows no video.
`IStreamUriProvider` already takes the client address for this reason.

**Spec 5 says no administrator rights.** So use Kestrel on a raw socket, never
`HttpListener` — the latter needs a URL ACL or elevation for any non-localhost
prefix. The firewall rules in spec 27 do need elevation once; make that an
explicit optional action, never a silent UAC prompt.

---

## Architecture

Dependencies point inwards. `Screen2VMS.Core` holds every interface and model
and references nothing; each other project references only Core; only
`Screen2VMS.App` composes them.

```
Camera → Encoder → EncodedFrame → StreamManager → RtspServer → clients
                                                        ↑
                                   OnvifServiceHost ────┘  (metadata + URIs only)
```

Two rules keep this honest:

- **The RTSP server never touches the camera.** It consumes encoded frames
  (spec 43).
- **The ONVIF layer never knows a webcam exists.** It publishes metadata and
  hands out a stream URI; the pixels come from the RTSP server (spec 44).

| Project | Contains |
|---|---|
| `Screen2VMS.Core` | Interfaces, models, pure logic. No Windows dependency except `Video/`. |
| `Screen2VMS.Camera` | Media Foundation capture. All COM interop lives here. |
| `Screen2VMS.Encoding` | Phase 2. H.264 MFT. |
| `Screen2VMS.Rtsp` | Phase 3. |
| `Screen2VMS.Onvif` | Phase 4. SOAP services. |
| `Screen2VMS.Discovery` | Phase 4. WS-Discovery. |
| `Screen2VMS.Configuration` | JSON config under ProgramData. |
| `Screen2VMS.Logging` | Serilog setup. |
| `Screen2VMS.App` | WPF UI and composition root. |

---

## Things that will bite you

**Media Foundation objects are not agile.** Anything created on one thread must
be used on that thread. Capture owns a dedicated MTA thread
(`MediaFoundationCameraSource.CaptureLoop`); enumeration uses `MtaRunner`, which
spins a short-lived MTA thread per call. Never hand a COM object across those
boundaries, and never touch one from the WPF UI thread.

**`MfInterfaces.cs` declaration order is the vtable.** Reordering or deleting a
method silently corrupts every call after it. Unused slots are `Reserved…`
placeholders that exist only to hold position — calling one is a bug. Interfaces
deriving from `IMFAttributes` repeat its thirty slots first.

**`VideoFrame` is a `ref struct` wrapping borrowed memory.** It is valid only
inside the `IVideoFrameSink.OnFrame` call. A sink that needs to keep pixels must
copy them. Sinks must also return quickly and must not throw — they run on the
capture thread and block every other sink.

**NV12 is the pipeline's currency.** The source reader is asked for NV12 and
Media Foundation inserts whatever decoder or converter the camera needs, so an
MJPEG-only webcam still arrives as NV12 and the Phase 2 encoder gets its native
input with no conversion. Only the preview ever sees RGB.

**Device identity must be stable across restarts.** The serial number and the
synthetic MAC are generated once and persisted in `config.json`. Genetec keys on
the serial, Milestone on the MAC. If either changes, the VMS treats Screen2VMS
as a new camera and the operator has to add the unit again.

**Holding the webcam locks out Teams and Zoom.** That is expected. A camera in
use by another application surfaces as `CameraBusyException` and a retryable UI
message, not a crash (spec 32).

**`E_ACCESSDENIED` from the camera usually means privacy settings**, not a code
fault — Settings → Privacy & security → Camera. It has its own message for that
reason.

---

## Build, test, run

```bash
cd Screen2VMS
dotnet build                                          # whole solution
dotnet test tests/Screen2VMS.Tests                    # 28 tests, no hardware needed
dotnet run --project src/Screen2VMS.App               # the GUI
```

Runtime state lives in `%ProgramData%\Screen2VMS\` — `config.json` and `Logs\`.
Delete `config.json` to reset, but remember that regenerates the device identity.

---

## Verifying without a VMS

`ffmpeg` is installed on the owner's machine and is the reference for
cross-checking capture:

```powershell
# What the camera really offers, independent of our enumeration
ffmpeg -f dshow -list_options true -i video="Integrated Camera"

# What it really delivers, independent of our pipeline
ffmpeg -f dshow -pixel_format nv12 -video_size 1920x1080 -framerate 30 `
       -i video="Integrated Camera" -t 5 -f null -
```

**Known environment quirk:** the owner's Integrated Camera negotiates 30 fps and
delivers exactly 10 fps in typical indoor light, at every resolution. This was
confirmed to be the hardware, not the pipeline — ffmpeg through DirectShow gets
the same 10 fps. Requesting 5 fps yields exactly 5. Do not chase this as a bug,
and do not use this machine to validate spec 52's 30 fps target without good
lighting or an external webcam.

For phases 3 and 4, `vlc rtsp://<ip>:8554/live` must work before any VMS
troubleshooting starts (spec 47).

---

## Protocol captures

Real request/response pairs from each VMS go in `docs/protocol/genetec/` and
`docs/protocol/xprotect/` as they are gathered (spec 67). The goal is not valid
ONVIF XML — it is ONVIF that Genetec and XProtect accept. Never commit
credentials.
