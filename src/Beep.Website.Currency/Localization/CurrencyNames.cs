using System.Globalization;

namespace Beep.Website.Currency;

/// <summary>
/// The names of the currencies with rates in every language of <see cref="Languages"/>, by ISO code. The API gives
/// a currency's name in English, as the database has it ("UK Pound Sterling", "PRC Yuan Renminbi"); <see cref="BeepApiClient"/>
/// puts it in the current UI culture's language with <see cref="Name"/>, English included. A currency not listed here
/// keeps the API's name.
/// </summary>
public static class CurrencyNames
{
	private static readonly Dictionary<string, UiTextEntry> Names = new(StringComparer.OrdinalIgnoreCase)
	{
		["AUD"] = new("Australian Dollar", "Đô la Úc", "ดอลลาร์ออสเตรเลีย", "澳大利亚元", "オーストラリア・ドル", "호주 달러", "Dollar australien", "Australischer Dollar", "Dólar australiano", "Dolar Australia", "Dolar Australia", "Australische dollar", "دولار أسترالي"),
		["BRL"] = new("Brazilian Real", "Real Brazil", "เรียลบราซิล", "巴西雷亚尔", "ブラジル・レアル", "브라질 레알", "Réal brésilien", "Brasilianischer Real", "Real brasileño", "Real Brasil", "Real Brazil", "Braziliaanse real", "ريال برازيلي"),
		["CAD"] = new("Canadian Dollar", "Đô la Canada", "ดอลลาร์แคนาดา", "加拿大元", "カナダ・ドル", "캐나다 달러", "Dollar canadien", "Kanadischer Dollar", "Dólar canadiense", "Dolar Kanada", "Dolar Kanada", "Canadese dollar", "دولار كندي"),
		["CHF"] = new("Swiss Franc", "Franc Thụy Sĩ", "ฟรังก์สวิส", "瑞士法郎", "スイス・フラン", "스위스 프랑", "Franc suisse", "Schweizer Franken", "Franco suizo", "Franc Swiss", "Franc Swiss", "Zwitserse frank", "فرنك سويسري"),
		["CNY"] = new("Chinese Yuan", "Nhân dân tệ", "หยวนจีน", "人民币", "人民元", "중국 위안", "Yuan chinois", "Chinesischer Yuan", "Yuan chino", "Yuan Tiongkok", "Yuan China", "Chinese yuan", "يوان صيني"),
		["CZK"] = new("Czech Koruna", "Koruna Séc", "โครูนาเช็ก", "捷克克朗", "チェコ・コルナ", "체코 코루나", "Couronne tchèque", "Tschechische Krone", "Corona checa", "Koruna Ceko", "Koruna Czech", "Tsjechische kroon", "كرونة تشيكية"),
		["DKK"] = new("Danish Krone", "Krone Đan Mạch", "โครนเดนมาร์ก", "丹麦克朗", "デンマーク・クローネ", "덴마크 크로네", "Couronne danoise", "Dänische Krone", "Corona danesa", "Krone Denmark", "Krone Denmark", "Deense kroon", "كرونة دنماركية"),
		["EUR"] = new("Euro", "Euro", "ยูโร", "欧元", "ユーロ", "유로", "Euro", "Euro", "Euro", "Euro", "Euro", "Euro", "يورو"),
		["GBP"] = new("Pound Sterling", "Bảng Anh", "ปอนด์สเตอร์ลิง", "英镑", "英ポンド", "영국 파운드", "Livre sterling", "Pfund Sterling", "Libra esterlina", "Pound sterling", "Paun sterling", "Pond sterling", "جنيه إسترليني"),
		["HKD"] = new("Hong Kong Dollar", "Đô la Hồng Kông", "ดอลลาร์ฮ่องกง", "港元", "香港ドル", "홍콩 달러", "Dollar de Hong Kong", "Hongkong-Dollar", "Dólar de Hong Kong", "Dolar Hong Kong", "Dolar Hong Kong", "Hongkongse dollar", "دولار هونغ كونغ"),
		["HUF"] = new("Hungarian Forint", "Forint Hungary", "โฟรินต์ฮังการี", "匈牙利福林", "ハンガリー・フォリント", "헝가리 포린트", "Forint hongrois", "Ungarischer Forint", "Forinto húngaro", "Forint Hungaria", "Forint Hungary", "Hongaarse forint", "فورنت مجري"),
		["IDR"] = new("Indonesian Rupiah", "Rupiah Indonesia", "รูเปียห์อินโดนีเซีย", "印度尼西亚盾", "インドネシア・ルピア", "인도네시아 루피아", "Roupie indonésienne", "Indonesische Rupiah", "Rupia indonesia", "Rupiah Indonesia", "Rupiah Indonesia", "Indonesische roepia", "روبية إندونيسية"),
		["ILS"] = new("Israeli New Shekel", "Shekel Israel", "เชเกลอิสราเอล", "以色列新谢克尔", "イスラエル・新シェケル", "이스라엘 신 셰켈", "Nouveau shekel israélien", "Neuer Israelischer Schekel", "Nuevo séquel israelí", "Shekel Israel", "Shekel Israel", "Israëlische nieuwe sjekel", "شيكل إسرائيلي جديد"),
		["INR"] = new("Indian Rupee", "Rupee Ấn Độ", "รูปีอินเดีย", "印度卢比", "インド・ルピー", "인도 루피", "Roupie indienne", "Indische Rupie", "Rupia india", "Rupee India", "Rupee India", "Indiase roepie", "روبية هندية"),
		["ISK"] = new("Icelandic Króna", "Króna Iceland", "โครนาไอซ์แลนด์", "冰岛克朗", "アイスランド・クローナ", "아이슬란드 크로나", "Couronne islandaise", "Isländische Krone", "Corona islandesa", "Krona Islandia", "Krona Iceland", "IJslandse kroon", "كرونة آيسلندية"),
		["JPY"] = new("Japanese Yen", "Yên Nhật", "เยนญี่ปุ่น", "日元", "日本円", "일본 엔", "Yen japonais", "Japanischer Yen", "Yen japonés", "Yen Jepang", "Yen Jepun", "Japanse yen", "ين ياباني"),
		["KRW"] = new("South Korean Won", "Won Hàn Quốc", "วอนเกาหลีใต้", "韩元", "韓国ウォン", "대한민국 원", "Won sud-coréen", "Südkoreanischer Won", "Won surcoreano", "Won Korea Selatan", "Won Korea Selatan", "Zuid-Koreaanse won", "وون كوري جنوبي"),
		["KWD"] = new("Kuwaiti Dinar", "Dinar Kuwait", "ดีนาร์คูเวต", "科威特第纳尔", "クウェート・ディナール", "쿠웨이트 디나르", "Dinar koweïtien", "Kuwait-Dinar", "Dinar kuwaití", "Dinar Kuwait", "Dinar Kuwait", "Koeweitse dinar", "دينار كويتي"),
		["LAK"] = new("Lao Kip", "Kíp Lào", "กีบลาว", "老挝基普", "ラオス・キープ", "라오스 킵", "Kip laotien", "Laotischer Kip", "Kip laosiano", "Kip Laos", "Kip Laos", "Laotiaanse kip", "كيب لاوي"),
		["MXN"] = new("Mexican Peso", "Peso Mexico", "เปโซเม็กซิโก", "墨西哥比索", "メキシコ・ペソ", "멕시코 페소", "Peso mexicain", "Mexikanischer Peso", "Peso mexicano", "Peso Meksiko", "Peso Mexico", "Mexicaanse peso", "بيزو مكسيكي"),
		["MYR"] = new("Malaysian Ringgit", "Ringgit Malaysia", "ริงกิตมาเลเซีย", "马来西亚林吉特", "マレーシア・リンギット", "말레이시아 링깃", "Ringgit malaisien", "Malaysischer Ringgit", "Ringgit malayo", "Ringgit Malaysia", "Ringgit Malaysia", "Maleisische ringgit", "رينغيت ماليزي"),
		["NOK"] = new("Norwegian Krone", "Krone Na Uy", "โครนนอร์เวย์", "挪威克朗", "ノルウェー・クローネ", "노르웨이 크로네", "Couronne norvégienne", "Norwegische Krone", "Corona noruega", "Krone Norwegia", "Krone Norway", "Noorse kroon", "كرونة نرويجية"),
		["NZD"] = new("New Zealand Dollar", "Đô la New Zealand", "ดอลลาร์นิวซีแลนด์", "新西兰元", "ニュージーランド・ドル", "뉴질랜드 달러", "Dollar néo-zélandais", "Neuseeland-Dollar", "Dólar neozelandés", "Dolar Selandia Baru", "Dolar New Zealand", "Nieuw-Zeelandse dollar", "دولار نيوزيلندي"),
		["PHP"] = new("Philippine Peso", "Peso Philippines", "เปโซฟิลิปปินส์", "菲律宾比索", "フィリピン・ペソ", "필리핀 페소", "Peso philippin", "Philippinischer Peso", "Peso filipino", "Peso Filipina", "Peso Filipina", "Filipijnse peso", "بيزو فلبيني"),
		["PLN"] = new("Polish Zloty", "Zloty Ba Lan", "ซลอตีโปแลนด์", "波兰兹罗提", "ポーランド・ズウォティ", "폴란드 즐로티", "Złoty polonais", "Polnischer Złoty", "Esloti polaco", "Zloty Polandia", "Zloty Poland", "Poolse zloty", "زلوتي بولندي"),
		["RON"] = new("Romanian Leu", "Leu Romania", "ลิวโรมาเนีย", "罗马尼亚列伊", "ルーマニア・レウ", "루마니아 레우", "Leu roumain", "Rumänischer Leu", "Leu rumano", "Leu Rumania", "Leu Romania", "Roemeense leu", "ليو روماني"),
		["RUB"] = new("Russian Ruble", "Rúp Nga", "รูเบิลรัสเซีย", "俄罗斯卢布", "ロシア・ルーブル", "러시아 루블", "Rouble russe", "Russischer Rubel", "Rublo ruso", "Rubel Rusia", "Rubel Rusia", "Russische roebel", "روبل روسي"),
		["SAR"] = new("Saudi Riyal", "Riyal Ả Rập Xê Út", "ริยาลซาอุดีอาระเบีย", "沙特里亚尔", "サウジアラビア・リヤル", "사우디 리얄", "Riyal saoudien", "Saudi-Riyal", "Riyal saudí", "Riyal Arab Saudi", "Riyal Arab Saudi", "Saoedi-Arabische riyal", "ريال سعودي"),
		["SEK"] = new("Swedish Krona", "Krona Thụy Điển", "โครนาสวีเดน", "瑞典克朗", "スウェーデン・クローナ", "스웨덴 크로나", "Couronne suédoise", "Schwedische Krone", "Corona sueca", "Krona Swedia", "Krona Sweden", "Zweedse kroon", "كرونة سويدية"),
		["SGD"] = new("Singapore Dollar", "Đô la Singapore", "ดอลลาร์สิงคโปร์", "新加坡元", "シンガポール・ドル", "싱가포르 달러", "Dollar de Singapour", "Singapur-Dollar", "Dólar de Singapur", "Dolar Singapura", "Dolar Singapura", "Singaporese dollar", "دولار سنغافوري"),
		["THB"] = new("Thai Baht", "Baht Thái", "บาทไทย", "泰铢", "タイ・バーツ", "태국 바트", "Baht thaïlandais", "Thailändischer Baht", "Baht tailandés", "Baht Thailand", "Baht Thailand", "Thaise baht", "بات تايلندي"),
		["TRY"] = new("Turkish Lira", "Lira Thổ Nhĩ Kỳ", "ลีราตุรกี", "土耳其里拉", "トルコ・リラ", "튀르키예 리라", "Livre turque", "Türkische Lira", "Lira turca", "Lira Turki", "Lira Turki", "Turkse lira", "ليرة تركية"),
		["TWD"] = new("New Taiwan Dollar", "Đô la Đài Loan", "ดอลลาร์ไต้หวัน", "新台币", "新台湾ドル", "신 타이완 달러", "Nouveau dollar de Taïwan", "Neuer Taiwan-Dollar", "Nuevo dólar taiwanés", "Dolar Taiwan Baru", "Dolar Taiwan Baharu", "Nieuwe Taiwanese dollar", "دولار تايواني جديد"),
		["USD"] = new("US Dollar", "Đô la Mỹ", "ดอลลาร์สหรัฐ", "美元", "米ドル", "미국 달러", "Dollar américain", "US-Dollar", "Dólar estadounidense", "Dolar AS", "Dolar AS", "Amerikaanse dollar", "دولار أمريكي"),
		["VND"] = new("Vietnamese Dong", "Đồng Việt Nam", "ดองเวียดนาม", "越南盾", "ベトナム・ドン", "베트남 동", "Dông vietnamien", "Vietnamesischer Dong", "Dong vietnamita", "Dong Vietnam", "Dong Vietnam", "Vietnamese dong", "دونغ فيتنامي"),
		["ZAR"] = new("South African Rand", "Rand Nam Phi", "แรนด์แอฟริกาใต้", "南非兰特", "南アフリカ・ランド", "남아프리카 랜드", "Rand sud-africain", "Südafrikanischer Rand", "Rand sudafricano", "Rand Afrika Selatan", "Rand Afrika Selatan", "Zuid-Afrikaanse rand", "راند جنوب أفريقي"),
	};

	/// <summary>
	/// The name of the currency <paramref name="code"/> in the current UI culture's language, or
	/// <paramref name="english"/> (the API's name) for a currency not listed.
	/// </summary>
	public static string Name(string code, string english) =>
		Names.TryGetValue(code, out var entry)
			? entry.In(Languages.For(CultureInfo.CurrentUICulture).Code)
			: english;
}
