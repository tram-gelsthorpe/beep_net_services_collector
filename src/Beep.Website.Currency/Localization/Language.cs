namespace Beep.Website.Currency;

/// <param name="Code">Two-letter ISO 639-1 code, also the UI culture name</param>
/// <param name="NativeName">The language's name in that language, as shown in the language menu</param>
public sealed record Language(string Code, string NativeName)
{
	/// <summary>"rtl" for a language written right to left (Arabic), otherwise "ltr": the page's <c>dir</c>.</summary>
	public string Direction { get; } = new System.Globalization.CultureInfo(Code).TextInfo.IsRightToLeft ? "rtl" : "ltr";
}
