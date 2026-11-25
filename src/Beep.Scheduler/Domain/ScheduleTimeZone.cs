namespace Beep.Scheduler;

/// <summary>
/// Time zone of a schedule. The database evaluates schedules with <c>AT TIME ZONE</c>, which takes
/// Windows time zone ids, so ids are stored in that form.
/// </summary>
public static class ScheduleTimeZone
{
	/// <summary>
	/// Resolve a configured time zone to the Windows id stored on the schedule.
	/// Accepts an IANA id ("Asia/Bangkok") or a Windows id ("SE Asia Standard Time");
	/// null or empty means this machine's local time zone.
	/// </summary>
	/// <exception cref="ArgumentException">The id is not a known time zone.</exception>
	public static string Resolve(string? timeZone)
	{
		var zone = string.IsNullOrWhiteSpace(timeZone) ? TimeZoneInfo.Local : Find(timeZone.Trim());

		if (zone.HasIanaId)
			return TimeZoneInfo.TryConvertIanaIdToWindowsId(zone.Id, out var windowsId)
				? windowsId
				: throw new ArgumentException($"Time zone '{zone.Id}' has no Windows equivalent.", nameof(timeZone));

		return zone.Id;
	}

	private static TimeZoneInfo Find(string id)
	{
		try
		{
			return TimeZoneInfo.FindSystemTimeZoneById(id);
		}
		catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
		{
			throw new ArgumentException($"Unknown time zone '{id}'.", nameof(id), ex);
		}
	}
}
