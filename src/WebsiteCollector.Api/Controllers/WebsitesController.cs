using Microsoft.AspNetCore.Mvc;
using WebsiteCollector.Api.Repositories;
using WebsiteCollector.Worker.Features.WebsiteCollector.Models;

namespace WebsiteCollector.Api.Controllers;

/// <summary>
/// Endpoints for managing websites.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class WebsitesController : ControllerBase
{
	private readonly IWebsiteRepository _repository;
	private readonly ILogger<WebsitesController> _logger;

	public WebsitesController(IWebsiteRepository repository, ILogger<WebsitesController> logger)
	{
		_repository = repository;
		_logger = logger;
	}

	/// <summary>
	/// Get all websites.
	/// </summary>
	[HttpGet]
	public async Task<IActionResult> GetAllWebsites(CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving all websites");
			var websites = await _repository.GetAllWebsitesAsync(cancellationToken);
			return Ok(new { count = websites.Count, items = websites });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving websites");
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve websites" });
		}
	}

	/// <summary>
	/// Get a website by ID.
	/// </summary>
	[HttpGet("{id:guid}")]
	public async Task<IActionResult> GetWebsite(Guid id, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Retrieving website. Id: {Id}", id);
			var website = await _repository.GetWebsiteByIdAsync(id, cancellationToken);

			if (website == null)
				return NotFound(new { error = "Website not found" });

			return Ok(website);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error retrieving website. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to retrieve website" });
		}
	}

	/// <summary>
	/// Create a new website.
	/// </summary>
	[HttpPost]
	public async Task<IActionResult> CreateWebsite(
		[FromBody] CreateWebsiteRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Creating website. Name: {Name}", request.Name);
			var website = new Website(
				Guid.NewGuid(),
				request.Name,
				request.Url,
				request.IsEnabled);

			var id = await _repository.CreateWebsiteAsync(website, cancellationToken);
			return CreatedAtAction(nameof(GetWebsite), new { id }, new { id, website.Name, website.Url });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error creating website");
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to create website" });
		}
	}

	/// <summary>
	/// Update a website.
	/// </summary>
	[HttpPut("{id:guid}")]
	public async Task<IActionResult> UpdateWebsite(
		Guid id,
		[FromBody] UpdateWebsiteRequest request,
		CancellationToken cancellationToken)
	{
		if (!ModelState.IsValid)
			return BadRequest(ModelState);

		try
		{
			_logger.LogInformation("Updating website. Id: {Id}", id);
			var website = new Website(id, request.Name, request.Url, request.IsEnabled);
			await _repository.UpdateWebsiteAsync(website, cancellationToken);
			return Ok(new { message = "Website updated successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error updating website. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to update website" });
		}
	}

	/// <summary>
	/// Delete a website.
	/// </summary>
	[HttpDelete("{id:guid}")]
	public async Task<IActionResult> DeleteWebsite(Guid id, CancellationToken cancellationToken)
	{
		try
		{
			_logger.LogInformation("Deleting website. Id: {Id}", id);
			await _repository.DeleteWebsiteAsync(id, cancellationToken);
			return Ok(new { message = "Website deleted successfully" });
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Error deleting website. Id: {Id}", id);
			return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to delete website" });
		}
	}
}

/// <summary>
/// Request model for creating a website.
/// </summary>
public class CreateWebsiteRequest
{
	public required string Name { get; set; }
	public required string Url { get; set; }
	public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Request model for updating a website.
/// </summary>
public class UpdateWebsiteRequest
{
	public required string Name { get; set; }
	public required string Url { get; set; }
	public bool IsEnabled { get; set; }
}
