# Screen2VMS — Product Specification

**Windows Webcam → ONVIF IP Camera → RTSP → Genetec / Milestone XProtect**

| | |
|---|---|
| Version | 0.1 MVP |
| Target OS | Windows 10/11 x64 |
| Language | C# / .NET 8 |
| Primary targets | Genetec Security Center, Milestone XProtect |
| Video source | Windows laptop/desktop webcam |
| Streams | 1 |
| Codec | H.264 |
| Transport | RTSP over TCP |
| Discovery | ONVIF WS-Discovery |
| ONVIF target | Minimal Profile-S compatible, architected to allow Profile T later |

> **Read `CLAUDE.md` alongside this document.** It records the decisions taken
> since this spec was written and the places where this spec is factually wrong
> (notably §22, §23, §24, §26 and §66). Where the two disagree, `CLAUDE.md` wins.

---

## 1. Product objective

Build a lightweight Windows application that turns the computer's built-in
webcam into a network IP camera, so that a VMS sees it as an ordinary ONVIF
camera.

```
Windows webcam → video capture → H.264 encoder → RTSP server
                                       ↓
                              ONVIF device service
                                       ↓
                    Genetec Security Center / Milestone XProtect
                                       ↓
                           live video / recording
```

No proprietary VMS plugin. Communication is standard ONVIF + RTSP.

## 2. MVP success criteria

The MVP is successful only when all of the following work:

1. Application starts on Windows.
2. It detects the built-in webcam.
3. The user can select the webcam.
4. It captures live video.
5. It encodes to H.264.
6. It exposes an RTSP stream.
7. VLC can open that stream.
8. It responds to ONVIF WS-Discovery.
9. Genetec Security Center can discover it.
10. Genetec can add it as an ONVIF video unit.
11. Genetec can obtain the RTSP URI through ONVIF.
12. Genetec displays live video.
13. Genetec records the stream.
14. XProtect can discover/add it as an ONVIF device.
15. XProtect displays live video.
16. XProtect records the stream.

Do not implement advanced features until this end-to-end scenario works.

## 3. Out of scope for v0.1

Screen capture, desktop capture, window capture, multiple cameras, multiple
streams, H.265, AV1, PTZ, digital zoom, audio, motion detection, AI analytics,
metadata, ONVIF events, ONVIF recording, edge storage, cloud connectivity, cloud
management, mobile app, remote access, user accounts beyond camera
authentication, image controls (brightness, contrast, white balance, exposure,
focus), firmware update, network configuration, a configuration web portal, a
VMS plugin, a database, a cloud API, Docker, Kubernetes.

Keep the MVP intentionally small.

## 4. Product name

`Screen2VMS`. Use this in the codebase.

## 5. Target environment

Windows 10/11, x64. ARM64 may come later.

The application must not require administrator privileges for normal operation
where technically possible, nor internet connectivity, a cloud account, a licence
server or a database. It must work entirely on the local LAN.

## 6–7. Architecture and technology stack

```
Screen2VMS.exe
  Configuration / WPF UI
        ↓
  Camera discovery (Media Foundation)
        ↓
  Video capture pipeline
        ↓
  H.264 encoder (hardware preferred)
        ↓
  ┌─────────────┬──────────────────────┐
  RTSP server    ONVIF device server
        ↓                ↓
   VMS / VLC     Genetec / XProtect
   (RTSP)        (SOAP/HTTP, WS-Discovery)
```

- **Application:** C#, .NET 8.
- **UI:** WPF, extremely simple.
- **Capture:** Windows Media Foundation, behind an `ICameraSource` interface so
  the backend can be replaced.
- **Encoding:** H.264. Preference order — hardware encoder (Quick Sync, NVENC,
  AMD), then the Media Foundation H.264 encoder, then software. Hardware
  acceleration must not be mandatory.
- **RTSP:** use an existing mature implementation. Do not write an RTSP server
  from scratch unless there is no practical alternative. Must support DESCRIBE,
  SETUP, PLAY, RTP, RTCP, TCP interleaved transport, optional UDP, H.264, and
  optionally authentication. RTSP over TCP is the primary target.
- **ONVIF:** minimal device implementation — WS-Discovery, device service, media
  service, authentication, `GetDeviceInformation`, `GetCapabilities`,
  `GetProfiles`, `GetStreamUri`. Implement only what the target VMSes need.
