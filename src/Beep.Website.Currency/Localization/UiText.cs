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

		// Navigation (all pages)
		new("Compare banks", "So sánh ngân hàng", "เปรียบเทียบธนาคาร", "比较银行", "銀行を比較", "은행 비교", "Comparer les banques"),
		new("Converter", "Quy đổi", "แปลงสกุลเงิน", "换算", "換算", "환전 계산기", "Convertisseur"),
		new("Open menu", "Mở menu", "เปิดเมนู", "打开菜单", "メニューを開く", "메뉴 열기", "Ouvrir le menu"),
		new("Language", "Ngôn ngữ", "ภาษา", "语言", "言語", "언어", "Langue"),

		// Compare banks
		new("Amount", "Số tiền", "จำนวนเงิน", "金额", "金額", "금액", "Montant"),
		new("I want to", "Tôi muốn", "ฉันต้องการ", "我想", "したいこと", "원하는 거래", "Je veux"),
		new("Buy {0}", "Mua {0}", "ซื้อ {0}", "买入 {0}", "{0} を買う", "{0} 사기", "Acheter des {0}"),
		new("Sell {0}", "Bán {0}", "ขาย {0}", "卖出 {0}", "{0} を売る", "{0} 팔기", "Vendre des {0}"),
		new("Compare", "So sánh", "เปรียบเทียบ", "比较", "比較", "비교", "Comparer"),
		new("Cheapest place to buy {0}", "Nơi mua {0} rẻ nhất", "ที่ซื้อ {0} ถูกที่สุด", "买入 {0} 最便宜的银行", "{0} を最も安く買える銀行", "{0}를 가장 싸게 살 수 있는 곳", "Où acheter des {0} au meilleur prix"),
		new("Sells at", "Bán ra", "ขายที่", "卖出价", "売値", "매도율", "Vend à"),
		new("you pay", "bạn trả", "คุณจ่าย", "您支付", "支払額", "지불액", "vous payez"),
		new("Best place to sell {0}", "Nơi bán {0} tốt nhất", "ที่ขาย {0} ได้ดีที่สุด", "卖出 {0} 最划算的银行", "{0} を最も高く売れる銀行", "{0}를 가장 좋게 팔 수 있는 곳", "Où vendre des {0} au meilleur prix"),
		new("Buys at", "Mua vào", "รับซื้อที่", "买入价", "買値", "매입률", "Achète à"),
		new("you get", "bạn nhận", "คุณได้รับ", "您收到", "受取額", "수령액", "vous recevez"),
		new("Difference best vs. worst", "Chênh lệch tốt nhất và kém nhất", "ส่วนต่างระหว่างดีที่สุดกับแย่ที่สุด", "最优与最差的差额", "最良と最悪の差", "최고와 최저의 차이", "Écart entre la meilleure et la moins bonne"),
		new("on {0} {1} across {2} bank(s)", "trên {0} {1} tại {2} ngân hàng", "สำหรับ {0} {1} จาก {2} ธนาคาร", "按 {0} {1}，共 {2} 家银行", "{0} {1}・{2} 行で比較", "{0} {1} 기준, 은행 {2}곳", "sur {0} {1} dans {2} banque(s)"),
		new("{0} at banks in {1}", "{0} tại các ngân hàng ở {1}", "{0} ที่ธนาคารใน {1}", "{1} 各银行的 {0}", "{1} の銀行の {0}", "{1} 은행들의 {0}", "{0} dans les banques — {1}"),
		new("sorted by lowest sell rate", "sắp xếp theo giá bán thấp nhất", "เรียงตามอัตราขายต่ำสุด", "按卖出价从低到高", "売値の安い順", "매도율 낮은 순", "triées par taux de vente le plus bas"),
		new("sorted by highest buy rate", "sắp xếp theo giá mua cao nhất", "เรียงตามอัตรารับซื้อสูงสุด", "按买入价从高到低", "買値の高い順", "매입률 높은 순", "triées par taux d’achat le plus haut"),
		new("sorted by lowest spread", "sắp xếp theo chênh lệch thấp nhất", "เรียงตามส่วนต่างต่ำสุด", "按价差从低到高", "スプレッドの小さい順", "스프레드 낮은 순", "triées par écart le plus faible"),
		new("all values in {0}", "tất cả giá trị tính bằng {0}", "ค่าทั้งหมดเป็น {0}", "所有金额以 {0} 计", "金額はすべて {0}", "모든 금액은 {0}", "tous les montants en {0}"),
		new("Sort", "Sắp xếp", "เรียงลำดับ", "排序", "並べ替え", "정렬", "Trier"),
		new("Sell rate", "Giá bán", "อัตราขาย", "卖出价", "売値", "매도율", "Taux de vente"),
		new("Buy rate", "Giá mua", "อัตรารับซื้อ", "买入价", "買値", "매입률", "Taux d’achat"),
		new("Bank", "Ngân hàng", "ธนาคาร", "银行", "銀行", "은행", "Banque"),
		new("You pay for {0} {1}", "Bạn trả cho {0} {1}", "คุณจ่ายสำหรับ {0} {1}", "买 {0} {1} 需支付", "{0} {1} の支払額", "{0} {1} 지불액", "Vous payez pour {0} {1}"),
		new("Showing {0} bank(s)", "Hiển thị {0} ngân hàng", "แสดง {0} ธนาคาร", "显示 {0} 家银行", "{0} 行を表示", "은행 {0}곳 표시", "{0} banque(s) affichée(s)"),
		new("“You pay” = amount × sell rate", "“Bạn trả” = số tiền × giá bán", "“คุณจ่าย” = จำนวนเงิน × อัตราขาย", "“支付”= 金额 × 卖出价", "「支払額」= 金額 × 売値", "“지불액” = 금액 × 매도율", "« Vous payez » = montant × taux de vente"),
		new("Pay for {0}", "Trả cho {0}", "จ่ายสำหรับ {0}", "买 {0} 需付", "{0} の支払額", "{0} 지불액", "Payer pour {0}"),
		new("Rates are indicative and taken from each bank’s published table. Always confirm with the bank before exchanging.",
			"Tỷ giá chỉ mang tính tham khảo, lấy từ bảng tỷ giá do từng ngân hàng công bố. Hãy xác nhận với ngân hàng trước khi giao dịch.",
			"อัตราเป็นเพียงข้อมูลอ้างอิง นำมาจากตารางที่แต่ละธนาคารประกาศ โปรดตรวจสอบกับธนาคารก่อนแลกเปลี่ยน",
			"汇率仅供参考，取自各银行公布的汇率表。兑换前请向银行确认。",
			"レートは参考値で、各銀行が公表した表から取得しています。両替の前に必ず銀行にご確認ください。",
			"환율은 참고용이며 각 은행이 게시한 환율표에서 가져온 것입니다. 환전 전에 반드시 은행에 확인하세요.",
			"Taux indicatifs, repris du tableau publié par chaque banque. Vérifiez toujours auprès de la banque avant de changer."),

		// Converter
		new("Currency converter", "Quy đổi ngoại tệ", "แปลงสกุลเงิน", "货币换算", "通貨換算", "환율 계산기", "Convertisseur de devises"),
		new("You have", "Bạn có", "คุณมี", "您有", "お持ちの金額", "보유 금액", "Vous avez"),
		new("You get", "Bạn nhận", "คุณได้รับ", "您得到", "受取額", "받는 금액", "Vous obtenez"),
		new("Swap direction", "Đổi chiều", "สลับทิศทาง", "切换方向", "方向を入れ替え", "방향 바꾸기", "Inverser le sens"),
		new("Transfer", "Chuyển khoản", "โอน", "转账", "送金", "송금", "Virement"),
		new("Cash", "Tiền mặt", "เงินสด", "现钞", "現金", "현찰", "Espèces"),
		new("published {0}", "cập nhật {0}", "ประกาศ {0}", "发布于 {0}", "公表 {0}", "게시 {0}", "publiés le {0}"),
		new("Bank buys · cash", "Ngân hàng mua · tiền mặt", "ธนาคารรับซื้อ · เงินสด", "银行买入 · 现钞", "銀行の買値 · 現金", "은행 매입 · 현찰", "La banque achète · espèces"),
		new("Bank buys · transfer", "Ngân hàng mua · chuyển khoản", "ธนาคารรับซื้อ · โอน", "银行买入 · 转账", "銀行の買値 · 送金", "은행 매입 · 송금", "La banque achète · virement"),
		new("Bank sells", "Ngân hàng bán", "ธนาคารขาย", "银行卖出", "銀行の売値", "은행 매도", "La banque vend"),
		new("How it works", "Cách tính", "วิธีคำนวณ", "计算方式", "計算方法", "계산 방법", "Comment ça marche"),
		new("Changing foreign currency into {0} uses the bank’s buy rate. Changing {0} into foreign currency uses the bank’s sell rate. Cash rates are usually lower than transfer rates.",
			"Đổi ngoại tệ sang {0} dùng giá mua của ngân hàng. Đổi {0} sang ngoại tệ dùng giá bán của ngân hàng. Giá mua tiền mặt thường thấp hơn giá mua chuyển khoản.",
			"การแลกเงินตราต่างประเทศเป็น {0} ใช้อัตรารับซื้อของธนาคาร การแลก {0} เป็นเงินตราต่างประเทศใช้อัตราขายของธนาคาร อัตราเงินสดมักต่ำกว่าอัตราโอน",
			"外币兑换成 {0} 使用银行的买入价。{0} 兑换成外币使用银行的卖出价。现钞价通常低于转账价。",
			"外貨を {0} に替えるときは銀行の買値、{0} を外貨に替えるときは銀行の売値を使います。現金のレートは通常、送金のレートより低くなります。",
			"외화를 {0}(으)로 바꿀 때는 은행 매입률을, {0}을(를) 외화로 바꿀 때는 은행 매도율을 사용합니다. 현찰 환율은 보통 송금 환율보다 낮습니다.",
			"Changer des devises en {0} utilise le taux d’achat de la banque. Changer des {0} en devises utilise son taux de vente. Les taux espèces sont en général plus bas que les taux virement."),
		new("1 unit in {0} today", "1 đơn vị bằng {0} hôm nay", "1 หน่วยเป็น {0} วันนี้", "今日 1 单位兑 {0}", "本日の 1 単位あたりの {0}", "오늘 1단위당 {0}", "1 unité en {0} aujourd’hui"),
		new("Bank buys (transfer) · Bank sells — click to convert",
			"Ngân hàng mua (chuyển khoản) · Ngân hàng bán — nhấn để quy đổi",
			"ธนาคารรับซื้อ (โอน) · ธนาคารขาย — คลิกเพื่อแปลง",
			"银行买入（转账）· 银行卖出 — 点击换算",
			"銀行の買値（送金）· 銀行の売値 — クリックで換算",
			"은행 매입(송금) · 은행 매도 — 클릭하여 계산",
			"La banque achète (virement) · La banque vend — cliquez pour convertir"),
		new("sell {0}", "bán {0}", "ขาย {0}", "卖出 {0}", "売値 {0}", "매도 {0}", "vente {0}"),
		new("Results are estimates based on {0}’s published rates and may differ from the amount you receive at the counter.",
			"Kết quả là ước tính theo tỷ giá do {0} công bố và có thể khác với số tiền bạn nhận tại quầy.",
			"ผลลัพธ์เป็นค่าประมาณตามอัตราที่ {0} ประกาศ และอาจแตกต่างจากจำนวนที่คุณได้รับที่เคาน์เตอร์",
			"结果是根据 {0} 公布的汇率估算的，可能与您在柜台实际收到的金额不同。",
			"結果は {0} の公表レートに基づく概算で、窓口で受け取る金額とは異なる場合があります。",
			"결과는 {0}이(가) 게시한 환율에 따른 추정치이며 창구에서 받는 금액과 다를 수 있습니다.",
			"Les résultats sont des estimations d’après les taux publiés par {0} et peuvent différer du montant reçu au guichet."),
		new("Rate used: {0}", "Tỷ giá áp dụng: {0}", "อัตราที่ใช้: {0}", "使用汇率：{0}", "適用レート: {0}", "적용 환율: {0}", "Taux appliqué : {0}"),
		new("bank buys (transfer)", "ngân hàng mua (chuyển khoản)", "ธนาคารรับซื้อ (โอน)", "银行买入（转账）", "銀行の買値（送金）", "은행 매입(송금)", "la banque achète (virement)"),
		new("bank buys (cash)", "ngân hàng mua (tiền mặt)", "ธนาคารรับซื้อ (เงินสด)", "银行买入（现钞）", "銀行の買値（現金）", "은행 매입(현찰)", "la banque achète (espèces)"),
		new("bank sells", "ngân hàng bán", "ธนาคารขาย", "银行卖出", "銀行の売値", "은행 매도", "la banque vend"),
		new("Compare {0} at other banks →", "So sánh {0} tại các ngân hàng khác →", "เปรียบเทียบ {0} กับธนาคารอื่น →", "比较其他银行的 {0} →", "他の銀行の {0} と比較 →", "다른 은행의 {0} 비교 →", "Comparer les {0} dans d’autres banques →"),

		// Compare banks: all countries
		new("Compare in", "So sánh tại", "เปรียบเทียบใน", "比较范围", "比較範囲", "비교 범위", "Comparer dans"),
		new("Lowest {0} spread anywhere", "Chênh lệch {0} thấp nhất", "ส่วนต่าง {0} ต่ำที่สุด", "{0} 价差最低", "{0} のスプレッドが最も小さい銀行", "{0} 스프레드가 가장 낮은 곳", "Écart {0} le plus bas"),
		new("spread", "chênh lệch", "ส่วนต่าง", "价差", "スプレッド", "스프레드", "écart"),
		new("Best country for {0}", "Quốc gia tốt nhất cho {0}", "ประเทศที่ดีที่สุดสำหรับ {0}", "{0} 最佳国家", "{0} に最適な国", "{0}에 가장 좋은 국가", "Meilleur pays pour les {0}"),
		new("Average spread", "Chênh lệch trung bình", "ส่วนต่างเฉลี่ย", "平均价差", "平均スプレッド", "평균 스프레드", "Écart moyen"),
		new("across {0} bank(s)", "trên {0} ngân hàng", "จาก {0} ธนาคาร", "共 {0} 家银行", "{0} 行の平均", "은행 {0}곳 기준", "sur {0} banque(s)"),
		new("Coverage", "Phạm vi", "ความครอบคลุม", "覆盖范围", "対象範囲", "범위", "Couverture"),
		new("{0} country(ies) · {1} bank(s)", "{0} quốc gia · {1} ngân hàng", "{0} ประเทศ · {1} ธนาคาร", "{0} 个国家 · {1} 家银行", "{0} か国 · {1} 行", "{0}개국 · 은행 {1}곳", "{0} pays · {1} banque(s)"),
		new("Each bank’s rates are in its local currency", "Tỷ giá của mỗi ngân hàng tính bằng nội tệ của nước đó", "อัตราของแต่ละธนาคารเป็นสกุลเงินท้องถิ่น", "各银行的汇率以其本币计", "各銀行のレートは現地通貨建てです", "각 은행의 환율은 현지 통화 기준입니다", "Les taux de chaque banque sont dans sa monnaie locale"),
		new("{0} at banks in all countries", "{0} tại các ngân hàng ở mọi quốc gia", "{0} ที่ธนาคารในทุกประเทศ", "所有国家银行的 {0}", "すべての国の銀行の {0}", "모든 국가 은행의 {0}", "{0} dans les banques de tous les pays"),
		new("sorted by country", "sắp xếp theo quốc gia", "เรียงตามประเทศ", "按国家排序", "国順", "국가순", "triées par pays"),
		new("rates in each bank’s local currency", "tỷ giá tính bằng nội tệ của từng ngân hàng", "อัตราเป็นสกุลเงินท้องถิ่นของแต่ละธนาคาร", "汇率以各银行本币计", "レートは各銀行の現地通貨建て", "환율은 각 은행의 현지 통화 기준", "taux dans la monnaie locale de chaque banque"),
		new("Filter by country", "Lọc theo quốc gia", "กรองตามประเทศ", "按国家筛选", "国で絞り込む", "국가별 필터", "Filtrer par pays"),
		new("Rates are in each bank’s local currency, so banks in different countries are compared by spread.",
			"Tỷ giá tính bằng nội tệ của từng ngân hàng, nên các ngân hàng ở các quốc gia khác nhau được so sánh theo chênh lệch.",
			"อัตราเป็นสกุลเงินท้องถิ่นของแต่ละธนาคาร จึงเปรียบเทียบธนาคารต่างประเทศกันด้วยส่วนต่าง",
			"汇率以各银行本币计，因此不同国家的银行按价差比较。",
			"レートは各銀行の現地通貨建てのため、国の異なる銀行はスプレッドで比較します。",
			"환율이 각 은행의 현지 통화 기준이므로 다른 국가의 은행은 스프레드로 비교합니다.",
			"Les taux sont dans la monnaie locale de chaque banque : les banques de pays différents sont donc comparées par écart."),

		// Compare banks: Countries picker
		new("Countries", "Quốc gia", "ประเทศ", "国家", "国", "국가", "Pays"),
		new("Choose countries", "Chọn quốc gia", "เลือกประเทศ", "选择国家", "国を選択", "국가 선택", "Choisir des pays"),
		new("{0} +{1} more", "{0} +{1} nước khác", "{0} +อีก {1}", "{0} 等 {1} 个", "{0} 他 {1} か国", "{0} 외 {1}개", "{0} +{1} autres"),
		new("Search countries", "Tìm quốc gia", "ค้นหาประเทศ", "搜索国家", "国を検索", "국가 검색", "Rechercher un pays"),
		new("Comparing prices in {0}", "So sánh giá tại {0}", "เปรียบเทียบราคาใน{0}", "比较{0}的价格", "{0}の価格を比較", "{0} 가격 비교", "Comparaison des prix : {0}"),
		new("Comparing {0} countries by spread", "So sánh {0} quốc gia theo chênh lệch", "เปรียบเทียบ {0} ประเทศตามส่วนต่าง", "按价差比较 {0} 个国家", "{0} か国をスプレッドで比較", "{0}개국을 스프레드로 비교", "Comparaison de {0} pays par écart"),
		new("Pick at least one", "Chọn ít nhất một", "เลือกอย่างน้อยหนึ่งรายการ", "请至少选择一个", "1つ以上選んでください", "하나 이상 선택하세요", "Choisissez-en au moins un"),
		new("{0} at banks in {1} countries", "{0} tại các ngân hàng ở {1} quốc gia", "{0} ที่ธนาคารใน {1} ประเทศ", "{1} 个国家银行的 {0}", "{1} か国の銀行の {0}", "{1}개국 은행의 {0}", "{0} dans les banques de {1} pays"),
		new("No matches.", "Không có kết quả.", "ไม่พบรายการที่ตรงกัน", "没有匹配项。", "一致する項目がありません。", "일치하는 항목이 없습니다.", "Aucun résultat."),
		new("Done", "Xong", "เสร็จสิ้น", "完成", "完了", "완료", "OK"),

		// Rate alerts: Subscribe button and dialog
		new("Subscribe", "Đăng ký", "สมัครรับ", "订阅", "登録", "구독", "S’abonner"),
		new("Subscribe to rate alerts", "Đăng ký nhận thông báo tỷ giá", "สมัครรับการแจ้งเตือนอัตรา", "订阅汇率提醒", "レートアラートに登録", "환율 알림 구독", "S’abonner aux alertes de taux"),
		new("Get rate alerts", "Nhận thông báo tỷ giá", "รับการแจ้งเตือนอัตรา", "获取汇率提醒", "レートアラートを受け取る", "환율 알림 받기", "Recevoir des alertes de taux"),
		new("We’ll email you when your banks publish new rates.", "Chúng tôi sẽ gửi email khi ngân hàng của bạn công bố tỷ giá mới.", "เราจะส่งอีเมลถึงคุณเมื่อธนาคารที่คุณเลือกประกาศอัตราใหม่", "当您选择的银行发布新汇率时，我们会给您发邮件。", "選んだ銀行が新しいレートを公表したらメールでお知らせします。", "선택한 은행이 새 환율을 발표하면 이메일로 알려 드립니다.", "Nous vous écrirons quand vos banques publieront de nouveaux taux."),
		new("Close", "Đóng", "ปิด", "关闭", "閉じる", "닫기", "Fermer"),
		new("Email address", "Địa chỉ email", "ที่อยู่อีเมล", "邮箱地址", "メールアドレス", "이메일 주소", "Adresse e-mail"),
		new("Banks", "Ngân hàng", "ธนาคาร", "银行", "銀行", "은행", "Banques"),
		new("Search banks", "Tìm ngân hàng", "ค้นหาธนาคาร", "搜索银行", "銀行を検索", "은행 검색", "Rechercher une banque"),
		new("Select all banks", "Chọn tất cả ngân hàng", "เลือกธนาคารทั้งหมด", "选择所有银行", "すべての銀行を選択", "모든 은행 선택", "Toutes les banques"),
		new("Currencies", "Loại tiền", "สกุลเงิน", "货币", "通貨", "통화", "Devises"),
		new("Search currencies", "Tìm loại tiền", "ค้นหาสกุลเงิน", "搜索货币", "通貨を検索", "통화 검색", "Rechercher une devise"),
		new("All currencies", "Tất cả loại tiền", "ทุกสกุลเงิน", "所有货币", "すべての通貨", "모든 통화", "Toutes les devises"),
		new("all currencies", "tất cả loại tiền", "ทุกสกุลเงิน", "所有货币", "すべての通貨", "모든 통화", "toutes les devises"),
		new("· {0} of {1} selected", "· đã chọn {0}/{1}", "· เลือก {0} จาก {1}", "· 已选 {0}/{1}", "· {1} 件中 {0} 件を選択", "· {1}개 중 {0}개 선택", "· {0} sur {1} sélectionné(s)"),
		new("Subscribe to alerts", "Đăng ký nhận thông báo", "สมัครรับการแจ้งเตือน", "订阅提醒", "アラートに登録", "알림 구독", "S’abonner aux alertes"),
		new("One email per rate update. Unsubscribe any time from any email.", "Một email cho mỗi lần cập nhật tỷ giá. Hủy đăng ký bất cứ lúc nào từ bất kỳ email nào.", "หนึ่งอีเมลต่อการอัปเดตอัตราแต่ละครั้ง ยกเลิกได้ทุกเมื่อจากอีเมลใดก็ได้", "每次汇率更新发送一封邮件。可随时通过任意邮件退订。", "レート更新ごとに1通。どのメールからでもいつでも配信停止できます。", "환율이 업데이트될 때마다 이메일 한 통. 어느 이메일에서든 언제든지 구독을 취소할 수 있습니다.", "Un e-mail par mise à jour des taux. Désabonnement possible à tout moment depuis n’importe quel e-mail."),
		new("Enter a valid email address.", "Hãy nhập địa chỉ email hợp lệ.", "โปรดป้อนที่อยู่อีเมลที่ถูกต้อง", "请输入有效的邮箱地址。", "有効なメールアドレスを入力してください。", "올바른 이메일 주소를 입력하세요.", "Saisissez une adresse e-mail valide."),
		new("Choose a country.", "Hãy chọn quốc gia.", "โปรดเลือกประเทศ", "请选择国家。", "国を選んでください。", "국가를 선택하세요.", "Choisissez un pays."),
		new("Choose at least one bank.", "Hãy chọn ít nhất một ngân hàng.", "โปรดเลือกอย่างน้อยหนึ่งธนาคาร", "请至少选择一家银行。", "銀行を1つ以上選んでください。", "은행을 하나 이상 선택하세요.", "Choisissez au moins une banque."),
		new("Choose at least one currency.", "Hãy chọn ít nhất một loại tiền.", "โปรดเลือกอย่างน้อยหนึ่งสกุลเงิน", "请至少选择一种货币。", "通貨を1つ以上選んでください。", "통화를 하나 이상 선택하세요.", "Choisissez au moins une devise."),
		new("Something went wrong. Please try again in a few minutes.", "Đã xảy ra lỗi. Vui lòng thử lại sau vài phút.", "เกิดข้อผิดพลาด โปรดลองอีกครั้งในอีกสักครู่", "出了点问题，请几分钟后重试。", "問題が発生しました。数分後にもう一度お試しください。", "문제가 발생했습니다. 몇 분 후에 다시 시도하세요.", "Une erreur s’est produite. Réessayez dans quelques minutes."),
		new("You’re subscribed", "Bạn đã đăng ký", "คุณสมัครรับแล้ว", "订阅成功", "登録しました", "구독되었습니다", "Vous êtes abonné"),
		new("We’ll email {0} when {1} in {2} publish new rates for {3}.", "Chúng tôi sẽ gửi email đến {0} khi {1} tại {2} công bố tỷ giá mới cho {3}.", "เราจะส่งอีเมลถึง {0} เมื่อ {1} ใน{2}ประกาศอัตราใหม่ของ {3}", "当{2}的 {1} 发布 {3} 的新汇率时，我们会发邮件到 {0}。", "{2}の{1}が{3}の新しいレートを公表したら、{0} にメールします。", "{2}의 {1}에서 {3}의 새 환율을 발표하면 {0}(으)로 이메일을 보내 드립니다.", "Nous écrirons à {0} quand {1} ({2}) publiera de nouveaux taux pour {3}."),
		new("Your alerts are updated: we’ll email {0} when {1} in {2} publish new rates for {3}.", "Đã cập nhật thông báo: chúng tôi sẽ gửi email đến {0} khi {1} tại {2} công bố tỷ giá mới cho {3}.", "อัปเดตการแจ้งเตือนแล้ว: เราจะส่งอีเมลถึง {0} เมื่อ {1} ใน{2}ประกาศอัตราใหม่ของ {3}", "提醒已更新：当{2}的 {1} 发布 {3} 的新汇率时，我们会发邮件到 {0}。", "アラートを更新しました。{2}の{1}が{3}の新しいレートを公表したら、{0} にメールします。", "알림이 업데이트되었습니다. {2}의 {1}에서 {3}의 새 환율을 발표하면 {0}(으)로 이메일을 보내 드립니다.", "Vos alertes sont à jour : nous écrirons à {0} quand {1} ({2}) publiera de nouveaux taux pour {3}."),
		new("{0} and {1} more banks", "{0} và {1} ngân hàng khác", "{0} และอีก {1} ธนาคาร", "{0} 等另外 {1} 家银行", "{0} ほか {1} 行", "{0} 외 은행 {1}곳", "{0} et {1} autres banques"),
		new("Check your inbox to confirm your email address.", "Hãy kiểm tra hộp thư để xác nhận địa chỉ email.", "โปรดตรวจสอบกล่องจดหมายเพื่อยืนยันที่อยู่อีเมลของคุณ", "请查收邮件以确认您的邮箱地址。", "受信トレイを確認して、メールアドレスを確認してください。", "받은편지함에서 이메일 주소를 확인해 주세요.", "Consultez votre boîte de réception pour confirmer votre adresse e-mail."),
		new("Edit alerts", "Sửa thông báo", "แก้ไขการแจ้งเตือน", "编辑提醒", "アラートを編集", "알림 수정", "Modifier les alertes"),

		// Rate alerts: confirm and unsubscribe pages
		new("Rate alerts", "Thông báo tỷ giá", "การแจ้งเตือนอัตรา", "汇率提醒", "レートアラート", "환율 알림", "Alertes de taux"),
		new("Your alerts are on", "Đã bật thông báo", "เปิดการแจ้งเตือนแล้ว", "提醒已开启", "アラートが有効になりました", "알림이 켜졌습니다", "Vos alertes sont activées"),
		new("This link doesn’t work", "Liên kết này không hoạt động", "ลิงก์นี้ใช้ไม่ได้", "此链接无效", "このリンクは無効です", "이 링크는 작동하지 않습니다", "Ce lien ne fonctionne pas"),
		new("It may be incomplete or out of date. Subscribe again to get a new link.", "Liên kết có thể không đầy đủ hoặc đã hết hạn. Hãy đăng ký lại để nhận liên kết mới.", "ลิงก์อาจไม่สมบูรณ์หรือหมดอายุ โปรดสมัครใหม่เพื่อรับลิงก์ใหม่", "链接可能不完整或已过期。请重新订阅以获取新链接。", "リンクが不完全か、期限切れの可能性があります。もう一度登録して新しいリンクを受け取ってください。", "링크가 불완전하거나 만료되었을 수 있습니다. 다시 구독하여 새 링크를 받으세요.", "Il est peut-être incomplet ou expiré. Abonnez-vous à nouveau pour recevoir un nouveau lien."),
		new("It may be incomplete or out of date.", "Liên kết có thể không đầy đủ hoặc đã hết hạn.", "ลิงก์อาจไม่สมบูรณ์หรือหมดอายุ", "链接可能不完整或已过期。", "リンクが不完全か、期限切れの可能性があります。", "링크가 불완전하거나 만료되었을 수 있습니다.", "Il est peut-être incomplet ou expiré."),
		new("Something went wrong", "Đã xảy ra lỗi", "เกิดข้อผิดพลาด", "出了点问题", "問題が発生しました", "문제가 발생했습니다", "Une erreur s’est produite"),
		new("Please try again in a few minutes.", "Vui lòng thử lại sau vài phút.", "โปรดลองอีกครั้งในอีกสักครู่", "请几分钟后重试。", "数分後にもう一度お試しください。", "몇 분 후에 다시 시도하세요.", "Réessayez dans quelques minutes."),
		new("Go to bank rates", "Xem tỷ giá ngân hàng", "ไปที่อัตราธนาคาร", "查看银行汇率", "銀行レートへ", "은행 환율로 이동", "Voir les taux des banques"),
		new("Stop rate alerts?", "Dừng thông báo tỷ giá?", "หยุดการแจ้งเตือนอัตราหรือไม่", "停止汇率提醒？", "レートアラートを停止しますか？", "환율 알림을 중지할까요?", "Arrêter les alertes de taux ?"),
		new("You won’t get any more emails about new rates.", "Bạn sẽ không nhận thêm email nào về tỷ giá mới.", "คุณจะไม่ได้รับอีเมลเกี่ยวกับอัตราใหม่อีก", "您将不再收到新汇率的邮件。", "新しいレートのメールは届かなくなります。", "새 환율에 대한 이메일을 더 이상 받지 않습니다.", "Vous ne recevrez plus d’e-mails sur les nouveaux taux."),
		new("Unsubscribe", "Hủy đăng ký", "ยกเลิกการสมัคร", "退订", "配信停止", "구독 취소", "Se désabonner"),
		new("You’re unsubscribed", "Bạn đã hủy đăng ký", "คุณยกเลิกการสมัครแล้ว", "您已退订", "配信を停止しました", "구독이 취소되었습니다", "Vous êtes désabonné"),
		new("You won’t get any more emails about new rates. You can subscribe again at any time.", "Bạn sẽ không nhận thêm email nào về tỷ giá mới. Bạn có thể đăng ký lại bất cứ lúc nào.", "คุณจะไม่ได้รับอีเมลเกี่ยวกับอัตราใหม่อีก และสามารถสมัครใหม่ได้ทุกเมื่อ", "您将不再收到新汇率的邮件。您可以随时重新订阅。", "新しいレートのメールは届かなくなります。いつでも再登録できます。", "새 환율에 대한 이메일을 더 이상 받지 않습니다. 언제든지 다시 구독할 수 있습니다.", "Vous ne recevrez plus d’e-mails sur les nouveaux taux. Vous pouvez vous réabonner à tout moment."),
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
