# Screen2VMS

Turn a Windows webcam into an ONVIF IP camera that Genetec Security Center and
Milestone XProtect can discover, stream and record — no VMS plugin required.

```
Windows webcam → Screen2VMS → H.264 → RTSP + ONVIF → Genetec / XProtect
```

Screen2VMS presents itself on the LAN as an ONVIF Profile-S compatible video
transmitter. A VMS finds it by WS-Discovery, reads its media profile over ONVIF,
and pulls H.264 over RTSP exactly as it would from a physical IP camera.

**Version 0.1 — the streaming device is complete and independently verified.
Acceptance against Genetec and XProtect themselves is still outstanding.**

---

## What works

| | |
|---|---|
| Camera capture | Media Foundation, NV12, closest-supported-mode negotiation |
| Encoding | H.264 Main profile, CBR, 1-second GOP, no B-frames |
| Streaming | RTSP over TCP, multiple simultaneous clients from one encode |
| Discovery | ONVIF WS-Discovery on UDP 3702 with Profile-S scopes |
| ONVIF | Device and media services, 28 operations, snapshots |
| Security | WS-Security UsernameToken and HTTP Digest, per-install password |
| Recovery | Camera disconnect, camera busy, sleep and resume |

Verified independently of the code that produced it: ffmpeg confirms the camera
enumeration and delivered frame rate, ffmpeg decodes the H.264 with zero errors,
VLC plays the RTSP stream over the LAN address, three concurrent clients receive
byte-identical output, and an ONVIF probe drives 28 operations including
negative authentication tests.

Not yet done: adding the unit inside Genetec Config Tool or the XProtect
Management Client. That needs the VMS software.

---

## Requirements

- Windows 10 or 11, 64-bit
- A webcam
- The .NET 8 SDK, only if you are building from source. The published
  executable carries its own runtime and needs nothing installed.

No administrator rights for normal operation, no internet connection, no cloud
account, no licence server and no database. Optionally one UAC prompt if you use
the built-in button to add firewall rules.

---

## Build and run

```bash
cd Screen2VMS
dotnet build
dotnet run --project src/Screen2VMS.App
```

Pick a camera, resolution, frame rate and bitrate, then press **Start**. The
window then shows everything needed to add the camera to a VMS:

- the ONVIF address, e.g. `http://192.168.1.50:8000/onvif/device_service`
- the RTSP address, e.g. `rtsp://192.168.1.50:8554/live`
- the user name and the password generated for this installation

The RTSP stream requires those same credentials, which is what a real IP camera
does and what stops anyone on the LAN from watching. A VMS handles this by
itself. To open the stream by hand, put them in the URL:

```
vlc rtsp://admin:<password>@192.168.1.50:8554/live
```

Set `rtsp.requireAuthentication` to `false` in `config.json` if you need an
anonymous stream to diagnose a client that cannot authenticate.

If the requested capture mode is unavailable the closest supported one is used
rather than failing — asking for 1920x1080 at 30 fps on a camera that tops out
at 1280x720 gets you 1280x720, not an error.

## Building a standalone executable

```powershell
.\tools\Publish.ps1
```

Produces `dist\Screen2VMS.exe`: one self-contained file of about 79 MB with the
.NET runtime, every dependency and the native WPF libraries bundled inside it.
Copy it anywhere and double-click. There is no installer, nothing to install
beside it, and no .NET runtime needed on the machine. It still keeps its
settings and logs in `%ProgramData%\Screen2VMS\`.

Pass `-Compress $false` for a larger file that starts a little faster.

Two things about that script are deliberate:

- The publish switches live in the script rather than the `.csproj`. Setting a
  runtime identifier in the project would move every ordinary build into a
  `win-x64` subfolder and break the paths the tests and dev loop already use.
- Trimming is off. CoreWCF, the XML serialisers and WPF all resolve types by
  reflection, and a trimmed build fails at runtime rather than at publish time.

Run the tests with:

```bash
dotnet test tests/Screen2VMS.Tests
```

73 tests covering mode negotiation, H.264 bitstream parsing, pixel conversion,
ONVIF discovery scopes, credential protection, configuration round-tripping and
the ONVIF host's start/stop lifecycle. None of them need a camera.

---

## Adding it to a VMS

1. Put the machine on the same subnet as the VMS server. WS-Discovery is
   multicast and does not cross subnets; from another VLAN, add the device by IP
   instead.
2. Press **Firewall Rules** once if inbound traffic is blocked. This is the only
   action that asks for administrator rights.
3. In the VMS, add an ONVIF device and run discovery. "Screen2VMS Virtual
   Camera" should appear.
4. Authenticate with the user name and password from the window.

**Genetec Security Center** — Config Tool → add video unit → ONVIF → discover.

**Milestone XProtect** — Management Client → Add Hardware → ONVIF driver →
discover.

---

## Where things are kept

```
%ProgramData%\Screen2VMS\config.json    settings, device identity, credential
%ProgramData%\Screen2VMS\Logs\          daily rolling logs
```

`config.json` holds a serial number, a synthetic MAC address and an encrypted
password, all generated on first run. A VMS uses the serial and MAC to recognise
the camera, so they never change once written. Deleting the file resets them and
any VMS that had already added the unit will see a different camera.

---

## Ports

| Port | Protocol | Purpose |
|---|---|---|
| 8554 | TCP | RTSP |
| 8000 | TCP | ONVIF HTTP and JPEG snapshots |
| 3702 | UDP | WS-Discovery |

---

## Project layout

```
Screen2VMS.sln
├── src/
│   ├── Screen2VMS.Core              interfaces, models, pure logic
│   ├── Screen2VMS.MediaFoundation   COM interop, shared by capture and encoding
│   ├── Screen2VMS.Camera            Media Foundation capture
│   ├── Screen2VMS.Encoding          H.264 encoder
│   ├── Screen2VMS.Rtsp              RTSP server
│   ├── Screen2VMS.Onvif             ONVIF device and media services
│   ├── Screen2VMS.Discovery         WS-Discovery scopes
│   ├── Screen2VMS.Configuration     JSON configuration and credential storage
│   ├── Screen2VMS.Engine            pipeline, runtime, firewall, snapshots
│   ├── Screen2VMS.Logging           Serilog setup
│   └── Screen2VMS.App               WPF user interface
├── tests/Screen2VMS.Tests
└── docs/protocol/                   captured VMS exchanges
```

Every project depends only on `Screen2VMS.Core`. `Screen2VMS.Engine` composes
the runtime and is deliberately free of WPF, so the same engine can run as a
Windows service later.

---

## Documentation

- `PROJECT_INSTRUCTIONS.md` — the full product specification
- `CLAUDE.md` — architecture, decisions, spec corrections, and the traps worth
  knowing about before changing anything
