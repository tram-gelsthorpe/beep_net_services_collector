using System.Security.Cryptography;
using System.Text;

namespace Beep.Platform.Api.Shared.Infrastructure.Authentication;

/// <summary>
/// Checks API keys against the configured keys. Keys are compared as SHA-256 hashes with
/// <see cref="CryptographicOperations.FixedTimeEquals"/> against every configured key, so the time
/// taken does not reveal how much of a key matched, its length, or which key it was.
/// </summary>
public sealed class ApiKeyValidator
{
	private readonly byte[][] _keyHashes;

	public ApiKeyValidator(IEnumerable<string> apiKeys)
	{
		ArgumentNullException.ThrowIfNull(apiKeys);
		_keyHashes = [.. apiKeys.Select(Hash)];
		if (_keyHashes.Length == 0)
			throw new ArgumentException("At least one API key is required.", nameof(apiKeys));
	}

	public bool IsValid(string? apiKey)
	{
		if (string.IsNullOrEmpty(apiKey))
			return false;

		var candidate = Hash(apiKey);
		var valid = false;
		foreach (var keyHash in _keyHashes)
			valid |= CryptographicOperations.FixedTimeEquals(candidate, keyHash);
		return valid;
	}

	private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
