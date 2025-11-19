<#
.SYNOPSIS
    Mirrors the configured DataRoot to a backup destination using robocopy.
#>

#region Parameters
param(
    [string]$DataRoot = "D:\\ProjectManagementData",
    [string]$BackupRoot = "E:\\PM-Backups\\files"
)
#endregion

#region Preparation
if (-not (Test-Path $DataRoot)) {
    throw "DataRoot not found: $DataRoot"
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$destination = Join-Path $BackupRoot "Data-$timestamp"
New-Item -ItemType Directory -Force -Path $destination | Out-Null
#endregion

#region Execution
$logPath = Join-Path $BackupRoot "backup.log"
$logArgument = '/LOG+:"' + $logPath + '"'
$robocopyArgs = @(
    $DataRoot,
    $destination,
    '/MIR',
    '/R:2',
    '/W:5',
    '/NP',
    '/NDL',
    '/NFL',
    $logArgument
)

robocopy @robocopyArgs
$exitCode = $LASTEXITCODE

if ($exitCode -gt 3) {
    throw "robocopy reported a failure (exit code $exitCode). Review $logPath for details."
}
#endregion

#region Output
Write-Host "File backup completed:" $destination "(robocopy exit code $exitCode)"
#endregion
