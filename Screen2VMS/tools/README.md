# Verification scripts

These drive a running Screen2VMS from the outside, the way a VMS would. They
are how phases 3, 4 and 7 were signed off without a VMS present, and they are
the first thing to run when a real VMS refuses to add the camera.

## Test-Onvif.ps1

Drives 28 ONVIF operations against a running instance, including the negative
authentication cases.

```powershell
.\Test-Onvif.ps1 -Host2 192.168.1.50 -User admin -Pass <password from the window>
```

Checks, among others:

- `GetSystemDateAndTime` answers **without** credentials, and reports the right
  time and POSIX timezone
- a wrong password and a missing password are both rejected with 401
- `GetScopes` carries the `Profile/Streaming` and `video_encoder` scopes
- `GetNetworkInterfaces` returns the persisted MAC
- `GetStreamUri` and `GetSnapshotUri` return addresses reachable by the caller
- the snapshot endpoint returns real JPEG bytes

## Test-Discovery.ps1

Sends a WS-Discovery Probe for `NetworkVideoTransmitter` and checks the
ProbeMatch. Run it from the machine you expect the VMS to search from — it
binds to the LAN address deliberately, because probing from a virtual adapter
tests nothing useful.

```powershell
.\Test-Discovery.ps1
```

## Invoke-Soak.ps1

Runs the application with an RTSP client attached and re-connecting, sampling
working set, private bytes, handles and threads (spec 7). A leak shows as a
trend in handles or private bytes; a flat line over the run is the pass.

```powershell
.\Invoke-Soak.ps1 -Minutes 60
```

## Note on RTSP credentials

The stream requires authentication by default, so ffmpeg and VLC need them in
the URL:

```powershell
ffprobe -rtsp_transport tcp -i rtsp://admin:<password>@192.168.1.50:8554/live
```
