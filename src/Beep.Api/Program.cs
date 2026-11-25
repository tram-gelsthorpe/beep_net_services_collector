using Beep.Api.Backfill;
using Beep.Configuration;
using Beep.Api.Infrastructure.Audit;
using Beep.Api.Infrastructure.Authentication;
using Beep.Api.Repositories.Collector;
using Beep.Api.Repositories.Currency;

// "backfill vietcombank ..." fills past rates and exits instead of starting the web server.
if (BackfillCommand.IsBackfill(args))
	return await BackfillCommand.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

// Settings: appsettings.json (no secrets), then user secrets (Development only), environment
// variables, command line, and Azure Key Vault when KeyVault:Uri is set.
builder.Configuration.AddKeyVaultIfConfigured();
// Currency: A_1_Beep_Global_Currency (rates are written here). Collector: Beep.Collector's database
// (collection results are read from here).
builder.Configuration.RequireConnectionStrings("Currency", "Collector");
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
builder.Services.AddSingleton<ICurrencyRateRepository>(
	new CurrencyRateRepository(builder.Configuration.GetConnectionString("Currency")!, audit.CreatedBy));


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
