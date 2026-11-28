-- WebsiteCollector: the Bank of England's (BKENGB2L) daily spot exchange rates, the United Kingdom's reference rates,
-- from its public table. No UK bank publishes its own rates outside online banking, so these stand for the United
-- Kingdom, as the ECB's do for Belgium.
--
-- One dbo.Websites row ("Bank of England (BKENGB2L)") and one HTML dbo.WebsiteSelectors row for the table. Its
-- ContentSelector picks out the rate column's heading, with the day, then the table's cells, so a collection's
-- ExtractedJson is {"ContentSelector": ["£1 26 Nov 2026", "Australian Dollar", 1.9776, 2.0282, 1.8620, ...]}. The
-- DispatchUrl is POST <ApiBaseUrl>/api/currencyrate/currency/boe, which reads them with
-- Beep.Platform.Api/Websites/Currency/Parsers/BankOfEnglandRateParser.cs.
--
-- Run against the WebsiteCollector database, with the Api's address (no trailing slash) in the ApiBaseUrl
-- scripting variable. sqlcmd reads it from the environment (its -v option refuses a value with "/"):
--   $env:ApiBaseUrl = "https://api.example.com"
--   sqlcmd -S <server> -d WebsiteCollector -I -f 65001 -i UnitedKingdom_BankOfEngland_Rates.sql
-- (-I: the selector indexes are filtered; -f 65001: the file is UTF-8). In SSMS, use SQLCMD mode and first run
-- :setvar ApiBaseUrl "https://api.example.com". Safe to run more than once: the website is matched by Url and the
-- selector by website and CollectionUrl. An existing selector gets this script's ContentSelector and DispatchUrl; its
-- IsEnabled is left as it is.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ApiBaseUrl nvarchar(1000) = N'$(ApiBaseUrl)';
IF @ApiBaseUrl NOT LIKE N'https://%' OR RIGHT(@ApiBaseUrl, 1) = N'/'
	THROW 50000, N'ApiBaseUrl must be the Api''s https address without a trailing slash', 1;

DECLARE @Name nvarchar(200) = N'Bank of England (BKENGB2L)';
DECLARE @Url nvarchar(2048) = N'https://www.bankofengland.co.uk/';
DECLARE @CollectionUrl nvarchar(2048) = N'https://www.bankofengland.co.uk/boeapps/database/Rates.asp?into=GBP&rateview=D';
DECLARE @ContentSelector nvarchar(1000) = N'table.table thead th:nth-child(2), table.table tbody td';
DECLARE @DispatchUrl nvarchar(2048) = @ApiBaseUrl + N'/api/currencyrate/currency/boe';

BEGIN TRANSACTION;

IF NOT EXISTS (SELECT 1 FROM dbo.Websites WHERE Url = @Url)
	INSERT INTO dbo.Websites (Name, Url) VALUES (@Name, @Url);

DECLARE @WebsitesId uniqueidentifier = (SELECT TOP (1) Id FROM dbo.Websites WHERE Url = @Url);

UPDATE dbo.WebsiteSelectors
SET ContentSelector = @ContentSelector,
	DispatchUrl = @DispatchUrl
WHERE WebsitesId = @WebsitesId AND CollectionUrl = @CollectionUrl;

IF @@ROWCOUNT = 0
	INSERT INTO dbo.WebsiteSelectors (WebsitesId, CollectionUrl, CollectionType, ContentSelector, DispatchUrl, IsEnabled)
	VALUES (@WebsitesId, @CollectionUrl, 'HTML', @ContentSelector, @DispatchUrl, 1);

COMMIT TRANSACTION;

SELECT w.Name, s.CollectionUrl, s.ContentSelector, s.DispatchUrl, s.IsEnabled
FROM dbo.Websites w
JOIN dbo.WebsiteSelectors s ON s.WebsitesId = w.Id
WHERE w.Url = @Url AND s.CollectionUrl = @CollectionUrl;
