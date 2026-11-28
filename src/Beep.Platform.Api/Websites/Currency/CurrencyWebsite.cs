using Beep.Configuration;
using Beep.Notification;
using Beep.Platform.Api.Shared.Infrastructure.Audit;
using Beep.Platform.Api.Websites.Currency.Notifications;
using Beep.Platform.Api.Websites.Currency.Repositories.Collector;
using Beep.Platform.Api.Websites.Currency.Repositories.Country;
using Beep.Platform.Api.Websites.Currency.Repositories.Currency;

namespace Beep.Platform.Api.Websites.Currency;

/// <summary>
/// Beep Currency: the bank rates the Collector delivers (recorded and served to the website), and its rate alerts.
/// </summary>
public static class CurrencyWebsite
{
	/// <summary>The website's name: <c>Websites:Currency</c>, and <c>Notification:Websites:Currency</c> for its alerts.</summary>
	public const string Name = "Currency";

	/// <summary>
	/// Registers Beep Currency's services. Requires the connection strings Currency (A_1_Beep_Global_Currency: rates
	/// are written and read here), Collector (Beep.Collector's database: collection results are read here) and
	/// Country (A_1_Beep_Global_Country: country names).
	/// </summary>
	public static IServiceCollection AddCurrencyWebsite(this IServiceCollection services, IConfiguration configuration, AuditOptions audit)
	{
		configuration.RequireConnectionStrings("Currency", "Collector", "Country");
		var currency = configuration.GetConnectionString("Currency")!;

		services.AddSingleton<ICollectedDataRepository>(new CollectedDataRepository(configuration.GetConnectionString("Collector")!));
		services.AddSingleton<ICurrencyRateQueries>(new CurrencyRateQueries(currency));
		services.AddSingleton<ICountryRepository>(new CountryRepository(configuration.GetConnectionString("Country")!));
		services.AddSingleton<ICurrencyRateRepository>(new CurrencyRateRepository(currency, audit.CreatedBy));

		// Rate alerts, sent by Beep.Notification (Notification:Websites:Currency).
		services.AddSingleton<INotificationType, BankRatesNotification>();
		services.AddSingleton<IBankRatesAlerts, BankRatesAlerts>();
		return services;
	}
}
