# Screen2VMS

Turn a Windows webcam into an ONVIF IP camera that Genetec Security Center and
Milestone XProtect can discover, stream and record — no VMS plugin required.

```
Windows webcam → Screen2VMS → H.264 → RTSP + ONVIF → Genetec / XProtect
```

Screen2VMS presents itself on the LAN as an ONVIF Profile-S compatible video
transmitter. A VMS finds it by WS-Discovery, reads its media profile over ONVIF,
and pulls H.264 over RTSP exactly as it would from a physical IP camera.

Several cameras can run at once. Each one is published as its own independent
ONVIF device with its own ports, serial number and password, so a VMS adds them
as separate units.

**Version 0.1 — the streaming device is complete and independently verified.
Acceptance against Genetec and XProtect themselves is still outstanding.**

---

## Download

Grab `Screen2VMS.exe` from the
[latest release](https://github.com/xplugindev/XPlug.Utilities/releases/latest).

One file, about 79 MB. No installer, no .NET runtime to install, nothing to
unpack — copy it anywhere and double-click it. Settings and logs go to
`%ProgramData%\Screen2VMS\`; delete that folder and the executable to remove it
completely.

The binary is **not code-signed**, so Windows SmartScreen will show "Windows
protected your PC" the first time. *More info → Run anyway* if you trust the
source. If you would rather not, build it yourself — see
[Building a standalone executable](#building-a-standalone-executable); the
script is eight lines of `dotnet publish`.

Read the scope note immediately below before you put this anywhere that matters.

---

## Scope: personal utility, not a product

This is a **personal tool**, built for one person's own projects. It sits in the
shared `XPlug.Utilities` repository next to `DesktopSessionManager`, which makes
it easy to mistake for team-owned production code. It is not.

It is published under MIT and downloadable by anyone, which is a licensing
decision and nothing more — it has not been through security review, ONVIF
conformance testing, or any release process. Several design decisions were taken
*because* it started as an internal tool, and each of them is wrong for a
product:

| Decision | Why it is fine here | Why it would not be elsewhere |
|---|---|---|
| No TLS anywhere — ONVIF, snapshots and RTSP are plaintext | Trusted LAN only | Credentials and video cross the wire in the clear |
| Credential stored with DPAPI, machine scope | Keeps it out of a config file that gets copied around | Anyone who can run code on the box can recover it |
| MIT, after a dependency audit but not a patent one | All 77 bundled packages are MIT or Apache-2.0, so redistribution is clean; ffmpeg and VLC are debugging tools that ship with nothing | AVC patent licensing was never reviewed — it rests on the Windows Media Foundation encoder rather than on anything shipped here |
| ONVIF-compatible, not ONVIF-certified | Works with the clients it was tested against | Has never seen the ONVIF Device Test Tool; Profile S is deprecated for new conformance submissions after 31 March 2027 |
| No installer, no code signing, no auto-update | Copy the .exe and run it | SmartScreen will flag it; there is no update path |

Known gaps, in case they matter to you: **Milestone XProtect has never been
tested** (Genetec 5.14 is verified, with a single camera), **running several
cameras has never been tried against either VMS**, the single-camera process settles around 354 MB against
the spec's 250 MB target, and the 24-hour soak has only ever been run for 16
minutes.

If this ever changes hands or purpose, treat that table as a blocking checklist
rather than a footnote.

---

## What works

| | |
|---|---|
| Camera capture | Media Foundation, NV12, closest-supported-mode negotiation |
| Multiple cameras | A live grid of every added camera, each an independent ONVIF device and RTSP stream |
| Encoding | H.264 Main profile, CBR, 1-second GOP, no B-frames |
| Streaming | RTSP over TCP or UDP, multiple simultaneous clients from one encode |
| Discovery | ONVIF WS-Discovery on UDP 3702 with Profile-S scopes |
| ONVIF | Device and media services, 28 operations, snapshots |
| Security | WS-Security UsernameToken and HTTP Digest, per-install password |
| Recovery | Camera disconnect, camera busy, sleep and resume |

Verified independently of the code that produced it: ffmpeg confirms the camera
enumeration and delivered frame rate, ffmpeg decodes the H.264 with zero errors,
VLC plays the RTSP stream over the LAN address, three concurrent clients receive
byte-identical output, and an ONVIF probe drives 28 operations including
negative authentication tests.

Genetec Security Center 5.14 discovers, adds and displays a single camera.
Not yet done: adding the unit in the XProtect Management Client, and adding
more than one camera to either VMS at the same time. Running two cameras'
ONVIF hosts side by side in one process, including WS-Discovery on the same
UDP port, is covered by an automated test; what a VMS makes of two
simultaneous Screen2VMS devices is not.

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

Pick a webcam under **Add a camera** and press **Add camera**. It appears as a
tile in the grid on the right with its own live preview. Choose its resolution
and frame rate on the tile, then press **Start** on the tile, or **Start All**
to start every added camera. A webcam that is already added no longer appears
in the list, and cameras are never started automatically when the app opens.

Each running tile shows everything needed to add that camera to a VMS:

- its ONVIF address, e.g. `http://192.168.1.50:8000/onvif/device_service`
- its RTSP address, e.g. `rtsp://192.168.1.50:8554/live`
- the user name and the password generated for that camera

Every camera has its own password and its own pair of ports, so copy the
details from the tile you are adding, not from another one.

The RTSP stream requires those same credentials, which is what a real IP camera
does and what stops anyone on the LAN from watching. A VMS handles this by
itself. To open the stream by hand, put them in the URL:

```
vlc rtsp://admin:<password>@192.168.1.50:8554/live
```

Set a camera's `rtsp.requireAuthentication` to `false` in `config.json` (under
its entry in `cameras`) if you need an anonymous stream to diagnose a client
that cannot authenticate.

