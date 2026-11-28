using Beep.Configuration;
using Beep.Notification;
using Beep.Platform.Api.Shared.Infrastructure.Audit;
using Beep.Platform.Api.Shared.Infrastructure.Authentication;
using Beep.Platform.Api.Shared.Infrastructure.Websites;
using Beep.Platform.Api.Websites.Currency;
using Beep.Platform.Api.Websites.Currency.Backfill;
using Beep.ServiceBus;

// "backfill <bank> ..." fills past rates and exits instead of starting the web server.
if (BackfillCommand.IsBackfill(args))
	return await BackfillCommand.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

// Settings: appsettings.json (no secrets), then user secrets (Development only), environment
// variables, command line, and Azure Key Vault when KeyVault:Uri is set.
builder.Configuration.AddKeyVaultIfConfigured();
// Shared by every website: Default, A_1_Beep_Global_Default (alert subscribers, their subscriptions and queued
// emails), and ServiceBus, Beep.Collector's ServiceBus database (the work items that send queued emails, and retry
// them). Each website requires its own settings when it is registered (below).
builder.Configuration.RequireConnectionStrings("Default", "ServiceBus");
builder.Configuration.Require("Audit:CreatedBy");

// Audit values (CreatedBy) shared by everything this API records.
var audit = AuditOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(audit);

// API keys: at least one; outside Development each must be 32+ characters and not a placeholder.
builder.Services.AddSingleton(new ApiKeyValidator(
	builder.Configuration.GetRequiredSecrets("Authentication:ApiKeys", builder.Environment)));

// The websites this API serves (Websites/<Name>). One that is off (Websites:<Name>:Enabled = false) is left out:
// its services, its settings and its controllers.
var websites = new PlatformWebsites(builder.Configuration);

// Add services
builder.Services.AddControllers()
	.ConfigureApplicationPartManager(websites.LeaveOutDisabledControllers);
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

// Alerts (Beep.Notification), for every website: subscriptions in A_1_Beep_Global_Default, emails with
// Beep.Email.Core (Mail section) sent by ServiceBus work items (Notification:Email section). Each website adds
// its alert types. The ServiceBus tables are in the "ServiceBus" schema, as Beep.Collector creates them.
builder.Services.AddSingleton<IWorkItemRepository>(p => new WorkItemRepository(
	builder.Configuration.GetConnectionString("ServiceBus")!, "ServiceBus", p.GetRequiredService<ILogger<WorkItemRepository>>()));
builder.Services.AddBeepNotification(builder.Configuration, builder.Configuration.GetConnectionString("Default")!, audit.CreatedBy);

// Websites
if (websites.IsEnabled(CurrencyWebsite.Name))
	builder.Services.AddCurrencyWebsite(builder.Configuration, audit);


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
