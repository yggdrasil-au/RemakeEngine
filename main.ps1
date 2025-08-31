#Usage: main.ps1 publish release -v v1.99.6-A
#Optional flags: -MetaPath project-meta.json -SonarPath .sonarcloud.properties -Branch main -DryRun
param(
  [Parameter(Position=0)]
  [ValidateSet('publish')]
  [string]$Command = 'publish',
  [Parameter(Position=1)]
  [ValidateSet('release')]
  [string]$Subcommand = 'release',
  [Parameter(Mandatory=$true)]
  [Alias('v')]
  [string]$Version,
  [string]$MetaPath = 'project-meta.json',
  [string]$SonarPath = '.sonarcloud.properties',
  [string]$Branch = 'main',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Run($cmd) {
  Write-Host "› $cmd"
  if ($DryRun) { return }
  & pwsh -NoProfile -Command $cmd
  if ($LASTEXITCODE -ne 0) { throw "Command failed: $cmd" }
}

function Ensure-OnBranch {
  $current = (git rev-parse --abbrev-ref HEAD).Trim()
  if ($current -ne $Branch) { throw "Not on branch '$Branch' (current: '$current')." }
}

function Ensure-GitClean {
  $status = git status --porcelain
  if ($status) { throw "Working tree not clean. Commit or stash changes first." }
}

# Validate version early (accepts v1.2.3[-suffix])
if ($Version -notmatch '^[vV]?\d+(\.\d+){1,3}([\-+][0-9A-Za-z\.-]+)?$') {
  throw "Invalid version format: '$Version'"
}

function Ensure-RepoRoot {
  $root = (git rev-parse --show-toplevel 2>$null).Trim()
  if (-not $root) { throw "Not a Git repository." }
  Set-Location $root
}

function Ensure-RemoteUpToDate {
  Run "git fetch origin --quiet"
  $local = (git rev-parse "$Branch").Trim()
  $remote = (git rev-parse "origin/$Branch" 2>$null).Trim()
  if (-not $remote) { throw "Remote branch origin/$Branch not found. Push or set upstream first." }
  if ($local -ne $remote) { throw "Local $Branch ($local) differs from origin/$Branch ($remote). Pull/rebase first." }
}

function Ensure-Tag-DoesntExist {
  git fetch --tags --quiet
  git rev-parse -q --verify "refs/tags/$Version" *> $null
  if ($LASTEXITCODE -eq 0) { throw "Tag '$Version' already exists." }
}

function Update-Sonar {
  $content = if (Test-Path $SonarPath) { Get-Content $SonarPath -Raw } else { "" }
  if ($content -match '(?m)^\s*sonar\.projectVersion\s*=') {
    $content = [regex]::Replace($content,'(?m)^\s*sonar\.projectVersion\s*=.*$',"sonar.projectVersion=$Version")
  } else {
    if ($content.Length -gt 0 -and $content[-1] -ne "`n") { $content += "`n" }
    $content += "sonar.projectVersion=$Version`n"
  }
  if (-not $DryRun) { Set-Content -Path $SonarPath -NoNewline -Value $content -Encoding UTF8 }
  Write-Host "Updated $SonarPath"
}

function Update-Meta {
  $now = (Get-Date).ToString("o")
  # Use tag instead of commit to avoid self-referential commit hashing issues
  $entry = [ordered]@{ version = $Version; date = $now; tag = $Version }

  $meta = $null
  if (Test-Path $MetaPath) {
    try { $meta = Get-Content $MetaPath -Raw | ConvertFrom-Json -EA Stop } catch { $meta = $null }
  }

  if ($null -eq $meta) {
    $meta = [pscustomobject]@{ currentVersion = $Version; releases = @([pscustomobject]$entry) }
  } else {
    if (-not $meta.PSObject.Properties.Name.Contains('releases')) { $meta | Add-Member NoteProperty releases @() }
    if (($meta.releases | Where-Object { $_.version -eq $Version })) { throw "Version '$Version' already exists in $MetaPath." }
    $meta.currentVersion = $Version
    $meta.releases = @($meta.releases) + ([pscustomobject]$entry)
  }

  if (-not $DryRun) { $meta | ConvertTo-Json -Depth 6 | Set-Content -Path $MetaPath -Encoding utf8 }
  Write-Host "Updated $MetaPath"
}

if ($Command -eq 'publish' -and $Subcommand -eq 'release') {
  Ensure-RepoRoot
  Ensure-OnBranch
  Ensure-GitClean
  Ensure-RemoteUpToDate
  Ensure-Tag-DoesntExist

  Update-Sonar
  Update-Meta

  Run "git add -- '$SonarPath' '$MetaPath'"
  Run "git commit -m 'chore(release): $Version'"
  Run "git push origin $Branch"
  Run "git tag -a '$Version' -m 'Release $Version'"
  Run "git push origin '$Version'"

  Write-Host "Done. CI should detect tag $Version."
} else {
  throw "Unknown command. Use: publish release -v <version>"
}
