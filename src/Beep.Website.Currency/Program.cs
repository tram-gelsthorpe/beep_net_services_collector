using Beep.Configuration;
using Beep.Website.Currency.Services;

var builder = WebApplication.CreateBuilder(args);

// Settings: appsettings.json (no secrets), then user secrets (Development only), environment
// variables, command line, and Azure Key Vault when KeyVault:Uri is set.
builder.Configuration.AddKeyVaultIfConfigured();
builder.Configuration.Require("BeepApi:BaseUrl", "BeepApi:ApiKey");

if (!Uri.TryCreate(builder.Configuration["BeepApi:BaseUrl"], UriKind.Absolute, out var apiBaseUrl))
	throw new InvalidOperationException("BeepApi:BaseUrl must be an absolute URL, e.g. https://localhost:55281/");

builder.Services.AddRazorPages();
builder.Services.AddHttpClient<BeepApiClient>(client =>
{
	// A trailing slash keeps relative paths ("api/...") under a base URL that has a path.
	client.BaseAddress = apiBaseUrl.AbsoluteUri.EndsWith('/') ? apiBaseUrl : new Uri(apiBaseUrl.AbsoluteUri + "/");
	client.Timeout = TimeSpan.FromSeconds(30);
	client.DefaultRequestHeaders.Add(BeepApiClient.ApiKeyHeader, builder.Configuration["BeepApi:ApiKey"]);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
