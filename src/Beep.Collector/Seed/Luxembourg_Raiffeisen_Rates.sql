-- WebsiteCollector: the exchange rates of Banque Raiffeisen (CCRALULL) in Luxembourg, from its public page: transfer
-- rates for 15 currencies, and banknote rates for 7 of them. Luxembourg's other large banks publish only card-payment
-- margins, or a daily PDF (Spuerkeess), which the collector cannot read.
--
-- One dbo.Websites row ("Banque Raiffeisen (CCRALULL)") and one HTML dbo.WebsiteSelectors row for the page. Its
-- ContentSelector picks out the table's day and time, then its cells, so a collection's ExtractedJson is
-- {"ContentSelector": ["27/11/2026", "07:15", "AUD", "1,654400", "1,637300", "", "", ...]}. The DispatchUrl is
-- POST <ApiBaseUrl>/api/currencyrate/currency/raiffeisenlu, which reads them with
-- Beep.Api/Domain/Currency/Parsers/RaiffeisenLuRateParser.cs.
--
-- Run against the WebsiteCollector database, with the Api's address (no trailing slash) in the ApiBaseUrl
-- scripting variable. sqlcmd reads it from the environment (its -v option refuses a value with "/"):
--   $env:ApiBaseUrl = "https://api.example.com"
--   sqlcmd -S <server> -d WebsiteCollector -I -f 65001 -i Luxembourg_Raiffeisen_Rates.sql
-- (-I: the selector indexes are filtered; -f 65001: the file is UTF-8). In SSMS, use SQLCMD mode and first run
-- :setvar ApiBaseUrl "https://api.example.com". Safe to run more than once: the website is matched by Url and the
-- selector by website and CollectionUrl. An existing selector gets this script's ContentSelector and DispatchUrl; its
-- IsEnabled is left as it is.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ApiBaseUrl nvarchar(1000) = N'$(ApiBaseUrl)';
IF @ApiBaseUrl NOT LIKE N'https://%' OR RIGHT(@ApiBaseUrl, 1) = N'/'
	THROW 50000, N'ApiBaseUrl must be the Api''s https address without a trailing slash', 1;

DECLARE @Name nvarchar(200) = N'Banque Raiffeisen (CCRALULL)';
DECLARE @Url nvarchar(2048) = N'https://raiffeisen.lu/';
DECLARE @CollectionUrl nvarchar(2048) = N'https://raiffeisen.lu/fr/particuliers/gerer-au-quotidien/cours-de-change';
DECLARE @ContentSelector nvarchar(1000) = N'app-exchange-rates thead th:first-child div, app-exchange-rates thead th:first-child small, app-exchange-rates tbody td';
DECLARE @DispatchUrl nvarchar(2048) = @ApiBaseUrl + N'/api/currencyrate/currency/raiffeisenlu';

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
