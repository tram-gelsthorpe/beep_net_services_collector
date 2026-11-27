using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;

namespace Beep.Website.Currency;

/// <summary>
/// A label made of two parts joined by a dot ("Bank buys · transfer"), as HTML that wraps only after the dot and
/// keeps each part whole: "Aankoop ·" / "contant", not "Aankoop" / "· contant"; "Mua ·" / "chuyển khoản", not
/// "Mua · chuyển" / "khoản". Other labels are returned as they are.
/// </summary>
public static class DotLabel
{
	public static IHtmlContent BreakOnlyAfterDot(string label)
	{
		var encoder = HtmlEncoder.Default;
		var parts = label.Split(" · ");
		return new HtmlString(parts.Length == 2
			? $"<span class=\"nowrap\">{encoder.Encode(parts[0])} ·</span> <span class=\"nowrap\">{encoder.Encode(parts[1])}</span>"
			: encoder.Encode(label));
	}
}