- **Logging:** `Microsoft.Extensions.Logging` with Serilog file logging to
  `%ProgramData%\Screen2VMS\Logs\`, daily rolling files.
- **Configuration:** JSON at `%ProgramData%\Screen2VMS\config.json`. No database.
- **Installer:** self-contained executable first; WiX or MSIX later.

## 8. Camera input

Enumerate all Windows video capture devices. Expose `ICameraSourceService` with
`EnumerateDevices()`, `GetCapabilities(deviceId)`, `Start(deviceId, settings)`,
`Stop()`, `GetCurrentStatus()`.

Each device exposes device ID, friendly name, manufacturer if available,
supported resolutions, frame rates and pixel formats.

## 9. Initial camera requirements

One active camera. Default to the first available. Preferred 1920x1080, fallback
1280x720. Preferred 30 fps, fallback 15.

If the webcam does not support the requested combination, automatically select
the closest supported mode. **Do not fail simply because 1920x1080@30 is
unavailable.**

## 10. Video pipeline

```
camera → capture frames → convert pixel format if necessary → H.264 encoder
       → H.264 elementary stream → RTP packetisation → RTSP server
```

Avoid unnecessary frame copies. The architecture must permit zero-copy GPU paths
later.

## 11. H.264 settings

1920x1080, 30 fps, 4 Mbps, CBR preferred, GOP 30 frames, keyframe interval 1
second, Main profile preferred (Baseline acceptable), level appropriate to
resolution and frame rate, no B-frames, low latency.

The objective is compatibility, not maximum compression.

## 12. Stream profiles

One profile in the MVP. Token `Profile_1`, name `MainStream`, H264,
1920x1080@30, 4000 Kbps, GOP 30.

## 13–15. RTSP

Default port **8554**, path **`/live`**, so `rtsp://<PC-IP>:8554/live`. The URI
returned through ONVIF must match the actual endpoint. VLC must play it before
any VMS integration is attempted.

Primary transport is RTP over RTSP/TCP; UDP optional later. Handle `OPTIONS`,
`DESCRIBE`, `SETUP`, `PLAY`, `TEARDOWN`.

`DESCRIBE` must return valid SDP correctly describing H.264, the payload type,
clock rate, packetisation mode, SPS/PPS and the track identifier:

```
v=0
o=- ...
s=Screen2VMS
t=0 0
m=video 0 RTP/AVP 96
c=IN IP4 ...
a=rtpmap:96 H264/90000
a=fmtp:96 packetization-mode=1;profile-level-id=...
a=control:trackID=0
```

## 16–18. ONVIF architecture and endpoints

Implement `OnvifDeviceService`, `OnvifMediaService`, `OnvifDiscoveryService`,
`OnvifAuthenticationService`. Do not implement all ONVIF services.

WS-Discovery on multicast `239.255.255.250:3702`, responding to Probe requests,
identifying as `NetworkVideoTransmitter` and exposing the device service address.

ONVIF HTTP on port **8000** by default:
`http://<ip>:8000/onvif/device_service` and `/onvif/media_service`. A single
endpoint serving multiple services is acceptable. Ports are configurable.

## 19. Device information

| Field | Value |
|---|---|
| Manufacturer | Screen2VMS |
| Model | Virtual Camera |
| FirmwareVersion | 0.1.0 |
| SerialNumber | generated persistent UUID |
| HardwareId | Screen2VMS-VirtualCamera |

The serial number must remain stable across restarts. Generate once, store in
configuration.

## 20. Capabilities

Device service: supported. Media service: supported. Events, PTZ, imaging,
analytics, recording, search: not supported.

## 21–23. Media profile and stream URI

One profile: token `Profile_1`, name `MainStream`, video source token
`VideoSource_1`, video encoder token `VideoEncoder_1`.

Implement first — device service: `GetDeviceInformation`, `GetCapabilities`,
`GetServices`. Media service: `GetProfiles`, `GetProfile`, `GetStreamUri`.
Prefer also `GetVideoSources`, `GetVideoSourceConfigurations`,
`GetVideoEncoderConfigurations`, `GetVideoEncoderConfigurationOptions`.
`SetVideoEncoderConfiguration` can come later.

`GetStreamUri` returns `rtsp://<device-ip>:8554/live`, generated from the actual
network interface. Never hard-code `127.0.0.1`.

> **Correction:** §22's list is incomplete and §23's address selection is wrong.
> See `CLAUDE.md`.

## 24–25. Authentication and security

