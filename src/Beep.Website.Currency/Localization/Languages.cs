namespace Beep.Website.Currency.Localization;

/// <param name="Code">Two-letter ISO 639-1 code, also the UI culture name</param>
/// <param name="NativeName">The language's name in that language, as shown in the language menu</param>
public sealed record Language(string Code, string NativeName)
{
	/// <summary>"rtl" for a language written right to left (Arabic), otherwise "ltr": the page's <c>dir</c>.</summary>
	public string Direction { get; } = new System.Globalization.CultureInfo(Code).TextInfo.IsRightToLeft ? "rtl" : "ltr";
}

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
