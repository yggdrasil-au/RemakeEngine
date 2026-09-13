<#
.SYNOPSIS
Cross-platform Build & Publish script for RemakeEngine.

.DESCRIPTION
Automates the build and publish process for the RemakeEngine project.

The script publishes EngineNet for exactly the runtime specified by -Runtime,
bundles necessary assets, and optionally signs Windows executables.

By default, both Release and Debug configurations are built.

Examples:
    .\dotnet.publish.ps1
    .\dotnet.publish.ps1 -Runtime 'win-x64'
    .\dotnet.publish.ps1 -Runtime 'linux-x64' -ConfigFilter 'Release'
    .\dotnet.publish.ps1 -Runtime 'win-x64' -SkipAssets $false -EnablePython $false -EnableJs $false
#>

param(
    [Alias("h", "?")]
    [switch]$Help,
    [string]$Framework = "net10.0",
    [string]$Runtime = "win-x64",
    [string]$ConfigFilter = "",
    [bool]$SkipAssets = $false,
    [bool]$EnableLua = $true,
    [bool]$EnableJs = $true,
    [bool]$EnablePython = $true
)

# Display Help and Exit
if ($Help) {
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host " RemakeEngine Build & Publish Script" -ForegroundColor Cyan
    Write-Host "======================================================" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "DESCRIPTION:" -ForegroundColor Yellow
    Write-Host "  Automates the build and publish process for RemakeEngine."
    Write-Host "  Publishes EngineNet for exactly the specified runtime."
    Write-Host "  Bundles required assets (Registries/Games), and optionally signs executables."
    Write-Host ""
    Write-Host "PARAMETERS:" -ForegroundColor Yellow
    Write-Host "  -Help, -h, -?           : Displays this help message and exits."
    Write-Host "  -Framework <string>     : Target framework (Default: 'net10.0')."
    Write-Host "  -Runtime <string>       : Target runtime identifier (Default: 'win-x64')."
    Write-Host "  -ConfigFilter <string>  : Build only the specified configuration ('Release' or 'Debug')."
    Write-Host "                            If omitted, both Release and Debug are built."
    Write-Host "  -SkipAssets <bool>      : If `$true, skips bundling Registries and Demo Game assets (Default: `$false)."
    Write-Host "  -EnableLua <bool>       : Includes the Lua script engine in the build (Default: `$true)."
    Write-Host "  -EnableJs <bool>        : Includes the JavaScript engine in the build (Default: `$true)."
    Write-Host "  -EnablePython <bool>    : Includes the Python engine in the build (Default: `$true)."
    Write-Host ""
    Write-Host "EXAMPLES:" -ForegroundColor Yellow
    Write-Host "  .\dotnet.publish.ps1 -Help"
    Write-Host "  .\dotnet.publish.ps1"
    Write-Host "  .\dotnet.publish.ps1 -Runtime 'win-x64'"
    Write-Host "  .\dotnet.publish.ps1 -Runtime 'linux-x64' -ConfigFilter 'Release'"
    Write-Host "  .\dotnet.publish.ps1 -Runtime 'win-x64' -SkipAssets `$false -EnablePython `$false -EnableJs `$false"
    Write-Host "======================================================" -ForegroundColor Cyan
    exit 0
}

# Platform-agnostic pathing
$Root = Split-Path -Parent $PSCommandPath
$EngineNetProj = Join-Path $Root (Join-Path "EngineNet" "EngineNet.csproj")
$OutputRoot = Join-Path $Root "EngineBuild"
$Icon = Join-Path $Root (Join-Path "EngineNet" "icon.ico")

$SigningCertificate = $null

$CanSignBuilds = $IsWindows -and (Get-Command New-SelfSignedCertificate -ErrorAction SilentlyContinue) -and (Get-Command Set-AuthenticodeSignature -ErrorAction SilentlyContinue)

if ($CanSignBuilds) {
    $SigningCertificate = New-SelfSignedCertificate -Subject "CN=yggdrasilAu" -Type CodeSigningCert -CertStoreLocation "Cert:\CurrentUser\My"
    Write-Host "Created temporary code-signing certificate for CN=yggdrasilAu" -ForegroundColor Cyan
}
elseif ($IsWindows) {
    Write-Warning "Code signing cmdlets are not available; builds will continue without signing."
}

