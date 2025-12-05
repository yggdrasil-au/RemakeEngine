# dotnet.publish.ps1
param(
    [string]$Framework = "net9.0",
    [string]$Runtime   = "win-x64"
)

# Root of the repo (where this script lives)
$Root = Split-Path -Parent $PSCommandPath

$EngineNetProj = Join-Path $Root "EngineNet\EngineNet.csproj"
$OutputRoot    = Join-Path $Root "EngineBuild"

$EngineAppsRoot     = Join-Path $Root "EngineApps"
$GamesDemoSource    = Join-Path $EngineAppsRoot "Games\Demo"        # 👈 change "Demo" if needed
$RegistriesSource   = Join-Path $EngineAppsRoot "Registries"
$PlaceholderSource  = Join-Path $Root "placeholder.png"

$targets = @(
    @{ Configuration = "Release"; Output = "win-x64-Release" }
    @{ Configuration = "Debug";   Output = "win-x64-Debug"   }
)

foreach ($t in $targets) {
    $config   = $t.Configuration
    $outName  = $t.Output
    $outDir   = Join-Path $OutputRoot $outName

    Write-Host "=== Publishing $config to $outDir ==="

    dotnet publish $EngineNetProj `
        -v:d `
        -c $config `
        -f $Framework `
        -r $Runtime `
        --self-contained true `
        -o $outDir `
        -p:PublishSingleFile=true

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for configuration $config"
    }

    Write-Host "=== Copying extra assets for $config ==="

    # Ensure base folders exist
    $engineAppsDest = Join-Path $outDir "EngineApps"
    if (-not (Test-Path $engineAppsDest)) {
        New-Item -ItemType Directory -Path $engineAppsDest | Out-Null
    }

    # 1) Copy Demo game -> EngineApps/Games/Demo/...
    if (Test-Path $GamesDemoSource) {
        $gamesDest = Join-Path $engineAppsDest "Games"
        $demoLeaf  = Split-Path $GamesDemoSource -Leaf
        $finalDemoDest = Join-Path $gamesDest $demoLeaf

        # Clean destination to ensure excluded files (like TMP) are removed if they exist from previous builds
        if (Test-Path $finalDemoDest) {
            Remove-Item $finalDemoDest -Recurse -Force
        }
        New-Item -ItemType Directory -Path $finalDemoDest -Force | Out-Null

        Write-Host "  - Copying Demo game from $GamesDemoSource (excluding TMP and config.toml)"

        Get-ChildItem -Path $GamesDemoSource |
            Where-Object { $_.Name -ne 'TMP' -and $_.Name -ne 'config.toml' } |
            Copy-Item -Destination $finalDemoDest -Recurse -Force

        # Result: EngineBuild/<config>/EngineApps/Games/Demo/... (no TMP/, no config.toml)
    }
    else {
        Write-Warning "Demo game source not found at $GamesDemoSource"
    }

    # 2) Copy Registries -> EngineApps/Registries/...
    if (Test-Path $RegistriesSource) {
        Write-Host "  - Copying Registries from $RegistriesSource"
        Copy-Item $RegistriesSource -Destination $engineAppsDest -Recurse -Force
        # Result: EngineBuild/<config>/EngineApps/Registries/...
    }
    else {
        Write-Warning "Registries source not found at $RegistriesSource"
    }

    # 3) Copy placeholder.png -> build root
    if (Test-Path $PlaceholderSource) {
        Write-Host "  - Copying placeholder.png"
        Copy-Item $PlaceholderSource -Destination $outDir -Force
        # Result: EngineBuild/<config>/placeholder.png
    }
    else {
        Write-Warning "placeholder.png not found at $PlaceholderSource"
    }

    Write-Host "=== Done $config ===`n"
}
