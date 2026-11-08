using Dapper;
using Microsoft.Data.SqlClient;
using WebsiteCollector.Worker.Features.WebsiteCollector.Models;

namespace WebsiteCollector.Api.Repositories;

/// <summary>
/// SQL-based implementation of selector repository.
/// </summary>
public sealed class SelectorRepository : ISelectorRepository
{
	private readonly IConfiguration _configuration;

	public SelectorRepository(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	private SqlConnection Open() => 
		new(_configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task<IReadOnlyList<WebsiteSelector>> GetSelectorsByWebsiteIdAsync(Guid websiteId, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			SELECT Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector, IntervalSeconds, IsEnabled
			FROM dbo.WebsiteSelectors 
			WHERE WebsitesId = @WebsiteId AND IsEnabled = 1
			ORDER BY CollectionUrl";

		var selectors = await connection.QueryAsync<WebsiteSelector>(sql, new { WebsiteId = websiteId });
		return selectors.ToList();
	}

	public async Task<WebsiteSelector?> GetSelectorByIdAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			SELECT Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector, IntervalSeconds, IsEnabled
			FROM dbo.WebsiteSelectors 
			WHERE Id = @Id";

		return await connection.QueryFirstOrDefaultAsync<WebsiteSelector>(sql, new { Id = id });
	}

	public async Task<Guid> CreateSelectorAsync(WebsiteSelector selector, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			INSERT INTO dbo.WebsiteSelectors (Id, WebsitesId, CollectionUrl, CollectionType, ContentSelector, TableSelector, IntervalSeconds, IsEnabled, CreatedAt)
			VALUES (@Id, @WebsitesId, @CollectionUrl, @CollectionType, @ContentSelector, @TableSelector, @IntervalSeconds, @IsEnabled, SYSUTCDATETIME());

			INSERT INTO dbo.WebsiteSelectorsMonitor (Id, NextRunAt, TimeoutSeconds, MaxAttempts, AttemptCount, CreatedAt)
			VALUES (@Id, SYSUTCDATETIME(), 30, 5, 0, SYSUTCDATETIME());

			SELECT @Id";

		await connection.ExecuteAsync(sql, new 
		{ 
			selector.Id,
			selector.WebsitesId,
			selector.CollectionUrl,
			selector.CollectionType,
			selector.ContentSelector,
			selector.TableSelector,
			selector.IntervalSeconds,
			selector.IsEnabled
		});

		return selector.Id;
	}

	public async Task UpdateSelectorAsync(WebsiteSelector selector, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			UPDATE dbo.WebsiteSelectors 
			SET CollectionUrl = @CollectionUrl, CollectionType = @CollectionType, 
				ContentSelector = @ContentSelector, TableSelector = @TableSelector, 
				IntervalSeconds = @IntervalSeconds, IsEnabled = @IsEnabled, UpdatedAt = SYSUTCDATETIME()
			WHERE Id = @Id";

		await connection.ExecuteAsync(sql, new 
		{ 
			selector.Id,
			selector.CollectionUrl,
			selector.CollectionType,
			selector.ContentSelector,
			selector.TableSelector,
			selector.IntervalSeconds,
			selector.IsEnabled
		});
	}

	public async Task DeleteSelectorAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = "UPDATE dbo.WebsiteSelectors SET IsEnabled = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id";
		await connection.ExecuteAsync(sql, new { Id = id });
	}

	public async Task EnableSelectorAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = "UPDATE dbo.WebsiteSelectors SET IsEnabled = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id";
		await connection.ExecuteAsync(sql, new { Id = id });
	}

	public async Task DisableSelectorAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = "UPDATE dbo.WebsiteSelectors SET IsEnabled = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id";
		await connection.ExecuteAsync(sql, new { Id = id });
	}
}
