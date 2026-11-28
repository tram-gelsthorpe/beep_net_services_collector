using System.Text.Json;
using Beep.Platform.Api.Repositories.Country;
using Beep.Platform.Api.Repositories.Currency;
using Beep.Notification;

namespace Beep.Platform.Api.Notifications;

/// <summary>
/// Beep Currency's rate alerts: an email when one of the chosen banks publishes new rates, with the chosen
/// currencies. The banks must be of the chosen country and have rates; the currencies must be ones those banks
/// publish.
/// </summary>
public sealed class BankRatesNotification(ICurrencyRateQueries queries, ICountryRepository countries) : INotificationType
{
	public const string WebsiteName = "Currency";
	public const string TypeName = "BankRates";

	public string Website => WebsiteName;
	public string Name => TypeName;

	internal static readonly EmailText Text = new(
	[
		["Choose a country.", "Hãy chọn quốc gia.", "โปรดเลือกประเทศ", "请选择国家。", "国を選んでください。", "국가를 선택하세요.", "Choisissez un pays.", "Wählen Sie ein Land.", "Elija un país.", "Pilih negara.", "Pilih negara.", "Kies een land.", "اختر دولة."],
		["Choose at least one bank.", "Hãy chọn ít nhất một ngân hàng.", "โปรดเลือกอย่างน้อยหนึ่งธนาคาร", "请至少选择一家银行。", "銀行を1つ以上選んでください。", "은행을 하나 이상 선택하세요.", "Choisissez au moins une banque.", "Wählen Sie mindestens eine Bank.", "Elija al menos un banco.", "Pilih setidaknya satu bank.", "Pilih sekurang-kurangnya satu bank.", "Kies minstens één bank.", "اختر بنكًا واحدًا على الأقل."],
		["Choose at least one currency.", "Hãy chọn ít nhất một loại tiền.", "โปรดเลือกอย่างน้อยหนึ่งสกุลเงิน", "请至少选择一种货币。", "通貨を1つ以上選んでください。", "통화를 하나 이상 선택하세요.", "Choisissez au moins une devise.", "Wählen Sie mindestens eine Währung.", "Elija al menos una divisa.", "Pilih setidaknya satu mata uang.", "Pilih sekurang-kurangnya satu mata wang.", "Kies minstens één valuta.", "اختر عملة واحدة على الأقل."],
		["when {0} in {1} publish new rates for {2}", "khi {0} tại {1} công bố tỷ giá mới cho {2}", "เมื่อ {0} ใน{1}ประกาศอัตราใหม่ของ {2}", "当{1}的 {0} 发布 {2} 的新汇率时", "{1}の{0}が{2}の新しいレートを公表したときに", "{1}의 {0}에서 {2}의 새 환율을 발표할 때", "quand {0} ({1}) publie de nouveaux taux pour {2}", "wenn {0} in {1} neue Kurse für {2} veröffentlichen", "cuando {0} en {1} publiquen nuevos tipos de {2}", "saat {0} di {1} menerbitkan kurs baru untuk {2}", "apabila {0} di {1} menerbitkan kadar baharu untuk {2}", "wanneer {0} in {1} nieuwe koersen publiceren voor {2}", "عندما ينشر {0} في {1} أسعارًا جديدة لـ {2}"],
		["{0}: new rates", "{0}: tỷ giá mới", "{0}: อัตราใหม่", "{0}：新汇率", "{0}：新しいレート", "{0}: 새 환율", "{0} : nouveaux taux", "{0}: neue Kurse", "{0}: nuevos tipos", "{0}: kurs baru", "{0}: kadar baharu", "{0}: nieuwe koersen", "{0}: أسعار جديدة"],
		["{0} published new rates", "{0} đã công bố tỷ giá mới", "{0} ประกาศอัตราใหม่แล้ว", "{0} 发布了新汇率", "{0} が新しいレートを公表しました", "{0}에서 새 환율을 발표했습니다", "{0} a publié de nouveaux taux", "{0} hat neue Kurse veröffentlicht", "{0} publicó nuevos tipos", "{0} menerbitkan kurs baru", "{0} telah menerbitkan kadar baharu", "{0} heeft nieuwe koersen gepubliceerd", "نشر {0} أسعارًا جديدة"],
		["Rates of {0} · published {1} · all values in {2}", "Tỷ giá ngày {0} · công bố {1} · đơn vị {2}", "อัตราวันที่ {0} · ประกาศ {1} · หน่วยเป็น {2}", "{0} 的汇率 · 发布于 {1} · 单位 {2}", "{0} のレート · {1} 公表 · 単位 {2}", "{0} 환율 · {1} 발표 · 단위 {2}", "Taux du {0} · publiés le {1} · en {2}", "Kurse vom {0} · veröffentlicht {1} · alle Werte in {2}", "Tipos del {0} · publicado {1} · todos los valores en {2}", "Kurs tanggal {0} · diterbitkan {1} · semua nilai dalam {2}", "Kadar pada {0} · diterbitkan {1} · semua nilai dalam {2}", "Koersen van {0} · gepubliceerd {1} · alle bedragen in {2}", "أسعار {0} · نُشرت {1} · جميع القيم بـ {2}"],
		["Currency", "Loại tiền", "สกุลเงิน", "货币", "通貨", "통화", "Devise", "Währung", "Divisa", "Mata uang", "Mata wang", "Valuta", "العملة"],
		["Buy · cash", "Mua · tiền mặt", "ซื้อ · เงินสด", "买入 · 现金", "買い · 現金", "매입 · 현금", "Achat · espèces", "Ankauf · bar", "Compra · efectivo", "Beli · tunai", "Beli · tunai", "Aankoop · contant", "شراء · نقدًا"],
		["Buy · transfer", "Mua · chuyển khoản", "ซื้อ · โอน", "买入 · 转账", "買い · 送金", "매입 · 송금", "Achat · virement", "Ankauf · Überweisung", "Compra · transferencia", "Beli · transfer", "Beli · pindahan", "Aankoop · overboeking", "شراء · تحويل"],
		["Sell", "Bán", "ขาย", "卖出", "売り", "매도", "Vente", "Verkauf", "Venta", "Jual", "Jual", "Verkoop", "بيع"],
		["See all rates", "Xem tất cả tỷ giá", "ดูอัตราทั้งหมด", "查看全部汇率", "すべてのレートを見る", "전체 환율 보기", "Voir tous les taux", "Alle Kurse ansehen", "Ver todos los tipos", "Lihat semua kurs", "Lihat semua kadar", "Alle koersen bekijken", "عرض كل الأسعار"],
	]);

