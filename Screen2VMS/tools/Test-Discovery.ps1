# WS-Discovery probe: does Screen2VMS answer the way a VMS expects? (spec 17)
$multicast = [System.Net.IPAddress]::Parse("239.255.255.250")
$port = 3702
$messageId = "uuid:" + [Guid]::NewGuid().ToString()

$probe = @"
<?xml version="1.0" encoding="UTF-8"?>
<e:Envelope xmlns:e="http://www.w3.org/2003/05/soap-envelope"
            xmlns:w="http://schemas.xmlsoap.org/ws/2004/08/addressing"
            xmlns:d="http://schemas.xmlsoap.org/ws/2005/04/discovery"
            xmlns:dn="http://www.onvif.org/ver10/network/wsdl">
  <e:Header>
    <w:MessageID>$messageId</w:MessageID>
    <w:To e:mustUnderstand="true">urn:schemas-xmlsoap-org:ws:2005:04:discovery</w:To>
    <w:Action e:mustUnderstand="true">http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</w:Action>
  </e:Header>
  <e:Body>
    <d:Probe><d:Types>dn:NetworkVideoTransmitter</d:Types></d:Probe>
  </e:Body>
</e:Envelope>
"@

$bytes = [System.Text.Encoding]::UTF8.GetBytes($probe)
$client = New-Object System.Net.Sockets.UdpClient
$client.Client.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::Socket, [System.Net.Sockets.SocketOptionName]::ReuseAddress, $true)
# Bind to the LAN address so the probe leaves via the real adapter rather than
# a virtual one, which is how a VMS on the same subnet would send it.
$localIp = (Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike "127.*" -and $_.IPAddress -notlike "172.*" -and $_.IPAddress -notlike "169.254.*" } | Select-Object -First 1).IPAddress
if (-not $localIp) { $localIp = "0.0.0.0" }
Write-Host "probing from $localIp"
$client.Client.Bind((New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Parse($localIp), 0)))
$client.Client.SetSocketOption([System.Net.Sockets.SocketOptionLevel]::IP, [System.Net.Sockets.SocketOptionName]::MulticastInterface, [System.Net.IPAddress]::HostToNetworkOrder([BitConverter]::ToInt32([System.Net.IPAddress]::Parse($localIp).GetAddressBytes(), 0)))
$client.Client.ReceiveTimeout = 4000

Write-Host "Sending WS-Discovery Probe for NetworkVideoTransmitter to 239.255.255.250:3702 ..."
[void]$client.Send($bytes, $bytes.Length, (New-Object System.Net.IPEndPoint($multicast, $port)))

$deadline = (Get-Date).AddSeconds(5)
$found = @()

while ((Get-Date) -lt $deadline) {
    try {
        $remote = New-Object System.Net.IPEndPoint([System.Net.IPAddress]::Any, 0)
        $data = $client.Receive([ref]$remote)
        $text = [System.Text.Encoding]::UTF8.GetString($data)
        if ($text -match "ProbeMatch") { $found += @{ From = $remote.Address.ToString(); Xml = $text } }
    } catch { break }
}

$client.Close()

Write-Host ""
if ($found.Count -eq 0) {
    Write-Host "  FAIL  No ProbeMatch received." -ForegroundColor Red
    exit 1
}

$fails = 0
foreach ($m in $found) {
    Write-Host "=== ProbeMatch from $($m.From) ===" -ForegroundColor Cyan
    $x = $m.Xml

    function Check($name, $ok, $detail = "") {
        if ($ok) { Write-Host ("  PASS  {0,-46} {1}" -f $name, $detail) }
        else { Write-Host ("  FAIL  {0,-46} {1}" -f $name, $detail) -ForegroundColor Red; $script:fails++ }
    }

    Check "advertises NetworkVideoTransmitter" ($x -match "NetworkVideoTransmitter")
    Check "has the Profile/Streaming scope" ($x -match "Profile/Streaming")
    Check "has the video_encoder type scope" ($x -match "type/video_encoder")
    Check "has a name scope" ($x -match "onvif://www.onvif.org/name/")
    Check "has a hardware scope" ($x -match "onvif://www.onvif.org/hardware/")

    if ($x -match "<[^>]*XAddrs>([^<]+)<") {
        $xaddrs = $Matches[1]
        Check "publishes device service XAddrs" ($xaddrs -match "onvif/device_service") $xaddrs
        Check "  XAddrs is not a loopback address" ($xaddrs -notmatch "127\.0\.0\.1") $xaddrs
    } else {
        Check "publishes device service XAddrs" $false "(no XAddrs element)"
    }
}

Write-Host ""
if ($fails -eq 0) { Write-Host "=== WS-Discovery OK ===" -ForegroundColor Green } else { Write-Host "=== $fails discovery check(s) failed ===" -ForegroundColor Red }
exit $fails
