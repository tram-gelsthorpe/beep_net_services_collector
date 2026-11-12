using Microsoft.Data.SqlClient;
using static WebsiteCollector.Api.Controllers.CurrencyRateController;

namespace WebsiteCollector.Api.Repositories;

/// <summary>
/// Repository for currency rate operations.
/// </summary>
public interface ICurrencyRateRepository
{
	Task<bool> RecordVietcombankCurrencyRate(RecordDeliveryResultRequest request, CancellationToken cancellationToken);
}

public sealed class CurrencyRateRepository : ICurrencyRateRepository
{
	private readonly IConfiguration _configuration;
	public CurrencyRateRepository(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	private SqlConnection Open() =>
		new(_configuration.GetConnectionString("Currency") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task<bool> RecordVietcombankCurrencyRate(RecordDeliveryResultRequest request, CancellationToken cancellationToken)
	{
		return true;
	}
}
