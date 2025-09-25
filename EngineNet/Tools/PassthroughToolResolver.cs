namespace RemakeEngine.Tools;

/// <summary>
/// Fallback tool resolver that simply returns the tool id as the path.
/// Useful when tools are expected to be on PATH.
/// </summary>
public sealed class PassthroughToolResolver:IToolResolver {
    public String ResolveToolPath(String toolId) => toolId;
}

