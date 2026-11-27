-- WebsiteCollector: the Vietnamese banks whose exchange-rate page has its rates in the HTML itself.
--
-- One dbo.Websites row per bank (named "<bank> (<BIC>)") and one HTML dbo.WebsiteSelectors row for its rate page.
-- Each ContentSelector picks out the rate table's cells, so a collection's ExtractedJson is
-- {"ContentSelector": ["USD", "25,635", "25,640", "26,065", "26,060", "EUR", ...]} (SeABank's: the script that
-- holds its rates). Each DispatchUrl is the bank's Beep.Api endpoint, POST <ApiBaseUrl>/api/currencyrate/currency/<name>,
-- which reads the cells with the bank's parser (Beep.Api/Domain/Currency/BankRateTable.cs, SeABankRateParser.cs).
--
-- Switched off (IsEnabled = 0) for now: VietinBank refuses a request without a User-Agent (the collector now sends
-- WebsiteCollector:UserAgent, Beep.Collector/1.0 by default); Nam A Bank and, at times, NCB refuse an agent that is
-- not a browser's.
--
-- Not here: banks whose rates are drawn with JavaScript, block the collector, or whose link is gone or missing.
-- Vietcombank (BFTVVNVX) is set up separately.
--
-- Run against the WebsiteCollector database, with the Api's address (no trailing slash) in the ApiBaseUrl
-- scripting variable. sqlcmd reads it from the environment (its -v option refuses a value with "/"):
--   $env:ApiBaseUrl = "https://api.example.com"
--   sqlcmd -S <server> -d WebsiteCollector -I -f 65001 -i Vietnam_Banks.sql
-- (-I: the selector indexes are filtered; -f 65001: the file is UTF-8). In SSMS, use SQLCMD mode and first run
-- :setvar ApiBaseUrl "https://api.example.com". Safe to run more than once: a website is
-- matched by Url and a selector by website and CollectionUrl. An existing selector gets this script's
-- ContentSelector and DispatchUrl; its IsEnabled is left as it is.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @ApiBaseUrl nvarchar(1000) = N'$(ApiBaseUrl)';
IF @ApiBaseUrl NOT LIKE N'https://%' OR RIGHT(@ApiBaseUrl, 1) = N'/'
	THROW 50000, N'ApiBaseUrl must be the Api''s https address without a trailing slash', 1;

DECLARE @Banks TABLE (
	Bic varchar(11) NOT NULL,
	Name nvarchar(200) NOT NULL,
	Url nvarchar(2048) NOT NULL,
	CollectionUrl nvarchar(2048) NOT NULL,
	ContentSelector nvarchar(1000) NOT NULL,
	Endpoint nvarchar(50) NOT NULL,
	IsEnabled bit NOT NULL
);

INSERT INTO @Banks (Bic, Name, Url, CollectionUrl, ContentSelector, Endpoint, IsEnabled) VALUES
	('HSBCVNVX', N'Ngân hàng TNHH MTV HSBC (Việt Nam)', N'https://www.hsbc.com.vn/',
		N'https://www.hsbc.com.vn/foreign-exchange/rate/', N'#content_main_basicTable_2 tbody tr > *', N'hsbc', 1),
	('HVBKVNVX', N'Ngân hàng Woori Bank Việt Nam', N'https://woori.com.vn/',
		N'https://woori.com.vn/ty-gia/', N'#change-dl tr > *', N'woori', 1),
	('ORCOVNVX', N'Ngân hàng TMCP Phương Đông – OCB', N'https://ocb.com.vn/',
		N'https://ocb.com.vn/vi/cong-cu/ti-gia', N'table tbody tr td', N'ocb', 1),
	('SEAVVNVX', N'Ngân hàng TMCP Đông Nam Á – SeABank', N'https://www.seabank.com.vn/',
		N'https://www.seabank.com.vn/cong-cu-tien-ich/ty-gia', N'script:contains("listExchangeRate")', N'seabank', 1),
	('VBAAVNVX', N'Ngân hàng Nông nghiệp và Phát triển Nông thôn Việt Nam – Agribank', N'https://www.agribank.com.vn/',
		N'https://www.agribank.com.vn/vn/ty-gia', N'#tyGiaCn tbody td', N'agribank', 1),
	('VCBCVNVX', N'Ngân hàng TMCP Bản Việt – BVBank', N'https://bvbank.net.vn/',
		N'https://bvbank.net.vn/ca-nhan/bang-ty-gia/', N'div.currency-table tbody td', N'bvbank', 1),
	('VNACVNVX', N'Ngân hàng TMCP Việt Á – VietABank', N'https://vietabank.com.vn/',
		N'https://vietabank.com.vn/tien-ich/ty-gia/ty-gia-ngoai-te.html', N'#dataCurrency tbody td', N'vietabank', 1),
	('ICBVVNVX', N'Ngân hàng TMCP Công Thương Việt Nam – VietinBank', N'https://www.vietinbank.vn/',
		N'https://www.vietinbank.vn/ca-nhan/ty-gia-khcn', N'table tbody tr td', N'vietinbank', 0),
	('NAMAVNVX', N'Ngân hàng TMCP Nam Á – Nam A Bank', N'https://www.namabank.com.vn/',
		N'https://www.namabank.com.vn/ty-gia', N'div.table table tbody td', N'namabank', 0),
	('NVBAVNVX', N'Ngân hàng TMCP Quốc Dân – NCB', N'https://www.ncb-bank.vn/',
		N'https://www.ncb-bank.vn/vi/ty-gia-tien-te', N'table.tb-content td', N'ncb', 0);

BEGIN TRANSACTION;

INSERT INTO dbo.Websites (Name, Url)
SELECT b.Name + N' (' + b.Bic + N')', b.Url
FROM @Banks b
WHERE NOT EXISTS (SELECT 1 FROM dbo.Websites w WHERE w.Url = b.Url);

UPDATE s
SET ContentSelector = b.ContentSelector,
	DispatchUrl = @ApiBaseUrl + N'/api/currencyrate/currency/' + b.Endpoint
FROM dbo.WebsiteSelectors s
JOIN dbo.Websites w ON w.Id = s.WebsitesId
JOIN @Banks b ON b.Url = w.Url AND b.CollectionUrl = s.CollectionUrl;

INSERT INTO dbo.WebsiteSelectors (WebsitesId, CollectionUrl, CollectionType, ContentSelector, DispatchUrl, IsEnabled)
SELECT w.Id, b.CollectionUrl, 'HTML', b.ContentSelector, @ApiBaseUrl + N'/api/currencyrate/currency/' + b.Endpoint, b.IsEnabled
FROM @Banks b
JOIN dbo.Websites w ON w.Url = b.Url
WHERE NOT EXISTS (
	SELECT 1 FROM dbo.WebsiteSelectors s
	WHERE s.WebsitesId = w.Id AND s.CollectionUrl = b.CollectionUrl);

COMMIT TRANSACTION;

SELECT b.Bic, w.Name, s.CollectionUrl, s.ContentSelector, s.DispatchUrl, s.IsEnabled
FROM @Banks b
JOIN dbo.Websites w ON w.Url = b.Url
JOIN dbo.WebsiteSelectors s ON s.WebsitesId = w.Id AND s.CollectionUrl = b.CollectionUrl
ORDER BY s.IsEnabled DESC, b.Bic;
