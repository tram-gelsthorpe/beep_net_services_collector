using System.Globalization;

namespace Beep.Website.Currency;

/// <summary>
/// The names of the countries with rates in every language of <see cref="Languages"/>, by ISO code. The API gives
/// a country's name in English; <see cref="BeepApiClient"/> puts it in the current UI culture's language with
/// <see cref="Name"/>. A country not listed here keeps its English name.
/// </summary>
public static class CountryNames
{
	private static readonly Dictionary<string, UiTextEntry> Names = new(StringComparer.OrdinalIgnoreCase)
	{
		["BE"] = new("Belgium", "Bỉ", "เบลเยียม", "比利时", "ベルギー", "벨기에", "Belgique", "Belgien", "Bélgica", "Belgia", "Belgium", "België", "بلجيكا"),
		["DE"] = new("Germany", "Đức", "เยอรมนี", "德国", "ドイツ", "독일", "Allemagne", "Deutschland", "Alemania", "Jerman", "Jerman", "Duitsland", "ألمانيا"),
		["FR"] = new("France", "Pháp", "ฝรั่งเศส", "法国", "フランス", "프랑스", "France", "Frankreich", "Francia", "Prancis", "Perancis", "Frankrijk", "فرنسا"),
		["GB"] = new("United Kingdom", "Vương quốc Anh", "สหราชอาณาจักร", "英国", "イギリス", "영국", "Royaume-Uni", "Vereinigtes Königreich", "Reino Unido", "Britania Raya", "United Kingdom", "Verenigd Koninkrijk", "المملكة المتحدة"),
		["LU"] = new("Luxembourg", "Luxembourg", "ลักเซมเบิร์ก", "卢森堡", "ルクセンブルク", "룩셈부르크", "Luxembourg", "Luxemburg", "Luxemburgo", "Luksemburg", "Luxembourg", "Luxemburg", "لوكسمبورغ"),
		["NL"] = new("Netherlands", "Hà Lan", "เนเธอร์แลนด์", "荷兰", "オランダ", "네덜란드", "Pays-Bas", "Niederlande", "Países Bajos", "Belanda", "Belanda", "Nederland", "هولندا"),
		["VN"] = new("Vietnam", "Việt Nam", "เวียดนาม", "越南", "ベトナム", "베트남", "Viêt Nam", "Vietnam", "Vietnam", "Vietnam", "Vietnam", "Vietnam", "فيتنام"),
	};

	/// <summary>
	/// The name of the country <paramref name="code"/> in the current UI culture's language, or
	/// <paramref name="english"/> (the API's name) for a country not listed.
	/// </summary>
	public static string? Name(string? code, string? english) =>
		code is not null && Names.TryGetValue(code, out var entry)
			? entry.In(Languages.For(CultureInfo.CurrentUICulture).Code)
			: english;

	/// <summary>Compares names in the order of the current UI culture's language (Bỉ before Đức in Vietnamese).</summary>
	public static StringComparer Order =>
		StringComparer.Create(CultureInfo.GetCultureInfo(Languages.For(CultureInfo.CurrentUICulture).Code), ignoreCase: true);
}
