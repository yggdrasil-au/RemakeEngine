param(
    [Parameter(Mandatory = $true)]
    [string]$Folder1,
    [Parameter(Mandatory = $true)]
    [string]$Folder2
)

if (!(Test-Path $Folder1 -PathType Container)) {
    Write-Error "Folder1 '$Folder1' does not exist or is not a directory."
    exit 1
}
if (!(Test-Path $Folder2 -PathType Container)) {
    Write-Error "Folder2 '$Folder2' does not exist or is not a directory."
    exit 1
}

# Normalise roots
$Folder1 = (Resolve-Path $Folder1).Path
$Folder2 = (Resolve-Path $Folder2).Path

# Get JSON files with relative paths
$files1 = Get-ChildItem -Path $Folder1 -Recurse -Filter *.json |
    Where-Object { -not $_.PSIsContainer } |
    Select-Object @{Name='RelativePath';Expression={ $_.FullName.Substring($Folder1.Length).TrimStart('\','/') }}, FullName

$files2 = Get-ChildItem -Path $Folder2 -Recurse -Filter *.json |
    Where-Object { -not $_.PSIsContainer } |
    Select-Object @{Name='RelativePath';Expression={ $_.FullName.Substring($Folder2.Length).TrimStart('\','/') }}, FullName

$map1 = @{}
$files1 | ForEach-Object { $map1[$_.RelativePath] = $_.FullName }

$map2 = @{}
$files2 | ForEach-Object { $map2[$_.RelativePath] = $_.FullName }

$onlyIn1 = @()
$onlyIn2 = @()
$different = @()

$allRelPaths = ($map1.Keys + $map2.Keys) | Sort-Object -Unique

foreach ($rel in $allRelPaths) {
    $in1 = $map1.ContainsKey($rel)
    $in2 = $map2.ContainsKey($rel)

    if ($in1 -and -not $in2) {
        $onlyIn1 += $rel
        continue
    }
    if (-not $in1 -and $in2) {
        $onlyIn2 += $rel
        continue
    }

    # Present in both: compare contents by hash
    $hash1 = Get-FileHash -Path $map1[$rel] -Algorithm SHA256
    $hash2 = Get-FileHash -Path $map2[$rel] -Algorithm SHA256

    if ($hash1.Hash -ne $hash2.Hash) {
        $different += $rel
    }
}

$hasIssues = $false

if ($onlyIn1.Count -gt 0) {
    $hasIssues = $true
    Write-Host "Files only in '$Folder1':" -ForegroundColor Yellow
    $onlyIn1 | ForEach-Object { Write-Host "  $_" }
    Write-Host
}

if ($onlyIn2.Count -gt 0) {
    $hasIssues = $true
    Write-Host "Files only in '$Folder2':" -ForegroundColor Yellow
    $onlyIn2 | ForEach-Object { Write-Host "  $_" }
    Write-Host
}

if ($different.Count -gt 0) {
    $hasIssues = $true
    Write-Host "Files present in both but with DIFFERENT content:" -ForegroundColor Red
    $different | ForEach-Object { Write-Host "  $_" }
    Write-Host
}

if (-not $hasIssues) {
    Write-Host "✅ All JSON files match between '$Folder1' and '$Folder2' (names and contents)." -ForegroundColor Green
    exit 0
}
else {
    Write-Host "❌ Mismatches found." -ForegroundColor Red
    exit 1
}
