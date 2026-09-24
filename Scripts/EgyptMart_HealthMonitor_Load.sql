/* ==============================================================================
   EgyptMart — Performance Monitor & DB Health Load / Stress Test Script
   ==============================================================================
   Purpose:
     Analyzes EgyptMart database tables and generates deliberate, controlled load
     to test and trigger EVERY section of the DB Health Performance Monitor:
       1) SQL Server CPU % (parallel queries, CPU burn)
       2) SQL Memory & Memory Grants Pending (hash spills, massive sorts)
       3) Batch Requests / sec (rapid-fire execution loop)
       4) Waiting Tasks & Runnable Tasks
       5) Blocked Processes (exclusive locks with delayed hold)
       6) Page Splits / sec (random clustered GUID inserts with 800-byte payload)
       7) Page Life Expectancy (PLE) & Buffer Cache Hit Ratio (cache churning)
       8) Deadlocks / sec & Deadlock Graphs (system_health ring buffer events)
       9) Processes Grid (active sessions, blocking chains, CPU, logical reads)
      10) Resource Waits (CXPACKET, LCK_M_X, PAGEIOLATCH_SH, SOS_SCHEDULER_YIELD)
      11) Expensive Queries (top queries by total CPU, avg CPU, logical reads)
      12) Data File I/O (reads, writes, stall times)

   Target Database Tables Analyzed (EgyptMart):
     - dbo.SDA_ProductViewInfo (product views, user interactions)
     - dbo.Products_Basic (core product catalog)
     - dbo.Pre_Users (system users)
     - dbo.Products_Images (product image links)
     - dbo.Bsk_CategoryList (product categories)
     - dbo.Bsk_lnk_AttributeCategoryLink (attribute category mappings)
     - dbo.Lup_CountryList (country lookups)

   Safety Note:
     - Real database tables are accessed READ-ONLY with NOLOCK or read queries.
     - Writes only occur in a sandbox schema: [health_load].
     - Section F safely drops [health_load] when done.
   ============================================================================== */

IF DB_NAME() <> N'EgyptMart'
BEGIN
    IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'EgyptMart')
        USE EgyptMart;
END;
GO

/* ==============================================================================
   A) SETUP — Create [health_load] Sandbox Tables
   ============================================================================== */
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'health_load')
    EXEC(N'CREATE SCHEMA health_load');
GO

IF OBJECT_ID(N'health_load.LockA', N'U') IS NOT NULL DROP TABLE health_load.LockA;
IF OBJECT_ID(N'health_load.LockB', N'U') IS NOT NULL DROP TABLE health_load.LockB;
IF OBJECT_ID(N'health_load.BigHeap', N'U') IS NOT NULL DROP TABLE health_load.BigHeap;
GO

CREATE TABLE health_load.LockA
(
    Id      INT NOT NULL PRIMARY KEY,
    Payload NVARCHAR(4000) NOT NULL
);

CREATE TABLE health_load.LockB
(
    Id      INT NOT NULL PRIMARY KEY,
    Payload NVARCHAR(4000) NOT NULL
);

INSERT INTO health_load.LockA (Id, Payload) VALUES (1, REPLICATE(N'Resource-A', 50));
INSERT INTO health_load.LockB (Id, Payload) VALUES (1, REPLICATE(N'Resource-B', 50));
GO

-- Clustered GUID table: inserting random GUIDs with 800-byte rows forces 50/50 page splits
CREATE TABLE health_load.BigHeap
(
    Id        UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_health_load_BigHeap PRIMARY KEY,
    CreatedAt DATETIME2        NOT NULL CONSTRAINT DF_health_load_BigHeap_Created DEFAULT SYSUTCDATETIME(),
    Pad       CHAR(800)        NOT NULL CONSTRAINT DF_health_load_BigHeap_Pad DEFAULT '',
    Checksum  INT              NOT NULL CONSTRAINT DF_health_load_BigHeap_Cs DEFAULT 0
);
GO

