using System;
using System.Collections.Generic;
using RemakeEngine.Core;
using Xunit;

namespace EngineNet.Tests;

public class PlaceholdersTests
{
    [Fact]
    public void Resolve_ReplacesSimpleTokens()
    {
        var ctx = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["User"] = new Dictionary<string, object?> {
                ["Name"] = "Bart"
            }
        };
        var input = "hello {{User.Name}}";
        var result = Placeholders.Resolve(input, ctx);
        Assert.Equal("hello Bart", result);
    }

    [Fact]
    public void Resolve_LeavesUnknownTokens()
    {
        var ctx = new Dictionary<string, object?>();
        var input = "hi {{missing}}";
        var result = Placeholders.Resolve(input, ctx);
        Assert.Equal("hi {{missing}}", result);
    }

    [Fact]
    public void Resolve_RecursesCollections()
    {
        var ctx = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Root"] = new Dictionary<string, object?> { ["Path"] = "/games/foo" }
        };
        var input = new Dictionary<string, object?>
        {
            ["a"] = "{{Root.Path}}/bin",
            ["b"] = new List<object?> { "x", "{{Root.Path}}/y" }
        };
        var resolved = (Dictionary<string, object?>?)Placeholders.Resolve(input, ctx);
        Assert.NotNull(resolved);
        Assert.Equal("/games/foo/bin", resolved!["a"]);
        var b = Assert.IsType<List<object?>>(resolved["b"]);
        Assert.Equal(new object?[]{"x", "/games/foo/y"}, b);
    }
}
