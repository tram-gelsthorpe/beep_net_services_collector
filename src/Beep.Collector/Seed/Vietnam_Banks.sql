-- WebsiteCollector: the Vietnamese banks whose exchange-rate page has its rates in the HTML itself.
--
-- One dbo.Websites row per bank (named "<bank> (<BIC>)") and one HTML dbo.WebsiteSelectors row for its rate page.
-- Each ContentSelector picks out the rate table's rows (or cells, where a row's cells run together), so a
-- collection's ExtractedJson is {"ContentSelector": ["USD 25,635 25,640 26,065 26,060", ...]}.
-- No DispatchUrl yet: the pages are collected and stored, and nothing reads them until a bank has a parser.
--
-- Switched off (IsEnabled = 0) until the collector sends a User-Agent header:
--   VietinBank refuses a request without one; Nam A Bank and NCB refuse any agent that is not a browser's.
-- Notes for parsers:
--   VietinBank writes 25.470 for 25,470; the collector turns such a cell into the JSON number 25.47.
--   SeABank's rates are JSON inside a Next.js script ("listExchangeRate":{"details":[...]}).
--
-- Not here: banks whose rates are drawn with JavaScript, block the collector, or whose link is gone or missing.
-- Vietcombank (BFTVVNVX) is set up separately.
--
-- Run against the WebsiteCollector database: sqlcmd -S <server> -d WebsiteCollector -I -f 65001 -i Vietnam_Banks.sql
-- (-I: the selector indexes are filtered; -f 65001: the file is UTF-8). Safe to run more than once: a website is
-- matched by Url and a selector by website and CollectionUrl, and existing rows are left as they are.

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Banks TABLE (
	Bic varchar(11) NOT NULL,
	Name nvarchar(200) NOT NULL,
	Url nvarchar(2048) NOT NULL,
	CollectionUrl nvarchar(2048) NOT NULL,
	ContentSelector nvarchar(1000) NOT NULL,
	IsEnabled bit NOT NULL
);

INSERT INTO @Banks (Bic, Name, Url, CollectionUrl, ContentSelector, IsEnabled) VALUES
	('HSBCVNVX', N'Ngân hàng TNHH MTV HSBC (Việt Nam)', N'https://www.hsbc.com.vn/',
		N'https://www.hsbc.com.vn/foreign-exchange/rate/', N'#content_main_basicTable_2 tbody tr', 1),
	('HVBKVNVX', N'Ngân hàng Woori Bank Việt Nam', N'https://woori.com.vn/',
		N'https://woori.com.vn/ty-gia/', N'#change-dl tr', 1),
	('ORCOVNVX', N'Ngân hàng TMCP Phương Đông – OCB', N'https://ocb.com.vn/',
		N'https://ocb.com.vn/vi/cong-cu/ti-gia', N'table tbody tr td', 1),
	('SEAVVNVX', N'Ngân hàng TMCP Đông Nam Á – SeABank', N'https://www.seabank.com.vn/',
		N'https://www.seabank.com.vn/cong-cu-tien-ich/ty-gia', N'script:contains("listExchangeRate")', 1),
	('VBAAVNVX', N'Ngân hàng Nông nghiệp và Phát triển Nông thôn Việt Nam – Agribank', N'https://www.agribank.com.vn/',
		N'https://www.agribank.com.vn/vn/ty-gia', N'#tyGiaCn tbody tr', 1),
	('VCBCVNVX', N'Ngân hàng TMCP Bản Việt – BVBank', N'https://bvbank.net.vn/',
		N'https://bvbank.net.vn/ca-nhan/bang-ty-gia/', N'div.currency-table tbody tr', 1),
	('VNACVNVX', N'Ngân hàng TMCP Việt Á – VietABank', N'https://vietabank.com.vn/',
		N'https://vietabank.com.vn/tien-ich/ty-gia/ty-gia-ngoai-te.html', N'#dataCurrency tbody tr', 1),
	('ICBVVNVX', N'Ngân hàng TMCP Công Thương Việt Nam – VietinBank', N'https://www.vietinbank.vn/',
		N'https://www.vietinbank.vn/ca-nhan/ty-gia-khcn', N'table tbody tr td', 0),
	('NAMAVNVX', N'Ngân hàng TMCP Nam Á – Nam A Bank', N'https://www.namabank.com.vn/',
		N'https://www.namabank.com.vn/ty-gia', N'div.table table tbody tr', 0),
	('NVBAVNVX', N'Ngân hàng TMCP Quốc Dân – NCB', N'https://www.ncb-bank.vn/',
		N'https://www.ncb-bank.vn/vi/ty-gia-tien-te', N'table.tb-content tr', 0);

BEGIN TRANSACTION;

INSERT INTO dbo.Websites (Name, Url)
SELECT b.Name + N' (' + b.Bic + N')', b.Url
FROM @Banks b
WHERE NOT EXISTS (SELECT 1 FROM dbo.Websites w WHERE w.Url = b.Url);

INSERT INTO dbo.WebsiteSelectors (WebsitesId, CollectionUrl, CollectionType, ContentSelector, IsEnabled)
SELECT w.Id, b.CollectionUrl, 'HTML', b.ContentSelector, b.IsEnabled
FROM @Banks b
JOIN dbo.Websites w ON w.Url = b.Url
WHERE NOT EXISTS (
	SELECT 1 FROM dbo.WebsiteSelectors s
	WHERE s.WebsitesId = w.Id AND s.CollectionUrl = b.CollectionUrl);

COMMIT TRANSACTION;

SELECT b.Bic, w.Name, s.CollectionUrl, s.ContentSelector, s.IsEnabled
FROM @Banks b
JOIN dbo.Websites w ON w.Url = b.Url
JOIN dbo.WebsiteSelectors s ON s.WebsitesId = w.Id AND s.CollectionUrl = b.CollectionUrl
ORDER BY s.IsEnabled DESC, b.Bic;
