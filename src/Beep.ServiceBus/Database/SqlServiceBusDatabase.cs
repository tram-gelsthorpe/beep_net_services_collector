using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Beep.ServiceBus;

public sealed class SqlServiceBusDatabase : IServiceBusDatabase
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

	private const string _prefix = "Beep.ServiceBus.Resource";
	private readonly DbConnection _dbConnection;
	private readonly string _databaseName;
	private readonly ILogger _logger;
	private string? _schemaScript;
	private string _schemaName = "dbo"; // Store schema name, default to dbo

	public SqlServiceBusDatabase(string connectionString, ILogger logger)
	{
		_databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
		_dbConnection = new SqlConnection(new SqlConnectionStringBuilder(connectionString)
		{
			InitialCatalog = "master"
		}
		.ToString());

		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	public async Task Create(string schema = "dbo", CancellationToken cancellationToken = default)
	{
		_schemaName = schema; // Store the schema name for use in other methods

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
			_logger.LogError("An error occurred while dropping '{DatabaseName}' database.", _databaseName);
			throw;
		}
	}

	private async Task Create_Table(CancellationToken cancellationToken = default)
	{
		try
		{
			_dbConnection.ChangeDatabase(_databaseName);

			// First, create the schema if it doesn't exist (only for non-dbo schemas)
			if (_schemaName != "dbo")
			{
				await CreateSchemaIfNotExists(cancellationToken);
			}

			var sqlScript = LoadCreateSchemaResource();

			// Replace hardcoded dbo schema name with the actual schema name
			// This handles both regular SQL and SQL embedded in EXEC(N'...') statements
			sqlScript = ReplaceSchemaNamesInScript(sqlScript);

			var command = _dbConnection.CreateCommand();
			command.CommandText = sqlScript;

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch
		{
			_logger.LogError("An error occurred while creating '{DatabaseName}' tables.", _databaseName);

			await Drop_Database();

			throw;
		}
	}

	private string ReplaceSchemaNamesInScript(string script)
	{
		// Replace [dbo]. with [SchemaName].
		script = script.Replace("[dbo].", $"[{_schemaName}].");

		// Replace dbo. with SchemaName. but avoid replacing within other contexts
		// Use a regex to avoid replacing inside comments or other special contexts
		// Replace dbo. when followed by a valid SQL identifier character
		script = System.Text.RegularExpressions.Regex.Replace(
			script,
			@"\bdbo\.(?=[\w\[\`])",
			$"{_schemaName}.",
			System.Text.RegularExpressions.RegexOptions.IgnoreCase
		);

		// Also handle the escaped quote variant ''dbo.'' which appears in EXEC(N'...') strings
		script = script.Replace("''dbo.", $"''{_schemaName}.");
		script = script.Replace("''[dbo].", $"''[{_schemaName}].");

		return script;
	}

	private async Task CreateSchemaIfNotExists(CancellationToken cancellationToken = default)
	{
		try
		{
			var command = _dbConnection.CreateCommand();
			command.CommandText = @"
				IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = @schemaName)
				BEGIN
					DECLARE @createSchema NVARCHAR(MAX) = N'CREATE SCHEMA ' + QUOTENAME(@schemaName);
					EXEC sp_executesql @createSchema;
				END;
			";

			var parameter = command.CreateParameter();
			parameter.ParameterName = "@schemaName";
			parameter.Value = _schemaName;
			command.Parameters.Add(parameter);

			await command.ExecuteNonQueryAsync(cancellationToken);
		}
		catch
		{
			_logger.LogError("An error occurred while creating schema '{SchemaName}' in database '{DatabaseName}'.", _schemaName, _databaseName);
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
		command.CommandText = "SELECT TOP 1 Version FROM DatabaseVersion ORDER BY Id DESC";

		var result = await command.ExecuteScalarAsync();

		Int32.TryParse(result?.ToString(), out int version);
		return version;
	}

	private async Task<int> UpdateCurrentDatabaseVersion(int version)
	{
		_dbConnection.ChangeDatabase(_databaseName);

		var command = _dbConnection.CreateCommand();
		command.CommandText = "INSERT INTO DatabaseVersion (Version) VALUES (@version); SELECT SCOPE_IDENTITY();";
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
