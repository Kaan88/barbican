// Mirrors: Bulwark lib/plugin-sandbox/host-api-input.ts:25-30; host-added atomic session/selection fencing.
using System.Text.Json.Serialization;
namespace Barbican.Core.Accounts;

/// <summary>Host-only coherent session selection. A null selection generation means an explicit owner.</summary>
public sealed record AccountSessionSnapshot([property: JsonIgnore] IAccountSession Session, long? ActiveSelectionGeneration);
