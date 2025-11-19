<#
.SYNOPSIS
    Restores files from a backup location back into DataRoot.
#>

#region Parameters
param(
    [Parameter(Mandatory = $true)][string]$BackupSource,
    [string]$DataRoot = "D:\\ProjectManagementData"
)
#endregion

#region Validation
if (-not (Test-Path $BackupSource)) {
    throw "Backup source not found: $BackupSource"
}

New-Item -ItemType Directory -Force -Path $DataRoot | Out-Null
#endregion

#region Execution
$robocopyArgs = @(
    $BackupSource,
    $DataRoot,
    '/MIR',
    '/R:2',
    '/W:5',
    '/NP',
    '/NDL',
    '/NFL'
)

robocopy @robocopyArgs
$exitCode = $LASTEXITCODE

if ($exitCode -gt 3) {
    throw "robocopy reported a failure (exit code $exitCode). Inspect the console output for details."
}
#endregion

#region Output
Write-Host "File restore completed from" $BackupSource "(robocopy exit code $exitCode)"
#endregion
