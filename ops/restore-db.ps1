<#
.SYNOPSIS
    Restore into a separate database, validate, then atomically rename into place.
.DESCRIPTION
    Retains the previous database. Requires an idle target and trusted archive;
    see docs/disaster-recovery.md before use. Never terminates other sessions.
#>
param(
    [string]$PgBin = "C:\Program Files\PostgreSQL\16\bin",
    [Alias('Host')][string]$PgHost = "localhost",
    [int]$Port = 5432,
    [string]$DbName = "ProjectManagement",
    [string]$User = "postgres",
    [Parameter(Mandatory = $true)][string]$DumpFile
)

if (-not (Test-Path -LiteralPath $DumpFile -PathType Leaf)) { throw "Dump file not found: $DumpFile" }
foreach ($tool in @('dropdb', 'createdb', 'pg_restore', 'psql')) {
    if (-not (Test-Path -LiteralPath (Join-Path $PgBin "$tool.exe") -PathType Leaf)) {
        throw "Required PostgreSQL utility not found: $tool"
    }
}
if ([string]::IsNullOrWhiteSpace($DbName) -or $DbName -match '[\x00-\x1f\x7f]') {
    throw 'Database name must be nonempty and contain no control characters.'
}
$adminDb = if ($DbName -eq 'postgres') { 'template1' } else { 'postgres' }
$stem = ($DbName -replace '[^a-zA-Z0-9_]', '_')
$stem = $stem.Substring(0, [Math]::Min(16, $stem.Length))
$suffix = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss') + '_' + [Guid]::NewGuid().ToString('N').Substring(0, 16)
$stage = $stem + '_stage_' + $suffix
$previous = $stem + '_previous_' + $suffix
$common = @("--host=$PgHost", "--port=$Port", "--username=$User", '--no-password')
$environmentNames = @('PGDATABASE', 'PGHOSTADDR', 'PGSERVICE', 'PGSERVICEFILE', 'PGCONNECT_TIMEOUT', 'PGCLIENTENCODING',
    'PRISM_RESTORE_TARGET', 'PRISM_RESTORE_TARGET_HEX', 'PRISM_RESTORE_STAGE', 'PRISM_RESTORE_PREVIOUS',
    'PRISM_RESTORE_TARGET_OID', 'PRISM_RESTORE_STAGE_OID')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }

function Invoke-Sql([string]$Phase, [string]$Database, [string]$Sql) {
    # An ASCII URI transports UTF-8 names through Windows native arguments
    # without treating '=' or URI-looking database names as connection options.
    $env:PGDATABASE = $Database
    $databaseUri = 'postgresql:///' + [Uri]::EscapeDataString($Database)
    $inputSql = "-- prism-restore: $Phase" + [Environment]::NewLine + $sqlPrefix + [Environment]::NewLine + $Sql
    $lines = @($inputSql | & (Join-Path $PgBin 'psql.exe') @common "--dbname=$databaseUri" -X --set=ON_ERROR_STOP=1 --tuples-only --no-align --file=-)
    $nativeExit = $LASTEXITCODE
    foreach ($line in $lines) { Write-Host $line }
    if ($nativeExit -ne 0) { throw "psql ($Phase) failed with exit code $nativeExit." }
    $markers = @($lines | Where-Object { $_ -cmatch '^PRISM_OID=[0-9]+$' })
    if ($Phase -in @('preflight', 'identity', 'validate', 'activated')) {
        if ($markers.Count -ne 1) { throw "psql ($Phase) did not return one database identity." }
        return $markers[0].Substring('PRISM_OID='.Length)
    }
}
$sqlPrefix = @'
\getenv target_hex PRISM_RESTORE_TARGET_HEX
SELECT convert_from(decode(:'target_hex', 'hex'), 'UTF8') AS target \gset
\getenv stage PRISM_RESTORE_STAGE
\getenv previous PRISM_RESTORE_PREVIOUS
\getenv target_oid PRISM_RESTORE_TARGET_OID
\getenv stage_oid PRISM_RESTORE_STAGE_OID
'@
$sqlPreflight = @'
SELECT (octet_length(:'target') BETWEEN 1 AND current_setting('max_identifier_length')::int AND NOT EXISTS (SELECT FROM pg_database WHERE datname = :'target' AND datistemplate)) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Invalid target name or template database target.'; END $$;
\endif
SELECT (NOT EXISTS (SELECT FROM pg_database WHERE datname IN (:'stage', :'previous'))) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Generated database name already exists; nothing was changed.'; END $$;
\endif
SELECT 'PRISM_OID=' || COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0);
'@
$sqlIdentity = @'
SELECT 'PRISM_OID=' || oid::text FROM pg_database WHERE datname = current_database();
'@
$sqlValidate = @'
SELECT (EXISTS (
 SELECT FROM pg_namespace n WHERE n.nspname NOT LIKE 'pg\_%' ESCAPE '\' AND n.nspname <> 'information_schema'
 AND (SELECT count(*) FROM pg_class c WHERE c.relnamespace = n.oid
      AND c.relkind IN ('r', 'p') AND c.relname IN ('Projects', 'AspNetUsers', '__EFMigrationsHistory')) = 3
)) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Restored PRISM schema is missing Projects, AspNetUsers or __EFMigrationsHistory in one user schema.'; END $$;
\endif
SELECT 'PRISM_OID=' || oid::text FROM pg_database WHERE datname = current_database();
'@
$sqlCutover = @'
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '15s';
SELECT pg_advisory_xact_lock(726747384021::bigint);
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Database identities changed before cutover; inspect retained databases.'; END $$;
\endif
SELECT (NOT EXISTS (SELECT FROM pg_stat_activity WHERE datid IN (:'target_oid'::oid, :'stage_oid'::oid))
 AND NOT EXISTS (SELECT FROM pg_prepared_xacts WHERE database IN (:'target', :'stage', :'previous'))) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Active sessions or prepared transactions prevent cutover; stop the app and close connections, then retry.'; END $$;
