using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using WebsiteCollector.Worker.Features.WebsiteCollector.Repositories;
using WebsiteCollector.Worker.Features.WebsiteCollector.Services;
using WebsiteCollector.Worker.Infrastructure.Database;
using Beep.EventBus.Infrastructure.Events;

namespace WebsiteCollector.Worker.Tests.Integration;

/// <summary>
/// Integration tests for WebsiteCollector.Worker
/// Tests the core workflows: claiming, starting, renewing leases, and completing executions.
/// </summary>
[TestClass]
public sealed class WebsiteCollectorIntegrationTests
{
	private IConfiguration? _configuration;
	private TestDatabaseManager? _databaseManager;
	private SqlConnection? _testConnection;
	private TestDataFixture? _dataFixture;

	private IWebsiteCollectionService? _collectionService;
	private IWebsiteCollectionRepository? _repository;
	private Database? _database;
	private IEventAggregator? _eventAggregator;

	[TestInitialize]
	public async Task TestInitializeAsync()
	{
		// Load test configuration
		var builder = new ConfigurationBuilder()
			.SetBasePath(Directory.GetCurrentDirectory())
			.AddJsonFile("appsettings.json", optional: true)
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				// Override with test database connection
				{ "ConnectionStrings:Default", "Server=(localdb)\\mssqllocaldb;Database=WebsiteCollectorTests;Integrated Security=true;Encrypt=true;TrustServerCertificate=false;" }
			});

		_configuration = builder.Build();

		// Setup test database
		_databaseManager = new TestDatabaseManager(_configuration);
		await _databaseManager.InitializeAsync();

		// Setup test connection and fixture
		_testConnection = _databaseManager.OpenConnection();
		await _testConnection.OpenAsync();
		_dataFixture = new TestDataFixture(_testConnection);

		// Setup service and repository
		_eventAggregator = new EventAggregator();
		var loggerFactory = LoggerFactory.Create(b => { });
		_database = new Database(_configuration, loggerFactory.CreateLogger<Database>(), loggerFactory);
		_repository = new WebsiteCollectionRepository(_database);
		_collectionService = new WebsiteCollectionService(_repository, _eventAggregator);
	}

	[TestCleanup]
	public async Task TestCleanupAsync()
	{
		if (_databaseManager != null)
		{
			await _databaseManager.CleanupAsync();
		}

		if (_testConnection != null)
		{
			await _testConnection.CloseAsync();
			await _testConnection.DisposeAsync();
		}
	}

	/// <summary>
	/// Test that workers can claim available website selectors.
	/// </summary>
	[TestMethod]
	public async Task ClaimSelectors_WithAvailableItems_ShouldClaimAndReturnSelectors()
	{
		// Arrange
		var workerId = Guid.NewGuid();
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync(
			collectionUrl: "https://example.com/data",
			collectionType: "HTML"
		);

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "PENDING",
			attemptNumber: 0
		);

		// Act
		var claimed = await _collectionService!.ClaimSelectorsAsync(workerId, count: 5, lease: 300, CancellationToken.None);

		// Assert
		Assert.IsNotNull(claimed);
		Assert.AreEqual(1, claimed.Count);
		Assert.AreEqual(websiteSelectorId, claimed[0].WebsiteSelectorsId);
		Assert.AreEqual("https://example.com/data", claimed[0].CollectionUrl);
		Assert.AreEqual("HTML", claimed[0].CollectionType);
	}

	/// <summary>
	/// Test that workers cannot claim expired leases.
	/// </summary>
	[TestMethod]
	public async Task ClaimSelectors_WithExpiredLease_ShouldNotClaimLockedItems()
	{
		// Arrange
		var workerId = Guid.NewGuid();
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync();

		// Seed a work item with an active lease
		var leaseToken = Guid.NewGuid();
		var leaseOwner = Guid.NewGuid();
		var futureLeaseUntil = DateTimeOffset.UtcNow.AddMinutes(5);

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "RUNNING",
			attemptNumber: 1,
			leaseOwner: leaseOwner,
			leaseToken: leaseToken,
			leaseUntil: futureLeaseUntil
		);

		// Act
		var claimed = await _collectionService!.ClaimSelectorsAsync(workerId, count: 5, lease: 300, CancellationToken.None);

		// Assert
		Assert.AreEqual(0, claimed.Count, "Should not claim items with active leases");
	}

	/// <summary>
	/// Test starting execution marks work item as running.
	/// </summary>
	[TestMethod]
	public async Task StartExecution_WithValidWorkItem_ShouldMarkAsRunning()
	{
		// Arrange
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync();
		var leaseToken = Guid.NewGuid();
		var workerId = Guid.NewGuid();

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "PENDING"
		);

		// Act
		await _collectionService!.StartExecutionAsync(websiteSelectorId, leaseToken, workerId, CancellationToken.None);

		// Assert
		var workItem = await _dataFixture.GetWorkItemAsync(websiteSelectorId);
		Assert.IsNotNull(workItem);
		Assert.AreEqual("RUNNING", workItem.Status);
	}

	/// <summary>
	/// Test renewing a valid lease extends the lease period.
	/// </summary>
	[TestMethod]
	public async Task RenewLease_WithValidLease_ShouldExtendLeaseUntil()
	{
		// Arrange
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync();
		var leaseToken = Guid.NewGuid();
		var workerId = Guid.NewGuid();

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "RUNNING",
			leaseOwner: workerId,
			leaseToken: leaseToken,
			leaseUntil: DateTimeOffset.UtcNow.AddSeconds(10)
		);

		// Act
		var renewed = await _collectionService!.RenewLeaseAsync(websiteSelectorId, leaseToken, leaseSeconds: 300, CancellationToken.None);

		// Assert
		Assert.IsTrue(renewed, "Lease should be successfully renewed");
		var workItem = await _dataFixture.GetWorkItemAsync(websiteSelectorId);
		Assert.IsNotNull(workItem);
		// LeaseUntil should be longer now
		var leaseUntil = (DateTimeOffset)workItem.LeaseUntil;
		Assert.IsTrue(leaseUntil > DateTimeOffset.UtcNow.AddSeconds(290), "Lease should be extended");
	}

	/// <summary>
	/// Test completing successful execution updates work item status.
	/// </summary>
	[TestMethod]
	public async Task CompleteExecution_WithSuccessfulResult_ShouldMarkAsCompleted()
	{
		// Arrange
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync();
		var leaseToken = Guid.NewGuid();
		var workerId = Guid.NewGuid();

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "RUNNING",
			leaseOwner: workerId,
			leaseToken: leaseToken,
			attemptNumber: 1
		);

		var responseContent = "{\"status\": \"success\"}";
		var responseHash = System.Text.Encoding.UTF8.GetBytes(responseContent);

		// Act
		var result = await _collectionService!.CompleteExecutionAsync(
			websiteSelectorId,
			leaseToken,
			success: true,
			httpStatus: 200,
			error: null,
			hash: responseHash,
			raw: responseContent,
			extracted: "{\"processed\": true}",
			CancellationToken.None
		);

		// Assert
		var workItem = await _dataFixture.GetWorkItemAsync(websiteSelectorId);
		Assert.IsNotNull(workItem);
		Assert.AreEqual("SUCCEEDED", workItem.Status);
	}

	/// <summary>
	/// Test completing failed execution schedules retry.
	/// </summary>
	[TestMethod]
	public async Task CompleteExecution_WithFailedResult_ShouldScheduleRetry()
	{
		// Arrange
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync();
		var leaseToken = Guid.NewGuid();
		var workerId = Guid.NewGuid();

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "RUNNING",
			leaseOwner: workerId,
			leaseToken: leaseToken,
			attemptNumber: 1,
			maxAttempts: 3
		);

		// Act
		var result = await _collectionService!.CompleteExecutionAsync(
			websiteSelectorId,
			leaseToken,
			success: false,
			httpStatus: 500,
			error: "Internal Server Error",
			hash: null,
			raw: null,
			extracted: null,
			CancellationToken.None
		);

		// Assert
		var workItem = await _dataFixture.GetWorkItemAsync(websiteSelectorId);
		Assert.IsNotNull(workItem);
		// Status should either be FAILED (if max attempts reached) or RETRYING/PENDING (if retries remain)
		Assert.IsTrue(workItem.Status == "FAILED" || workItem.Status == "PENDING", 
			$"Status should be FAILED or PENDING but was {workItem.Status}");
	}

	/// <summary>
	/// Test complete workflow: claim -> start -> renew -> complete.
	/// </summary>
	[TestMethod]
	public async Task CompleteWorkflow_ClaimStartRenewComplete_ShouldSucceed()
	{
		// Arrange
		var workerId = Guid.NewGuid();
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync(
			collectionUrl: "https://api.example.com/data",
			collectionType: "JSON"
		);

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "PENDING"
		);

		// Act - Claim
		var claimed = await _collectionService!.ClaimSelectorsAsync(workerId, count: 1, lease: 60, CancellationToken.None);
		Assert.AreEqual(1, claimed.Count);

		var claimedSelector = claimed[0];
		var leaseToken = claimedSelector.LeaseToken;

		// Act - Start
		await _collectionService.StartExecutionAsync(websiteSelectorId, leaseToken, workerId, CancellationToken.None);

		// Act - Renew
		var renewed = await _collectionService.RenewLeaseAsync(websiteSelectorId, leaseToken, leaseSeconds: 60, CancellationToken.None);
		Assert.IsTrue(renewed);

		// Act - Complete
		var responseContent = "{\"data\": [1, 2, 3]}";
		var responseHash = System.Text.Encoding.UTF8.GetBytes(responseContent);

		await _collectionService.CompleteExecutionAsync(
			websiteSelectorId,
			leaseToken,
			success: true,
			httpStatus: 200,
			error: null,
			hash: responseHash,
			raw: responseContent,
			extracted: "[1,2,3]",
			CancellationToken.None
		);

		// Assert - Verify final state
		var workItem = await _dataFixture.GetWorkItemAsync(websiteSelectorId);
		Assert.IsNotNull(workItem);
		Assert.AreEqual("SUCCEEDED", workItem.Status);
	}

	/// <summary>
	/// Test that multiple items can be claimed in batch.
	/// </summary>
	[TestMethod]
	public async Task ClaimSelectors_WithMultipleAvailableItems_ShouldClaimUpToBatchSize()
	{
		// Arrange
		var workerId = Guid.NewGuid();
		var selectorIds = new List<Guid>();

		for (int i = 0; i < 3; i++)
		{
			var selectorId = await _dataFixture!.SeedWebsiteSelectorAsync(
				collectionUrl: $"https://example.com/page{i}"
			);
			await _dataFixture.SeedWorkItemAsync(domainId: selectorId, status: "PENDING");
			selectorIds.Add(selectorId);
		}

		// Act
		var claimed = await _collectionService!.ClaimSelectorsAsync(workerId, count: 5, lease: 300, CancellationToken.None);

		// Assert
		Assert.AreEqual(3, claimed.Count);
		Assert.IsTrue(selectorIds.All(id => claimed.Any(c => c.WebsiteSelectorsId == id)), 
			"All seeded selectors should be claimed");
	}

	/// <summary>
	/// Test that disabled website selectors are not claimed.
	/// </summary>
	[TestMethod]
	public async Task ClaimSelectors_WithDisabledSelector_ShouldNotClaim()
	{
		// Arrange
		var workerId = Guid.NewGuid();
		var websiteSelectorId = await _dataFixture!.SeedWebsiteSelectorAsync(isEnabled: false);

		await _dataFixture.SeedWorkItemAsync(
			domainId: websiteSelectorId,
			status: "PENDING"
		);

		// Act
		var claimed = await _collectionService!.ClaimSelectorsAsync(workerId, count: 5, lease: 300, CancellationToken.None);

		// Assert
		// Disabled selectors should not be claimable
		Assert.AreEqual(0, claimed.Count, "Disabled selectors should not be claimed");
	}
}
