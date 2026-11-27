namespace Beep.Website.Currency;

/// <summary>One text of <see cref="UiText"/> in each of its languages.</summary>
internal sealed record UiTextEntry(string En, string Vi, string Th, string Zh, string Ja, string Ko, string Fr,
	string De, string Es, string Id, string Ms, string Nl, string Ar)
{
	public string In(string language) => language switch
	{
		"vi" => Vi,
		"th" => Th,
		"zh" => Zh,
		"ja" => Ja,
		"ko" => Ko,
		"fr" => Fr,
		"de" => De,
		"es" => Es,
		"id" => Id,
		"ms" => Ms,
		"nl" => Nl,
		"ar" => Ar,
		_ => En,
	};
}
