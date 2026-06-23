param(
    [Parameter()]
    [ValidateSet('wpf', 'desktop')]
    [string]$Variant = 'wpf',

    [Parameter()]
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',

    [Parameter()]
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release',

    [switch]$SubmodulesOnly,
    [switch]$SkipSubmodules,
    [switch]$Clean
)

$ErrorActionPreference = 'Stop'
$Root = $PSScriptRoot
$V2rayNRoot = Join-Path $Root 'v2rayN'
$ArtifactsRoot = Join-Path $Root 'artifacts'

$Rid = "win-$Arch"
$OutputDir = Join-Path $ArtifactsRoot $Rid

$Projects = @{
    wpf      = 'v2rayN\v2rayN.csproj'
    desktop  = 'v2rayN.Desktop\v2rayN.Desktop.csproj'
    amaztool = 'AmazTool\AmazTool.csproj'
}

$ProxyBridgeDir  = Join-Path $V2rayNRoot 'ProxyBridge\Windows'
$NetBridgeDir    = Join-Path $V2rayNRoot 'NetBridgeBridge'
$WinDivertPath   = Join-Path $V2rayNRoot 'WinDivert-2.2.2-A'
$NetBridgeAssets = Join-Path $V2rayNRoot 'NetBridge\src\NetBridgeLib\Assets'

function Write-Step($msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "   OK: $msg" -ForegroundColor Green }
function Write-Fail($msg) { Write-Host "   FAIL: $msg" -ForegroundColor Red }

function Test-Prerequisites {
    Write-Step "Checking prerequisites"

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Fail "dotnet SDK not found"; exit 1
    }
    Write-Ok "dotnet: $(dotnet --version)"

    if (-not $SkipSubmodules) {
        if (-not (Test-Path (Join-Path $ProxyBridgeDir 'src\ProxyBridge.c'))) {
            Write-Fail "ProxyBridge submodule not initialized"; exit 1
        }
        if (-not (Test-Path (Join-Path $NetBridgeDir 'cmd\bridge\main.go'))) {
            Write-Fail "NetBridgeBridge submodule not initialized"; exit 1
        }
        Write-Ok "Submodules present"
    }
}

function Build-ProxyBridge {
    Write-Step "Building ProxyBridge (C/C++)"

    $compileScript = Join-Path $ProxyBridgeDir 'compile.ps1'
    if (-not (Test-Path $compileScript)) {
        Write-Fail "compile.ps1 not found at $compileScript"; exit 1
    }

    Push-Location $ProxyBridgeDir
    try {
        & $compileScript -Compiler auto -NoSign 2>&1 | ForEach-Object {
            if ($_ -match 'FAIL|ERROR|error C') { Write-Host "   $_" -ForegroundColor Red }
            elseif ($_ -match 'SUCCESS|Moved:|Copied:') { Write-Host "   $_" -ForegroundColor Green }
        }
    } finally {
        Pop-Location
    }

    $outputDir = Join-Path $ProxyBridgeDir 'output'
    $required = @('ProxyBridgeCore.dll', 'WinDivert.dll', 'WinDivert64.sys')
    $missing = @()
    foreach ($f in $required) {
        if (-not (Test-Path (Join-Path $outputDir $f))) {
            $missing += $f
        }
    }
    if ($missing.Count -gt 0) {
        Write-Fail "Required outputs missing: $($missing -join ', ')"
        Write-Host "   (GUI/CLI/NSIS failures are non-fatal, ignoring)" -ForegroundColor Yellow
        exit 1
    }
    Write-Ok "ProxyBridge built (ProxyBridgeCore.dll + WinDivert)"
}

function Build-NetBridgeBridge {
    Write-Step "Building NetBridgeBridge (Go)"

    if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
        Write-Fail "Go not found in PATH"; exit 1
    }

    Push-Location $NetBridgeDir
    try {
        $env:CGO_ENABLED = '0'
        $env:GOOS = 'windows'
        $env:GOARCH = if ($Arch -eq 'arm64') { 'arm64' } else { 'amd64' }

        $outFile = Join-Path $ProxyBridgeDir 'output\NetBridgeBridge.exe'
        go build -o $outFile ./cmd/bridge
        if ($LASTEXITCODE -ne 0) {
            Write-Fail "Go build failed"; exit 1
        }
    } finally {
        Pop-Location
        Remove-Item Env:CGO_ENABLED -ErrorAction SilentlyContinue
        Remove-Item Env:GOOS -ErrorAction SilentlyContinue
        Remove-Item Env:GOARCH -ErrorAction SilentlyContinue
    }

    Write-Ok "NetBridgeBridge built"
}

