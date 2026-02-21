using Xunit;

namespace OpenOrca.Core.Tests.Orchestration;

/// <summary>
/// Ensures tests that modify the static AgentTypeRegistry run sequentially.
/// </summary>
[CollectionDefinition("AgentRegistry")]
public class AgentRegistryCollection;
