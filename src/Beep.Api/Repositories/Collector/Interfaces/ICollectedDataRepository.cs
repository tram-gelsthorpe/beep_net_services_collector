namespace Beep.Api.Repositories.Collector;

/// <summary>Reads collection results from the Beep.Collector database (<c>ConnectionStrings:Collector</c>).</summary>
public interface ICollectedDataRepository
{
	/// <summary>
	/// The <c>ExtractedJson</c> of a collection result, or null when the result does not exist or has
	/// no extracted data.
	/// </summary>
	Task<string?> GetExtractedJsonAsync(Guid collectedDataId, CancellationToken cancellationToken);
}
