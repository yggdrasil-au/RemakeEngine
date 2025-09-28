using System;
using System.Collections.Generic;
using Xunit;

namespace EngineNet.Tests;

public class PlaceholdersTests
{
    [Fact]
    public void Resolve_ReplacesSimpleTokens()
    {
        Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["User"] = new Dictionary<String, Object?> {
                ["Name"] = "Bart"
            }
        };
        String input = "hello {{User.Name}}";
        Object? result = EngineNet.Core.Sys.Placeholders.Resolve(input, ctx);
        Assert.Equal("hello Bart", result);
    }

    [Fact]
    public void Resolve_LeavesUnknownTokens()
    {
        Dictionary<String, Object?> ctx = new Dictionary<String, Object?>();
        String input = "hi {{missing}}";
        Object? result = EngineNet.Core.Sys.Placeholders.Resolve(input, ctx);
        Assert.Equal("hi {{missing}}", result);
    }

    [Fact]
    public void Resolve_RecursesCollections()
    {
        Dictionary<String, Object?> ctx = new Dictionary<String, Object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Root"] = new Dictionary<String, Object?> { ["Path"] = "/games/foo" }
        };
        Dictionary<String, Object?> input = new Dictionary<String, Object?>
        {
            ["a"] = "{{Root.Path}}/bin",
            ["b"] = new List<Object?> { "x", "{{Root.Path}}/y" }
        };
        Dictionary<String, Object?>? resolved = (Dictionary<String, Object?>?)EngineNet.Core.Sys.Placeholders.Resolve(input, ctx);
        Assert.NotNull(resolved);
        Assert.Equal("/games/foo/bin", resolved!["a"]);
        List<Object?> b = Assert.IsType<List<Object?>>(resolved["b"]);
        Assert.Equal(new Object?[]{"x", "/games/foo/y"}, b);
    }
}
