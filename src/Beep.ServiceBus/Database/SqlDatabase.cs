using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Beep.ServiceBus.Database
{
	public sealed class SqlDatabase : IDatabase
	{
		private readonly DbConnection _dbConnection;
		private readonly string _databaseName;
		private string? _schemaScript;

		public SqlDatabase(string connectionString)
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
					await Update(GetCurrentDatabaseVersion(), cancellationToken);
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
			command.CommandText = LoadSchemaResource("0");

			await command.ExecuteNonQueryAsync(cancellationToken);
		}

		private async Task Update(int currentVersion, CancellationToken cancellationToken = default)
		{
			_dbConnection.ChangeDatabase(_databaseName);

			var transaction = _dbConnection.BeginTransaction();

			try
			{
				for (int version = currentVersion + 1; version <= 0; version++)
				{
					var scriptContent = LoadSchemaResource($"{version}");
					if (string.IsNullOrWhiteSpace(scriptContent))
						continue;

					var command = _dbConnection.CreateCommand();
					command.CommandText = scriptContent;
					command.Transaction = transaction;

					await command.ExecuteNonQueryAsync(cancellationToken);
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

		private int GetCurrentDatabaseVersion()
		{
			var command = _dbConnection.CreateCommand();
			command.CommandText = "SELECT TOP 1 Version FROM dbo.DatabaseVersion ORDER BY Id DESC";

			var result = command.ExecuteScalar();
			return int.TryParse(result?.ToString(), out var version) ? version : 0;
		}

		private string LoadSchemaResource(string name)
		{
			if (_schemaScript != null)
				return _schemaScript;

			var assembly = Assembly.GetExecutingAssembly();
			var resourceName = $"Beep.ServiceBus.Resource.{name}.sql";

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
