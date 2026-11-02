using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using WebsiteCollector.Features.WebsiteCollection.Models;

namespace WebsiteCollector.Infrastructure.Database;

public sealed class Database(IConfiguration config)
{
	private SqlConnection Open() => new(config.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task<IReadOnlyList<ClaimedWebsiteSelector>> ClaimAsync(Guid worker, int count, int lease, CancellationToken ct)
	{
		await using var conn = Open();
		var rows = await conn.QueryAsync<ClaimedWebsiteSelector>(new CommandDefinition("dbo.ClaimWebsiteSelectors", new { WorkerId = worker, BatchSize = count, LeaseSeconds = lease }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
		return rows.AsList();
	}

	public async Task StartAsync(Guid websiteSelectorsId, Guid leaseToken, Guid worker, CancellationToken ct)
	{
		await using var conn = Open();
		await conn.ExecuteAsync(new CommandDefinition("dbo.StartWebsiteSelectorExecution", new { WebsiteSelectorsId = websiteSelectorsId, LeaseToken = leaseToken, WorkerId = worker }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
	}

	public async Task<bool> RenewAsync(Guid websiteSelectorsId, Guid leaseToken, int leaseSeconds, CancellationToken ct)
	{
		await using var conn = Open();
		var result = await conn.ExecuteAsync(new CommandDefinition("dbo.RenewWebsiteSelectorLease", new { WebsiteSelectorsId = websiteSelectorsId, LeaseToken = leaseToken, LeaseSeconds = leaseSeconds }, commandType: CommandType.StoredProcedure, cancellationToken: ct));
		return result == 1;
	}

	public async Task<IReadOnlyList<WebsiteSelector>> SelectorsAsync(Guid websiteSelectorsId, CancellationToken ct)
	{
		await using var conn = Open();
		const string sql = """
			SELECT Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector, IntervalSeconds, IsEnabled 
			FROM dbo.WebsiteSelectors 
			WHERE Id = @WebsiteSelectorsId AND IsEnabled = 1
			""";
		var rows = await conn.QueryAsync<WebsiteSelector>(new CommandDefinition(sql, new { WebsiteSelectorsId = websiteSelectorsId }, cancellationToken: ct));
		return rows.AsList();
	}

	public async Task<int> FinishAsync(Guid websiteSelectorsId, Guid leaseToken, bool success, int? httpStatus, string? error, byte[]? hash, string? raw, string? extracted, CancellationToken ct)
	{
		await using var conn = Open();
		var p = new DynamicParameters();
		p.Add("WebsiteSelectorsId", websiteSelectorsId);
		p.Add("LeaseToken", leaseToken);
		p.Add("Succeeded", success);
		p.Add("HttpStatus", httpStatus);
		p.Add("Error", error);
		p.Add("ContentHash", hash, DbType.Binary, size: 32);
		p.Add("ResponseContent", raw);
		p.Add("ExtractedJson", extracted);
		p.Add("ReturnValue", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);
		await conn.ExecuteAsync(new CommandDefinition("dbo.CompleteWebsiteSelector", p, commandType: CommandType.StoredProcedure, cancellationToken: ct));
		return p.Get<int>("ReturnValue");
	}
}
