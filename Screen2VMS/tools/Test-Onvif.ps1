# ONVIF conformance probe for Screen2VMS (phase 4 acceptance).
param(
    [string]$Host2 = "127.0.0.1",
    [int]$Port = 8000,
    [string]$User = "admin",
    [string]$Pass = "Screen2VMS!probe"
)

$DeviceNs = "http://www.onvif.org/ver10/device/wsdl"
$MediaNs  = "http://www.onvif.org/ver10/media/wsdl"
$rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider

function New-WsSecurityHeader([string]$u, [string]$p) {
    $nonceBytes = New-Object byte[] 16
    $rng.GetBytes($nonceBytes)
    $created = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")

    $stream = New-Object System.IO.MemoryStream
    $stream.Write($nonceBytes, 0, $nonceBytes.Length)
    $cb = [System.Text.Encoding]::UTF8.GetBytes($created); $stream.Write($cb, 0, $cb.Length)
    $pb = [System.Text.Encoding]::UTF8.GetBytes($p);       $stream.Write($pb, 0, $pb.Length)

    $sha = [System.Security.Cryptography.SHA1]::Create()
    $digest = [Convert]::ToBase64String($sha.ComputeHash($stream.ToArray()))
    $nonce  = [Convert]::ToBase64String($nonceBytes)

    $wsse = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd"
    $wsu  = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"
    $pt   = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"
    $et   = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"

    return "<s:Header><Security s:mustUnderstand=`"1`" xmlns=`"$wsse`"><UsernameToken><Username>$u</Username><Password Type=`"$pt`">$digest</Password><Nonce EncodingType=`"$et`">$nonce</Nonce><Created xmlns=`"$wsu`">$created</Created></UsernameToken></Security></s:Header>"
}

