namespace EngineNet.Core.Abstractions;

/// <summary>
/// Defines a resolver for locating external tool paths.
/// </summary>
public interface IJsonToolResolver {
    string ResolveToolPath(string toolId, string? version = null);
}