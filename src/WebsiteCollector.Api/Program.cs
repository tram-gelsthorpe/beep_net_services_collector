using Beep.Configuration;
using WebsiteCollector.Api.Infrastructure.Authentication;
using WebsiteCollector.Api.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Settings: appsettings.json (no secrets), then user secrets (Development only), environment
// variables, command line, and Azure Key Vault when KeyVault:Uri is set.
builder.Configuration.AddKeyVaultIfConfigured();
builder.Configuration.RequireConnectionStrings("Currency");

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
builder.Services.AddScoped<ICurrencyRateRepository, CurrencyRateRepository>();


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