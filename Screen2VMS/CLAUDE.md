# Screen2VMS — working notes

> **Scope:** this is a personal / internal utility, not a product. It has had no
> security review, no ONVIF conformance testing and no release process, and
> several decisions here were taken on the basis that it stays internal. The
> table in `README.md` under "Scope: personal utility, not a product" lists them
> and is the checklist to work through if that ever changes.
>
> It is published publicly under MIT, with every bundled dependency audited
> (`THIRD-PARTY-NOTICES.md`). That is a licensing decision, not a quality claim:
> nothing above changes because the binary is downloadable.

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
| 2 | H.264 encoding (Media Foundation MFT) | **Done, verified with ffmpeg** |
| 3 | RTSP server, VLC plays the stream | **Done, verified with VLC + ffmpeg** |
| 4 | ONVIF device/media services + WS-Discovery | **Done, 28/28 conformance checks** |
| 5 | Genetec discovers, adds, displays, records | Discovery + full ONVIF interrogation working against real Genetec 5.14 |
| 6 | XProtect discovers, adds, displays, records | **Built, not verified** — needs XProtect |
| 7 | Stability, reconnect, sleep/resume | Implemented; soak run done, 24-hour run outstanding |

Phases 5 and 6 are code-complete: everything a VMS asks for is implemented and
passes an independent ONVIF probe. What has *not* happened is an actual add
against Genetec or XProtect. See "Verifying against a real VMS" below.

---

## Decisions already made

Settled with the project owner. Do not relitigate.

- **Encoder: Windows Media Foundation H.264 MFT.** Hardware-accelerated where
  available, royalties covered by the Windows licence, no bundled binaries.
  Synchronous MFTs only — hardware encoders usually present as asynchronous
  MFTs needing an event pump, which is the v0.7 milestone, not the MVP.
- **RTSP: `SharpRTSPServer` in-process.** Not MediaMTX, not a child process.
- **ONVIF: `SharpOnvifServer` (CoreWCF).** See the correction below.
- **Licensing: MIT, published on GitHub Releases.** Every one of the 77 bundled
  packages is MIT or Apache-2.0 — audited from the `.nuspec` files, recorded in
  `THIRD-PARTY-NOTICES.md`, and that file is what satisfies the attribution
  obligations of a self-contained single-file build. GPL tools (ffmpeg, ffprobe,
  VLC) stay *debugging* tools; the moment one becomes a shipping dependency the
  audit is void. Adding a package means re-running the audit — the command is at
  the bottom of the notices file.
- **Target framework: `net8.0`** (`net8.0-windows` where Windows APIs are used).
- **Test targets: both Genetec and XProtect are available** on the owner's
  network.

---

## Corrections to the spec

**Spec 66 said to evaluate ONVIF libraries first, and an earlier version of this
file claimed no .NET ONVIF *server* library existed. That was wrong.**
`SharpOnvifServer` (with `.DeviceMgmt` and `.Media`) provides generated ONVIF
contracts over CoreWCF, WS-Discovery with scopes, and both authentication
schemes. Screen2VMS uses it, and hand-rolling SOAP would have been a mistake.

**Spec 22's operation list is incomplete.** These are also implemented because a
VMS calls them:

- `GetSystemDateAndTime` — the first call both platforms make, and it **must
  answer unauthenticated**. The library's default pre-auth list already allows
  it; do not remove that.
- `GetScopes` and the discovery scopes in `OnvifDiscoveryScopes`. Without the
  `Profile/Streaming` scope and the `NetworkVideoTransmitter` type, a VMS sees
  the probe response and still refuses to list the device as a camera.
- `GetNetworkInterfaces` — Milestone keys the hardware on the MAC returned here.
- `GetServiceCapabilities` on device and media, and `GetServices`.
- `GetVideoSources`, `GetVideoSourceConfigurations`, `GetVideoEncoderConfigurations`,
  `GetVideoEncoderConfigurationOptions`, `GetVideoSourceConfigurationOptions`.
- `GetSnapshotUri` **and a working JPEG endpoint** at `/onvif/snapshot`. Both
  platforms fetch snapshots for thumbnails.
- `SetVideoEncoderConfiguration` — XProtect writes it while adding a unit. It
  stores the values and deliberately does **not** restart the stream, because
  dropping the stream mid-add aborts the add.

**Spec 24 has the authentication priority backwards.** Clients lead with
WS-Security UsernameToken; HTTP Digest is the fallback. Both are enabled.

**Spec 23 and 26 pick the IP address the wrong way.** `GetStreamUri` resolves
the local address that routes to *the requesting client*
(`NetworkAddressResolver.GetLocalAddressFor`). Discovery advertises only
non-virtual adapters — announcing loopback and the WSL/Hyper-V adapters makes a
VMS pick an address it cannot reach.

