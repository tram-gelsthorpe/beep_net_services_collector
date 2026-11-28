using Beep.Platform.Api.Shared.Controllers;
using Beep.Platform.Api.Shared.Infrastructure.Websites;
using Beep.Platform.Api.Websites.Currency;
using Beep.Platform.Api.Websites.Currency.Controllers;
using Microsoft.Extensions.Configuration;

namespace Beep.Collector.Tests.Integration;

/// <summary>Beep.Platform.Api's websites: on by default, and a website that is off has no controllers.</summary>
[TestClass]
public sealed class PlatformWebsitesTests
{
	[TestMethod]
	public void Website_IsOnUnlessTurnedOff()
	{
		Assert.IsTrue(Websites().IsEnabled(CurrencyWebsite.Name), "no setting: on");
		Assert.IsTrue(Websites(("Websites:Currency:Enabled", "true")).IsEnabled(CurrencyWebsite.Name));
		Assert.IsFalse(Websites(("Websites:Currency:Enabled", "false")).IsEnabled(CurrencyWebsite.Name));
	}

	[TestMethod]
	public void WebsiteTurnedOff_LeavesOutItsControllers_NotTheSharedOnes()
	{
		var off = Websites(("Websites:Currency:Enabled", "false"));
		Assert.IsFalse(off.IsControllerEnabled(typeof(CurrencyRateController)));
		Assert.IsTrue(off.IsControllerEnabled(typeof(NotificationController)), "shared controllers stay");

		var on = Websites();
		Assert.IsTrue(on.IsControllerEnabled(typeof(CurrencyRateController)));
	}

	[TestMethod]
	public void EveryWebsiteController_IsInItsWebsitesNamespace()
	{
		// A controller outside Websites/<Name> or Shared would be on whatever the settings say.
		var controllers = typeof(CurrencyWebsite).Assembly.GetTypes()
			.Where(t => t.IsClass && !t.IsAbstract && typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t));
		foreach (var controller in controllers)
			Assert.IsTrue(
				controller.Namespace!.StartsWith(PlatformWebsites.NamespacePrefix, StringComparison.Ordinal)
				|| controller.Namespace.StartsWith("Beep.Platform.Api.Shared.", StringComparison.Ordinal),
				$"{controller.FullName} is neither a website's nor shared");
	}

	private static PlatformWebsites Websites(params (string Key, string Value)[] settings) =>
		new(new ConfigurationBuilder()
			.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
			.Build());
}
