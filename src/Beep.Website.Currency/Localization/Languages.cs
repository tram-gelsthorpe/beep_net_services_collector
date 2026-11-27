namespace Beep.Website.Currency;

/// <summary>Languages of the language menu, in menu order. English is the default.</summary>
public static class Languages
{
	public const string DefaultCode = "en";

	public static readonly IReadOnlyList<Language> All =
	[
		new("en", "English"),
		new("vi", "Tiếng Việt"),
		new("th", "ไทย"),
		new("zh", "中文"),
		new("ja", "日本語"),
		new("ko", "한국어"),
		new("fr", "Français"),
		new("de", "Deutsch"),
		new("es", "Español"),
		new("id", "Bahasa Indonesia"),
		new("ms", "Bahasa Melayu"),
		new("nl", "Nederlands"),
		new("ar", "العربية"),
	];

	public static Language? Find(string? code) =>
		All.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));

	/// <summary>The language of a UI culture (vi-VN → vi), or English.</summary>
	public static Language For(System.Globalization.CultureInfo uiCulture) =>
		Find(uiCulture.TwoLetterISOLanguageName) ?? Find(DefaultCode)!;
}
