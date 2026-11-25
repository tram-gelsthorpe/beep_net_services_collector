using System.Threading;
using System.Threading.Tasks;

namespace Beep.Database;

/// <summary>
/// Creates a database and its objects, or upgrades an existing one to the latest script version.
/// </summary>
public interface IDatabaseInitializer
{
	/// <summary>
	/// Creates the database and its objects in <paramref name="schema"/> if the database does not exist;
	/// otherwise applies any pending upgrade scripts.
	/// </summary>
	Task CreateOrUpdateAsync(string schema, CancellationToken cancellationToken = default);
}
