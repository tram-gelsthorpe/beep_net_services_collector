using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Beep.Configuration;

/// <summary>
/// Checks at startup that required settings and secrets are present, so a misconfigured deployment
/// stops straight away instead of running half-configured. Error messages name the missing settings,
/// never their values.
/// </summary>
public static class RequiredSettings
{
	/// <summary>Values that are examples, not real secrets; rejected outside Development.</summary>
	private static readonly string[] PlaceholderMarkers =
		["replace", "your-", "changeme", "change-me", "example", "placeholder", "dev-api-key", "<", ">"];

	/// <summary>Minimum length of a secret outside Development.</summary>
	public const int MinimumSecretLength = 32;

	/// <summary>
	/// Throw when any of the named settings (e.g. "ConnectionStrings:Collector") is missing or empty.
	/// All missing names are reported at once.
	/// </summary>
	public static void Require(this IConfiguration configuration, params string[] keys)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var missing = keys.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToList();
		if (missing.Count > 0)
			throw new InvalidOperationException(
				$"Missing required setting(s): {string.Join(", ", missing)}. " +
				"Set them in user secrets (Development), environment variables, or Azure Key Vault.");
	}

	/// <summary>Throw when any of the named connection strings is missing or empty.</summary>
	public static void RequireConnectionStrings(this IConfiguration configuration, params string[] names) =>
		configuration.Require([.. names.Select(name => $"ConnectionStrings:{name}")]);

	/// <summary>
	/// Read a list of secrets (e.g. "Authentication:ApiKeys", from Authentication__ApiKeys__0,
	/// __1, ... or secrets named Authentication--ApiKeys--0, ...). At least one is required. Outside
	/// Development each must be at least <see cref="MinimumSecretLength"/> characters and not a placeholder.
	/// </summary>
	public static IReadOnlyList<string> GetRequiredSecrets(this IConfiguration configuration, string key, IHostEnvironment environment)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(environment);

		var secrets = configuration.GetSection(key).GetChildren()
			.Select(child => child.Value?.Trim())
			.Where(value => !string.IsNullOrEmpty(value))
			.Select(value => value!)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		if (secrets.Count == 0)
			throw new InvalidOperationException(
				$"Missing required setting '{key}': configure at least one value " +
				$"(environment variable {key.Replace(":", "__")}__0, or Key Vault secret {key.Replace(":", "--")}--0).");

		if (!environment.IsDevelopment())
		{
			for (var i = 0; i < secrets.Count; i++)
			{
				var secret = secrets[i];
				if (secret.Length < MinimumSecretLength)
					throw new InvalidOperationException(
						$"'{key}' value #{i} is shorter than {MinimumSecretLength} characters.");
				if (PlaceholderMarkers.Any(marker => secret.Contains(marker, StringComparison.OrdinalIgnoreCase)))
					throw new InvalidOperationException(
						$"'{key}' value #{i} looks like a placeholder; set a real secret.");
			}
		}

		return secrets;
	}
}