function Invoke-Onvif {
    param([string]$Path, [string]$Ns, [string]$Op, [string]$Inner = "", [string]$Password = $null, [switch]$NoAuth)

    $hdr = if ($NoAuth) { "" } else { New-WsSecurityHeader $User ($(if ($Password) { $Password } else { $Pass })) }
    $envelope = "<s:Envelope xmlns:s=`"http://www.w3.org/2003/05/soap-envelope`">$hdr<s:Body><$Op xmlns=`"$Ns`">$Inner</$Op></s:Body></s:Envelope>"
    $ct = "application/soap+xml; charset=utf-8; action=`"$Ns/$Op`""
    $uri = "http://${Host2}:${Port}$Path"

    try {
        $r = Invoke-WebRequest -Uri $uri -Method Post -Body $envelope -ContentType $ct -UseBasicParsing -TimeoutSec 25
        return @{ Code = [int]$r.StatusCode; Body = $r.Content }
    } catch {
        $c = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        return @{ Code = $c; Body = "" }
    }
}

function Get-NodeText([string]$xmlText, [string]$xpath) {
    try {
        $x = [xml]$xmlText
        $ns = New-Object System.Xml.XmlNamespaceManager($x.NameTable)
        $ns.AddNamespace("tt", "http://www.onvif.org/ver10/schema")
        $ns.AddNamespace("tds", $DeviceNs)
        $ns.AddNamespace("trt", $MediaNs)
        $n = $x.SelectSingleNode($xpath, $ns)
        if ($n) { return $n.InnerText } else { return "(absent)" }
    } catch { return "(unparseable)" }
}

$okCount = 0; $failCount = 0
function Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { $script:okCount++; Write-Host ("  PASS  {0,-42} {1}" -f $name, $detail) }
    else     { $script:failCount++; Write-Host ("  FAIL  {0,-42} {1}" -f $name, $detail) -ForegroundColor Red }
}

Write-Host ""
Write-Host "=== Authentication ===" -ForegroundColor Cyan
$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetSystemDateAndTime" -NoAuth
Check "GetSystemDateAndTime without credentials" ($r.Code -eq 200) "HTTP $($r.Code)"

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetDeviceInformation"
Check "GetDeviceInformation with credentials" ($r.Code -eq 200) "HTTP $($r.Code)"

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetDeviceInformation" -Password "definitely-wrong"
Check "GetDeviceInformation rejects a bad password" ($r.Code -eq 401) "HTTP $($r.Code)"

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetDeviceInformation" -NoAuth
Check "GetDeviceInformation rejects no credentials" ($r.Code -eq 401) "HTTP $($r.Code)"

Write-Host ""
Write-Host "=== Device service ===" -ForegroundColor Cyan
$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetDeviceInformation"
$serial = Get-NodeText $r.Body "//tds:SerialNumber"
Check "GetDeviceInformation returns a serial" ($serial -ne "(absent)" -and $serial.StartsWith("S2V-")) $serial
Check "  manufacturer" ((Get-NodeText $r.Body "//tds:Manufacturer") -eq "Screen2VMS") (Get-NodeText $r.Body "//tds:Manufacturer")
Check "  model" ((Get-NodeText $r.Body "//tds:Model") -eq "Virtual Camera") (Get-NodeText $r.Body "//tds:Model")

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetCapabilities" "<Category>All</Category>"
Check "GetCapabilities advertises the media XAddr" ($r.Body -match "onvif/media_service") (Get-NodeText $r.Body "//tt:Media/tt:XAddr")
Check "  advertises RTP over RTSP/TCP" ($r.Body -match "RTP_RTSP_TCP>true")

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetServices" "<IncludeCapability>false</IncludeCapability>"
Check "GetServices lists device and media" (($r.Body -match "ver10/device/wsdl") -and ($r.Body -match "ver10/media/wsdl"))

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetScopes"
Check "GetScopes has the Profile/Streaming scope" ($r.Body -match "Profile/Streaming")
Check "  has the video_encoder type scope" ($r.Body -match "type/video_encoder")
Check "  has a name scope" ($r.Body -match "onvif://www.onvif.org/name/")

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetNetworkInterfaces"
$mac = Get-NodeText $r.Body "//tt:HwAddress"
Check "GetNetworkInterfaces returns a MAC" ($mac -match "^([0-9A-F]{2}:){5}[0-9A-F]{2}$") $mac

$r = Invoke-Onvif "/onvif/device_service" $DeviceNs "GetServiceCapabilities"
Check "GetServiceCapabilities advertises both auth schemes" (($r.Body -match 'UsernameToken="true"') -and ($r.Body -match 'HttpDigest="true"'))

Write-Host ""
Write-Host "=== Media service ===" -ForegroundColor Cyan
$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetProfiles"
Check "GetProfiles returns a profile" ($r.Body -match 'token="Profile_1"') "HTTP $($r.Code)"
Check "  profile is fixed (ready to stream)" ($r.Body -match 'fixed="true"')
Check "  encoding is H264" ($r.Body -match "<tt:Encoding>H264</tt:Encoding>" -or $r.Body -match "H264")

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetStreamUri" "<StreamSetup><Stream xmlns=`"http://www.onvif.org/ver10/schema`">RTP-Unicast</Stream><Transport xmlns=`"http://www.onvif.org/ver10/schema`"><Protocol>RTSP</Protocol></Transport></StreamSetup><ProfileToken>Profile_1</ProfileToken>"
$streamUri = Get-NodeText $r.Body "//tt:Uri"
Check "GetStreamUri returns an RTSP URI" ($streamUri -match "^rtsp://") $streamUri
Check "  URI is not loopback-hardcoded for a LAN client" ($streamUri -notmatch "0\.0\.0\.0")

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetSnapshotUri" "<ProfileToken>Profile_1</ProfileToken>"
$snapUri = Get-NodeText $r.Body "//tt:Uri"
Check "GetSnapshotUri returns an HTTP URI" ($snapUri -match "^http://") $snapUri

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetVideoSources"
Check "GetVideoSources returns a source" ($r.Body -match 'token="VideoSource_1"')

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetProfile" "<ProfileToken>Profile_1</ProfileToken>"
Check "GetProfile (singular) returns the profile" ($r.Body -match 'token="Profile_1"') "HTTP $($r.Code)"

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetVideoSourceConfigurationOptions" "<ConfigurationToken>VideoSourceConfig_1</ConfigurationToken><ProfileToken>Profile_1</ProfileToken>"
Check "GetVideoSourceConfigurationOptions responds" ($r.Code -eq 200) "HTTP $($r.Code)"

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetVideoEncoderConfigurations"
Check "GetVideoEncoderConfigurations returns one" ($r.Body -match 'token="VideoEncoder_1"')

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetVideoEncoderConfigurationOptions" "<ConfigurationToken>VideoEncoder_1</ConfigurationToken><ProfileToken>Profile_1</ProfileToken>"
Check "GetVideoEncoderConfigurationOptions returns H264 options" ($r.Body -match "H264")

$r = Invoke-Onvif "/onvif/media_service" $MediaNs "GetServiceCapabilities"
Check "Media GetServiceCapabilities advertises SnapshotUri" ($r.Body -match 'SnapshotUri="true"')

Write-Host ""
Write-Host "=== Snapshot endpoint ===" -ForegroundColor Cyan
try {
    $snap = Invoke-WebRequest -Uri $snapUri -UseBasicParsing -TimeoutSec 25
    $bytes = $snap.RawContentLength
    $isJpeg = $snap.Content[0] -eq 0xFF -and $snap.Content[1] -eq 0xD8
    Check "Snapshot returns JPEG bytes" ($snap.StatusCode -eq 200 -and $isJpeg) "$bytes bytes, content-type $($snap.Headers['Content-Type'])"
    $tmp = Join-Path $env:TEMP ("snap_" + (Get-Random) + ".jpg")
    [System.IO.File]::WriteAllBytes($tmp, $snap.Content)
    Write-Host "        saved to $tmp"
} catch {
    Check "Snapshot returns JPEG bytes" $false $_.Exception.Message
}

Write-Host ""
Write-Host ("=== {0} passed, {1} failed ===" -f $okCount, $failCount) -ForegroundColor $(if ($failCount -eq 0) { "Green" } else { "Red" })
exit $failCount