	public async Task<FilterResult> NormalizeAsync(JsonElement filter, CancellationToken cancellationToken)
	{
		var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var countryCode = filter.ValueKind == JsonValueKind.Object && filter.TryGetProperty("country", out var c) && c.ValueKind == JsonValueKind.String
			? c.GetString()?.Trim() : null;
		var banks = Strings(filter, "banks");
		var currencies = Strings(filter, "currencies");

		var all = await countries.GetCountriesAsync(cancellationToken);
		var country = all.Values.FirstOrDefault(x => string.Equals(x.Code, countryCode, StringComparison.OrdinalIgnoreCase));
		if (country is null)
		{
			errors["country"] = Text.Get("Choose a country.", "en");
			return FilterResult.Invalid(errors);
		}

		// Only banks of the country that have rates, and currencies those banks publish.
		var rates = await queries.GetLatestRatesAsync(null, country.Id, null, cancellationToken);
		var knownBanks = rates.Select(r => r.Bic).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var chosenBanks = banks.Where(knownBanks.Contains).Select(b => b.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToList();
		var knownCurrencies = rates.Where(r => chosenBanks.Contains(r.Bic, StringComparer.OrdinalIgnoreCase))
			.Select(r => r.CurrencyCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var chosenCurrencies = currencies.Where(knownCurrencies.Contains).Select(x => x.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal).ToList();

		if (chosenBanks.Count == 0)
			errors["banks"] = Text.Get("Choose at least one bank.", "en");
		if (chosenCurrencies.Count == 0)
			errors["currencies"] = Text.Get("Choose at least one currency.", "en");
		return errors.Count > 0
			? FilterResult.Invalid(errors)
			: FilterResult.Valid(new BankRatesFilter(country.Code, chosenBanks, chosenCurrencies).ToJson());
	}

	public async Task<string> DescribeAsync(string? filter, string language, CancellationToken cancellationToken)
	{
		var choices = BankRatesFilter.Parse(filter);
		if (choices is null)
			return string.Empty;

		var all = await countries.GetCountriesAsync(cancellationToken);
		var country = all.Values.FirstOrDefault(x => string.Equals(x.Code, choices.Country, StringComparison.OrdinalIgnoreCase));
		var banks = await queries.GetBanksWithRatesAsync(country?.Id, null, cancellationToken);
		var bankNames = choices.Banks
			.Select(bic => banks.FirstOrDefault(b => string.Equals(b.Bic, bic, StringComparison.OrdinalIgnoreCase)) is { } b ? ShortName(b.Name) : bic);

		return Text.Format("when {0} in {1} publish new rates for {2}", language,
			string.Join(", ", bankNames), country?.Name ?? choices.Country, string.Join(", ", choices.Currencies));
	}

	/// <summary>
	/// The alert of the bank in <paramref name="data"/> (<see cref="BankRatesAlerts.AlertData"/>): its latest rates of
	/// the currencies the subscriber chose. Null when the subscription no longer has that bank, or none of the
	/// chosen currencies has rates.
	/// </summary>
	public async Task<NotificationEmail?> AlertEmailAsync(
		Subscription subscription, NotificationWebsite website, string? data, CancellationToken cancellationToken)
	{
		var bankBic = BankRatesAlerts.BankOf(data);
		var filter = BankRatesFilter.Parse(subscription.Filter);
		if (bankBic is null || filter is null || !filter.Banks.Contains(bankBic, StringComparer.OrdinalIgnoreCase))
			return null;

		var rates = await queries.GetLatestRatesAsync(null, null, bankBic, cancellationToken);
		var chosen = rates.Where(r => filter.Currencies.Contains(r.CurrencyCode, StringComparer.OrdinalIgnoreCase))
			.OrderBy(r => r.CurrencyCode, StringComparer.Ordinal).ToList();
		if (chosen.Count == 0)
			return null;

		var country = (await countries.GetCountriesAsync(cancellationToken)).GetValueOrDefault(chosen[0].CountryId);
		var quote = country?.CurrencyId is { } currencyId
			? (await queries.GetCurrencyCodesAsync([currencyId], cancellationToken)).GetValueOrDefault(currencyId)
			: null;
		return BankRatesAlerts.AlertEmail(subscription, website, chosen, quote ?? string.Empty);
	}

	/// <summary>"Vietcombank" from "Ngân hàng TMCP Ngoại thương Việt Nam – Vietcombank".</summary>
	public static string ShortName(string name)
	{
		foreach (var separator in new[] { " – ", " — ", " - " })
		{
			var at = name.LastIndexOf(separator, StringComparison.Ordinal);
			if (at > 0 && at + separator.Length < name.Length)
				return name[(at + separator.Length)..].Trim();
		}
		return name;
	}

	private static List<string> Strings(JsonElement filter, string name) =>
		filter.ValueKind == JsonValueKind.Object && filter.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array
			? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!.Trim()).Where(s => s.Length > 0).Take(500).ToList()
			: [];
}