function Sync-SubmoduleAssets {
    Write-Step "Syncing submodule assets to NetBridgeLib/Assets"

    $srcDir  = Join-Path $ProxyBridgeDir 'output'
    $destDir = $NetBridgeAssets

    if (-not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
    }

    $files = @('ProxyBridgeCore.dll', 'WinDivert.dll', 'WinDivert64.sys')
    foreach ($f in $files) {
        $src = Join-Path $srcDir $f
        if (Test-Path $src) {
            Copy-Item $src -Destination $destDir -Force
            Write-Ok "Copied $f"
        } else {
            Write-Host "   WARN: $f not found in output, skipped" -ForegroundColor Yellow
        }
    }
}

function Build-DotNet {
    param([string]$Project, [string]$OutputPath)

    Write-Step "Building .NET: $Project ($Configuration|$Rid)"

    $extOpt = ''
    if ($Variant -eq 'wpf') {
        $extOpt = '-p:EnableWindowsTargeting=true'
    }

    $projPath = Join-Path $V2rayNRoot $Project
    $slnDir  = $V2rayNRoot

    if ($Clean) {
        Write-Host "   Cleaning..." -ForegroundColor Gray
        dotnet clean $projPath -c $Configuration -v q 2>$null
    }

    $args = @(
        'publish', $projPath,
        '-c', $Configuration,
        '-r', $Rid,
        '-p:SelfContained=true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true'
    )
    if ($extOpt) { $args += $extOpt }
    $args += @('-o', $OutputPath)

    Write-Host "   dotnet $($args -join ' ')" -ForegroundColor Gray
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "dotnet publish failed for $Project"; exit 1
    }

    Write-Ok "Published to $OutputPath"
}

function Copy-NetBridgeBridgeToOutput {
    param([string]$OutputPath)

    $src = Join-Path $ProxyBridgeDir 'output\NetBridgeBridge.exe'
    if (Test-Path $src) {
        Copy-Item $src -Destination $OutputPath -Force
        Write-Ok "Copied NetBridgeBridge.exe to output root"
    } else {
        Write-Host "   WARN: NetBridgeBridge.exe not found" -ForegroundColor Yellow
    }
}

function Show-Artifacts {
    param([string]$OutputPath)

    Write-Step "Artifacts: $OutputPath"
    if (-not (Test-Path $OutputPath)) {
        Write-Fail "Output directory does not exist"; return
    }

    Get-ChildItem $OutputPath -Recurse | ForEach-Object {
        $rel = $_.FullName.Substring($OutputPath.Length + 1)
        $size = if ($_.PSIsContainer) { '<DIR>' } else { "$([math]::Round($_.Length/1KB, 1)) KB" }
        Write-Host "   $rel  $size" -ForegroundColor Gray
    }
}

# ── Main ────────────────────────────────────────────────────────────────────

Write-Host "========================================" -ForegroundColor White
Write-Host " v2rayN Build" -ForegroundColor White
Write-Host " Variant: $Variant  Arch: $Arch  Config: $Configuration" -ForegroundColor White
Write-Host " Output:  $OutputDir" -ForegroundColor White
Write-Host "========================================" -ForegroundColor White

Test-Prerequisites

if (-not $SkipSubmodules) {
    Build-ProxyBridge
    Build-NetBridgeBridge
    Sync-SubmoduleAssets
}

if ($SubmodulesOnly) {
    Write-Host "`nSubmodules-only mode. Done." -ForegroundColor Green
    exit 0
}

if ($Clean -and (Test-Path $OutputDir)) {
    Remove-Item $OutputDir -Recurse -Force
}

Build-DotNet -Project $Projects[$Variant] -OutputPath $OutputDir
Copy-NetBridgeBridgeToOutput -OutputPath $OutputDir

if ($Variant -eq 'wpf') {
    Build-DotNet -Project $Projects['amaztool'] -OutputPath $OutputDir
}

Show-Artifacts -OutputPath $OutputDir

Write-Host "`nBuild complete." -ForegroundColor Green
