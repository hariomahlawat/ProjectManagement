param(
    [Parameter(Mandatory = $false)]
    [string]$ProjectRoot = (Get-Location).Path
)

$ErrorActionPreference = "Stop"

$projectRootFull = [IO.Path]::GetFullPath($ProjectRoot)
$actionTasksPath = Join-Path $projectRootFull "Pages\ActionTasks\Index.cshtml.cs"
$composerPath = Join-Path $projectRootFull "Utilities\Reporting\BrochurePrintCompactComposer.cs"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

Write-Step "Checking Phase 7 compact brochure composer"

if (-not (Test-Path $composerPath)) {
    throw "BrochurePrintCompactComposer.cs not found at: $composerPath"
}

$composer = Get-Content $composerPath -Raw

# These patterns caused CS0023 because QuestPDF Text(Action<TextDescriptor>) returns void.
$badProcurementPattern = '(?s)\.Text\(text\s*=>\s*\{.*?Procurement:.*?\}\)\s*\.FontSize\('
$badClosingPattern = '(?s)New Simulators\..*?\}\)\s*\.FontSize\('

if ($composer -match $badProcurementPattern -or $composer -match $badClosingPattern) {
    throw @"
The old Phase 7 composer is still present.

Copy the replacement file from this hotfix package over:
  Utilities\Reporting\BrochurePrintCompactComposer.cs

Then run this script again.
"@
}

if ($composer -notmatch 'text\.DefaultTextStyle\(style\s*=>\s*style' -or
    $composer -notmatch 'New Simulators\.') {
    Write-Warning "Expected corrected QuestPDF rich-text style pattern was not found. Verify the replacement composer manually."
}
else {
    Write-Host "[OK] BrochurePrintCompactComposer rich-text calls are compile-safe." -ForegroundColor Green
}

Write-Step "Fixing ActionTasks ResolveActorName nullability"

if (-not (Test-Path $actionTasksPath)) {
    throw "ActionTasks Index.cshtml.cs not found at: $actionTasksPath"
}

$content = Get-Content $actionTasksPath -Raw

$alreadyFixedPattern = '(?s)public\s+string\s+ResolveActorName\(string\?\s+performedByUserId\)\s*\{\s*if\s*\(string\.IsNullOrWhiteSpace\(performedByUserId\)\)'
$oldPattern = '(?s)public\s+string\s+ResolveActorName\(string\s+performedByUserId\)\s*\{\s*return\s+TaskActorNames\.TryGetValue\(performedByUserId,\s*out\s+var\s+actorName\)\s*\?\s*actorName\s*:\s*"User";\s*\}'

$replacement = @'
public string ResolveActorName(string? performedByUserId)
    {
        if (string.IsNullOrWhiteSpace(performedByUserId))
        {
            return "System";
        }

        return TaskActorNames.TryGetValue(performedByUserId, out var actorName)
            ? actorName
            : "User";
    }
'@

if ($content -match $alreadyFixedPattern) {
    Write-Host "[OK] ResolveActorName is already nullable-safe." -ForegroundColor Green
}
elseif ($content -match $oldPattern) {
    $updated = [regex]::Replace($content, $oldPattern, $replacement, 1)
    Set-Content -Path $actionTasksPath -Value $updated -Encoding utf8NoBOM
    Write-Host "[FIXED] ResolveActorName now accepts nullable actor IDs." -ForegroundColor Green
}
else {
    throw @"
The expected ResolveActorName implementation was not found exactly.
No ActionTasks source was modified.

Replace only that method manually using:
  ACTIONTASKS-ResolveActorName-Replacement.txt
"@
}

Write-Step "Hotfix applied"

Write-Host ""
Write-Host "Run these final checks:" -ForegroundColor Yellow
Write-Host "  dotnet build .\ProjectManagement.csproj"
Write-Host "  dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj"
