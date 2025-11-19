<#
.SYNOPSIS
    Restores a PostgreSQL database from a pg_dump custom-format file.
.DESCRIPTION
    Drops and recreates the database before running pg_restore with --clean and --if-exists to ensure schema alignment.
#>

#region Parameters
param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\16\bin",
    [string]$Host = "localhost",
    [int]$Port = 5432,
    [string]$DbName = "ProjectManagement",
    [string]$User = "postgres",
    [Parameter(Mandatory = $true)][string]$DumpFile
)
#endregion

#region Validation
if (-not (Test-Path $DumpFile)) {
    throw "Dump file not found: $DumpFile"
}

if (-not (Test-Path $PgBin)) {
    throw "PostgreSQL bin directory not found: $PgBin"
}
#endregion

#region Drop and recreate database
$dropDb = Join-Path $PgBin "dropdb.exe"
$createDb = Join-Path $PgBin "createdb.exe"

& $dropDb --if-exists --host=$Host --port=$Port --username=$User $DbName
& $createDb --host=$Host --port=$Port --username=$User $DbName
#endregion

#region Restore
$pgRestore = Join-Path $PgBin "pg_restore.exe"

& $pgRestore `
    --host=$Host `
    --port=$Port `
    --username=$User `
    --dbname=$DbName `
    --clean `
    --if-exists `
    $DumpFile
#endregion

#region Output
Write-Host "Database restore completed from $DumpFile"
#endregion