HTTP Digest authentication, username `admin`, configurable password. Do not ship
a universal production password; `admin`/`admin` is for development only and the
application must force a change before release. Support WS-Security
UsernameToken as required for Profile S.

LAN security for the MVP: digest authentication, configurable credentials, no
exposure to the public internet, bind to LAN interfaces, optional
localhost-only mode, no cloud communication. Later: HTTPS, TLS, Profile T,
certificates.

Structure the authentication and service layer so Profile T can be added later —
Profile S is deprecated for new conformance submissions after 31 March 2027.

> **Correction:** WS-Security UsernameToken is the primary scheme in practice,
> not the optional one. See `CLAUDE.md`.

## 26–27. Network interfaces and firewall

Enumerate local IPv4 addresses, ignoring virtual adapters by default, and let the
user choose an interface or Auto. RTSP and ONVIF endpoints must use a reachable
LAN address.

Optionally create Windows Firewall inbound rules named `Screen2VMS RTSP`,
`Screen2VMS ONVIF`, `Screen2VMS WS-Discovery` for TCP 8554, TCP 8000 and UDP
3702. Always display the ports in use.

## 28. User interface

Keep it extremely simple: camera, resolution, frame rate and bitrate selectors;
camera status (state, resolution, fps, encoder, bitrate); the RTSP URI with a
Copy button; ONVIF status and endpoint; Start and Stop; a View Logs button.

## 29–30. Start and stop behaviour

The application starts stopped. On **Start**: open camera, start encoder, start
RTSP server, start ONVIF service, start WS-Discovery, update status, display the
RTSP URI and ONVIF endpoint, begin serving.

On **Stop**: stop accepting new RTSP connections, stop the ONVIF service, stop
the encoder, stop capture, release the camera, close ports, update the UI.
Existing RTSP clients should get a clean disconnect where possible.

## 31–32. Camera disconnect and contention

If the webcam is disconnected, show "Camera disconnected", do not crash, and
retry every 5 seconds. On reconnect, reinitialise capture, restart the encoder
and resume the stream without an application restart.

If another application owns the webcam, show "Camera is currently unavailable or
being used by another application", do not crash, and allow a retry.

## 33. Client limit

At least 3 simultaneous RTSP clients; preferably unlimited within CPU and network
limits. **The source must be encoded once.** Do not create one encoder per
client.

## 34–36. Latency, performance, health

Target under 500 ms end-to-end on the LAN, preferably under 300 ms, using a short
GOP, a low-latency encoder, no B-frames, minimal buffering and RTSP/TCP. Do not
optimise aggressively before compatibility is proven.

On a typical modern i5/i7 laptop at 1080p30: under 15% CPU with a hardware
encoder, higher with software fallback; under 250 MB memory; no leak over 24
hours.

Expose the state of camera, encoder, RTSP, ONVIF and discovery, plus client
count, current fps, current bitrate, dropped frames and encoded frames.

## 37. Logging

Log `ApplicationStarted`, `CameraEnumerated`, `CameraSelected`, `CameraStarted`,
`CameraStopped`, `CameraDisconnected`, `CameraReconnected`, `EncoderStarted`,
`EncoderStopped`, `RtspServerStarted`, `RtspClientConnected`,
`RtspClientDisconnected`, `OnvifStarted`, `OnvifProbeReceived`,
`OnvifClientConnected`, `OnvifAuthenticationSuccess`,
`OnvifAuthenticationFailure`, `StreamStarted`, `StreamStopped`, `Exception`.

**Never log passwords.**

## 38–44. Structure and interfaces

Projects: `Screen2VMS.App`, `.Core`, `.Camera`, `.Encoding`, `.Rtsp`, `.Onvif`,
`.Discovery`, `.Configuration`, `.Logging`, `.Tests`. Later: `.Service`,
`.Installer`.

Interfaces: `ICameraSource`, `IEncoder`, `IRtspServer`, `IOnvifDeviceService`,
`IOnvifMediaService`, `IWsDiscoveryService`, `IStreamManager`,
`IConfigurationService`, `IHealthMonitor`. Depend on interfaces, not
implementations.

`ICameraSource` exposes Id, Name, Manufacturer, Capabilities, IsRunning, and
Start/Stop/GetFrame/Dispose. Do not expose framework-specific objects throughout
the application.

