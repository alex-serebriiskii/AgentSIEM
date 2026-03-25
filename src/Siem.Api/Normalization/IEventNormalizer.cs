using Siem.Api.Kafka;
using Siem.Rules.Core;

namespace Siem.Api.Normalization;

/// <summary>
/// Transforms raw events from various agent frameworks into the canonical
/// F# <see cref="AgentEvent"/> type used by the rules engine.
/// </summary>
public interface IEventNormalizer
{
    AgentEvent Normalize(RawAgentEvent raw);
}