\endif
SELECT (:'target_oid'::bigint <> 0) AS had_target \gset
\if :had_target
ALTER DATABASE :"target" RENAME TO :"previous";
\endif
ALTER DATABASE :"stage" RENAME TO :"target";
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Cutover identity check failed; transaction will roll back.'; END $$;
\endif
COMMIT;
'@
$sqlRollback = @'
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '15s';
SELECT pg_advisory_xact_lock(726747384021::bigint);
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Cannot safely roll back changed database identities; retain both copies for inspection.'; END $$;
\endif
SELECT (NOT EXISTS (SELECT FROM pg_stat_activity WHERE datid IN (:'target_oid'::oid, :'stage_oid'::oid))
 AND NOT EXISTS (SELECT FROM pg_prepared_xacts WHERE database IN (:'target', :'stage', :'previous'))) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Active sessions or prepared transactions prevent cutover; stop the app and close connections, then retry.'; END $$;
\endif
ALTER DATABASE :"target" RENAME TO :"stage";
SELECT (:'target_oid'::bigint <> 0) AS had_target \gset
\if :had_target
ALTER DATABASE :"previous" RENAME TO :"target";
\endif
SELECT (COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'target'), 0) = :'target_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'stage'), 0) = :'stage_oid'::bigint AND COALESCE((SELECT oid::bigint FROM pg_database WHERE datname = :'previous'), 0) = 0) AS prism_ok \gset
\if :prism_ok
\else
DO $$ BEGIN RAISE EXCEPTION 'Rollback identity check failed; transaction will roll back.'; END $$;
\endif
COMMIT;
'@

$stageOwned = $false
$cutoverAttempted = $false
$cutoverCommitted = $false
try {
    $env:PGSERVICE = $null
    $env:PGSERVICEFILE = $null
    $env:PGHOSTADDR = $null
    $env:PGCONNECT_TIMEOUT = '10'
    $env:PGCLIENTENCODING = 'UTF8'
    $env:PRISM_RESTORE_TARGET = $DbName
    $env:PRISM_RESTORE_TARGET_HEX = [BitConverter]::ToString([Text.Encoding]::UTF8.GetBytes($DbName)).Replace('-', '')
    $env:PRISM_RESTORE_STAGE = $stage
    $env:PRISM_RESTORE_PREVIOUS = $previous
    $env:PRISM_RESTORE_TARGET_OID = '0'
    $env:PRISM_RESTORE_STAGE_OID = '0'
    & (Join-Path $PgBin 'pg_restore.exe') --list -- $DumpFile
    if ($LASTEXITCODE -ne 0) { throw "pg_restore (list) failed with exit code $LASTEXITCODE." }
    $env:PRISM_RESTORE_TARGET_OID = Invoke-Sql 'preflight' $adminDb $sqlPreflight
    Write-Host "Staging database: $stage; retained-name reservation: $previous"
    & (Join-Path $PgBin 'createdb.exe') @common "--maintenance-db=$adminDb" --template=template0 -- $stage
    if ($LASTEXITCODE -ne 0) { throw "createdb failed with exit code $LASTEXITCODE." }
    # Record the identity of this attempt's staging database for failure reporting.
    $env:PRISM_RESTORE_STAGE_OID = Invoke-Sql 'identity' $stage $sqlIdentity
    $stageOwned = $true
    & (Join-Path $PgBin 'pg_restore.exe') @common "--dbname=$stage" --single-transaction --exit-on-error -- $DumpFile
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed with exit code $LASTEXITCODE." }
    $validatedOid = Invoke-Sql 'validate' $stage $sqlValidate
    if ($validatedOid -ne $env:PRISM_RESTORE_STAGE_OID) { throw 'Staging database identity changed.' }
    $cutoverAttempted = $true
    Invoke-Sql 'cutover' $adminDb $sqlCutover
    $cutoverCommitted = $true
    $activatedOid = Invoke-Sql 'activated' $DbName $sqlValidate
    if ($activatedOid -ne $env:PRISM_RESTORE_STAGE_OID) { throw 'Activated database identity changed.' }
    Write-Host "Database restore completed: $DbName from $DumpFile"
    if ($env:PRISM_RESTORE_TARGET_OID -ne '0') { Write-Host "Retained previous database: $previous" }
    else { Write-Host 'No previous target database existed.' }
}
catch {
    $primaryFailure = $_
    if ($cutoverCommitted) {
        try {
            Invoke-Sql 'rollback' $adminDb $sqlRollback
            Write-Warning "Activation verification failed; original name restored. Staged copy retained: $stage"
        }
        catch { Write-Warning "Automatic rollback failed: $_. Inspect target=$DbName, stage=$stage, previous=$previous." }
    }
    elseif ($cutoverAttempted) {
        Write-Warning "Cutover failed or its commit outcome is unknown. Both renames use one transaction. Inspect target=$DbName, stage=$stage, previous=$previous; no databases were deleted."
    }
    elseif ($stageOwned) {
        # DROP DATABASE cannot share the identity-check transaction. A separate
        # check/drop races with administrative renames; retain rather than risk
        # deleting a replacement database under the generated name.
        Write-Warning "Automatic staging cleanup deferred for identity safety. Retained staging database: $stage (OID $env:PRISM_RESTORE_STAGE_OID). Inspect before deliberate cleanup."
    }
    else { Write-Warning "If created, staging database was retained for inspection: $stage" }
    throw $primaryFailure
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
}
