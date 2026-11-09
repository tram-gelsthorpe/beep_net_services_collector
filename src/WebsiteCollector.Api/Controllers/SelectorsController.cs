using Microsoft.AspNetCore.Mvc;
using WebsiteCollector.Api.Repositories;
using WebsiteCollector.Worker;

namespace WebsiteCollector.Api.Controllers;

/// <summary>
/// Endpoints for managing website selectors.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SelectorsController : ControllerBase
{
	private readonly ISelectorRepository _repository;
	private readonly ILogger<SelectorsController> _logger;

	public SelectorsController(ISelectorRepository repository, ILogger<SelectorsController> logger)
	{
		_repository = repository;
		_logger = logger;
	}

	/// <summary>
	/// Get all selectors for a website.
	/// </summary>
	[HttpGet("website/{websiteId:guid}")]
	public async Task<IActionResult> GetSelectorsByWebsite(Guid websiteId, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving selectors for website. WebsiteId: {WebsiteId}", websiteId);
			var selectors = await _repository.GetSelectorsByWebsiteIdAsync(websiteId, cancellationToken);
			return Ok(new { count = selectors.Count, items = selectors });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving selectors for website. WebsiteId: {WebsiteId}", websiteId);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve selectors" });
		}
	}

	/// <summary>
	/// Get a selector by ID.
	/// </summary>
	[HttpGet("{id:guid}")]
	public async Task<IActionResult> GetSelector(Guid id, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving selector. Id: {Id}", id);
			var selector = await _repository.GetSelectorByIdAsync(id, cancellationToken);

			if (selector == null)
				return NotFound(new { error = "Selector not found" });

			return Ok(selector);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving selector. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve selector" });
		}
	}

	/// <summary>
	/// Create a new selector.
	/// </summary>
	[HttpPost]
	public async Task<IActionResult> CreateSelector(
		[FromBody] CreateSelectorRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Creating selector for website. WebsiteId: {WebsiteId}", request.WebsitesId);
			var selector = new WebsiteSelector(
				Guid.NewGuid(),
				request.WebsitesId,
				request.CollectionUrl,
				request.CollectionType,
				request.ContentSelector,
				request.TableSelector,
				request.IntervalSeconds,
				request.IsEnabled);

			var id = await _repository.CreateSelectorAsync(selector, cancellationToken);
			return CreatedAtAction(nameof(GetSelector), new { id }, new { id });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating selector");
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to create selector" });
		}
	}

	/// <summary>
	/// Update a selector.
	/// </summary>
	[HttpPut("{id:guid}")]
	public async Task<IActionResult> UpdateSelector(
		Guid id,
		[FromBody] UpdateSelectorRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Updating selector. Id: {Id}", id);
			var selector = new WebsiteSelector(
				id,
				request.WebsitesId,
				request.CollectionUrl,
				request.CollectionType,
				request.ContentSelector,
				request.TableSelector,
				request.IntervalSeconds,
				request.IsEnabled);

			await _repository.UpdateSelectorAsync(selector, cancellationToken);
			return Ok(new { message = "Selector updated successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error updating selector. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to update selector" });
		}
	}

	/// <summary>
	/// Delete a selector.
	/// </summary>
	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> DeleteSelector(Guid id, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Deleting selector. Id: {Id}", id);
			await _repository.DeleteSelectorAsync(id, cancellationToken);
			return Ok(new { message = "Selector deleted successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error deleting selector. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete selector" });
		}
	}

	/// <summary>
	/// Enable a selector.
	/// </summary>
	[HttpPost("{id:guid}/enable")]
	public async Task<IActionResult> EnableSelector(Guid id, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Enabling selector. Id: {Id}", id);
			await _repository.EnableSelectorAsync(id, cancellationToken);
			return Ok(new { message = "Selector enabled successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error enabling selector. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to enable selector" });
		}
	}

	/// <summary>
	/// Disable a selector.
	/// </summary>
	[HttpPost("{id:guid}/disable")]
	public async Task<IActionResult> DisableSelector(Guid id, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Disabling selector. Id: {Id}", id);
			await _repository.DisableSelectorAsync(id, cancellationToken);
			return Ok(new { message = "Selector disabled successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error disabling selector. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to disable selector" });
		}
	}
}

/// <summary>
/// Request model for creating a selector.
/// </summary>
public class CreateSelectorRequest
{
	public required Guid WebsitesId { get; set; }
	public required string CollectionUrl { get; set; }
	public required string CollectionType { get; set; }
	public string? ContentSelector { get; set; }
	public string? TableSelector { get; set; }
	public int IntervalSeconds { get; set; } = 3600;
	public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Request model for updating a selector.
/// </summary>
public class UpdateSelectorRequest
{
	public required Guid WebsitesId { get; set; }
	public required string CollectionUrl { get; set; }
	public required string CollectionType { get; set; }
	public string? ContentSelector { get; set; }
	public string? TableSelector { get; set; }
	public int IntervalSeconds { get; set; }
	public bool IsEnabled { get; set; }
}
