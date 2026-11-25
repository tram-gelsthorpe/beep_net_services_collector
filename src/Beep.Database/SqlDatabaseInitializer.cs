using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Beep.Database;

/// <summary>
/// Creates a SQL Server database from the <c>Resource/0.sql</c> script embedded in a given
/// assembly, then applies upgrade scripts <c>Resource/1.sql</c>, <c>Resource/2.sql</c>, ...
/// that are newer than the version recorded in <c>{schema}.DatabaseVersion</c>.
/// </summary>
/// <remarks>
/// A project that owns a schema embeds its scripts with
/// <c>&lt;EmbeddedResource Include="Resource\*.sql" /&gt;</c>. Each script must create a
/// <c>DatabaseVersion (Id, Version, CreatedAt)</c> table in 0.sql and be a single batch
/// (no <c>GO</c>; wrap DDL such as procedures in <c>EXEC(N'...')</c>).
/// </remarks>
/// <example>
/// <code>
/// var initializer = new SqlDatabaseInitializer(connectionString, typeof(WorkItemRepository).Assembly, logger);
/// await initializer.CreateOrUpdateAsync("ServiceBus");
/// </code>
/// </example>
public sealed class SqlDatabaseInitializer : IDatabaseInitializer
{
	private const string CreateDatabaseScript = """
		IF DB_ID(@databaseName) IS NULL
		BEGIN
			DECLARE @sql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@databaseName);
			EXEC sp_executesql @sql;
			SELECT 1;
		END
		ELSE
			SELECT 0;
		""";

	private const string DropDatabaseScript = """
		IF DB_ID(@databaseName) IS NOT NULL
		BEGIN
			DECLARE @sql NVARCHAR(MAX) =
				N'ALTER DATABASE ' + QUOTENAME(@databaseName) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ' +
				N'DROP DATABASE ' + QUOTENAME(@databaseName) + N';';
			EXEC sp_executesql @sql;
		END
		""";

	private const string CreateSchemaScript = """
		IF SCHEMA_ID(@schemaName) IS NULL
		BEGIN
			DECLARE @sql NVARCHAR(MAX) = N'CREATE SCHEMA ' + QUOTENAME(@schemaName);
			EXEC sp_executesql @sql;
		END
		""";

	private readonly string _masterConnectionString;
	private readonly string _databaseName;
	private readonly string _scriptSchema;
	private readonly Assembly _assembly;
	private readonly string _resourcePrefix;
	private readonly ILogger _logger;

	/// <param name="connectionString">Connection string whose Initial Catalog is the database to create.</param>
	/// <param name="scriptAssembly">Assembly that embeds the <c>Resource/{n}.sql</c> scripts.</param>
	/// <param name="logger">Logger.</param>
	/// <param name="scriptSchema">
	/// The schema name the scripts are written against. References to it are rewritten to the
	/// schema passed to <see cref="CreateOrUpdateAsync"/>.
	/// </param>
	public SqlDatabaseInitializer(string connectionString, Assembly scriptAssembly, ILogger logger, string scriptSchema = "dbo")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
		ArgumentNullException.ThrowIfNull(scriptAssembly);
		ArgumentException.ThrowIfNullOrWhiteSpace(scriptSchema);

		var builder = new SqlConnectionStringBuilder(connectionString);
		_databaseName = string.IsNullOrWhiteSpace(builder.InitialCatalog)
			? throw new ArgumentException("The connection string must specify a database (Initial Catalog).", nameof(connectionString))
			: builder.InitialCatalog;

		builder.InitialCatalog = "master";
		_masterConnectionString = builder.ConnectionString;