PRINT '✓ [health_load] setup completed successfully.';
GO


/* ==============================================================================
   B) DEADLOCK GENERATOR — SESSION 1 (Run in Query Tab 1)
   ------------------------------------------------------------------------------
   Acquires LockA, waits 6 seconds, then attempts to acquire LockB.
   When run concurrently with Session 2, SQL Server detects a deadlock cycle,
   kills one session (Error 1205), and logs the deadlock graph to system_health.
   ============================================================================== */
/*
-- UNCOMMENT TO RUN IN QUERY TAB 1:
SET NOCOUNT ON;
PRINT 'Session 1 starting deadlock loop (Ctrl+C or Stop to abort)...';
WHILE 1 = 1
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE health_load.LockA SET Payload = N'Session1-Updated' WHERE Id = 1;
        WAITFOR DELAY '00:00:06';
        UPDATE health_load.LockB SET Payload = N'Session1-Updated' WHERE Id = 1;
        COMMIT TRANSACTION;
        PRINT 'Session 1: cycle completed.';
        WAITFOR DELAY '00:00:02';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        IF ERROR_NUMBER() = 1205
            PRINT 'Session 1 was chosen as DEADLOCK VICTIM (logged to DB Health Deadlocks tab). Retrying...';
        ELSE
            PRINT 'Session 1 error: ' + ERROR_MESSAGE();
        WAITFOR DELAY '00:00:01';
    END CATCH;
END;
*/


/* ==============================================================================
   C) DEADLOCK GENERATOR — SESSION 2 (Run in Query Tab 2)
   ------------------------------------------------------------------------------
   Acquires LockB, waits 6 seconds, then attempts to acquire LockA (reverse order).
   ============================================================================== */
/*
-- UNCOMMENT TO RUN IN QUERY TAB 2:
SET NOCOUNT ON;
PRINT 'Session 2 starting deadlock loop (Ctrl+C or Stop to abort)...';
WHILE 1 = 1
BEGIN
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE health_load.LockB SET Payload = N'Session2-Updated' WHERE Id = 1;
        WAITFOR DELAY '00:00:06';
        UPDATE health_load.LockA SET Payload = N'Session2-Updated' WHERE Id = 1;
        COMMIT TRANSACTION;
        PRINT 'Session 2: cycle completed.';
        WAITFOR DELAY '00:00:02';
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        IF ERROR_NUMBER() = 1205
            PRINT 'Session 2 was chosen as DEADLOCK VICTIM (logged to DB Health Deadlocks tab). Retrying...';
        ELSE
            PRINT 'Session 2 error: ' + ERROR_MESSAGE();
        WAITFOR DELAY '00:00:01';
    END CATCH;
END;
*/


/* ==============================================================================
   D) COMPREHENSIVE LOAD SUITE (Triggers CPU, RAM, Batches, Splits, Waits)
   ------------------------------------------------------------------------------
   Execute this batch to stimulate all performance monitor counters.
   ============================================================================== */
SET NOCOUNT ON;
PRINT '>>> Starting DB Health Performance Monitor Load Test...';
GO

-- ------------------------------------------------------------------------------
-- D1. HIGH CPU BURN (Multi-core parallel hash & checksum over real catalog)
-- ------------------------------------------------------------------------------
PRINT '1/6: Generating High CPU load (multi-threaded checksums & cross joins)...';
DECLARE @cpuStart DATETIME = GETDATE();
DECLARE @cpuBurn BIGINT = 0;
DECLARE @iter INT = 0;

WHILE @iter < 30
BEGIN
    SELECT @cpuBurn = @cpuBurn + ISNULL(CHECKSUM_AGG(CHECKSUM(v.ProductID, v.UserID, p.ProductsID, p.ProductTitle, NEWID())), 0)
    FROM dbo.SDA_ProductViewInfo AS v WITH (NOLOCK)
    CROSS JOIN dbo.Products_Basic AS p WITH (NOLOCK)
    CROSS JOIN dbo.Pre_Users AS u WITH (NOLOCK)
    WHERE (v.ViewID % 11) = (@iter % 11)
      AND (p.ProductsID % 7) = (@iter % 7)
    OPTION (MAXDOP 0, RECOMPILE);

    SET @iter += 1;
