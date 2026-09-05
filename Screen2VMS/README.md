# Screen2VMS

Turn a Windows webcam into an ONVIF IP camera that Genetec Security Center and
Milestone XProtect can discover, stream and record — no VMS plugin required.

```
Windows webcam → Screen2VMS → H.264 → RTSP + ONVIF → Genetec / XProtect
```

Screen2VMS presents itself on the LAN as an ONVIF Profile-S compatible video
transmitter. A VMS finds it by WS-Discovery, reads its media profile over ONVIF,
and pulls H.264 over RTSP exactly as it would from a physical IP camera.

**Version 0.1 — in development. Phase 1 of 7 complete.**

---

## Current state

Phase 1 is finished and verified on real hardware: the application enumerates
Windows capture devices, negotiates the closest supported mode to what you ask
for, captures NV12 frames through Media Foundation, and shows a live preview.

Streaming is not implemented yet. There is no RTSP endpoint and no ONVIF service
in this build — those are phases 3 and 4.

| Phase | Delivers | State |
|---|---|---|
| 1 | Camera enumeration and live preview | Complete |
| 2 | H.264 encoding | Planned |
| 3 | RTSP server (`rtsp://<ip>:8554/live`) | Planned |
| 4 | ONVIF device/media services, WS-Discovery | Planned |
| 5 | Genetec Security Center integration | Planned |
| 6 | Milestone XProtect integration | Planned |
| 7 | Stability, reconnect, sleep/resume | Planned |

---

## Requirements

- Windows 10 or 11, 64-bit
- .NET 8 desktop runtime (or the .NET SDK to build)
- A webcam

No administrator rights, no internet connection, no cloud account, no licence
server and no database. Everything runs on the local machine and the LAN.

---

## Build and run

```bash
cd Screen2VMS
dotnet build
dotnet run --project src/Screen2VMS.App
```

Pick a camera, resolution and frame rate, then press **Start**. The status panel
shows the mode actually negotiated, the measured frame rate and the frame
counters.

If the requested mode is unavailable the closest supported one is selected
automatically rather than failing — asking for 1920x1080 at 30 fps on a camera
that tops out at 1280x720 gets you 1280x720, not an error.

Run the tests with:

```bash
dotnet test tests/Screen2VMS.Tests
```

They cover mode negotiation, pixel conversion and configuration round-tripping,
and need no camera attached.

---

## Where things are kept

```
%ProgramData%\Screen2VMS\config.json    settings and device identity
%ProgramData%\Screen2VMS\Logs\          daily rolling logs
```

`config.json` holds a serial number and a synthetic MAC address generated on
first run. A VMS uses these to recognise the camera, so they never change once
written. Deleting the file resets them, and any VMS that had already added the
unit will see a different camera afterwards.

---

## Project layout

```
Screen2VMS.sln
├── src/
│   ├── Screen2VMS.Core             interfaces, models, pure logic
│   ├── Screen2VMS.Camera           Media Foundation capture
│   ├── Screen2VMS.Encoding         H.264 encoder            (phase 2)
│   ├── Screen2VMS.Rtsp             RTSP server              (phase 3)
│   ├── Screen2VMS.Onvif            ONVIF SOAP services      (phase 4)
│   ├── Screen2VMS.Discovery        WS-Discovery             (phase 4)
│   ├── Screen2VMS.Configuration    JSON configuration
│   ├── Screen2VMS.Logging          Serilog setup
│   └── Screen2VMS.App              WPF user interface
├── tests/Screen2VMS.Tests
└── docs/protocol/                  captured VMS exchanges
```

Every project depends only on `Screen2VMS.Core`, which depends on nothing.
`Screen2VMS.App` is the only place they are wired together, which is what keeps
the streaming engine free of WPF for the eventual Windows service.

---

## Ports

Once phases 3 and 4 land, Screen2VMS listens on:

| Port | Protocol | Purpose |
|---|---|---|
| 8554 | TCP | RTSP |
| 8000 | TCP | ONVIF HTTP |
| 3702 | UDP | WS-Discovery |

WS-Discovery is multicast and does not cross subnets — a VMS on a different VLAN
will not find the device automatically and must add it by IP.

---

## Documentation

- `PROJECT_INSTRUCTIONS.md` — the full product specification
- `CLAUDE.md` — architecture notes, decisions, and the traps worth knowing about
  before changing anything
