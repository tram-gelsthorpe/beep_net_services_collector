using Dapper;
using Microsoft.Data.SqlClient;
using WebsiteCollector.Worker.Features.WebsiteCollector.Models;

namespace WebsiteCollector.Api.Repositories;

/// <summary>
/// SQL-based implementation of website repository.
/// </summary>
public sealed class WebsiteRepository : IWebsiteRepository
{
	private readonly IConfiguration _configuration;

	public WebsiteRepository(IConfiguration configuration)
	{
		_configuration = configuration;
	}

	private SqlConnection Open() => 
		new(_configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("Missing Default connection string"));

	public async Task<IReadOnlyList<Website>> GetAllWebsitesAsync(CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = "SELECT Id, Name, Url, IsEnabled FROM dbo.Websites WHERE IsEnabled = 1 ORDER BY Name";
		var websites = await connection.QueryAsync<Website>(sql);
		return websites.ToList();
	}

	public async Task<Website?> GetWebsiteByIdAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = "SELECT Id, Name, Url, IsEnabled FROM dbo.Websites WHERE Id = @Id";
		return await connection.QueryFirstOrDefaultAsync<Website>(sql, new { Id = id });
	}

	public async Task<Guid> CreateWebsiteAsync(Website website, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			INSERT INTO dbo.Websites (Id, Name, Url, IsEnabled, CreatedAt)
			VALUES (@Id, @Name, @Url, @IsEnabled, SYSUTCDATETIME());
			SELECT @Id;";

		await connection.ExecuteAsync(sql, new 
		{ 
			website.Id, 
			website.Name, 
			website.Url, 
			website.IsEnabled 
		});

		return website.Id;
	}

	public async Task UpdateWebsiteAsync(Website website, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = @"
			UPDATE dbo.Websites 
			SET Name = @Name, Url = @Url, IsEnabled = @IsEnabled, UpdatedAt = SYSUTCDATETIME()
			WHERE Id = @Id";

		await connection.ExecuteAsync(sql, new 
		{ 
			website.Id, 
			website.Name, 
			website.Url, 
			website.IsEnabled 
		});
	}

	public async Task DeleteWebsiteAsync(Guid id, CancellationToken cancellationToken)
	{
		await using var connection = Open();
		const string sql = "UPDATE dbo.Websites SET IsEnabled = 0, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id";
		await connection.ExecuteAsync(sql, new { Id = id });
	}
}
