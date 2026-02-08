<#
.SYNOPSIS
    Cross-platform Build & Publish script for RemakeEngine.
#>
param(
    [string]$Framework = "net10.0",
    [string]$Runtime   = "win-x64",
    [string]$ConfigFilter = "",
    [bool]$SkipAssets = $false
)

# Platform-agnostic pathing
$Root = Split-Path -Parent $PSCommandPath
$EngineNetProj = (Join-Path $Root (Join-Path "EngineNet" "EngineNet.csproj"))
$OutputRoot    = Join-Path $Root "EngineBuild"
$Icon          = Join-Path $Root "icon.ico"

# 1. Version Extraction
$ProjectToml = (Join-Path $Root (Join-Path ".betterGit" "project.toml"))
if (Test-Path $ProjectToml) {
    $tomlContent = Get-Content $ProjectToml -Raw
    $major = ([regex]::Match($tomlContent, "(?m)^major\s*=\s*(\d+)")).Groups[1].Value
    $minor = ([regex]::Match($tomlContent, "(?m)^minor\s*=\s*(\d+)")).Groups[1].Value
    $patch = ([regex]::Match($tomlContent, "(?m)^patch\s*=\s*(\d+)")).Groups[1].Value
    $version = "$major.$minor.$patch"
} else {
    $version = "1.0.0"
}

# 2. Define Targets - Now dynamic based on input Runtime
$targets = @(
    @{ Configuration = "Release"; Output = "win-x64-Release" }
    @{ Configuration = "Release"; Output = "win-x86-Release" }
    @{ Configuration = "Release"; Output = "win-arm64-Release" }
    @{ Configuration = "Release"; Output = "win-arm-Release" }

    @{ Configuration = "Debug";   Output = "win-x64-Debug"   }
    @{ Configuration = "Debug";   Output = "win-x86-Debug"   }
    @{ Configuration = "Debug";   Output = "win-arm64-Debug" }
    @{ Configuration = "Debug";   Output = "win-arm-Debug"   }
)
if (-not [string]::IsNullOrWhiteSpace($ConfigFilter)) {
    $targets = $targets | Where-Object { $_.Configuration -eq $ConfigFilter }
}

Write-Host "--- Build Version: $version | RID: $Runtime ---" -ForegroundColor Cyan

foreach ($t in $targets) {
    $config   = $t.Configuration
    $outDir   = (Join-Path $OutputRoot $t.Output)

    Write-Host "==> Publishing $config to $outDir" -ForegroundColor Green

    dotnet publish $EngineNetProj `
        -c $config `
        -f $Framework `
        -r $Runtime `
        --self-contained true `
        -o $outDir `
        -p:PublishSingleFile=true `
        -p:ApplicationIcon="$Icon" `
        -p:Version=$version `
        -p:FileVersion=$version `
        -p:AssemblyVersion=$version `
        -v:m

    if ($LASTEXITCODE -ne 0) { throw "Build failed for $config" }

    # 3. Asset Bundling
    if (-not $SkipAssets) {
        $engineAppsDest = (Join-Path $outDir "EngineApps")
        $null = New-Item -ItemType Directory -Path $engineAppsDest -Force

        # Copy Registries
        $regSource = (Join-Path $Root (Join-Path "EngineApps" "Registries"))
        if (Test-Path $regSource) { Copy-Item $regSource -Destination $engineAppsDest -Recurse -Force }

        # Copy Demo Game (Git Aware)
        if (Test-Path (Join-Path $Root (Join-Path "EngineApps" (Join-Path "Games" "Demo")))) {
            $demoDest = (Join-Path $engineAppsDest (Join-Path "Games" "Demo"))
            $null = New-Item -ItemType Directory -Path $demoDest -Force

            # Use git ls-files (works on all OS)
            $files = git -C $Root ls-files --cached --others --exclude-standard "EngineApps/Games/Demo/"
            foreach ($f in $files) {
                $srcFile = (Join-Path $Root $f)
                $relPath = $f.Substring("EngineApps/Games/Demo/".Length)
                $targetFile = (Join-Path $demoDest $relPath)
                $null = New-Item -ItemType Directory -Path (Split-Path $targetFile) -Force
                Copy-Item $srcFile -Destination $targetFile -Force
            }
        }
    }
}

if ($env:GITHUB_ENV) {
    "ENGINE_VERSION=$version" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
}

