#Usage: main.ps1 publish release -v v1.99.6-A
#Optional flags: -MetaPath package.json -SonarPath .sonarcloud.properties -Branch main -DryRun
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
  [string]$MetaPath = 'package.json',
  [string]$SonarPath = '.sonarcloud.properties',
  [string]$Branch = 'main',
  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

$python = Get-Command python -ErrorAction SilentlyContinue
if (-not $python) { throw "Python not found. Install Python 3 and ensure 'python' is on PATH." }

$script = Join-Path $PSScriptRoot 'release.py'
if (-not (Test-Path $script)) { throw "release.py not found at $script" }

$argv = @($Command, $Subcommand, '-v', $Version, '--MetaPath', $MetaPath, '--SonarPath', $SonarPath, '--Branch', $Branch)
if ($DryRun) { $argv += '--DryRun' }

Write-Host "Delegating to Python: $script $($argv -join ' ')"
& $python.Path $script @argv
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
