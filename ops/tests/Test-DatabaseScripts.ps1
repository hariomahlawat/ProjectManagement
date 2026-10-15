<#
.SYNOPSIS
    Offline Windows regression tests for backup-db.ps1 and restore-db.ps1 (F01/R08).
.DESCRIPTION
    Run with powershell.exe or pwsh.exe -NoProfile -ExecutionPolicy Bypass -File ops/tests/Test-DatabaseScripts.ps1.
    Builds harmless native executable stubs using the Windows .NET Framework compiler.
    Tests every installed Windows PowerShell/PowerShell 7 host by default. No PostgreSQL,
    Pester, package restore, network access or real database is used.
#>
param([string[]]$PowerShellPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Quote-ProcessArgument([string]$Value) {
    # Windows command-line quoting, including embedded quotes and trailing backslashes.
    '"' + ($Value -replace '(\\*)"', '$1$1\"' -replace '(\\+)$', '$1$1') + '"'
}

function Invoke-ScriptCase([string]$Shell, [string]$Script, [string[]]$ScriptArguments,
                           [string]$LogPath, [string]$FailUtility) {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Shell
    $arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', $Script) + $ScriptArguments
    $startInfo.Arguments = ($arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join ' '
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.EnvironmentVariables['PRISM_TEST_NATIVE_LOG'] = $LogPath
    $startInfo.EnvironmentVariables['PRISM_TEST_FAIL_UTILITY'] = $FailUtility

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        # Allow for cold host/error-formatting startup on slow Windows runners.
        if (-not $process.WaitForExit(120000)) {
            $process.Kill()
            $partialOutput = if ($stdout.IsCompleted) { $stdout.GetAwaiter().GetResult() } else { '(stdout pending)' }
            $partialError = if ($stderr.IsCompleted) { $stderr.GetAwaiter().GetResult() } else { '(stderr pending)' }
            throw "Child PowerShell exceeded the 120 second test timeout. $partialOutput $partialError"
        }
        $output = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
        $calls = @()
        if (Test-Path -LiteralPath $LogPath) {
            $calls = @(Get-Content -LiteralPath $LogPath | ForEach-Object {
                $parts = $_ -split "`t"
                [pscustomobject]@{
                    Utility = $parts[0]
                    Arguments = @($parts | Select-Object -Skip 1 | ForEach-Object {
                        [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($_))
                    })
                }
            })
        }
        [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Calls = $calls }
    }
    finally { $process.Dispose() }
}

if (-not $PowerShellPath) {
    $PowerShellPath = @(foreach ($name in @('powershell.exe', 'pwsh.exe')) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { $command.Source }
    })
}
Assert-True ($PowerShellPath.Count -gt 0) 'No Windows PowerShell host found.'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
Assert-True (Test-Path -LiteralPath $compiler) 'Windows .NET Framework C# compiler is required.'
$opsRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $tempRoot ('prism-db-script-tests-' + [Guid]::NewGuid().ToString('N'))
$failures = 0
$passed = 0

