[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')]
    [string] $Name,

    [ValidateSet('ApplicationDbContext', 'MediaLibraryDbContext')]
    [string] $Context = 'ApplicationDbContext',

    [string] $ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path $ProjectRoot).Path
if ($Context -eq 'ApplicationDbContext') {
    $migrationDirectory = Join-Path $root 'Migrations'
    $relativeOutputDirectory = 'Migrations'
    $manifestPath = Join-Path $migrationDirectory 'immutable-migration-ids.txt'
}
else {
    $migrationDirectory = Join-Path $root 'Features/MediaLibrary/Data/Migrations'
    $relativeOutputDirectory = 'Features/MediaLibrary/Data/Migrations'
    $manifestPath = Join-Path $migrationDirectory 'immutable-migration-ids.txt'
}

if (-not (Test-Path $manifestPath)) {
    throw "Migration lineage manifest is missing: $manifestPath"
}

$manifestIds = Get-Content $manifestPath |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ -and -not $_.StartsWith('#') } |
    Sort-Object

if ($manifestIds.Count -eq 0) {
    throw "Migration lineage manifest is empty: $manifestPath"
}

$maximumExistingId = $manifestIds[-1]
$maximumTimestampText = $maximumExistingId.Substring(0, 14)
$maximumTimestamp = [DateTime]::ParseExact(
    $maximumTimestampText,
    'yyyyMMddHHmmss',
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal)

$existingMigrationFiles = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase)
Get-ChildItem $migrationDirectory -Filter '*.cs' |
    ForEach-Object { [void] $existingMigrationFiles.Add($_.FullName) }

Push-Location $root
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet tool restore failed.'
    }

    dotnet ef migrations add $Name `
        --context $Context `
        --project $root `
        --startup-project $root `
        --output-dir $relativeOutputDirectory
    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet ef migrations add failed.'
    }
}
finally {
    Pop-Location
}

$newMigrationFiles = Get-ChildItem $migrationDirectory -Filter '*.cs' |
    Where-Object { -not $existingMigrationFiles.Contains($_.FullName) }

$newMainFiles = @($newMigrationFiles | Where-Object {
    -not $_.Name.EndsWith('.Designer.cs') -and
    -not $_.Name.EndsWith('ModelSnapshot.cs') -and
    $_.BaseName -match '^\d{14}_'
})

if ($newMainFiles.Count -ne 1) {
    $createdNames = @($newMigrationFiles | ForEach-Object Name) -join ', '
    throw "Expected one newly generated migration main file, but found $($newMainFiles.Count). Created files: $createdNames"
}

$mainFile = $newMainFiles[0]
$generatedId = $mainFile.BaseName
$generatedTimestamp = [DateTime]::ParseExact(
    $generatedId.Substring(0, 14),
    'yyyyMMddHHmmss',
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal)

$targetTimestamp = $generatedTimestamp
if ($targetTimestamp -le $maximumTimestamp) {
    $targetTimestamp = $maximumTimestamp.AddSeconds(1)
}

$targetId = $targetTimestamp.ToString('yyyyMMddHHmmss', [Globalization.CultureInfo]::InvariantCulture) +
    $generatedId.Substring(14)

if ($targetId -ne $generatedId) {
    $designerPath = Join-Path $migrationDirectory ($generatedId + '.Designer.cs')
    $targetMainPath = Join-Path $migrationDirectory ($targetId + '.cs')
    $targetDesignerPath = Join-Path $migrationDirectory ($targetId + '.Designer.cs')

    if ((Test-Path $targetMainPath) -or (Test-Path $targetDesignerPath)) {
        throw "Target migration identifier already exists: $targetId"
    }

    $mainContent = Get-Content $mainFile.FullName -Raw
    Set-Content -Path $mainFile.FullName -Value ($mainContent.Replace($generatedId, $targetId)) -NoNewline

    if (Test-Path $designerPath) {
        $designerContent = Get-Content $designerPath -Raw
        Set-Content -Path $designerPath -Value ($designerContent.Replace($generatedId, $targetId)) -NoNewline
        Rename-Item -Path $designerPath -NewName ($targetId + '.Designer.cs')
    }

    Rename-Item -Path $mainFile.FullName -NewName ($targetId + '.cs')
}

$updatedManifestSet = [System.Collections.Generic.HashSet[string]]::new(
    [StringComparer]::Ordinal)
foreach ($migrationId in @($manifestIds + $targetId)) {
    [void] $updatedManifestSet.Add([string] $migrationId)
}
$updatedManifest = [System.Collections.Generic.List[string]]::new($updatedManifestSet)
$updatedManifest.Sort([StringComparer]::Ordinal)
Set-Content -Path $manifestPath -Value $updatedManifest -Encoding utf8

Write-Host "Created $Context migration $targetId" -ForegroundColor Green
Write-Host "Updated immutable lineage manifest: $manifestPath"
Write-Host 'Run the Release build, unit tests and PostgreSQL migration integration test before committing.'
