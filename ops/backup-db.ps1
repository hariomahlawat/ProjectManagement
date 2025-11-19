<#
.SYNOPSIS
    Creates a logical PostgreSQL backup using pg_dump (custom format).
.DESCRIPTION
    Mirrors the documented procedure in docs/production-readiness-backup.md. Provide credentials via environment variables
    (PGPASSWORD) or pgpass files instead of embedding secrets.
#>

#region Parameters
param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\16\bin",
    [string]$Host = "localhost",
    [int]$Port = 5432,
    [string]$DbName = "ProjectManagement",
    [string]$User = "pm_backup",
    [string]$BackupDir = "D:\\ProjectManagementData\\backups\\db"
)
#endregion

#region Preparation
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupFile = Join-Path $BackupDir "$($DbName)-$timestamp.dump"

if (-not (Test-Path $PgBin)) {
    throw "PostgreSQL bin directory not found: $PgBin"
}

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
#endregion

#region Execution
$pgDumpPath = Join-Path $PgBin "pg_dump.exe"

if (-not (Test-Path $pgDumpPath)) {
    throw "pg_dump not found at $pgDumpPath"
}

& $pgDumpPath `
    --format=custom `
    --host=$Host `
    --port=$Port `
    --username=$User `
    --file=$backupFile `
    $DbName
#endregion

#region Output
Write-Host "Database backup completed:" $backupFile
#endregion
