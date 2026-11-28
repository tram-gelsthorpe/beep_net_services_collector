using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Beep.Platform.Api.Websites.Currency.Parsers;

/// <summary>
/// Reads a bank's exchange-rate table that the collector extracted cell by cell: its WebsiteSelector's
/// ContentSelector picks the table's cells, so ExtractedJson is
/// <code>{"ContentSelector": ["USD", "25,635", "25,640", "26,065", "26,060", "EUR", ...]}</code>
/// A cell naming a currency ("USD", "USD100", "Đô la Mỹ (USD)") starts a row; the cells after it are its rates,
/// in the bank's <see cref="Columns"/> order. Cells before the first currency (headers) and after a row's rates
/// (another table, a note) are ignored, and only a currency's first row counts (e.g. USD $50-$100 notes).
/// The pages give no publication time, so rates are recorded at the time they were collected
/// (<see cref="UsesCollectionTime"/>).
/// </summary>
public sealed partial class BankRateTableParser : IBankRateParser
{
	public static readonly BankRateTableParser Hsbc = new("hsbc", "HSBCVNVX", RateNumberFormat.Dot,
		[RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.SellCash, RateColumn.SellTransfer]);

	/// <summary>Woori quotes JPY and KRW per 100.</summary>
	public static readonly BankRateTableParser Woori = new("woori", "HVBKVNVX", RateNumberFormat.Comma,
		[RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.SellCash, RateColumn.SellTransfer], per100: ["JPY", "KRW"]);

	public static readonly BankRateTableParser Ocb = new("ocb", "ORCOVNVX", RateNumberFormat.Comma,
		[RateColumn.Skip, RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.SellCash, RateColumn.SellTransfer]);

	public static readonly BankRateTableParser Agribank = new("agribank", "VBAAVNVX", RateNumberFormat.Comma,
		[RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.Sell]);

	public static readonly BankRateTableParser BvBank = new("bvbank", "VCBCVNVX", RateNumberFormat.Comma,
		[RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.SellCash, RateColumn.SellTransfer]);

	public static readonly BankRateTableParser VietABank = new("vietabank", "VNACVNVX", RateNumberFormat.Comma,
		[RateColumn.Skip, RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.SellCash, RateColumn.SellTransfer]);

	/// <summary>VietinBank marks its cash rates "# 25.470" (large notes) and "&amp; 24.730" (small notes, a row of their own).</summary>
	public static readonly BankRateTableParser VietinBank = new("vietinbank", "ICBVVNVX", RateNumberFormat.Dot,
		[RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.Sell]);

	public static readonly BankRateTableParser NamABank = new("namabank", "NAMAVNVX", RateNumberFormat.Dot,
		[RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.Sell]);

	/// <summary>NCB's name cell comes before the code, and its selling columns are transfer, then cash.</summary>
	public static readonly BankRateTableParser Ncb = new("ncb", "NVBAVNVX", RateNumberFormat.Comma,
		[RateColumn.BuyCash, RateColumn.BuyTransfer, RateColumn.SellTransfer, RateColumn.SellCash]);

	// Not currencies: gold, and the dong itself.
	private static readonly HashSet<string> NotCurrencies = new(StringComparer.Ordinal) { "XAU", "VND" };

	private readonly RateNumberFormat _format;
	private readonly HashSet<string> _per100;

	public BankRateTableParser(string name, string bic, RateNumberFormat format, IReadOnlyList<RateColumn> columns, IEnumerable<string>? per100 = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentException.ThrowIfNullOrWhiteSpace(bic);
		if (columns.Count == 0 || !columns.Any(c => c != RateColumn.Skip))
			throw new ArgumentException("At least one rate column is needed", nameof(columns));

		Name = name;
		Bic = bic;
		_format = format;
		Columns = columns;
		_per100 = new HashSet<string>(per100 ?? [], StringComparer.OrdinalIgnoreCase);
	}

	public string Name { get; }

	public string Bic { get; }

	public bool UsesCollectionTime => true;

	/// <summary>The cells after a currency's code, in order.</summary>
	public IReadOnlyList<RateColumn> Columns { get; }

	public IReadOnlyList<CurrencyRateBank> Parse(string extractedJson, DateTimeOffset collectedAt, ICollection<string>? skipped = null)
	{
		var notes = new List<string>();
		var rates = ParseCells(ReadCells(extractedJson), collectedAt, notes);
		if (rates.Count == 0)
			throw new FormatException(notes.Count == 0
				? $"No {Bic} rates found in the collected data"
				: $"No {Bic} rates could be read: {string.Join("; ", notes)}");
		foreach (var note in notes)
			skipped?.Add(note);
		return rates;
	}

