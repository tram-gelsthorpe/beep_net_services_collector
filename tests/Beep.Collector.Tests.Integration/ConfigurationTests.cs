using Beep.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Beep.Platform.Api.Shared.Infrastructure.Authentication;

namespace Beep.Collector.Tests.Integration;

/// <summary>
/// Startup checks for settings and secrets, Key Vault opt-in, and API key comparison.
/// No database: these run in parallel with everything else.
/// </summary>
[TestClass]
public sealed class ConfigurationTests
{
	private const string ApiKeys = "Authentication:ApiKeys";
	private static readonly string StrongKey = new('k', RequiredSettings.MinimumSecretLength);

	[TestMethod]
	[DataRow("Beep.Collector/1.0")]
	[DataRow("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36")]
	public void WebsiteCollector_UserAgent_IsAHeaderValue(string userAgent) =>
		new WebsiteCollectorOptions { UserAgent = userAgent }.Validate();

	[TestMethod]
	[DataRow("")]
	[DataRow(" ")]
	[DataRow("two\nlines")]
	[DataRow("Beep Collector/")]
	public void WebsiteCollector_UserAgent_OtherwiseIsRefused(string userAgent) =>
		StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(
			() => new WebsiteCollectorOptions { UserAgent = userAgent }.Validate()).Message, "UserAgent");

	[TestMethod]
	public void RequireConnectionStrings_ReportsEveryMissingOrEmptyName_WithoutValues()
	{
		var configuration = Build(new()
		{
			["ConnectionStrings:Collector"] = "Server=db;Password=hunter2",
			["ConnectionStrings:EventBus"] = "  ",
		});

		var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
			configuration.RequireConnectionStrings("Collector", "EventBus", "Scheduler"));

		StringAssert.Contains(error.Message, "ConnectionStrings:EventBus");
		StringAssert.Contains(error.Message, "ConnectionStrings:Scheduler");
		Assert.DoesNotContain("Collector", error.Message);
		Assert.DoesNotContain("hunter2", error.Message);
	}

	[TestMethod]
	public void EnvironmentVariables_FillTheSettingsAppsettingsLeavesEmpty()
	{
		// appsettings.json ships the keys with empty values; production sets them in the environment.
		var name = $"BEEP_TEST_{Guid.NewGuid():N}_";
		Environment.SetEnvironmentVariable($"{name}ConnectionStrings__Collector", "Server=prod");
		Environment.SetEnvironmentVariable($"{name}Authentication__ApiKeys__0", StrongKey);
		try
		{
			var configuration = new ConfigurationBuilder()
				.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Collector"] = "" })
				.AddEnvironmentVariables(name)
				.Build();

			configuration.RequireConnectionStrings("Collector");
			CollectionAssert.AreEqual(new[] { StrongKey },
				configuration.GetRequiredSecrets(ApiKeys, HostEnvironment("Production")).ToArray());
		}
		finally
		{
			Environment.SetEnvironmentVariable($"{name}ConnectionStrings__Collector", null);
			Environment.SetEnvironmentVariable($"{name}Authentication__ApiKeys__0", null);
		}
	}

	[TestMethod]
	public void GetRequiredSecrets_WithNoValue_Throws()
	{
		var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
			Build(new() { [$"{ApiKeys}:0"] = "" }).GetRequiredSecrets(ApiKeys, HostEnvironment("Production")));

		StringAssert.Contains(error.Message, "Authentication__ApiKeys__0");
		StringAssert.Contains(error.Message, "Authentication--ApiKeys--0");
	}

	[TestMethod]
	[DataRow("dev-api-key-replace-in-production")]
	[DataRow("your-api-key-here")]
	[DataRow("too-short")]
	[DataRow("<put-a-real-key-here-at-least-32-chars>")]
	public void GetRequiredSecrets_OutsideDevelopment_RejectsPlaceholdersAndShortKeys(string key)
	{
		var configuration = Build(new() { [$"{ApiKeys}:0"] = StrongKey, [$"{ApiKeys}:1"] = key });

		var error = Assert.ThrowsExactly<InvalidOperationException>(() =>
			configuration.GetRequiredSecrets(ApiKeys, HostEnvironment("Production")));
		Assert.DoesNotContain(key, error.Message, "the value is never echoed");
	}

	[TestMethod]
	public void GetRequiredSecrets_InDevelopment_AllowsShortLocalKeys()
	{
		var keys = Build(new() { [$"{ApiKeys}:0"] = "dev-api-key-12345" })
			.GetRequiredSecrets(ApiKeys, HostEnvironment(Environments.Development));

		CollectionAssert.AreEqual(new[] { "dev-api-key-12345" }, keys.ToArray());
	}

	[TestMethod]
	public void AddKeyVaultIfConfigured_WithoutUri_AddsNothing()
	{
		var configuration = new ConfigurationManager();
		configuration.AddInMemoryCollection(new Dictionary<string, string?> { [KeyVaultConfiguration.UriKey] = "" });
		var sources = configuration.Sources.Count;

		Assert.IsFalse(configuration.AddKeyVaultIfConfigured());
		Assert.HasCount(sources, configuration.Sources);
	}

	[TestMethod]
	[DataRow("http://my-vault.vault.azure.net/")]
	[DataRow("my-vault")]
	public void AddKeyVaultIfConfigured_WithInvalidUri_Throws(string uri)
	{
		var configuration = new ConfigurationManager();
		configuration.AddInMemoryCollection(new Dictionary<string, string?> { [KeyVaultConfiguration.UriKey] = uri });

		Assert.ThrowsExactly<InvalidOperationException>(() => configuration.AddKeyVaultIfConfigured());
	}

	[TestMethod]
	public void ApiKeyValidator_AcceptsOnlyConfiguredKeys()
	{
		var other = new string('z', 40);
		var validator = new ApiKeyValidator([StrongKey, other]);

		Assert.IsTrue(validator.IsValid(StrongKey));
		Assert.IsTrue(validator.IsValid(other));
		Assert.IsFalse(validator.IsValid(StrongKey[..^1]), "prefix");
		Assert.IsFalse(validator.IsValid(StrongKey + "k"), "longer");
		Assert.IsFalse(validator.IsValid(StrongKey.ToUpperInvariant()), "case-sensitive");
		Assert.IsFalse(validator.IsValid(""));
		Assert.IsFalse(validator.IsValid(null));
	}

	private static IConfiguration Build(Dictionary<string, string?> values) =>
		new ConfigurationBuilder().AddInMemoryCollection(values).Build();

	private static IHostEnvironment HostEnvironment(string name) => new TestEnvironment { EnvironmentName = name };

	private sealed class TestEnvironment : IHostEnvironment
	{
		public string EnvironmentName { get; set; } = Environments.Production;
		public string ApplicationName { get; set; } = "Tests";
		public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
	}
}