**Spec 5 says no administrator rights.** Kestrel on a raw socket, never
`HttpListener`. The firewall helper is the only elevated path and only runs when
the user presses the button.

---

## A library bug you must not "clean up"

`SharpOnvifServer.Media` declares four SOAP actions with the separator in the
wrong place — `GetVideoSources` is declared as
`http://www.onvif.org/ver10/media/wsdlGetVideoSources/` instead of
`…/media/wsdl/GetVideoSources`. A VMS sends the correct action, CoreWCF finds no
matching operation, and the call comes back as an ActionNotSupported fault.
`GetProfile` and `GetVideoSourceConfigurationOptions` are affected too, and all
three are called while adding a unit.

`OnvifActionCompatibility` rewrites the incoming action for exactly those
operations. The map is **built by reflecting over the contract**, so it empties
itself if the library is ever fixed. `OnvifActionCompatibilityTests` asserts the
invariant that matters — every operation a VMS calls is reachable by its correct
action — and passes either way. Do not delete this middleware without checking
that test still passes.

---

## Architecture

Dependencies point inwards. `Screen2VMS.Core` holds every interface and model
and references nothing; each other project references only Core (plus the
interop assembly); `Screen2VMS.Engine` composes the runtime and
`Screen2VMS.App` is the only WPF layer.

```
Camera → Encoder → EncodedFrame → StreamManager → RtspServer → clients
                                        ↑
                       OnvifServiceHost ┘  (metadata, URIs and snapshots only)
```

Two rules keep this honest:

- **The RTSP server never touches the camera.** It consumes encoded frames.
- **The ONVIF layer never knows a webcam exists.** It talks to
  `IOnvifDeviceContext`, whose only implementation is
  `StreamManagerOnvifContext`.

| Project | Contains |
|---|---|
| `Screen2VMS.Core` | Interfaces, models, pure logic (H.264 parsing, NV12 conversion, mode selection, address resolution). |
| `Screen2VMS.MediaFoundation` | All COM interop. Internal, shared by capture and encoding. |
| `Screen2VMS.Camera` | Media Foundation capture. |
| `Screen2VMS.Encoding` | H.264 encoder MFT. |
| `Screen2VMS.Rtsp` | SharpRTSPServer adapter. |
| `Screen2VMS.Onvif` | ONVIF device and media services, host, snapshot endpoint. |
| `Screen2VMS.Discovery` | WS-Discovery scope construction. |
| `Screen2VMS.Configuration` | JSON config, DPAPI password protection. |
| `Screen2VMS.Engine` | StreamManager, runtime facade, firewall helper, JPEG snapshots. |
| `Screen2VMS.Logging` | Serilog setup. |
| `Screen2VMS.App` | WPF UI and composition root. |

Two projects are not in spec 38: `Screen2VMS.MediaFoundation` exists because
capture and encoding both need the interop and neither should depend on the
other, and `Screen2VMS.Engine` exists because the runtime must be hostable
without WPF (spec 58, 59).

---

## Things that will bite you

**Media Foundation objects are not agile.** Anything created on one thread must
be used on that thread. Capture and encoding each own a dedicated MTA thread;
enumeration uses `MtaRunner`. Never touch a COM object from the WPF UI thread.

**`MfInterfaces.cs` declaration order is the vtable.** Reordering or deleting a
method silently corrupts every call after it. Unused slots are `Reserved…`
placeholders; calling one is a bug.

**Array parameters on COM interfaces need `[MarshalAs(UnmanagedType.LPArray)]`.**
The default is SAFEARRAY, which crashes the runtime with an "Internal CLR error"
rather than a normal exception. This cost real debugging time on `GetBlob` and
would have hit `ProcessOutput` next.

**`VideoFrame` is a `ref struct` wrapping borrowed memory.** Valid only inside
`IVideoFrameSink.OnFrame`. Sinks must copy what they keep, return quickly, and
not throw — they run on the capture thread.

**NV12 is the pipeline's currency.** The source reader is asked for NV12 and
Media Foundation inserts whatever decoder the camera needs, so an MJPEG-only
webcam still arrives as NV12 and the encoder gets its native input.

**Device identity must be stable across restarts.** The serial number and the
synthetic MAC are generated once into `config.json`. Genetec keys on the serial,
Milestone on the MAC. Change either and the VMS treats this as a new camera.

**The ONVIF password is generated per install** and stored DPAPI-encrypted
(machine scope, so a future service account can read it). There is deliberately
no default password. A config copied from another machine will not decrypt, and
that is treated as "no password set" rather than an error.

**Holding the webcam locks out Teams and Zoom.** Expected. A busy camera
surfaces as `CameraBusyException` and a retry, not a crash.