try {
    $binRoot = Join-Path $fixtureRoot 'stub bin'
    [void](New-Item -ItemType Directory -Path $binRoot -Force)
    $stubPath = Join-Path $binRoot 'stub.exe'
    $stubSource = Join-Path $fixtureRoot 'PostgresUtilityStub.cs'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'stubs/PostgresUtilityStub.cs.txt') -Destination $stubSource
    & $compiler /nologo /target:exe "/out:$stubPath" $stubSource
    Assert-True ($LASTEXITCODE -eq 0) 'Could not compile harmless PostgreSQL stub.'
    foreach ($utility in @('pg_dump', 'dropdb', 'createdb', 'pg_restore')) {
        Copy-Item -LiteralPath $stubPath -Destination (Join-Path $binRoot "$utility.exe")
    }
    $dumpPath = Join-Path $fixtureRoot 'input dump.dump'
    Set-Content -LiteralPath $dumpPath -Value 'test fixture only; not a real dump'

    $cases = @(
        @{ Name = 'backup default host'; Script = 'backup-db.ps1'; HostFlag = ''; HostValue = 'localhost'; Fail = '' },
        @{ Name = 'backup explicit PgHost'; Script = 'backup-db.ps1'; HostFlag = '-PgHost'; HostValue = 'lan-db'; Fail = '' },
        @{ Name = 'backup legacy Host alias'; Script = 'backup-db.ps1'; HostFlag = '-Host'; HostValue = 'legacy-db'; Fail = '' },
        @{ Name = 'restore default host'; Script = 'restore-db.ps1'; HostFlag = ''; HostValue = 'localhost'; Fail = '' },
        @{ Name = 'restore explicit PgHost'; Script = 'restore-db.ps1'; HostFlag = '-PgHost'; HostValue = 'lan-db'; Fail = '' },
        @{ Name = 'restore legacy Host alias'; Script = 'restore-db.ps1'; HostFlag = '-Host'; HostValue = 'legacy-db'; Fail = '' },
        @{ Name = 'pg_dump failure'; Script = 'backup-db.ps1'; HostFlag = ''; HostValue = 'localhost'; Fail = 'pg_dump' },
        @{ Name = 'dropdb failure'; Script = 'restore-db.ps1'; HostFlag = '-PgHost'; HostValue = 'lan-db'; Fail = 'dropdb' },
        @{ Name = 'createdb failure'; Script = 'restore-db.ps1'; HostFlag = '-PgHost'; HostValue = 'lan-db'; Fail = 'createdb' },
        @{ Name = 'pg_restore failure'; Script = 'restore-db.ps1'; HostFlag = '-Host'; HostValue = 'legacy-db'; Fail = 'pg_restore' }
    )

    foreach ($shell in $PowerShellPath) {
        foreach ($case in $cases) {
            try {
                $caseId = [Guid]::NewGuid().ToString('N')
                $caseArguments = @('-PgBin', $binRoot, '-Port', '5544', '-DbName', 'prism test db', '-User', 'test operator')
                if ($case.HostFlag) { $caseArguments += @($case.HostFlag, $case.HostValue) }
                $isBackup = $case.Script -eq 'backup-db.ps1'
                $backupRoot = Join-Path $fixtureRoot "backup output $caseId"
                if ($isBackup) { $caseArguments += @('-BackupDir', $backupRoot) }
                else { $caseArguments += @('-DumpFile', $dumpPath) }
                $result = Invoke-ScriptCase $shell (Join-Path $opsRoot $case.Script) $caseArguments (Join-Path $fixtureRoot "$caseId.log") $case.Fail
                $diagnostic = "Exit=$($result.ExitCode); output=$($result.Output)"
                [string[]]$expectedUtilities = if ($isBackup) { @('pg_dump') } else { @('dropdb', 'createdb', 'pg_restore') }
                if ($case.Fail) {
                    $stopIndex = [Array]::IndexOf($expectedUtilities, $case.Fail)
                    $expectedUtilities = @($expectedUtilities[0..$stopIndex])
                    Assert-True ($result.ExitCode -ne 0) "Native failure returned success. $diagnostic"
                    Assert-True ($result.Output -notmatch 'Database (backup|restore) completed') "False success message. $diagnostic"
                    Assert-True ($result.Output -match "$($case.Fail) failed with exit code 23") "Missing utility/exit diagnostic. $diagnostic"
                }
                else {
                    Assert-True ($result.ExitCode -eq 0) "Script did not bind/complete. $diagnostic"
                    Assert-True ($result.Output -match 'Database (backup|restore) completed') "Missing success message. $diagnostic"
                }
                Assert-True ((@($result.Calls | ForEach-Object { $_.Utility }) -join ',') -eq ($expectedUtilities -join ',')) "Unexpected native call order. $diagnostic"
                foreach ($call in $result.Calls) {
                    $expected = switch ($call.Utility) {
                        'pg_dump' {
                            $fileArgument = @($call.Arguments | Where-Object { $_ -like '--file=*' })
                            Assert-True ($fileArgument.Count -eq 1) 'Expected exactly one dump output argument.'
                            $outputPath = $fileArgument[0].Substring('--file='.Length)
                            Assert-True ((Split-Path -Parent $outputPath) -eq $backupRoot) 'Dump output escaped requested backup folder.'
                            Assert-True ((Split-Path -Leaf $outputPath) -match '^prism test db-\d{8}-\d{6}\.dump$') 'Unexpected timestamped dump filename.'
                            @('--format=custom', "--host=$($case.HostValue)", '--port=5544', '--username=test operator', $fileArgument[0], 'prism test db')
                        }
                        'dropdb' { @('--if-exists', "--host=$($case.HostValue)", '--port=5544', '--username=test operator', 'prism test db') }
                        'createdb' { @("--host=$($case.HostValue)", '--port=5544', '--username=test operator', 'prism test db') }
                        'pg_restore' { @("--host=$($case.HostValue)", '--port=5544', '--username=test operator', '--dbname=prism test db', '--clean', '--if-exists', $dumpPath) }
                    }
                    Assert-True (($call.Arguments -join "`n") -eq ($expected -join "`n")) "Argument forwarding changed for $($call.Utility). $diagnostic"
                    Assert-True ($result.Output.Contains("stub stdout: $($call.Utility)")) 'Native stdout was lost.'
                    Assert-True ($result.Output.Contains("stub stderr: $($call.Utility)")) 'Native stderr was lost.'
                }
                $passed++
                Write-Host "PASS [$shell] $($case.Name)"
            }
            catch {
                $failures++
                Write-Host "FAIL [$shell] $($case.Name): $($_.Exception.Message)"
            }
        }
    }
}
finally {
    # Delete only this run's generated fixtures, never a supplied path or repository data.
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    if (-not $resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path -Leaf $resolvedFixture) -notmatch '^prism-db-script-tests-[a-f0-9]{32}$') {
        throw 'Refusing to clean an unexpected fixture path.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}

Write-Host "Database script regressions: $passed passed, $failures failed."
if ($failures -gt 0) { exit 1 }
