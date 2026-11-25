using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace Beep.Configuration;

/// <summary>
/// Adds Azure Key Vault as a configuration source when <c>KeyVault:Uri</c> is set.
/// </summary>
/// <remarks>
/// A secret named <c>ConnectionStrings--Collector</c> becomes the setting
/// <c>ConnectionStrings:Collector</c> (Key Vault names cannot contain ':'). The vault is added last, so
/// its values override appsettings.json, user secrets and environment variables.
/// <para>
/// Signs in with <see cref="DefaultAzureCredential"/>: the managed identity on Azure; on another
/// server, a service principal from the AZURE_TENANT_ID / AZURE_CLIENT_ID / AZURE_CLIENT_SECRET (or
/// AZURE_CLIENT_CERTIFICATE_PATH) environment variables, or Azure Arc's managed identity; on a
/// developer machine, the Azure CLI or Visual Studio sign-in.
/// </para>
/// </remarks>
public static class KeyVaultConfiguration
{
	public const string UriKey = "KeyVault:Uri";

	/// <summary>Client id of a user-assigned managed identity; empty uses the system-assigned one.</summary>
	public const string ManagedIdentityClientIdKey = "KeyVault:ManagedIdentityClientId";

	/// <summary>
	/// Add the vault named by <c>KeyVault:Uri</c> (an https URI such as
	/// <c>https://my-vault.vault.azure.net/</c>). Does nothing when it is not set.
	/// </summary>
	/// <returns>True when a vault was added.</returns>
	public static bool AddKeyVaultIfConfigured(this IConfigurationManager configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var value = configuration[UriKey];
		if (string.IsNullOrWhiteSpace(value))
			return false;

		if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var vaultUri) || vaultUri.Scheme != Uri.UriSchemeHttps)
			throw new InvalidOperationException($"'{UriKey}' must be an absolute https URI.");

		var clientId = configuration[ManagedIdentityClientIdKey];
		var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
		{
			ManagedIdentityClientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId
		});

		configuration.AddAzureKeyVault(vaultUri, credential);
		return true;
	}
}
