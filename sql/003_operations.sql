-- Pause a website
-- UPDATE dbo.Websites SET IsEnabled=0 WHERE Id=@WebsiteId;
-- Resume a website (run as soon as available)
-- UPDATE dbo.Websites SET IsEnabled=1,NextRunAt=SYSUTCDATETIME() WHERE Id=@WebsiteId;

-- Most overdue websites
SELECT TOP (100) Id,Name,NextRunAt,LeaseUntil,LastError
FROM dbo.Websites WHERE IsEnabled=1 ORDER BY NextRunAt;

-- Latest failures
SELECT TOP (100) WebsiteId,StartedAt,FinishedAt,ErrorMessage
FROM dbo.WebsiteExecutions WHERE Status='Failed' ORDER BY StartedAt DESC;

-- Count running jobs and overdue jobs
SELECT SUM(CASE WHEN LeaseUntil>SYSUTCDATETIME() THEN 1 ELSE 0 END) AS ActiveLeases,
 SUM(CASE WHEN NextRunAt<SYSUTCDATETIME() AND (LeaseUntil IS NULL OR LeaseUntil<SYSUTCDATETIME()) THEN 1 ELSE 0 END) AS DueJobs
FROM dbo.Websites WHERE IsEnabled=1;
