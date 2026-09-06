# Builds Screen2VMS as one self-contained executable.
#
# The result needs no .NET runtime, no installer and no files beside it: the
# framework, every dependency and the native WPF libraries are bundled into the
# single .exe. Copy it anywhere and double-click it.
#
# These switches live here rather than in the .csproj on purpose. Setting
# RuntimeIdentifier in the project would move every ordinary build into a
# win-x64 subfolder and break the paths the tests and dev loop already use.
[CmdletBinding()]
param(
    # Where the finished executable is written.
    [string]$OutputDirectory = (Join-Path (Join-Path $PSScriptRoot "..") "dist"),

    # Compression roughly halves the file at the cost of a slower first start.
    [bool]$Compress = $true,

    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$project = Join-Path (Join-Path (Join-Path (Join-Path $PSScriptRoot "..") "src") "Screen2VMS.App") "Screen2VMS.App.csproj"
$outDir = [System.IO.Path]::GetFullPath($OutputDirectory)

if (-not (Test-Path $project)) { throw "Could not find $project" }

# A running instance holds a lock on its own binaries.
Get-Process Screen2VMS -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host "Closing a running Screen2VMS (pid $($_.Id)) so its files can be replaced."
    $_.CloseMainWindow() | Out-Null
    Start-Sleep -Seconds 3
    if (-not $_.HasExited) { $_ | Stop-Process -Force }
}

Write-Host "Publishing $Configuration, self-contained, single file..." -ForegroundColor Cyan

$arguments = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", "win-x64",
    "--self-contained", "true",
    "-o", $outDir,
    "-p:PublishSingleFile=true",

    # Without this the native WPF and Media Foundation shims are written beside
    # the executable instead of inside it, which is not "a single file".
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:IncludeAllContentForSelfExtract=true",

    # Trimming would strip the types CoreWCF, the XML serialisers and WPF
    # resolve by reflection, and the failures only appear at runtime.
    "-p:PublishTrimmed=false",

    # Keeps the folder to exactly one file: no .pdb, no localisation subfolders.
    "-p:DebugType=none",
    "-p:DebugSymbols=false",
    "-p:SatelliteResourceLanguages=en",
    "-p:GenerateDocumentationFile=false"
)

if ($Compress) { $arguments += "-p:EnableCompressionInSingleFile=true" }

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $outDir "Screen2VMS.exe"
if (-not (Test-Path $exe)) { throw "Publish reported success but $exe is missing." }

# Anything else in the folder means the bundle leaked a dependency.
$stragglers = Get-ChildItem $outDir -File | Where-Object { $_.Name -ne "Screen2VMS.exe" }

Write-Host ""
Write-Host "Executable : $exe" -ForegroundColor Green
Write-Host ("Size       : {0:N1} MB" -f ((Get-Item $exe).Length / 1MB))
Write-Host ("Other files: {0}" -f $(if ($stragglers) { ($stragglers | ForEach-Object { $_.Name }) -join ", " } else { "none - it really is a single file" }))

if ($stragglers) {
    Write-Warning "The output folder is not a single file. Check the publish switches above."
    exit 1
}
