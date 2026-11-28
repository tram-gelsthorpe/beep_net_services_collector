using Beep.Platform.Api.Backfill;
using Beep.Configuration;
using Beep.Platform.Api.Infrastructure.Audit;
using Beep.Platform.Api.Infrastructure.Authentication;
using Beep.Platform.Api.Notifications;
using Beep.Platform.Api.Repositories.Collector;
using Beep.Platform.Api.Repositories.Country;
using Beep.Platform.Api.Repositories.Currency;
using Beep.Notification;
using Beep.ServiceBus;

// "backfill <bank> ..." fills past rates and exits instead of starting the web server.
if (BackfillCommand.IsBackfill(args))
	return await BackfillCommand.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

// Settings: appsettings.json (no secrets), then user secrets (Development only), environment
// variables, command line, and Azure Key Vault when KeyVault:Uri is set.
builder.Configuration.AddKeyVaultIfConfigured();
// Currency: A_1_Beep_Global_Currency (rates are written and read here). Collector: Beep.Collector's database
// (collection results are read from here). Country: A_1_Beep_Global_Country (country names). Default:
// A_1_Beep_Global_Default (alert subscribers, their subscriptions and queued emails). ServiceBus: Beep.Collector's
// ServiceBus database (the work items that send queued emails, and retry them).
builder.Configuration.RequireConnectionStrings("Currency", "Collector", "Country", "Default", "ServiceBus");
builder.Configuration.Require("Audit:CreatedBy");

// Audit values (CreatedBy) shared by everything this API records.
var audit = AuditOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(audit);

// API keys: at least one; outside Development each must be 32+ characters and not a placeholder.
builder.Services.AddSingleton(new ApiKeyValidator(
	builder.Configuration.GetRequiredSecrets("Authentication:ApiKeys", builder.Environment)));

// Add services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure CORS
builder.Services.AddCors(options =>
{
	options.AddPolicy("AllowAll", policy =>
	{
		policy.AllowAnyOrigin()
			  .AllowAnyMethod()
			  .AllowAnyHeader();
	});
});

// API repositories
builder.Services.AddSingleton<ICollectedDataRepository>(
	new CollectedDataRepository(builder.Configuration.GetConnectionString("Collector")!));
builder.Services.AddSingleton<ICurrencyRateQueries>(
	new CurrencyRateQueries(builder.Configuration.GetConnectionString("Currency")!));
builder.Services.AddSingleton<ICountryRepository>(
	new CountryRepository(builder.Configuration.GetConnectionString("Country")!));
builder.Services.AddSingleton<ICurrencyRateRepository>(
	new CurrencyRateRepository(builder.Configuration.GetConnectionString("Currency")!, audit.CreatedBy));

// Alerts (Beep.Notification): subscriptions in A_1_Beep_Global_Default, emails with Beep.Email.Core (Mail
// section) sent by ServiceBus work items (Notification:Email section), and each website's alert types.
// The ServiceBus tables are in the "ServiceBus" schema, as Beep.Collector creates them.
builder.Services.AddSingleton<IWorkItemRepository>(p => new WorkItemRepository(
	builder.Configuration.GetConnectionString("ServiceBus")!, "ServiceBus", p.GetRequiredService<ILogger<WorkItemRepository>>()));
builder.Services.AddBeepNotification(builder.Configuration, builder.Configuration.GetConnectionString("Default")!, audit.CreatedBy);
builder.Services.AddSingleton<INotificationType, BankRatesNotification>();
builder.Services.AddSingleton<IBankRatesAlerts, BankRatesAlerts>();


var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
	app.UseSwagger();
	app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// API Key middleware
app.UseMiddleware<ApiKeyMiddleware>();

app.UseAuthorization();
app.MapControllers();

app.Run();
return 0;