**`E_ACCESSDENIED` from the camera usually means privacy settings**, not a code
fault — Settings → Privacy & security → Camera.

---

## Build, test, run

```bash
cd Screen2VMS
dotnet build                                          # whole solution
dotnet test tests/Screen2VMS.Tests                    # 69 tests, no hardware needed
dotnet run --project src/Screen2VMS.App               # the GUI
```

Runtime state lives in `%ProgramData%\Screen2VMS\` — `config.json` and `Logs\`.
Deleting `config.json` regenerates the device identity and password, which makes
every VMS treat this as a new camera.

---

## Verifying without a VMS

`ffmpeg`, `ffprobe` and VLC are installed on the owner's machine.

```powershell
# What the camera really offers and really delivers, independent of our code
ffmpeg -f dshow -list_options true -i video="Integrated Camera"
ffmpeg -f dshow -pixel_format nv12 -video_size 1920x1080 -framerate 30 `
       -i video="Integrated Camera" -t 5 -f null -

# The RTSP stream
ffprobe -rtsp_transport tcp -i rtsp://<ip>:8554/live
vlc rtsp://<ip>:8554/live
```

`tools/Test-Onvif.ps1` drives 28 ONVIF operations including negative
authentication tests, `tools/Test-Discovery.ps1` checks the WS-Discovery
ProbeMatch scopes, and `tools/Invoke-Soak.ps1` runs the resource soak. See
`tools/README.md`.

**Known environment quirk:** the owner's Integrated Camera negotiates 30 fps and
delivers exactly 10 fps in typical indoor light, at every resolution. Confirmed
to be the hardware — ffmpeg through DirectShow gets the same 10 fps, and
requesting 5 fps yields exactly 5. Do not chase this as a bug. It means this
machine cannot validate spec 52's 30 fps target without good lighting or an
external webcam.

---

## Verifying against a real VMS

This is the outstanding work. Both platforms need the same three things:

1. The device on the same subnet as the VMS server. **WS-Discovery is multicast
   and does not cross subnets** — on a different VLAN the unit must be added by
   IP instead, which is supported but is a different test.
2. Inbound TCP 8554, TCP 8000 and UDP 3702 allowed. The window has a
   **Firewall Rules** button that creates them (one UAC prompt).
3. The user name and password shown in the window.

Capture the SOAP exchanges into `docs/protocol/genetec/` and
`docs/protocol/xprotect/` as they are gathered (spec 67) — the goal is not valid
ONVIF XML but ONVIF that these two products accept. Never commit credentials.

If a unit adds but shows no video, look first at what `GetStreamUri` returned
for that client's address: that is the single most likely failure.

---

## Soak result (phase 7)

16 minutes with an RTSP client attached and reconnecting every 60 seconds, ten
reconnect cycles:

| | start | end |
|---|---|---|
| Working set | 352 MB | 354 MB |
| Handles | 1111 | 1092 |
| Threads | 90 | 83 |

Flat memory, handles and threads trending slightly *down*, clean shutdown — no
leak. **But the ~354 MB baseline is well above spec 35's 250 MB target.** Most
of it is the ASP.NET Core and CoreWCF host living inside a WPF process. That is
a size problem, not a leak, and reducing it is optimisation work nobody has done
yet. The 24-hour run in spec 68 phase 7 is still outstanding; use
`tools/Invoke-Soak.ps1 -Minutes 1440`.

---

## What Genetec actually calls (5.14)

Verified against a real Genetec Security Center 5.14 Archiver on the same host.
Genetec's own log at
`C:\ProgramData\Genetec Security Center 5.14\Logs\Baseline_LogTargets__UnitControlHost_*.log`
names the failing operation directly — grep it for the unit id, which Genetec
derives from our MAC (`00000000-0000-0000-0007-<mac>`). That is far faster than
guessing from our side.

**The bug that blocked enrolment:** Genetec calls `GetNetworkDefaultGateway`
inside `GetNetworkSettingsCapsAsync`, which runs inside `GetCapabilitiesAsync`.
`DeviceBase` faulted, capability discovery aborted, and the unit sat in Error
with only "The server was unable to process the request" to show for it.

Beyond the ONVIF spec's core, Genetec also calls, and will fault the enrolment
if any of them return an internal error:

- `GetNetworkDefaultGateway`, `GetDNS`, `GetNTP`, `GetNetworkProtocols`,
  `GetZeroConfiguration`, `GetDiscoveryMode`, `GetDynamicDNS`, `GetHostname`
- `GetGuaranteedNumberOfVideoEncoderInstances`
- `SetSynchronizationPoint` — this is Genetec asking for a key frame, and it
  maps onto `IVideoEncoder.RequestKeyFrame`
