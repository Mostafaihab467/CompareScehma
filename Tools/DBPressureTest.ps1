<#
.SYNOPSIS
    Read-only DB pressure test for the DB Health window: generates real deadlocks
    and sustained CPU load without modifying any table.

.DESCRIPTION
    Opens two (or more) sessions that lock row batches in two tables with UPDLOCK
    in OPPOSITE order, so SQL Server kills one with error 1205 every few seconds
    — classic lock-order deadlock. The killed session retries, so deadlocks keep
    flowing into the system_health extended-events ring buffer (what the DB Health
    "Deadlocks" tab reads). Extra sessions burn CPU with read-only COUNT_BIG
    cross joins.

    NOTHING IS WRITTEN: every statement is a plain SELECT. The UPDLOCK is held
    only until the transaction ends (commit/rollback by the deadlock victim).

    This mirrors the standard technique documented for testing deadlock handling
    (see Microsoft docs "Deadlocking" / sp_deadlock sample scripts): two sessions,
    two resources, reversed acquisition order.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Tools\DBPressureTest.ps1 -DurationSeconds 60
    (then open DB Health in the app and watch Overview CPU + Deadlocks tab)

.NOTES
    Stop early with Ctrl+C. Safe to run against a dev database; do NOT point it
    at production.
#>
param(
    [string]$Server = "localhost",
    [string]$Database = "EgyptMart",
    [string]$TableA = "dbo.Lup_CountryList",
    [string]$TableB = "dbo.Pre_Users",
    [int]$DeadlockSessions = 2,
    [int]$CpuSessions = 2,
    [int]$DurationSeconds = 45,
    [int]$LockHoldMs = 40
)

$ErrorActionPreference = "Stop"
$script:stopAt = (Get-Date).AddSeconds($DurationSeconds)
$script:deadlocks = 0
$script:deadlockIterations = 0
$script:cpuIterations = 0

function New-Conn {
    $c = New-Object System.Data.SqlClient.SqlConnection
    $c.ConnectionString = "Server=$Server;Database=$Database;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=10"
    $c.Open()
    return $c
}

function Invoke-Scalar([System.Data.SqlClient.SqlConnection]$Conn, [string]$Sql) {
    $cmd = $Conn.CreateCommand()
    $cmd.CommandText = $Sql
    $cmd.CommandTimeout = 30
    return $cmd.ExecuteScalar()
}

# Lock two tables in opposite order per session, retrying after each 1205.
# UPDLOCK keeps the lock exclusive-to-readers-so-far; HOLDLOCK keeps it until
# the transaction ends. The delay between the two locks is what lets the other
# session get in and form the cycle.
function Start-DeadlockSession([int]$Id, [bool]$Reverse) {
    $first = if ($Reverse) { $TableB } else { $TableA }
    $second = if ($Reverse) { $TableA } else { $TableB }
    $holdMs = $LockHoldMs
    $sql = @"
WHILE 1 = 1
BEGIN
    BEGIN TRAN;
    SELECT TOP (1) 1 FROM $first WITH (UPDLOCK, HOLDLOCK);
    WAITFOR DELAY '00:00:00.$($holdMs.ToString('D3'))';
    SELECT TOP (1) 1 FROM $second WITH (UPDLOCK, HOLDLOCK);
    COMMIT TRAN;
END
"@
    $conn = New-Conn
    try {
        while ((Get-Date) -lt $script:stopAt) {
            try {
                $script:deadlockIterations++
                $null = Invoke-Scalar $conn $sql   # runs until a 1205 or deadline
            }
            catch [System.Data.SqlClient.SqlException] {
                if ($_.Exception.Number -eq 1205) {
                    $script:deadlocks++
                    Write-Host ("[deadlock] session {0} was chosen as victim (total: {1})" -f $Id, $script:deadlocks)
                }
                else {
                    Write-Host ("[deadlock] session {0} error {1}: {2}" -f $Id, $_.Exception.Number, $_.Exception.Message)
                    Start-Sleep -Milliseconds 200
                }
            }
            catch {
                Write-Host ("[deadlock] session {0}: {1}" -f $Id, $_.Exception.Message)
                Start-Sleep -Milliseconds 200
                if ($conn.State -ne "Open") { $conn = New-Conn }
            }
        }
    }
    finally { $conn.Dispose() }
}

# Pure CPU burner: read-only COUNT_BIG over a cross join (~5M rows per pass).
function Start-CpuSession([int]$Id) {
    $conn = New-Conn
    try {
        while ((Get-Date) -lt $script:stopAt) {
            try {
                $null = Invoke-Scalar $conn "SELECT COUNT_BIG(*) FROM sys.objects a CROSS JOIN sys.objects b"
                $script:cpuIterations++
            }
            catch {
                if ($conn.State -ne "Open") { $conn = New-Conn } else { Start-Sleep -Milliseconds 200 }
            }
        }
    }
    finally { $conn.Dispose() }
}

Write-Host ("DB pressure test: server={0}/{1}  deadlock sessions={2}  cpu sessions={3}  duration={4}s" -f `
    $Server, $Database, $DeadlockSessions, $CpuSessions, $DurationSeconds)
Write-Host "READ-ONLY: no table is modified. Locks only. Ctrl+C to stop early."
Write-Host ("Lock order: [A] {0} -> {1}   [B] {1} -> {0}" -f $TableA, $TableB)

$jobs = @()
for ($i = 1; $i -le $DeadlockSessions; $i++) {
    $jobs += Start-Job -ScriptBlock ${function:Start-DeadlockSession} -ArgumentList @($i, ($i % 2 -eq 0)) `
        -InitializationScript {
            $Server = $using:Server; $Database = $using:Database
            $TableA = $using:TableA; $TableB = $using:TableB
            $LockHoldMs = $using:LockHoldMs
            $script:stopAt = (Get-Date).AddSeconds($using:DurationSeconds)
            $script:deadlocks = 0; $script:deadlockIterations = 0; $script:cpuIterations = 0
        }
}
for ($i = 1; $i -le $CpuSessions; $i++) {
    $jobs += Start-Job -ScriptBlock ${function:Start-CpuSession} -ArgumentList @($i) `
        -InitializationScript {
            $Server = $using:Server; $Database = $using:Database
            $script:stopAt = (Get-Date).AddSeconds($using:DurationSeconds)
            $script:cpuIterations = 0
        }
}

# Because Start-Job isolates state, poll each job's output until the deadline.
while ((Get-Date) -lt $script:stopAt) {
    Start-Sleep -Seconds 5
    foreach ($j in $jobs) { Receive-Job $j -ErrorAction SilentlyContinue | Write-Host }
}

Write-Host "Deadline reached — stopping jobs…"
$jobs | Stop-Job
foreach ($j in $jobs) { Receive-Job $j -ErrorAction SilentlyContinue | Write-Host; Remove-Job $j -Force }
Write-Host "Done. Deadlock graphs are in the system_health ring buffer — open DB Health > Deadlocks."
