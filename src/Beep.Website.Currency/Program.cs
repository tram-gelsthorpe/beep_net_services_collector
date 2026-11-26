using System.Globalization;
using Beep.Configuration;
using Beep.Website.Currency.Localization;
using Beep.Website.Currency.Services;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

// Settings: appsettings.json (no secrets), then user secrets (Development only), environment
// variables, command line, and Azure Key Vault when KeyVault:Uri is set.
builder.Configuration.AddKeyVaultIfConfigured();
builder.Configuration.Require("BeepApi:BaseUrl", "BeepApi:ApiKey");

if (!Uri.TryCreate(builder.Configuration["BeepApi:BaseUrl"], UriKind.Absolute, out var apiBaseUrl))
	throw new InvalidOperationException("BeepApi:BaseUrl must be an absolute URL, e.g. https://localhost:55281/");

builder.Services.AddRazorPages();
builder.Services.AddSingleton<UiText>();
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

// The language menu sets the UI language only (from the cookie, else the browser's Accept-Language).
// Numbers and dates keep one format: the culture stays English (no Buddhist years in Thai, etc.).
app.UseRequestLocalization(new RequestLocalizationOptions
{
	DefaultRequestCulture = new RequestCulture(Languages.DefaultCode),
	SupportedCultures = [new CultureInfo(Languages.DefaultCode)],
	SupportedUICultures = Languages.All.Select(l => new CultureInfo(l.Code)).ToList(),
	RequestCultureProviders = [new CookieRequestCultureProvider(), new AcceptLanguageHeaderRequestCultureProvider()],
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Language menu: remember the choice for a year and go back to the page it was chosen on.
app.MapGet("/language/{code}", (string code, string? returnUrl, HttpContext context) =>
{
	if (Languages.Find(code) is { } language)
	{
		context.Response.Cookies.Append(
			CookieRequestCultureProvider.DefaultCookieName,
			CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(Languages.DefaultCode, language.Code)),
			new CookieOptions
			{
				Expires = DateTimeOffset.UtcNow.AddYears(1),
				IsEssential = true,
				HttpOnly = true,
				Secure = context.Request.IsHttps,
				SameSite = SameSiteMode.Lax,
			});
	}

	// Only back to a page of this site.
	var isLocal = returnUrl is ['/', ..] && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\");
	return Results.LocalRedirect(isLocal ? returnUrl! : "/");
});

app.MapRazorPages();

app.Run();