		_assembly = scriptAssembly;
		_scriptSchema = scriptSchema;
		_resourcePrefix = $"{_assembly.GetName().Name}.Resource.";
		_logger = logger ?? throw new ArgumentNullException(nameof(logger));
	}

	/// <inheritdoc />
	public async Task CreateOrUpdateAsync(string schema, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(schema);

		await using var connection = new SqlConnection(_masterConnectionString);
		await connection.OpenAsync(cancellationToken);

		if (await CreateDatabaseAsync(connection, cancellationToken))
		{
			try
			{
				await connection.ChangeDatabaseAsync(_databaseName, cancellationToken);
				await ExecuteAsync(connection, null, CreateSchemaScript, cancellationToken, ("@schemaName", schema));
				await ExecuteAsync(connection, null, LoadScript(0, schema), cancellationToken);
				await ApplyUpgradesAsync(connection, schema, currentVersion: 0, cancellationToken);

				_logger.LogInformation("Created database '{DatabaseName}' (schema '{Schema}')", _databaseName, schema);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "Failed to create objects in new database '{DatabaseName}'; dropping it", _databaseName);
				await connection.ChangeDatabaseAsync("master", CancellationToken.None);
				await ExecuteAsync(connection, null, DropDatabaseScript, CancellationToken.None, ("@databaseName", _databaseName));
				throw;
			}
		}
		else
		{
			await connection.ChangeDatabaseAsync(_databaseName, cancellationToken);
			var currentVersion = await GetCurrentVersionAsync(connection, schema, cancellationToken);
			await ApplyUpgradesAsync(connection, schema, currentVersion, cancellationToken);
		}
	}

	private async Task<bool> CreateDatabaseAsync(SqlConnection connection, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = CreateDatabaseScript;
		command.Parameters.AddWithValue("@databaseName", _databaseName);

		var result = await command.ExecuteScalarAsync(cancellationToken);
		return Convert.ToInt32(result) == 1;
	}

	private async Task ApplyUpgradesAsync(SqlConnection connection, string schema, int currentVersion, CancellationToken cancellationToken)
	{
		var latestVersion = GetLatestScriptVersion();
		if (currentVersion >= latestVersion)
			return;

		await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
		try
		{
			for (var version = currentVersion + 1; version <= latestVersion; version++)
			{
				_logger.LogInformation("Applying upgrade script {Version} to '{DatabaseName}'", version, _databaseName);
				await ExecuteAsync(connection, transaction, LoadScript(version, schema), cancellationToken);
			}

			await ExecuteAsync(
				connection,
				transaction,
				$"INSERT INTO {Quote(schema)}.DatabaseVersion (Version) VALUES (@version);",
				cancellationToken,
				("@version", latestVersion.ToString()));

			await transaction.CommitAsync(cancellationToken);
		}
		catch
		{
			await transaction.RollbackAsync(CancellationToken.None);
			throw;
		}
	}

	private static async Task<int> GetCurrentVersionAsync(SqlConnection connection, string schema, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = $"SELECT TOP 1 Version FROM {Quote(schema)}.DatabaseVersion ORDER BY CreatedAt DESC";

		var result = await command.ExecuteScalarAsync(cancellationToken);
		return int.TryParse(result?.ToString(), out var version) ? version : 0;
	}

	/// <summary>Highest N such that Resource/0.sql .. Resource/N.sql all exist.</summary>
	private int GetLatestScriptVersion()
	{
		var version = 0;
		while (_assembly.GetManifestResourceInfo(ResourceName(version + 1)) != null)
			version++;
		return version;
	}

	private string LoadScript(int version, string schema)
	{
		var resourceName = ResourceName(version);
		using var stream = _assembly.GetManifestResourceStream(resourceName)
			?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
		using var reader = new StreamReader(stream);

		return ReplaceSchema(reader.ReadToEnd(), schema);
	}

	private string ResourceName(int version) => $"{_resourcePrefix}{version}.sql";

	/// <summary>
	/// Rewrites references to the schema the scripts were written against
	/// (<c>[dbo].</c>, <c>dbo.</c>, including inside <c>EXEC(N'...')</c> strings) to the target schema.
	/// </summary>
	private string ReplaceSchema(string script, string schema)
	{
		if (string.Equals(schema, _scriptSchema, StringComparison.OrdinalIgnoreCase))
			return script;

		script = script.Replace($"[{_scriptSchema}].", $"[{schema}].", StringComparison.OrdinalIgnoreCase);
		return Regex.Replace(
			script,
			$@"\b{Regex.Escape(_scriptSchema)}\.(?=[\w\[])",
			$"{schema}.",
			RegexOptions.IgnoreCase);
	}

	private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

	private static async Task ExecuteAsync(
		SqlConnection connection,
		SqlTransaction? transaction,
		string sql,
		CancellationToken cancellationToken,
		params (string Name, object Value)[] parameters)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		command.Transaction = transaction;
		foreach (var (name, value) in parameters)
			command.Parameters.AddWithValue(name, value);

		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}
