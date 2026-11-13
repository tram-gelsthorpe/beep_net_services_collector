-- Example: replace example.com with a website you are authorized to collect.
DECLARE @WebsiteId bigint = SCOPE_IDENTITY();
DECLARE @WebsiteSelectorsId bigint = SCOPE_IDENTITY();

Use WebsiteCollector;

INSERT dbo.Websites (Id, Name, Url)
VALUES (@WebsiteId, N'Vietcombank', N'https://www.vietcombank.com.vn/');


INSERT dbo.WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector)
VALUES (@WebsiteSelectorsId, @WebsiteId, N'https://www.vietcombank.com.vn/en/Personal/Cong-cu-Tien-ich/Ty-gia', N'HTML', N'<div class="currency__description-bottom">', N'<table class="table-responsive">');

Use EventBus;

INSERT INTO dbo.EventBus (Id, EventBusOwnerId, EventBusOwnerDeliveryUrl, TimeoutSeconds, MaxRetryAttempts)
SELECT NEWID(), @WebsiteSelectorsId, "https://localhost:8080/", TimeoutSeconds, MaxAttempts FROM dbo.WebsiteSelectors
WHERE Id = @WebsiteSelectorsId AND IsEnabled = 1;