`IVideoEncoder` exposes Codec, Width, Height, FrameRate, Bitrate,
IsHardwareAccelerated, and Start/Encode/Stop, with encoded output behind a
reusable encoded-frame abstraction.

`IStreamManager` owns camera, encoder and RTSP, and is responsible for starting,
stopping, restarting, monitoring and reporting statistics.

**The RTSP server consumes encoded frames and must not capture the camera
itself.** **The ONVIF layer must know nothing about webcam capture** — it exposes
virtual camera metadata and stream configuration only.

## 45–46. VMS integration tests

**Genetec:** start Screen2VMS with the camera running on the same LAN as the
Genetec server. In Config Tool, add a video unit, choose ONVIF, discover.
Screen2VMS must appear. Authenticate, add, verify the unit, verify live video,
start recording, verify the recording, restart Screen2VMS and verify the stream
reconnects.

**XProtect:** start Screen2VMS and the webcam. In Management Client, add
hardware, select the ONVIF driver, discover. Screen2VMS must appear.
Authenticate, add and enable the camera. In Smart Client, add it to a view and
verify live video, enable recording and verify it, restart Screen2VMS and verify
recovery.

## 47–49. Local verification

VLC must work before any VMS is attempted: `vlc rtsp://<IP>:8554/live` shows live
webcam video. If VLC fails, do not proceed to VMS troubleshooting.

Use an ONVIF client/test tool to verify WS-Discovery, `GetDeviceInformation`,
`GetCapabilities`, `GetServices`, `GetProfiles`, `GetStreamUri` and then the RTSP
URI.

Provide optional debug logging of WS-Discovery, SOAP request/response, RTSP
request/response and H.264 statistics, disabled by default, at levels Normal /
Debug / Trace, writing to `%ProgramData%\Screen2VMS\Logs\trace.log`. Never log
passwords.

## 50–53. Test matrix

**Network:** same subnet, Wi-Fi, Ethernet, firewall on and off, multiple
adapters, VPN adapter present, no internet, DHCP, static IP, IP change.

**Resolution:** 640x480, 1280x720, 1920x1080. Do not expose higher resolutions
until tested.

**Frame rate:** 15, 20, 25, 30. If unsupported, select the nearest supported rate.

**Failure cases:** no camera; camera disconnected; camera connected after start;
camera occupied by Teams, Zoom or a browser; invalid username; invalid password;
ONVIF port unreachable; RTSP port unreachable; firewall blocking; IP change;
encoder failure; RTSP client disconnect; VMS disconnect; application restart;
Windows sleep/wake; laptop lid close/open.

## 54–55. Sleep/resume and single instance

On sleep, stop or suspend capture cleanly. On resume, detect camera state,
restart capture and reinitialise the encoder as needed; RTSP must become
available again automatically.

Only one instance runs by default, enforced with a named mutex. A second
instance focuses the existing one and shows "Screen2VMS is already running."

## 56–57. Configuration

```json
{
  "camera":    { "deviceId": "...", "name": "Integrated Camera",
                 "width": 1920, "height": 1080, "fps": 30 },
  "encoder":   { "codec": "H264", "bitrateKbps": 4000, "gop": 30,
                 "hardwareAcceleration": true },
  "rtsp":      { "enabled": true, "port": 8554, "path": "/live" },
  "onvif":     { "enabled": true, "port": 8000, "username": "admin" },
  "discovery": { "enabled": true, "port": 3702 }
}
```

Defaults: RTSP 8554/TCP, ONVIF 8000/TCP, WS-Discovery 3702/UDP. Ports are
changeable; the MVP UI need only expose RTSP and ONVIF. **Do not store plaintext
production passwords in source control.**

## 58–60. Application modes and API

GUI mode only for the MVP; a Windows service mode comes later, so the capture and
streaming engine must not be tightly coupled to WPF. Do not implement IPC unless
the MVP needs it. No REST API in the MVP; a later one would offer
`GET /api/status`, `/api/cameras`, `/api/config`, `PUT /api/config`,
`POST /api/start`, `/api/stop`, `/api/restart`.

## 61–63. Install, uninstall, observability

The installer places the executable, dependencies, configuration directory and
log directory, and optionally adds firewall rules. Do not start at boot in the
MVP unless explicitly enabled.

The uninstaller removes binaries and firewall rules, and asks before removing
configuration or logs. Never silently delete user configuration.

The dashboard shows camera, encoder, RTSP, ONVIF, client count, fps, bitrate and
dropped frames.

## 64. Automated testing

