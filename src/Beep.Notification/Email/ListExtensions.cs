namespace Beep.Notification;

internal static class ListExtensions
{
	public static int IndexOf(this IReadOnlyList<string> list, string value)
	{
		for (var i = 0; i < list.Count; i++)
			if (list[i] == value)
				return i;
		return -1;
	}
}