function Sign-PublishedExecutable {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ExecutablePath
    )

    if (-not $CanSignBuilds -or -not $SigningCertificate) {
        return
    }

    if (-not (Test-Path $ExecutablePath)) {
        Write-Warning "Skipping signing because the executable was not found: $ExecutablePath"
        return
    }

    Set-AuthenticodeSignature -FilePath $ExecutablePath -Certificate $SigningCertificate -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com" | Out-Null
    Write-Host "Signed $ExecutablePath" -ForegroundColor DarkCyan
}

# 1. Version Extraction
$ProjectToml = Join-Path $Root (Join-Path ".betterGit" "project.toml")

if (Test-Path $ProjectToml) {
    $tomlContent = Get-Content $ProjectToml -Raw
    $major = ([regex]::Match($tomlContent, "(?m)^major\s*=\s*(\d+)")).Groups[1].Value
    $minor = ([regex]::Match($tomlContent, "(?m)^minor\s*=\s*(\d+)")).Groups[1].Value
    $patch = ([regex]::Match($tomlContent, "(?m)^patch\s*=\s*(\d+)")).Groups[1].Value
    $version = "$major.$minor.$patch"
}
else {
    $version = "1.0.0"
}

# 2. Define configurations
$targets = @(
    @{
        Configuration = "Release"
        Output = "$Runtime-Release"
    }
    @{
        Configuration = "Debug"
        Output = "$Runtime-Debug"
    }
)

if (-not [string]::IsNullOrWhiteSpace($ConfigFilter)) {
    $targets = $targets | Where-Object { $_.Configuration -eq $ConfigFilter }
}

Write-Host "--- Build Version: $version | RID: $Runtime ---" -ForegroundColor Cyan

# Clear old output
Write-Host "Cleaning old build outputs..." -ForegroundColor Yellow

if (Test-Path $OutputRoot) {
    Remove-Item $OutputRoot -Recurse -Force
}

# Convert boolean parameters to lowercase strings for MSBuild
$luaArg = $EnableLua.ToString().ToLowerInvariant()
$jsArg = $EnableJs.ToString().ToLowerInvariant()
$pyArg = $EnablePython.ToString().ToLowerInvariant()

foreach ($t in $targets) {
    $config = $t.Configuration
    $outDir = Join-Path $OutputRoot $t.Output

    Write-Host "==> Publishing $config for $Runtime to $outDir" -ForegroundColor Green

    dotnet publish $EngineNetProj `
        -c $config `
        -f $Framework `
        -r $Runtime `
        -o $outDir `
        -p:ApplicationIcon="$Icon" `
        -p:Version=$version `
        -p:FileVersion=$version `
        -p:AssemblyVersion=$version `
        -p:EnableLua=$luaArg `
        -p:EnableJs=$jsArg `
        -p:EnablePython=$pyArg `
        -v:m

    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $config / $Runtime"
    }

    # 3. Asset Bundling
    if (-not $SkipAssets) {
        $engineAppsDest = Join-Path $outDir "EngineApps"
        $null = New-Item -ItemType Directory -Path $engineAppsDest -Force

        # Copy Registries
        $regSource = Join-Path $Root (Join-Path "EngineApps" "Registries")
        if (Test-Path $regSource) {
            Copy-Item $regSource -Destination $engineAppsDest -Recurse -Force
        }

        # Copy Demo Game (Git Aware)
        $demoSource = Join-Path $Root "EngineApps/Games/demo"
        if (Test-Path $demoSource) {
            $demoDest = Join-Path $engineAppsDest (Join-Path "Games" "demo")
            $null = New-Item -ItemType Directory -Path $demoDest -Force

            $files = git -C $Root ls-files --cached --others --exclude-standard "EngineApps/Games/demo/"
            foreach ($f in $files) {
                $srcFile = Join-Path $Root $f
                $relPath = $f.Substring("EngineApps/Games/demo/".Length)
                $targetFile = Join-Path $demoDest $relPath
                $targetDirectory = Split-Path $targetFile
                $null = New-Item -ItemType Directory -Path $targetDirectory -Force
                Copy-Item $srcFile -Destination $targetFile -Force
            }
        }
    }

    # 4. Sign Windows executable if possible
    Sign-PublishedExecutable -ExecutablePath (Join-Path $outDir "EngineNet.exe")
}

# 5. Export version to GitHub Actions
if ($env:GITHUB_ENV) {
    "ENGINE_VERSION=$version" | Out-File -FilePath $env:GITHUB_ENV -Append -Encoding utf8
}