**Unit:** camera configuration, configuration serialisation, stream URI
generation, ONVIF XML generation, authentication, profile generation, H.264
configuration, network interface selection.

**Integration:** camera→encoder, encoder→RTSP, RTSP→VLC, ONVIF→ONVIF client,
ONVIF→RTSP.

**End-to-end:** camera→Screen2VMS→Genetec, camera→Screen2VMS→XProtect.

## 65–67. Compatibility priority and ONVIF strategy

Priority order: H.264, RTSP/TCP, WS-Discovery, ONVIF device service, ONVIF media
service, digest authentication, VLC, Genetec, Milestone. Do not chase advanced
ONVIF compliance before these work.

Evaluate existing .NET SOAP/ONVIF libraries before implementing from the
specification. If library compatibility is poor, implement a minimal custom SOAP
service driven by the actual requests Genetec, XProtect and ONVIF test clients
generate. Build only the required operations.

The goal is not "valid ONVIF XML" but "ONVIF accepted by Genetec and XProtect".
Keep captured request/response examples in `docs/protocol/genetec/` and
`docs/protocol/xprotect/`. Do not commit credentials.

> **Correction:** no mature .NET ONVIF *server* library exists. See `CLAUDE.md`.

## 68–69. Development phases

| Phase | Work | Acceptance |
|---|---|---|
| 1 | Camera enumeration, selection, preview | WPF displays live webcam |
| 2 | H.264 encoding | Camera produces valid H.264 |
| 3 | RTSP server | VLC plays `rtsp://IP:8554/live` |
| 4 | WS-Discovery, device and media services | ONVIF client discovers the device |
| 5 | Genetec | Genetec discovers, adds and displays |
| 6 | XProtect | XProtect discovers, adds and displays |
| 7 | Stability | 24-hour run; memory, CPU, reconnect, disconnect, sleep/resume |

Do not build the whole system at once. Each layer must be independently
validated before the next begins.

## 70. Definition of done

On a Windows 11 laptop at 192.168.1.50 with Screen2VMS running and the built-in
webcam active: open Genetec Config Tool, add an ONVIF device, click discovery.
"Screen2VMS Virtual Camera" appears. Select it, authenticate, and one video
channel is offered. Open live view and see the webcam. Start recording and
recording works. Repeat exactly the same test with Milestone XProtect.

## 71. Roadmap after the MVP

| Version | Adds |
|---|---|
| 0.2 | Screen capture — whole display, a specific display, an application window |
| 0.3 | Multiple streams (main 1080p30, sub 640x360@10) |
| 0.4 | Audio from the webcam microphone, AAC/G.711 |
| 0.5 | ONVIF Profile T, HTTPS/TLS, improved digest, certificates |
| 0.6 | Windows service, start at boot, run without a logged-in user |
| 0.7 | Hardware encoding optimisation — QSV, NVENC, AMD |
| 0.8 | Camera controls — brightness, contrast, exposure, focus |
| 1.0 | Commercial release: installer, licensing, auto-update, diagnostics, support bundle, configuration backup, remote administration |

## 72. Product principle

Do not copy DeskCamera feature for feature. The first product is exactly this:
*turn a Windows webcam into an ONVIF IP camera that works with Genetec and
XProtect.* Nothing more. The architecture should make future features possible
while the MVP stays extremely small.

## 73–75. Instructions to the development agent

Create the solution and the projects listed in §38, then implement Phase 1. The
first milestone is: **enumerate Windows webcams and display a live preview in
WPF.** Do not start ONVIF until the camera pipeline works.

Do not over-engineer the MVP or implement anything out of scope. Use mature
libraries for H.264 encoding, RTSP, ONVIF and WS-Discovery wherever possible; do
not implement video codecs or RTSP from scratch unless there is no practical
alternative.

Prioritise, in order: working code, Genetec compatibility, XProtect
compatibility, VLC compatibility, stability, clean architecture, performance,
advanced features.

At every phase: build, run, test, fix, document, commit. Do not proceed to the
next phase until the current one has an objective acceptance test.

**First acceptance test.** Running `Screen2VMS.exe` displays "Integrated Camera"
and a live webcam preview. Pressing Start shows `Camera: Running`,
`Resolution: 1920x1080`, `FPS: 30`. No RTSP or ONVIF functionality is required
for this milestone. Once it works, implement H.264, then RTSP, then ONVIF, then
VMS integration.