	/// <summary>
	/// The rates in the table's cells (their text), timed <paramref name="at"/>; empty when no cell names a currency.
	/// A currency with a rate cell that is not a number (a typo on the bank's page: "15162..") is left out, and the
	/// others are read; <paramref name="skipped"/> gets a note for each currency left out.
	/// </summary>
	public IReadOnlyList<CurrencyRateBank> ParseCells(
		IReadOnlyList<string> cells, DateTimeOffset at, ICollection<string>? skipped = null)
	{
		// To the minute, as the banks write times.
		var local = at.ToOffset(BankRateParsers.VietnamOffset);
		local = local.AddTicks(-(local.Ticks % TimeSpan.TicksPerMinute));
		var date = DateOnly.FromDateTime(local.DateTime);

		var rates = new List<CurrencyRateBank>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		for (var i = 0; i < cells.Count; i++)
		{
			var code = CurrencyOf(cells[i]);
			if (code is null)
				continue;

			// The row's cells: up to the next currency, at most one per column.
			var row = cells.Skip(i + 1).TakeWhile(c => CurrencyOf(c) is null).Take(Columns.Count).ToList();
			if (NotCurrencies.Contains(code) || !seen.Add(code))
				continue;

			decimal? Rate(RateColumn column)
			{
				var index = IndexOf(column);
				return index < row.Count ? ParseRate(row[index], code, column) : null;
			}

			// Recorded with a gap, the currency would read as not offered at that rate: it is left out instead.
			decimal? buyCash, buyTransfer, sell;
			try
			{
				buyCash = Rate(RateColumn.BuyCash);
				buyTransfer = Rate(RateColumn.BuyTransfer);
				sell = Rate(RateColumn.Sell) ?? Rate(RateColumn.SellTransfer) ?? Rate(RateColumn.SellCash);
			}
			catch (FormatException ex)
			{
				skipped?.Add(ex.Message);
				continue;
			}

			if (buyCash is null && buyTransfer is null && sell is null)
				continue;

			rates.Add(new CurrencyRateBank(code, date, local, buyCash, buyTransfer, sell));
		}

		return rates;
	}

	private int IndexOf(RateColumn column)
	{
		for (var i = 0; i < Columns.Count; i++)
			if (Columns[i] == column)
				return i;
		return int.MaxValue;
	}

	/// <summary>"USD", "USD100" (OCB's $100 notes), or the code in brackets: "Đô la Mỹ (USD)".</summary>
	private static string? CurrencyOf(string cell)
	{
		var match = CurrencyCell().Match(cell.Trim());
		return match.Success ? match.Groups["code"].Value : null;
	}

	// "25.470", "# 25.470", "25,640.00", "158,61"; "", "-", "N/A" and 0 mean not offered.
	private decimal? ParseRate(string cell, string code, RateColumn column)
	{
		var text = cell.Trim().TrimStart('#', '&').Replace(" ", "").Replace(" ", "");
		if (text is "" or "-" or "N/A")
			return null;

		text = _format == RateNumberFormat.Dot
			? text.Replace(".", "").Replace(',', '.')
			: text.Replace(",", "");

		if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var rate))
			throw new FormatException($"{Bic} {code} {column} is not a number: '{cell}'");

		if (rate <= 0)
			return null;
		return _per100.Contains(code) ? rate / 100 : rate;
	}

	/// <summary>The ContentSelector values as text (a number keeps its written form: 25.470 stays "25.470").</summary>
	private List<string> ReadCells(string extractedJson)
	{
		if (string.IsNullOrWhiteSpace(extractedJson))
			throw new FormatException("The data is empty");

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(extractedJson);
		}
		catch (JsonException ex)
		{
			throw new FormatException($"The data is not valid JSON: {ex.Message}", ex);
		}

		using (document)
		{
			if (document.RootElement.ValueKind != JsonValueKind.Object
				|| !document.RootElement.TryGetProperty("ContentSelector", out var content)
				|| content.ValueKind != JsonValueKind.Array)
				throw new FormatException($"Expected {{\"ContentSelector\": [cells]}} for {Bic}");

			return [.. content.EnumerateArray().Select(cell => cell.ValueKind switch
			{
				JsonValueKind.String => cell.GetString() ?? "",
				JsonValueKind.Number => cell.GetRawText(),
				_ => "",
			})];
		}
	}

	[GeneratedRegex(@"^(?<code>[A-Z]{3})\d*$|\((?<code>[A-Z]{3})\)")]
	private static partial Regex CurrencyCell();
}
