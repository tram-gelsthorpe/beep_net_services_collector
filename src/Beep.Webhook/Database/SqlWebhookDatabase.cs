using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Beep.Webhook.Database
{
	public sealed class SqlWebhookDatabase : IWebhookDatabase
	{
		private readonly DbConnection _dbConnection;
		private readonly string _databaseName;
		private string? _schemaScript;

		public SqlWebhookDatabase(string connectionString)
		{
			_databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
			_dbConnection = new SqlConnection(new SqlConnectionStringBuilder(connectionString)
			{
				InitialCatalog = "master"
			}
			.ToString());
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
				// Allow exceptions to propagate to the caller for handling
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
			var command = _dbConnection.CreateCommand();
			command.CommandText = Create_Database_Script;
			command.Parameters.Add(new SqlParameter("@databaseName", _databaseName));

			var result = await command.ExecuteNonQueryAsync(cancellationToken);
			return result > 0;
		}

		private async Task Create_Table(CancellationToken cancellationToken = default)
		{
			_dbConnection.ChangeDatabase(_databaseName);

			var command = _dbConnection.CreateCommand();
			command.CommandText = LoadCreateSchemaResource();

			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		private async Task Update(int currentVersion, CancellationToken cancellationToken = default)
		{
			_dbConnection.ChangeDatabase(_databaseName);

			var transaction = _dbConnection.BeginTransaction();

			try
			{
				var version = currentVersion + 1;
				var maxVersion = GetEmbeddedResourceCount();

				while (version <= maxVersion)
				{
					var scriptContent = LoadUpdateSchemaResource(version);
					if (string.IsNullOrWhiteSpace(scriptContent))
						break;

					var command = _dbConnection.CreateCommand();
					command.CommandText = scriptContent;
					command.Transaction = transaction;

					await command.ExecuteNonQueryAsync(cancellationToken);
				}

				await UpdateCurrentDatabaseVersion(version);

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
				.Count(r => r.StartsWith("Beep.ServiceBus.Resource"));

			return resourceCount;
		}

		private async Task<int> GetCurrentDatabaseVersion()
		{
			var command = _dbConnection.CreateCommand();
			command.CommandText = "SELECT TOP 1 Version FROM dbo.DatabaseVersion ORDER BY Id DESC";

			var result = await command.ExecuteScalarAsync();

			Int32.TryParse(result?.ToString(), out int version);
			return version;
		}

		private async Task<int> UpdateCurrentDatabaseVersion(int version)
		{
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
			var resourceName = $"Beep.WebhookBus.Resource.0.sql";

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
			var resourceName = $"Beep.WebhookBus.Resource.{version}.sql";
			var result = "";

			using (var stream = assembly.GetManifestResourceStream(resourceName))
			{
				if (stream == null)
					throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

				using (var reader = new StreamReader(stream))
				{
					result = reader.ReadToEnd();
				}
			}

			return result;
		}

		// Placeholder for Create_Database_Script constant
		// This needs to be defined separately if it's used in Create_Database method
		private const string Create_Database_Script = @"
			IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = @databaseName)
			BEGIN
				CREATE DATABASE [@databaseName];
			END
		";
	}
}