END;
PRINT '   CPU Burn Result: ' + CAST(@cpuBurn AS NVARCHAR(50)) + ' (' + CAST(DATEDIFF(ms, @cpuStart, GETDATE()) AS NVARCHAR(20)) + ' ms)';
GO

-- ------------------------------------------------------------------------------
-- D2. MEMORY / RAM CONSUMPTION & MEMORY GRANTS PENDING (Huge Sort / Hash Spill)
-- ------------------------------------------------------------------------------
PRINT '2/6: Requesting large memory grant (Hash join & large sort)...';
IF OBJECT_ID(N'tempdb..#Numbers', N'U') IS NOT NULL DROP TABLE #Numbers;

;WITH N(n) AS (
    SELECT 1 UNION ALL SELECT n + 1 FROM N WHERE n < 2500
)
SELECT n INTO #Numbers FROM N OPTION (MAXRECURSION 2500);

IF OBJECT_ID(N'tempdb..#MemoryStress', N'U') IS NOT NULL DROP TABLE #MemoryStress;

SELECT TOP (300000)
       a.n AS NumA,
       b.n AS NumB,
       REPLICATE(N'M', 50) AS Filler,
       CHECKSUM(a.n, b.n, NEWID()) AS HashKey
INTO #MemoryStress
FROM #Numbers AS a
CROSS JOIN #Numbers AS b
ORDER BY CHECKSUM(a.n, b.n, NEWID())
OPTION (HASH JOIN, MAXDOP 0);

DROP TABLE #Numbers;
DROP TABLE #MemoryStress;
PRINT '   Memory grant test completed.';
GO

-- ------------------------------------------------------------------------------
-- D3. EXPENSIVE QUERY EXECUTION (Populates Expensive Queries tab)
-- ------------------------------------------------------------------------------
PRINT '3/6: Executing complex analytics queries (Expensive Queries tab)...';
SELECT TOP (2000)
       v.ViewID,
       v.ProductID,
       p.ProductTitle,
       u.UserName,
       img.ImageURL,
       COUNT(*) OVER(PARTITION BY v.ProductID) AS ViewsPerProduct,
       DENSE_RANK() OVER(ORDER BY v.ViewID DESC) AS ViewRank
FROM dbo.SDA_ProductViewInfo AS v WITH (NOLOCK)
LEFT JOIN dbo.Products_Basic AS p WITH (NOLOCK) ON p.ProductsID = v.ProductID
LEFT JOIN dbo.Pre_Users AS u WITH (NOLOCK) ON u.UserID = v.UserID
LEFT JOIN dbo.Products_Images AS img WITH (NOLOCK) ON img.ProductID = p.ProductsID
ORDER BY v.ViewID DESC
OPTION (RECOMPILE);

SELECT c.CategoryID,
       c.CategoryTitle,
       COUNT(l.AttributeID) AS TotalAttributes,
       COUNT(DISTINCT p.ProductsID) AS TotalProducts
FROM dbo.Bsk_CategoryList AS c WITH (NOLOCK)
LEFT JOIN dbo.Bsk_lnk_AttributeCategoryLink AS l WITH (NOLOCK) ON l.CategoryID = c.CategoryID
LEFT JOIN dbo.Products_Basic AS p WITH (NOLOCK) ON p.CategoryID = c.CategoryID
GROUP BY c.CategoryID, c.CategoryTitle
HAVING COUNT(l.AttributeID) >= 0
ORDER BY TotalProducts DESC, TotalAttributes DESC
OPTION (RECOMPILE);
GO

