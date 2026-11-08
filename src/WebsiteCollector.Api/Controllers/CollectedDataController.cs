using Microsoft.AspNetCore.Mvc;
using WebsiteCollector.Api.Repositories;

namespace WebsiteCollector.Api.Controllers;

/// <summary>
/// Endpoints for querying collected website data.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CollectedDataController : ControllerBase
{
	private readonly ICollectedDataRepository _repository;
	private readonly ILogger<CollectedDataController> _logger;

	public CollectedDataController(ICollectedDataRepository repository, ILogger<CollectedDataController> logger)
	{
		_repository = repository;
		_logger = logger;
	}

	/// <summary>
	/// Get collected data by execution ID.
	/// </summary>
	[HttpGet("execution/{executionId:guid}")]
	public async Task<IActionResult> GetByExecution(Guid executionId, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving collected data for execution. ExecutionId: {ExecutionId}", executionId);
			var data = await _repository.GetCollectedDataByExecutionIdAsync(executionId, cancellationToken);

			if (data == null)
				return NotFound(new { error = "Collected data not found" });

			return Ok(data);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving collected data for execution. ExecutionId: {ExecutionId}", executionId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve collected data" });
		}
	}

	/// <summary>
	/// Get the latest collected data for a website selector.
	/// </summary>
	[HttpGet("selector/{selectorId:guid}/latest")]
	public async Task<IActionResult> GetLatestBySelector(Guid selectorId, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving latest collected data for selector. SelectorId: {SelectorId}", selectorId);
			var data = await _repository.GetLatestCollectedDataBySelectorIdAsync(selectorId, cancellationToken);

			if (data == null)
				return NotFound(new { error = "No collected data found for this selector" });

			return Ok(data);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving latest collected data for selector. SelectorId: {SelectorId}", selectorId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve collected data" });
		}
	}

	/// <summary>
	/// Get collection history for a selector with pagination.
	/// </summary>
	[HttpGet("selector/{selectorId:guid}/history")]
	public async Task<IActionResult> GetCollectionHistory(
		Guid selectorId,
		[FromQuery] int pageNumber = 1,
		[FromQuery] int pageSize = 10,
		CancellationToken cancellationToken = default)
	{
		if (pageNumber <= 0 || pageSize <= 0 || pageSize > 100)
			return BadRequest(new { error = "pageNumber must be >= 1 and pageSize must be between 1 and 100" });

		try
		{
			_logger.LogInformation("Retrieving collection history for selector. SelectorId: {SelectorId}, Page: {PageNumber}, Size: {PageSize}", 
				selectorId, pageNumber, pageSize);

			var (items, total) = await _repository.GetCollectionHistoryAsync(selectorId, pageNumber, pageSize, cancellationToken);
			return Ok(new 
			{ 
				total,
				pageNumber,
				pageSize,
				count = items.Count,
				items 
			});
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving collection history for selector. SelectorId: {SelectorId}", selectorId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve collection history" });
		}
	}

	/// <summary>
	/// Get execution details by ID.
	/// </summary>
	[HttpGet("executions/{executionId:guid}")]
	public async Task<IActionResult> GetExecution(Guid executionId, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving execution details. ExecutionId: {ExecutionId}", executionId);
			var execution = await _repository.GetExecutionByIdAsync(executionId, cancellationToken);

			if (execution == null)
				return NotFound(new { error = "Execution not found" });

			return Ok(execution);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving execution. ExecutionId: {ExecutionId}", executionId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve execution" });
		}
	}

	/// <summary>
	/// Get recent executions for a selector.
	/// </summary>
	[HttpGet("selector/{selectorId:guid}/executions")]
	public async Task<IActionResult> GetExecutionsBySelector(
		Guid selectorId,
		[FromQuery] int limit = 10,
		CancellationToken cancellationToken = default)
	{
		if (limit <= 0 || limit > 100)
			return BadRequest(new { error = "limit must be between 1 and 100" });

		try
		{
			_logger.LogInformation("Retrieving recent executions for selector. SelectorId: {SelectorId}, Limit: {Limit}", selectorId, limit);
			var executions = await _repository.GetRecentExecutionsBySelectorIdAsync(selectorId, limit, cancellationToken);
			return Ok(new { count = executions.Count, items = executions });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving executions for selector. SelectorId: {SelectorId}", selectorId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve executions" });
		}
	}

	/// <summary>
	/// Search collected data by content hash or extracted JSON.
	/// </summary>
	[HttpPost("search")]
	public async Task<IActionResult> SearchCollectedData(
		[FromBody] SearchCollectedDataRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Searching collected data");
			var results = await _repository.SearchCollectedDataAsync(
				request.SelectorId,
				request.SearchTerm,
				request.FromDate,
				request.ToDate,
				cancellationToken);

			return Ok(new { count = results.Count, items = results });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error searching collected data");
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to search collected data" });
		}
	}

	/// <summary>
	/// Get aggregated statistics for a selector.
	/// </summary>
	[HttpGet("selector/{selectorId:guid}/statistics")]
	public async Task<IActionResult> GetSelectorStatistics(Guid selectorId, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving statistics for selector. SelectorId: {SelectorId}", selectorId);
			var stats = await _repository.GetSelectorStatisticsAsync(selectorId, cancellationToken);
			return Ok(stats);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving selector statistics. SelectorId: {SelectorId}", selectorId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve statistics" });
		}
	}
}

/// <summary>
/// Request model for searching collected data.
/// </summary>
public class SearchCollectedDataRequest
{
	public Guid? SelectorId { get; set; }
	public string? SearchTerm { get; set; }
	public DateTime? FromDate { get; set; }
	public DateTime? ToDate { get; set; }
}
