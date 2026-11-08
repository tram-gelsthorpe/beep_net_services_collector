# WebsiteCollector.Api

ASP.NET Core Web API for the WebsiteCollector system. Provides REST endpoints for managing website collection configurations and querying collected data, as well as receiving webhook delivery confirmations from the WebhookBus worker.

## Features

- **Webhook Delivery Management**: Receive and process webhook delivery results from WebhookBus
- **Website Management**: CRUD operations for website configurations
- **Selector Management**: Create and manage content selectors for websites
- **Webhook Configuration**: Register and manage webhooks for selectors
- **Data Query**: Query collected website data with filtering and pagination
- **API Key Authentication**: Secure endpoints with API key validation
- **Swagger/OpenAPI Documentation**: Interactive API documentation at `/swagger`

## Getting Started

### Prerequisites

- .NET 10 SDK
- SQL Server (local or remote)
- Database initialized with migrations from WebsiteCollector.Worker

### Configuration

1. Update `appsettings.json` with your database connection string:
   ```json
   {
	 "ConnectionStrings": {
	   "Default": "Server=YOUR_SERVER;Database=WebsiteCollector;User Id=sa;Password=YOUR_PASSWORD;"
	 },
	 "Authentication": {
	   "ApiKeys": ["your-secure-api-key"]
	 }
   }
   ```

2. For development, use `appsettings.Development.json` with appropriate settings.

### Running the API

```bash
dotnet run --project src/WebsiteCollector.Api/WebsiteCollector.Api.csproj
```

The API will be available at `https://localhost:5001` (HTTPS) or `http://localhost:5000` (HTTP).

## API Endpoints

### Authentication

All endpoints (except Swagger) require the `X-API-Key` header:

```
X-API-Key: your-api-key
```

### Webhook Deliveries

- `POST /api/webhookdeliveries/result` - Record a webhook delivery result
- `GET /api/webhookdeliveries/pending?batchSize=10` - Get pending deliveries
- `GET /api/webhookdeliveries/{deliveryId}` - Get delivery by ID
- `POST /api/webhookdeliveries/{deliveryId}/abandon` - Abandon a delivery
- `GET /api/webhookdeliveries/{deliveryId}/attempt-count` - Get attempt count

### Websites

- `GET /api/websites` - Get all websites
- `GET /api/websites/{id}` - Get website by ID
- `POST /api/websites` - Create new website
- `PUT /api/websites/{id}` - Update website
- `DELETE /api/websites/{id}` - Delete website

### Selectors

- `GET /api/selectors/website/{websiteId}` - Get selectors for website
- `GET /api/selectors/{id}` - Get selector by ID
- `POST /api/selectors` - Create new selector
- `PUT /api/selectors/{id}` - Update selector
- `DELETE /api/selectors/{id}` - Delete selector
- `POST /api/selectors/{id}/enable` - Enable selector
- `POST /api/selectors/{id}/disable` - Disable selector

### Webhooks

- `GET /api/webhooks/selector/{selectorId}` - Get webhooks for selector
- `POST /api/webhooks` - Register new webhook
- `PUT /api/webhooks/{webhookId}/status` - Update webhook status
- `POST /api/webhooks/{webhookId}/disable` - Disable webhook

### Collected Data

- `GET /api/collecteddata/execution/{executionId}` - Get data by execution
- `GET /api/collecteddata/selector/{selectorId}/latest` - Get latest data
- `GET /api/collecteddata/selector/{selectorId}/history?pageNumber=1&pageSize=10` - Get history with pagination
- `GET /api/collecteddata/executions/{executionId}` - Get execution details
- `GET /api/collecteddata/selector/{selectorId}/executions?limit=10` - Get recent executions
- `POST /api/collecteddata/search` - Search collected data
- `GET /api/collecteddata/selector/{selectorId}/statistics` - Get selector statistics

## Example Usage

### Create a Website

```bash
curl -X POST https://localhost:5001/api/websites \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
	"name": "Example Site",
	"url": "https://example.com",
	"isEnabled": true
  }'
```

### Create a Selector

```bash
curl -X POST https://localhost:5001/api/selectors \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
	"websitesId": "WEBSITE_ID",
	"collectionUrl": "https://example.com/data",
	"collectionType": "html",
	"contentSelector": "div.content",
	"intervalSeconds": 3600,
	"isEnabled": true
  }'
```

### Record Webhook Delivery Result

```bash
curl -X POST https://localhost:5001/api/webhookdeliveries/result \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/json" \
  -d '{
	"deliveryId": "DELIVERY_ID",
	"isSuccessful": true,
	"httpStatusCode": 200,
	"errorMessage": null,
	"responseContent": null
  }'
```

## Error Handling

All endpoints return appropriate HTTP status codes:

- `200 OK` - Successful GET/PUT request
- `201 Created` - Successful POST request (resource created)
- `400 Bad Request` - Invalid input or validation error
- `401 Unauthorized` - Missing or invalid API key
- `404 Not Found` - Resource not found
- `500 Internal Server Error` - Server processing error

Error responses include a JSON body with an `error` field:

```json
{
  "error": "Description of the error"
}
```

## Swagger Documentation

Interactive API documentation is available at:

- Development: `http://localhost:5000/swagger`
- Production: `https://your-domain/swagger`

The Swagger UI includes a way to specify your API key for testing endpoints.

## Security Considerations

1. **API Key Management**: 
   - Store API keys securely in production (use Key Vault, not appsettings.json)
   - Rotate keys regularly
   - Use different keys for different environments

2. **HTTPS**: Always use HTTPS in production

3. **CORS**: The API currently allows all origins. Restrict in production:
   ```csharp
   options.AddPolicy("RestrictedCORS", policy =>
   {
	   policy.WithOrigins("https://trusted-domain.com")
			 .AllowAnyMethod()
			 .AllowAnyHeader();
   });
   ```

4. **Rate Limiting**: Consider implementing rate limiting for production

## Integration with WebhookBus

The API receives webhook delivery confirmations from the WebhookBus worker. The worker calls:

```
POST /api/webhookdeliveries/result
X-API-Key: {api-key}
Content-Type: application/json

{
  "deliveryId": "guid",
  "isSuccessful": true/false,
  "httpStatusCode": 200,
  "errorMessage": "error details if any",
  "responseContent": "response body if any"
}
```

## Requirements

- The WebsiteCollector database must be initialized with all migrations
- IWebsiteCollectionRepository and IWebsiteSelectionWebhookRepository implementations must be available
- IWebhookRepository must be registered for webhook delivery management

## Development

See [WebsiteCollector.Worker README](../../src/WebsiteCollector.Worker/Resource/README.md) for the overall architecture and data model information.

## License

This project is part of the WebsiteCollector system.