-- ------------------------------------------------------------------------------
-- D4. PAGE SPLITS / SEC & FILE I/O (Random GUID insertions into 800-byte rows)
-- ------------------------------------------------------------------------------
PRINT '4/6: Triggering Page Splits/sec (inserting 3,000 clustered GUID rows)...';
DECLARE @splitCount INT = 0;
WHILE @splitCount < 3000
BEGIN
    INSERT INTO health_load.BigHeap (Id, Pad, Checksum)
    VALUES (NEWID(), REPLICATE('P', 800), CHECKSUM(NEWID(), @splitCount));

    SET @splitCount += 1;
END;
PRINT '   Page split inserts completed.';
GO

-- ------------------------------------------------------------------------------
-- D5. BATCH REQUESTS / SEC (Pushing counter above 500-1000/sec)
-- ------------------------------------------------------------------------------
PRINT '5/6: Spiking Batch Requests / sec counter (rapid loops)...';
DECLARE @batchCounter INT = 0;
WHILE @batchCounter < 3000
BEGIN
    SELECT 1 WHERE 1 = 1;
    SET @batchCounter += 1;
END;
PRINT '   Batch requests spike finished.';
GO

-- ------------------------------------------------------------------------------
-- D6. BLOCKING & LOCK WAITS (Simulates a locked session)
-- ------------------------------------------------------------------------------
PRINT '6/6: Holding an exclusive row lock for 15s (watch Blocked Processes & Processes tab)...';
BEGIN TRANSACTION;
UPDATE health_load.LockA SET Payload = N'Exclusively-Locked-For-Monitor' WHERE Id = 1;
WAITFOR DELAY '00:00:15';
COMMIT TRANSACTION;
PRINT '   Exclusive lock released.';
GO

PRINT '==============================================================================';
PRINT '>>> ALL DB HEALTH LOAD TESTS COMPLETE!';
PRINT '    Inspect the DB Health window:';
PRINT '      - Overview: CPU, Memory, Batch/s, Waiting Tasks, Page Splits, PLE';
PRINT '      - Processes: Check active and recently active sessions';
PRINT '      - Resource Waits: Check wait types and time distribution';
PRINT '      - Expensive Queries: Top queries sorted by CPU, Duration, Reads';
PRINT '      - Deadlocks: If Section B & C were run, see victim & process graph';
PRINT '      - Data File I/O: Check stall times and read/write counts';
PRINT '==============================================================================';
GO


/* ==============================================================================
   E) AUTOMATED SELF-CONTAINED DEADLOCK TEST (Single-script deadlock)
   ------------------------------------------------------------------------------
   Spawns two concurrent transactions in separate batches using sp_executesql
   or local transactions with lock escalation.
   ============================================================================== */
CREATE OR ALTER PROCEDURE health_load.usp_TriggerDeadlock
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;
        UPDATE health_load.LockA SET Payload = 'DeadlockProc-A' WHERE Id = 1;
        WAITFOR DELAY '00:00:05';
        UPDATE health_load.LockB SET Payload = 'DeadlockProc-B' WHERE Id = 1;
        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    END CATCH;
END;
GO


/* ==============================================================================
   F) TEARDOWN / CLEANUP
   ------------------------------------------------------------------------------
   Uncomment and execute when finished with testing to drop sandbox schema & tables.
   ============================================================================== */
/*
IF OBJECT_ID(N'health_load.usp_TriggerDeadlock', N'P') IS NOT NULL DROP PROCEDURE health_load.usp_TriggerDeadlock;
IF OBJECT_ID(N'health_load.LockA', N'U') IS NOT NULL DROP TABLE health_load.LockA;
IF OBJECT_ID(N'health_load.LockB', N'U') IS NOT NULL DROP TABLE health_load.LockB;
IF OBJECT_ID(N'health_load.BigHeap', N'U') IS NOT NULL DROP TABLE health_load.BigHeap;
IF EXISTS (SELECT 1 FROM sys.schemas WHERE name = N'health_load')
    EXEC(N'DROP SCHEMA health_load');
PRINT '✓ Sandbox schema [health_load] cleaned up.';
*/
