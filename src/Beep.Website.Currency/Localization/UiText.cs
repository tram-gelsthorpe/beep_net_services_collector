using System.Globalization;

namespace Beep.Website.Currency.Localization;

/// <summary>
/// The site's texts in every language of <see cref="Languages"/>, looked up by their English text in the
/// current UI culture: <c>T["Show rates"]</c>, <c>T.Format("Rates of {0}", date)</c>. Each text takes all
/// seven languages, so a missing translation does not compile. Unknown texts are shown in English.
/// </summary>
public sealed class UiText
{
	private sealed record Entry(string En, string Vi, string Th, string Zh, string Ja, string Ko, string Fr)
	{
		public string In(string language) => language switch
		{
			"vi" => Vi,
			"th" => Th,
			"zh" => Zh,
			"ja" => Ja,
			"ko" => Ko,
			"fr" => Fr,
			_ => En,
		};
	}

	private static readonly Dictionary<string, Entry> Entries = new Entry[]
	{
		// Layout and navigation
		new("Latest bank rates", "Tỷ giá ngân hàng mới nhất", "อัตราแลกเปลี่ยนธนาคารล่าสุด", "最新银行汇率", "最新の銀行レート", "최신 은행 환율", "Derniers taux bancaires"),
		new("Bank rates", "Tỷ giá ngân hàng", "อัตราธนาคาร", "银行汇率", "銀行レート", "은행 환율", "Taux bancaires"),
		new("Choose language", "Chọn ngôn ngữ", "เลือกภาษา", "选择语言", "言語を選択", "언어 선택", "Choisir la langue"),
		new("Error", "Lỗi", "ข้อผิดพลาด", "错误", "エラー", "오류", "Erreur"),
		new("Something went wrong and the page could not be shown. Please try again later.",
			"Đã xảy ra lỗi, không thể hiển thị trang. Vui lòng thử lại sau.",
			"เกิดข้อผิดพลาด ไม่สามารถแสดงหน้านี้ได้ โปรดลองอีกครั้งภายหลัง",
			"出现错误，无法显示页面。请稍后再试。",
			"エラーが発生したため、ページを表示できませんでした。しばらくしてからもう一度お試しください。",
			"오류가 발생하여 페이지를 표시할 수 없습니다. 잠시 후 다시 시도하세요.",
			"Une erreur s’est produite et la page n’a pas pu s’afficher. Réessayez plus tard."),

		// Filter bar
		new("Country", "Quốc gia", "ประเทศ", "国家", "国", "국가", "Pays"),
		new("All countries", "Tất cả quốc gia", "ทุกประเทศ", "所有国家", "すべての国", "모든 국가", "Tous les pays"),
		new("Date", "Ngày", "วันที่", "日期", "日付", "날짜", "Date"),
		new("Bank name or BIC", "Tên ngân hàng hoặc BIC", "ชื่อธนาคารหรือ BIC", "银行名称或 BIC", "銀行名または BIC", "은행 이름 또는 BIC", "Nom de la banque ou BIC"),
		new("e.g. Vietcombank or BFTVVNVX", "VD: Vietcombank hoặc BFTVVNVX", "เช่น Vietcombank หรือ BFTVVNVX", "例如 Vietcombank 或 BFTVVNVX", "例: Vietcombank または BFTVVNVX", "예: Vietcombank 또는 BFTVVNVX", "ex. Vietcombank ou BFTVVNVX"),
		new("Reset", "Đặt lại", "ล้างค่า", "重置", "リセット", "초기화", "Réinitialiser"),
		new("Show rates", "Xem tỷ giá", "แสดงอัตรา", "显示汇率", "レートを表示", "환율 보기", "Afficher les taux"),

		// Messages
		new("Choose a country, a date or a bank to see the latest rates.",
			"Chọn quốc gia, ngày hoặc ngân hàng để xem tỷ giá mới nhất.",
			"เลือกประเทศ วันที่ หรือธนาคาร เพื่อดูอัตราล่าสุด",
			"选择国家、日期或银行以查看最新汇率。",
			"国、日付、または銀行を選ぶと最新のレートが表示されます。",
			"국가, 날짜 또는 은행을 선택하면 최신 환율을 볼 수 있습니다.",
			"Choisissez un pays, une date ou une banque pour voir les derniers taux."),
		new("{0} bank(s) have rates.", "{0} ngân hàng có tỷ giá.", "มี {0} ธนาคารที่มีอัตรา", "{0} 家银行有汇率。", "レートのある銀行: {0} 行", "환율이 있는 은행: {0}곳", "{0} banque(s) avec des taux."),
		new("No rates match this search.", "Không có tỷ giá nào phù hợp.", "ไม่พบอัตราที่ตรงกับการค้นหานี้", "没有符合此搜索的汇率。", "この検索に一致するレートはありません。", "검색 조건에 맞는 환율이 없습니다.", "Aucun taux ne correspond à cette recherche."),
		new("No rates match this search on or before {0}.",
			"Không có tỷ giá nào phù hợp vào hoặc trước ngày {0}.",
			"ไม่พบอัตราที่ตรงกับการค้นหานี้ ณ หรือก่อนวันที่ {0}",
			"在 {0} 或之前没有符合此搜索的汇率。",
			"{0} 以前でこの検索に一致するレートはありません。",
			"{0} 이전에 검색 조건에 맞는 환율이 없습니다.",
			"Aucun taux ne correspond à cette recherche au {0} ou avant."),
		new("The rates service answered {0}.", "Dịch vụ tỷ giá trả về lỗi {0}.", "บริการอัตราแลกเปลี่ยนตอบกลับ {0}", "汇率服务返回 {0}。", "レートサービスが {0} を返しました。", "환율 서비스가 {0}을(를) 반환했습니다.", "Le service des taux a répondu {0}."),
		new("The rates service could not be reached.", "Không thể kết nối dịch vụ tỷ giá.", "ไม่สามารถเชื่อมต่อบริการอัตราแลกเปลี่ยนได้", "无法连接汇率服务。", "レートサービスに接続できません。", "환율 서비스에 연결할 수 없습니다.", "Le service des taux est injoignable."),

		// Highlight cards
		new("Spread {0}", "Chênh lệch {0}", "ส่วนต่าง {0}", "价差 {0}", "スプレッド {0}", "스프레드 {0}", "Écart {0}"),
		new("We buy", "Mua vào", "รับซื้อ", "买入", "買い", "매입", "Achat"),
		new("We sell", "Bán ra", "ขาย", "卖出", "売り", "매도", "Vente"),

		// Bank card
		new("Rates of {0}", "Tỷ giá ngày {0}", "อัตราวันที่ {0}", "{0} 汇率", "{0} のレート", "{0} 환율", "Taux du {0}"),
		new("Rates of {0} to {1}", "Tỷ giá từ {0} đến {1}", "อัตราวันที่ {0} ถึง {1}", "{0} 至 {1} 汇率", "{0}〜{1} のレート", "{0}~{1} 환율", "Taux du {0} au {1}"),
		new("Published {0} ({1})", "Cập nhật {0} ({1})", "ประกาศ {0} ({1})", "发布于 {0} ({1})", "公表 {0} ({1})", "게시 {0} ({1})", "Publié le {0} ({1})"),
		new("Filter currencies", "Lọc ngoại tệ", "กรองสกุลเงิน", "筛选货币", "通貨を絞り込む", "통화 필터", "Filtrer les devises"),
		new("All rates in {0}", "Tất cả tỷ giá tính bằng {0}", "อัตราทั้งหมดเป็น {0}", "所有汇率以 {0} 计", "すべて {0} 建て", "모든 환율은 {0} 기준", "Tous les taux en {0}"),
		new("“—” means the bank doesn’t quote this rate",
			"“—” nghĩa là ngân hàng không niêm yết tỷ giá này",
			"“—” หมายถึงธนาคารไม่ได้ประกาศอัตรานี้",
			"“—”表示银行未报此汇率",
			"「—」は銀行がこのレートを提示していないことを示します",
			"“—”는 은행이 이 환율을 고시하지 않음을 뜻합니다",
			"« — » signifie que la banque ne cote pas ce taux"),

		// Rates table
		new("Currency", "Ngoại tệ", "สกุลเงิน", "货币", "通貨", "통화", "Devise"),
		new("Buy · cash", "Mua · tiền mặt", "ซื้อ · เงินสด", "买入 · 现钞", "買い · 現金", "매입 · 현찰", "Achat · espèces"),
		new("Buy · transfer", "Mua · chuyển khoản", "ซื้อ · โอน", "买入 · 转账", "買い · 送金", "매입 · 송금", "Achat · virement"),
		new("Sell", "Bán", "ขาย", "卖出", "売り", "매도", "Vente"),
		new("Spread", "Chênh lệch", "ส่วนต่าง", "价差", "スプレッド", "스프레드", "Écart"),
		new("No currency matches this filter.", "Không có ngoại tệ nào phù hợp.", "ไม่พบสกุลเงินที่ตรงกับตัวกรองนี้", "没有符合筛选条件的货币。", "条件に一致する通貨はありません。", "필터에 맞는 통화가 없습니다.", "Aucune devise ne correspond à ce filtre."),
		new("Rates are indicative and taken from the bank’s published table. Spread = (sell − buy transfer) ÷ buy transfer.",
			"Tỷ giá chỉ mang tính tham khảo, lấy từ bảng tỷ giá do ngân hàng công bố. Chênh lệch = (bán − mua chuyển khoản) ÷ mua chuyển khoản.",
			"อัตราเป็นเพียงข้อมูลอ้างอิง นำมาจากตารางที่ธนาคารประกาศ ส่วนต่าง = (ขาย − ซื้อโอน) ÷ ซื้อโอน",
			"汇率仅供参考，取自银行公布的汇率表。价差 = (卖出 − 转账买入) ÷ 转账买入。",
			"レートは参考値で、銀行が公表した表から取得しています。スプレッド = (売り − 送金買い) ÷ 送金買い。",
			"환율은 참고용이며 은행이 게시한 환율표에서 가져온 것입니다. 스프레드 = (매도 − 송금 매입) ÷ 송금 매입.",
			"Taux indicatifs, repris du tableau publié par la banque. Écart = (vente − achat virement) ÷ achat virement."),
	}.ToDictionary(e => e.En, StringComparer.Ordinal);

	private readonly ILogger<UiText> _logger;

	public UiText(ILogger<UiText> logger) => _logger = logger;

	/// <summary>The text in the current UI culture's language.</summary>
	public string this[string english]
	{
		get
		{
			if (Entries.TryGetValue(english, out var entry))
				return entry.In(Languages.For(CultureInfo.CurrentUICulture).Code);

			_logger.LogWarning("No translations for the text '{Text}'", english);
			return english;
		}
	}

	/// <summary>The text in the current UI culture's language, with <paramref name="args"/> in its {0}, {1}.</summary>
	public string Format(string english, params object?[] args) =>
		string.Format(CultureInfo.InvariantCulture, this[english], args);
}
