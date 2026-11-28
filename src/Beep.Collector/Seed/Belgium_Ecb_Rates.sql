-- WebsiteCollector: the European Central Bank's euro reference rates, recorded as Belgium's rates under the National
-- Bank of Belgium (NBBEBEBB), which publishes the same rates. Belgium's banks show their own rates only to their
-- customers, so these are the country's public rates.
--
-- One dbo.Websites row ("European Central Bank (NBBEBEBB)") and one HTML dbo.WebsiteSelectors row for the ECB's rates
-- page. Its ContentSelector picks out the page's day and the table's currency codes and rates, so a collection's
-- ExtractedJson is {"ContentSelector": ["27 November 2026", "USD", 1.0957, "JPY", 174.81, ...]}. The DispatchUrl is
-- POST <ApiBaseUrl>/api/currencyrate/currency/ecb, which reads them with Beep.Platform.Api/Domain/Currency/Parsers/EcbRateParser.cs.
-- The ECB publishes the rates on its working days at about 16:00 Central European Time.
--
-- Run against the WebsiteCollector database, with the Api's address (no trailing slash) in the ApiBaseUrl
-- scripting variable. sqlcmd reads it from the environment (its -v option refuses a value with "/"):
--   $env:ApiBaseUrl = "https://api.example.com"
--   sqlcmd -S <server> -d WebsiteCollector -I -f 65001 -i Belgium_Ecb_Rates.sql
-- (-I: the selector indexes are filtered; -f 65001: the file is UTF-8). In SSMS, use SQLCMD mode and first run
-- :setvar ApiBaseUrl "https://api.example.com". Safe to run more than once: the website is matched by Url and the
-- selector by website and CollectionUrl. An existing selector gets this script's ContentSelector and DispatchUrl; its
-- IsEnabled is left as it is.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ApiBaseUrl nvarchar(1000) = N'$(ApiBaseUrl)';
IF @ApiBaseUrl NOT LIKE N'https://%' OR RIGHT(@ApiBaseUrl, 1) = N'/'
	THROW 50000, N'ApiBaseUrl must be the Api''s https address without a trailing slash', 1;

DECLARE @Name nvarchar(200) = N'European Central Bank (NBBEBEBB)';
DECLARE @Url nvarchar(2048) = N'https://www.ecb.europa.eu/';
DECLARE @CollectionUrl nvarchar(2048) =
	N'https://www.ecb.europa.eu/stats/policy_and_exchange_rates/euro_reference_exchange_rates/html/index.en.html';
DECLARE @ContentSelector nvarchar(1000) = N'div.jumbo-box h3, table.forextable td.currency, table.forextable span.rate';
DECLARE @DispatchUrl nvarchar(2048) = @ApiBaseUrl + N'/api/currencyrate/currency/ecb';

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
