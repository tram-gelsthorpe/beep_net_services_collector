-- WebsiteCollector: the exchange rates of SNS Bank (de Volksbank, SNSBNL2A) in the Netherlands, for transfers in a
-- foreign currency, from its public rates page. The Netherlands' other large banks show their rates only to their
-- customers, in an app, or on pages that refuse automated requests.
--
-- One dbo.Websites row ("SNS Bank (SNSBNL2A)") and one HTML dbo.WebsiteSelectors row for the rates page. Its
-- ContentSelector picks out the cells of the rates table, which follows the page's "Wisselkoersen" heading (and not
-- the fees table below it), so a collection's ExtractedJson is
-- {"ContentSelector": ["AUD", "Australische dollar", "1,63595", "1,65595", ...]}. The DispatchUrl is
-- POST <ApiBaseUrl>/api/currencyrate/currency/sns, which reads them with Beep.Platform.Api/Websites/Currency/Parsers/SnsRateParser.cs.
-- The page gives no time: the rates are recorded at the time they are collected.
--
-- Run against the WebsiteCollector database, with the Api's address (no trailing slash) in the ApiBaseUrl
-- scripting variable. sqlcmd reads it from the environment (its -v option refuses a value with "/"):
--   $env:ApiBaseUrl = "https://api.example.com"
--   sqlcmd -S <server> -d WebsiteCollector -I -f 65001 -i Netherlands_Sns_Rates.sql
-- (-I: the selector indexes are filtered; -f 65001: the file is UTF-8). In SSMS, use SQLCMD mode and first run
-- :setvar ApiBaseUrl "https://api.example.com". Safe to run more than once: the website is matched by Url and the
-- selector by website and CollectionUrl. An existing selector gets this script's ContentSelector and DispatchUrl; its
-- IsEnabled is left as it is.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ApiBaseUrl nvarchar(1000) = N'$(ApiBaseUrl)';
IF @ApiBaseUrl NOT LIKE N'https://%' OR RIGHT(@ApiBaseUrl, 1) = N'/'
	THROW 50000, N'ApiBaseUrl must be the Api''s https address without a trailing slash', 1;

DECLARE @Name nvarchar(200) = N'SNS Bank (SNSBNL2A)';
DECLARE @Url nvarchar(2048) = N'https://www.snsbank.nl/';
DECLARE @CollectionUrl nvarchar(2048) = N'https://www.snsbank.nl/particulier/betalen/actuele-wisselkoers-mijn-sns.html';
DECLARE @ContentSelector nvarchar(1000) = N'#Wisselkoersen-anchor + div.block-responsivetable td';
DECLARE @DispatchUrl nvarchar(2048) = @ApiBaseUrl + N'/api/currencyrate/currency/sns';

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
