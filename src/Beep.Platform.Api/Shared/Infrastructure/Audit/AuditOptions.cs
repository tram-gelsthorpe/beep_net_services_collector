namespace Beep.Platform.Api.Shared.Infrastructure.Audit;

/// <summary>
/// Audit values Beep.Platform.Api writes on every record it creates or changes (the <c>Audit</c> section).
/// </summary>
public sealed class AuditOptions
{
	public const string Name = "Audit";

	/// <summary>User recorded as CreatedBy / ModifiedBy (<c>Audit:CreatedBy</c>).</summary>
	public Guid CreatedBy { get; init; }

	/// <summary>Reads and checks the <c>Audit</c> section; fails at startup when CreatedBy is missing.</summary>
	public static AuditOptions FromConfiguration(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var value = configuration[$"{Name}:{nameof(CreatedBy)}"];
		if (!Guid.TryParse(value, out var createdBy) || createdBy == Guid.Empty)
			throw new InvalidOperationException(
				$"{Name}:{nameof(CreatedBy)} must be the GUID of the user Beep.Platform.Api records as CreatedBy " +
				$"(environment variable {Name}__{nameof(CreatedBy)}).");

		return new AuditOptions { CreatedBy = createdBy };
	}
}
