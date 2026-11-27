using System.Globalization;

namespace Beep.Notification;

/// <summary>
/// Texts of emails in the languages of the websites: en, vi, th, zh, ja, ko, fr, de, es, id, ms, nl, ar. Each entry is keyed by its
/// English text; a missing language falls back to English.
/// </summary>
public sealed class EmailText
{
	public static readonly IReadOnlyList<string> Languages = ["en", "vi", "th", "zh", "ja", "ko", "fr", "de", "es", "id", "ms", "nl", "ar"];

	private readonly Dictionary<string, string[]> _texts;

	/// <param name="entries">Each: the English text, then vi, th, zh, ja, ko, fr, de, es, id, ms, nl, ar</param>
	public EmailText(IEnumerable<string[]> entries)
	{
		_texts = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (var entry in entries)
		{
			if (entry.Length != Languages.Count)
				throw new ArgumentException($"\"{entry[0]}\" needs {Languages.Count} languages, has {entry.Length}.");
			_texts.Add(entry[0], entry);
		}
	}

	/// <summary>A supported language for a culture name ("vi-VN" → vi); otherwise en.</summary>
	public static string Language(string? culture)
	{
		var code = culture?.Trim().Split('-', '_')[0].ToLowerInvariant();
		return code is not null && Languages.Contains(code) ? code : "en";
	}

	/// <summary>Whether a language is written right to left (Arabic): its emails get <c>dir="rtl"</c>.</summary>
	public static bool IsRightToLeft(string? language) => Language(language) == "ar";

	public string Get(string english, string language)
	{
		if (!_texts.TryGetValue(english, out var entry))
			return english;
		var index = Languages.IndexOf(Language(language));
		return index < 0 ? english : entry[index];
	}

	public string Format(string english, string language, params object?[] args) =>
		string.Format(CultureInfo.InvariantCulture, Get(english, language), args);
}
