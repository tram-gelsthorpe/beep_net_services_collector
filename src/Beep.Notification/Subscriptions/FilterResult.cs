namespace Beep.Notification;

/// <param name="Filter">The choices as stored, when valid</param>
/// <param name="Errors">Field name → message, when not</param>
public sealed record FilterResult(string? Filter, IReadOnlyDictionary<string, string> Errors)
{
	public static FilterResult Valid(string filter) => new(filter, new Dictionary<string, string>());

	public static FilterResult Invalid(IReadOnlyDictionary<string, string> errors) => new(null, errors);

	public bool IsValid => Errors.Count == 0;
}
