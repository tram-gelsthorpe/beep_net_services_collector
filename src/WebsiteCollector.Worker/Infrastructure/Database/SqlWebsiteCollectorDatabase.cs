using System.Data;
using System.Data.Common;
using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace WebsiteCollector.Worker
{
	public sealed class SqlWebsiteCollectorDatabase : IWebsiteCollectorDatabase
	{
		private const string Create_Database_Script = """
			IF NOT EXISTS (SELECT TOP 1 * FROM sys.databases WHERE name = @databaseName)
			BEGIN
				DECLARE @sql NVARCHAR(MAX);

				SET @sql = N'CREATE DATABASE ' + QUOTENAME(@databaseName);

				EXEC sp_executesql @sql;

				SELECT 1 AS Success;
			END
			ELSE
			BEGIN
				SELECT 0 AS Success;
			END;
			""";

		private const string Drop_Database_Script = """
			USE master;

			DECLARE @SQL NVARCHAR(MAX);

			IF DB_ID(@DatabaseName) IS NOT NULL
			BEGIN
				SET @SQL = N'
					ALTER DATABASE ' + QUOTENAME(@DatabaseName) + N'
					SET SINGLE_USER WITH ROLLBACK IMMEDIATE;

					DROP DATABASE ' + QUOTENAME(@DatabaseName) + N';';

				EXEC sp_executesql @SQL;
			END
			""";

		private const string _prefix = "Beep.WebsiteCollector.Worker.Resource";
		private readonly DbConnection _dbConnection;
		private readonly string _databaseName;
		private readonly ILogger _logger;
		private string? _schemaScript;
		
		public SqlWebsiteCollectorDatabase(string connectionString, ILogger logger)
		{
			_databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
			_dbConnection = new SqlConnection(new SqlConnectionStringBuilder(connectionString)
			{
				InitialCatalog = "master"
			}
			.ToString());

			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		public async Task Create(CancellationToken cancellationToken = default)
		{
			ConnectionState originalState = _dbConnection.State;

			if (originalState != ConnectionState.Open)
				await _dbConnection.OpenAsync(cancellationToken);

			try
			{
				if (await Create_Database(cancellationToken))
				{
					await Create_Table(cancellationToken);
				}
				else 
				{
					await Update(await GetCurrentDatabaseVersion(), cancellationToken);
				}
			}
			catch
			{
				_logger.LogError("An error occurred while creating '{DatabaseName}' database.", _databaseName);

				throw;
			}
			finally
			{
				if (originalState == ConnectionState.Closed)
					await _dbConnection.CloseAsync();
			}
		}

		private async Task<bool> Create_Database(CancellationToken cancellationToken = default)
		{
			try
			{
				var command = _dbConnection.CreateCommand();
				command.CommandText = Create_Database_Script;
				command.Parameters.Add(new SqlParameter("@databaseName", SqlDbType.NVarChar, 128) { Value = _databaseName });

				var result = await command.ExecuteScalarAsync(cancellationToken);
				return Convert.ToInt32(result) > 0;
			}
			catch (SqlException ex) when (ex.Number == 1801) // Database already exists
			{
				_logger.LogInformation("Database '{DatabaseName}' already exists.", _databaseName);
				return false;
			}
			catch
			{
				_logger.LogError("An error occurred while creating '{DatabaseName}' database.", _databaseName);

				await Drop_Database();

				throw;
			}
		}

		private async Task<bool> Drop_Database(CancellationToken cancellationToken = default)
		{
			try
			{
				var command = _dbConnection.CreateCommand();
				command.CommandText = Drop_Database_Script;
				command.Parameters.Add(new SqlParameter("@databaseName", SqlDbType.NVarChar, 128) { Value = _databaseName });

				await command.ExecuteNonQueryAsync(cancellationToken);
				return true;
			}
			catch
			{
				_logger.LogError("An error occurred while '{DatabaseName}' database.", _databaseName);
				throw;
			}
		}

		private async Task Create_Table(CancellationToken cancellationToken = default)
		{
			try
			{
				_dbConnection.ChangeDatabase(_databaseName);

				var command = _dbConnection.CreateCommand();
				command.CommandText = LoadCreateSchemaResource();

				await command.ExecuteNonQueryAsync(cancellationToken);
			}
			catch
			{
				_logger.LogError("An error occurred while '{DatabaseName}' tables.", _databaseName);

				await Drop_Database();

				throw;
			}
		}

		private async Task Update(int currentVersion, CancellationToken cancellationToken = default)
		{
			_dbConnection.ChangeDatabase(_databaseName);

			var transaction = _dbConnection.BeginTransaction();

			try
			{
				var version = currentVersion + 1;
				var maxVersion = GetEmbeddedResourceCount();

				while (version < maxVersion)
				{
					var scriptContent = LoadUpdateSchemaResource(version);
					if (string.IsNullOrWhiteSpace(scriptContent))
						break;

					var command = _dbConnection.CreateCommand();
					command.CommandText = scriptContent;
					command.Transaction = transaction;

					await command.ExecuteNonQueryAsync(cancellationToken);
				}

				if (version != maxVersion)
				{
					await UpdateCurrentDatabaseVersion(version);
				}

				transaction.Commit();
			}
			catch
			{
				transaction.Rollback();
				throw;
			}
			finally
			{
				transaction.Dispose();
			}
		}

		private int GetEmbeddedResourceCount()
		{
			var assembly = Assembly.GetExecutingAssembly();
			var resourceCount = assembly
				.GetManifestResourceNames()
				.Count(r => r.StartsWith(_prefix));

			return resourceCount;
		}

		private async Task<int> GetCurrentDatabaseVersion()
		{
			_dbConnection.ChangeDatabase(_databaseName);

			var command = _dbConnection.CreateCommand();
			command.CommandText = "SELECT TOP 1 Version FROM dbo.DatabaseVersion ORDER BY Id DESC";

			var result = await command.ExecuteScalarAsync();

			Int32.TryParse(result?.ToString(), out int version);
			return version;
		}

		private async Task<int> UpdateCurrentDatabaseVersion(int version)
		{
			_dbConnection.ChangeDatabase(_databaseName);

			var command = _dbConnection.CreateCommand();
			command.CommandText = "INSERT INTO dbo.DatabaseVersion (Version) VALUES (@version); SELECT SCOPE_IDENTITY();";
			command.Parameters.Add(new SqlParameter("@version", version));
			
			var result = await command.ExecuteScalarAsync();
			return int.TryParse(result?.ToString(), out var versionId) ? versionId : 0;
		}

		private string LoadCreateSchemaResource()
		{
			if (_schemaScript != null)
				return _schemaScript;

			var assembly = Assembly.GetExecutingAssembly();
			var resourceName = $"{_prefix}.0.sql";

			using (var stream = assembly.GetManifestResourceStream(resourceName))
			{
				if (stream == null)
					throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

				using (var reader = new StreamReader(stream))
				{
					_schemaScript = reader.ReadToEnd();
				}
			}

			return _schemaScript;
		}

		private string LoadUpdateSchemaResource(int version)
		{
			var assembly = Assembly.GetExecutingAssembly();
			var resourceName = $"{_prefix}.{version}.sql";
			var result = "";

			using (var stream = assembly.GetManifestResourceStream(resourceName))
			{
				if (stream == null)
					return string.Empty;

				using (var reader = new StreamReader(stream))
				{
					result = reader.ReadToEnd();
				}
			}

			return result;
		}
	}
}