The bitrate is no longer set in the window. It is one setting shared by every
camera, `encoder.bitrateKbps` in `config.json` (default 4000), read when a
camera starts.

Removing a camera (**Remove** on a stopped tile) deletes its identity. Adding
the same webcam again creates a new serial number, MAC and password, and a VMS
treats it as a new device.

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

86 tests covering mode negotiation, H.264 bitstream parsing, pixel conversion,
ONVIF discovery scopes, credential protection, configuration round-tripping and
migration, per-camera port allocation, the multi-camera runtime manager, the
ONVIF host's start/stop lifecycle, and two ONVIF hosts running side by side.
None of them need a camera.

---

## Adding it to a VMS

1. Put the machine on the same subnet as the VMS server. WS-Discovery is
   multicast and does not cross subnets; from another VLAN, add the device by IP
   instead.
2. Press **Firewall Rules** if inbound traffic is blocked. This is the only
   action that asks for administrator rights. It opens the ports of every camera
   added *so far*, so press it again after adding another camera.
3. Start the cameras you want to add.
4. In the VMS, add an ONVIF device and run discovery. Each running camera
   appears as its own "Screen2VMS Virtual Camera". They all carry that same
   name, so tell them apart by port: match the ONVIF address shown on each tile.
5. Authenticate with the user name and password from that camera's tile.

**Genetec Security Center** — Config Tool → add video unit → ONVIF → discover.

**Milestone XProtect** — Management Client → Add Hardware → ONVIF driver →
discover.

Firewall rules are named per camera, e.g. `Screen2VMS RTSP (Integrated Camera)`.
Two cleanups are left to you in *Windows Defender Firewall → Inbound Rules*:
removing a camera does not delete its rules, and the older single-camera rules
(`Screen2VMS RTSP` and `Screen2VMS ONVIF`, without a camera name) are not
replaced by the new ones.

---

## Where things are kept

```
%ProgramData%\Screen2VMS\config.json    settings, device identity, credential
%ProgramData%\Screen2VMS\Logs\          daily rolling logs
```

`config.json` holds one entry per added camera under `cameras`. Each entry has
its own serial number, synthetic MAC address, encrypted password and ports,
generated when the camera is added. A VMS uses the serial and MAC to recognise
the camera, so they never change once written. Deleting the file resets every
camera, and any VMS that had already added them will see different cameras.

A `config.json` from a single-camera version is migrated on first start: its
identity, password and ports become the first entry under `cameras`, so a camera
a VMS already knows stays the same camera. The old top-level `device`, `camera`,
`rtsp` and `onvif` sections stay in the file after that but are no longer read.

---

## Ports

| Port | Protocol | Purpose |
|---|---|---|
| 8554 | TCP | RTSP, first camera |
| 8000 | TCP | ONVIF HTTP and JPEG snapshots, first camera |
| 3702 | UDP | WS-Discovery, shared by every camera |

Each further camera takes the next free pair with the same offset: 8555 and
8001, then 8556 and 8002, and so on. The ports are checked only against other
cameras, not against other software on the machine. Edit a camera's `rtsp.port`
or `onvif.port` in `config.json` if one collides.

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
│   ├── Screen2VMS.Engine            pipeline, per-camera runtimes, firewall, snapshots
│   ├── Screen2VMS.Logging           Serilog setup
│   └── Screen2VMS.App               WPF user interface
├── tests/Screen2VMS.Tests
└── docs/protocol/                   captured VMS exchanges
```

Every project depends only on `Screen2VMS.Core`. `Screen2VMS.Engine` composes
the runtime and is deliberately free of WPF, so the same engine can run as a
Windows service later.

---

## Licence

Screen2VMS is released under the **MIT Licence** — see [`LICENSE`](../LICENSE)
at the root of this repository. That file covers the whole `XPlug.Utilities`
repository, `DesktopSessionManager` included, not just this folder.

The published executable is self-contained, which means the .NET runtime and all
77 dependencies are bundled inside it and redistributed with every download.
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) lists every one of them with
its licence and copyright holder: 58 MIT, 19 Apache-2.0, no copyleft anywhere in
the graph, so redistribution is unencumbered.

Two things that file makes explicit and are worth repeating here:

- **No codec is shipped.** H.264 encoding calls the encoder built into Windows
  Media Foundation, so it runs under the licence of the Windows installation
  doing the encoding. AVC patent licensing is not passed on by this download.
- **ONVIF-compatible, not ONVIF-certified.** This implements the published
  Profile S specification. It has never been through the ONVIF Device Test Tool
  and is not affiliated with or endorsed by the ONVIF organisation.

MIT means no warranty. Given the scope note above, take that literally.

---

## Documentation

- `PROJECT_INSTRUCTIONS.md` — the full product specification
- `CLAUDE.md` — architecture, decisions, spec corrections, and the traps worth
  knowing about before changing anything
