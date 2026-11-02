-- Example: replace example.com with a website you are authorized to collect.
INSERT dbo.Websites (Name, Url, CollectionType, IntervalSeconds)
VALUES (N'Example homepage', N'https://example.com/', 'HTML', 300);
DECLARE @WebsiteId bigint = SCOPE_IDENTITY();
INSERT dbo.WebsiteSelectors (WebsiteId, FieldName, CssSelector)
VALUES (@WebsiteId, N'PageTitle', N'title');
