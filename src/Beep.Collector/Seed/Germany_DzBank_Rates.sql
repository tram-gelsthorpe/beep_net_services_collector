-- WebsiteCollector: the foreign-exchange fixing of DZ BANK (GENODEFF), the central bank of Germany's Volksbanken and
-- Raiffeisenbanken, from its public price list D810 "Devisenhandel - Hauptwährungen" (8 main currencies). Germany's other
-- large banks show their rates only to their customers, or publish only cash rates (Commerzbank).
--
-- One dbo.Websites row ("DZ BANK (GENODEFF)") and one HTML dbo.WebsiteSelectors row for the list. Its ContentSelector
-- picks out the cells of the list's rows, so a collection's ExtractedJson is
-- {"ContentSelector": ["EURUSD", "US Dollar", "1,09300", "1,09650", "1,10000", "27.11.2026 13:07:15", ...]}. The
-- DispatchUrl is POST <ApiBaseUrl>/api/currencyrate/currency/dzbank, which reads them with
-- Beep.Platform.Api/Domain/Currency/Parsers/DzBankRateParser.cs. Each row gives the time of its rates.
--
-- Run against the WebsiteCollector database, with the Api's address (no trailing slash) in the ApiBaseUrl
-- scripting variable. sqlcmd reads it from the environment (its -v option refuses a value with "/"):
--   $env:ApiBaseUrl = "https://api.example.com"
--   sqlcmd -S <server> -d WebsiteCollector -I -f 65001 -i Germany_DzBank_Rates.sql
-- (-I: the selector indexes are filtered; -f 65001: the file is UTF-8). In SSMS, use SQLCMD mode and first run
-- :setvar ApiBaseUrl "https://api.example.com". Safe to run more than once: the website is matched by Url and the
-- selector by website and CollectionUrl. An existing selector gets this script's ContentSelector and DispatchUrl; its
-- IsEnabled is left as it is.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ApiBaseUrl nvarchar(1000) = N'$(ApiBaseUrl)';
IF @ApiBaseUrl NOT LIKE N'https://%' OR RIGHT(@ApiBaseUrl, 1) = N'/'
	THROW 50000, N'ApiBaseUrl must be the Api''s https address without a trailing slash', 1;

DECLARE @Name nvarchar(200) = N'DZ BANK (GENODEFF)';
DECLARE @Url nvarchar(2048) = N'https://bvapps.vr-bankenportal.de/';
DECLARE @CollectionUrl nvarchar(2048) = N'https://bvapps.vr-bankenportal.de/gla/devisenkurse/D810.html';
DECLARE @ContentSelector nvarchar(1000) = N'tr.rowhell td, tr.rowdark td';
DECLARE @DispatchUrl nvarchar(2048) = @ApiBaseUrl + N'/api/currencyrate/currency/dzbank';

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