- `GetAudioOutputs`, `GetAudioSources`, the audio configuration and options
  getters, `GetMetadataConfigurations`, `GetVideoAnalyticsConfigurations`,
  `GetOSDs`, `GetVideoSourceModes`
- `CreateProfile` and `AddVideoSourceConfiguration` — Genetec tries to build its
  own profile

**The rule this taught us:** for anything the device does not have, answer with
an empty collection, and where refusal is genuinely required use the ONVIF fault
the spec defines (`CreateProfile` returns `MaxNVTProfiles`, a 400). Never let an
optional operation reach the base class and produce a 500 — a VMS treats an
internal error as a broken device, but handles "none" and a typed fault fine.

Note that 401 responses in our log are normal: Genetec sends unauthenticated
first, takes the digest challenge and retries. Each 401 should be followed by a
200 for the same operation. Only unmatched 5xx responses are real failures.

### Two Genetec messages that are not faults

- *"Event service is missing from the RetrievedCapabilities. Events will not be
  supported by the device."* — correct. ONVIF events are out of scope (spec 3);
  Genetec logs this and carries on without motion events.
- *"An error occurred while inquiring the certificate management capabilities."*
  — Genetec is querying the ONVIF **Advanced Security** service, which we do not
  advertise because there is no TLS yet (spec 71, v0.5). Nothing reaches our
  device service: a grep for "Certificate" in our log finds nothing. Implementing
  the device-service certificate getters does not silence it, and should not be
  attempted again for that reason.

### If a unit gets stuck

*"Unit with guid {…} is already in the Unit Map"* means Genetec is holding a
half-enrolled unit from an earlier failed attempt. Genetec caches the unit's
capabilities, so fixing the device does not heal an already-broken unit. Delete
it in Config Tool and add it again.

### Genetec asks for UDP

Genetec's SETUP is `Transport: RTP/AVP;unicast;client_port=…`, i.e. plain UDP,
not the TCP interleaving spec 14 nominates as primary. Both work — ffmpeg pulls
the same stream over either transport — so the server accepts what it is asked
for. Worth remembering when reading a capture: seeing UDP negotiated is normal,
not a fault.

---

## Never block the UI thread on the ONVIF host

`OnvifServiceHost.Start` and `Stop` are synchronous because a button click
calls them, and that click runs on the WPF dispatcher thread. Awaiting the
ASP.NET host from there and blocking on the result deadlocks: Kestrel's
continuations are posted back to the dispatcher, which is the thread sitting
blocked waiting for them. The window stopped responding, the RTSP port stayed
bound and the camera was never released — which reads as a crash.

`RunDetached` hands each host call to the thread pool so the continuations have
somewhere to run. Do not "simplify" it back to `.GetAwaiter().GetResult()`.

The trap is that each call also has a timeout backstop, so a deadlocked build
does not hang forever — it sits out the timeout and then **reports success**.
That is why `OnvifServiceHostLifecycleTests` asserts on elapsed time rather than
on completion: healthy start and stop take about 150 ms, a blocked stop burns
its full fifteen seconds. Those tests were confirmed to fail with the deadlock
reintroduced and pass once it was removed.

---

## Opening a specific camera

`MFEnumDeviceSources` filters **only** on the source-type attribute. It ignores
`MF_DEVSOURCE_ATTRIBUTE_SOURCE_TYPE_VIDCAP_SYMBOLIC_LINK`, which is honoured by
`MFCreateDeviceSource` instead. Setting that attribute and then taking element
zero of the result therefore opens whichever camera Windows enumerates first,
regardless of which one was asked for.

`MediaFoundationCameraSource.OpenDevice` matches on the symbolic link itself for
that reason. Do not "simplify" it back to taking the first result.

This is invisible on a machine with one camera, which is why it survived until a
second one was plugged in: every selection silently streamed the built-in
webcam. The cheapest way to catch it again is to ask for a resolution only one
of the cameras has — a Logitech C270 does 1280x960 and a typical built-in
camera cannot — and check what `GetStreamUri`'s stream actually reports:

```powershell
ffprobe -rtsp_transport tcp -i rtsp://admin:<password>@<ip>:8554/live
```

## Rebuilding a bound list resets its selection

`ObservableObject.SetProperty` suppresses the change notification when the value
is unchanged, which is normally what you want. It is wrong immediately after an
`ItemsSource` has been rebuilt: clearing a combo box's items resets its
selection, so if the new camera offers the same resolution or frame rate as the
old one — 30 fps is near universal — no notification fires and the box sits
blank while the view model still holds a perfectly good value.

`RebuildModeLists` and `RebuildFrameRates` therefore assign the backing field
directly and raise the notification unconditionally, guarded by `rebuilding` so
the transient nulls the combo boxes push back during the rebuild are ignored.
