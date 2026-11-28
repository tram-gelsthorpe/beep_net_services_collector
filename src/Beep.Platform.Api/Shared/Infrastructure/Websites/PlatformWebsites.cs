using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Beep.Platform.Api.Shared.Infrastructure.Websites;

/// <summary>
/// The websites this API serves. Each one is a folder <c>Websites/&lt;Name&gt;</c> (namespace
/// <c>Beep.Platform.Api.Websites.&lt;Name&gt;</c>) with its controllers, services and an
/// <c>Add&lt;Name&gt;Website</c> registration. A website is on unless <c>Websites:&lt;Name&gt;:Enabled</c> is false:
/// then its services are not registered, its settings are not required and its controllers are not mapped.
/// </summary>
public sealed class PlatformWebsites(IConfiguration configuration)
{
	public const string Section = "Websites";
	public const string NamespacePrefix = "Beep.Platform.Api.Websites.";

	public bool IsEnabled(string name) => configuration.GetValue($"{Section}:{name}:Enabled", true);

	/// <summary>False for a controller of a website that is off; shared controllers are always on.</summary>
	public bool IsControllerEnabled(Type controller)
	{
		var ns = controller.Namespace ?? string.Empty;
		if (!ns.StartsWith(NamespacePrefix, StringComparison.Ordinal))
			return true;
		return IsEnabled(ns[NamespacePrefix.Length..].Split('.')[0]);
	}

	/// <summary>Replaces MVC's controller discovery with one that leaves out the controllers of websites that are off.</summary>
	public void LeaveOutDisabledControllers(ApplicationPartManager manager)
	{
		foreach (var provider in manager.FeatureProviders.OfType<ControllerFeatureProvider>().ToList())
			manager.FeatureProviders.Remove(provider);
		manager.FeatureProviders.Add(new EnabledControllers(this));
	}

	private sealed class EnabledControllers(PlatformWebsites websites) : ControllerFeatureProvider
	{
		protected override bool IsController(TypeInfo typeInfo) =>
			base.IsController(typeInfo) && websites.IsControllerEnabled(typeInfo);
	}
}
