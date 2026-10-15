<#
.SYNOPSIS
    Restores a PostgreSQL database from a pg_dump custom-format file.
.DESCRIPTION
    Drops and recreates the database before running pg_restore with --clean and --if-exists to ensure schema alignment.
#>

#region Parameters
param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\16\bin",
    [Alias('Host')][string]$PgHost = "localhost",
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

foreach ($tool in @($dropDb, $createDb)) {
    if (-not (Test-Path $tool)) {
        throw "Required PostgreSQL utility not found: $tool"
    }
}

& $dropDb --if-exists --host=$PgHost --port=$Port --username=$User $DbName
if ($LASTEXITCODE -ne 0) {
    throw "dropdb failed with exit code $LASTEXITCODE."
}
& $createDb --host=$PgHost --port=$Port --username=$User $DbName
if ($LASTEXITCODE -ne 0) {
    throw "createdb failed with exit code $LASTEXITCODE."
}
#endregion

#region Restore
$pgRestore = Join-Path $PgBin "pg_restore.exe"

if (-not (Test-Path $pgRestore)) {
    throw "pg_restore not found at $pgRestore"
}

& $pgRestore `
    --host=$PgHost `
    --port=$Port `
    --username=$User `
    --dbname=$DbName `
    --clean `
    --if-exists `
    $DumpFile
if ($LASTEXITCODE -ne 0) {
    throw "pg_restore failed with exit code $LASTEXITCODE."
}
#endregion

#region Output
Write-Host "Database restore completed from $DumpFile"
#endregion
