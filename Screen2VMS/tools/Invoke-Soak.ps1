# Stability soak for Screen2VMS (spec 7 / phase 7).
# Runs the shipping application with a live RTSP client attached and samples
# process resources, so a leak or handle growth would show as a trend.
param([int]$Minutes = 15)

$exe = "D:\Projects\XPlug\XPlug.Utilities\Screen2VMS\src\Screen2VMS.App\bin\Debug\net8.0-windows\Screen2VMS.exe"
$report = "C:\Users\Prem\AppData\Local\Temp\claude\D--Projects-XPlug-XPlug-Utilities\dcc3c4d3-240c-47c2-a415-f4531804d1ca\scratchpad\soak-report.txt"
$env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")

Get-Process Screen2VMS, ffmpeg -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 3

$app = Start-Process $exe -PassThru
Start-Sleep -Seconds 10

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement
$win = $root.FindFirst(
    [System.Windows.Automation.TreeScope]::Children,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Screen2VMS")))

if ($null -eq $win) { "soak: window not found" | Out-File $report; exit 1 }

$start = $win.FindFirst(
    [System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, "Start")))
$start.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 8

$lines = @()
$lines += "Screen2VMS stability soak"
$lines += "started  : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$lines += "duration : $Minutes minutes"
$lines += ""
$lines += "elapsed  workingSet   private   handles  threads  rtspClient"
$lines += "-------  -----------  --------  -------  -------  ----------"

$deadline = (Get-Date).AddMinutes($Minutes)
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$client = $null
$restarts = 0

while ((Get-Date) -lt $deadline) {
    # Keep a client attached the whole time, restarting it as each pull ends,
    # so connect and teardown are exercised repeatedly too.
    if ($null -eq $client -or $client.HasExited) {
        if ($null -ne $client) { $restarts++ }
        $sink = Join-Path $env:TEMP "soak_sink.ts"
        $client = Start-Process ffmpeg -ArgumentList @(
            "-hide_banner","-v","error","-rtsp_transport","tcp",
            "-i","rtsp://127.0.0.1:8554/live","-t","60","-c","copy","-f","mpegts",$sink,"-y"
        ) -PassThru -WindowStyle Hidden
    }

    Start-Sleep -Seconds 30

    $p = Get-Process -Id $app.Id -ErrorAction SilentlyContinue
    if ($null -eq $p) { $lines += "PROCESS EXITED at $($clock.Elapsed.ToString('hh\:mm\:ss'))"; break }

    $lines += "{0,7}  {1,9:N0}KB  {2,6:N0}KB  {3,7}  {4,7}  {5,10}" -f `
        $clock.Elapsed.ToString('hh\:mm\:ss'),
        ($p.WorkingSet64 / 1KB),
        ($p.PrivateMemorySize64 / 1KB),
        $p.HandleCount,
        $p.Threads.Count,
        $(if ($client -and -not $client.HasExited) { "yes" } else { "no" })
}

if ($client -and -not $client.HasExited) { $client.Kill() }

$p = Get-Process -Id $app.Id -ErrorAction SilentlyContinue
$lines += ""
$lines += "client reconnects: $restarts"
$lines += "still running    : $($p -ne $null)"

if ($p) {
    $p.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 5
    if (-not $p.HasExited) { $p | Stop-Process -Force }
    $lines += "clean shutdown   : yes"
}

$lines += "finished : $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
$lines | Out-File $report -Encoding utf8
Write-Output "soak complete -> $report"
