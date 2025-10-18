
namespace EngineNet.Tests;

public sealed class InlineOperationOptionsTests {
    [Fact]
    public void Parse_Minimal_GameAndScript() {
        var opts = App.InlineOperationOptions.Parse(new[] { "--game", "Foo", "--script", "doit.lua" });
        Assert.Equal("Foo", opts.GameIdentifier);
        Assert.Equal("doit.lua", opts.Script);
        var op = opts.BuildOperation();
        Assert.Equal("doit.lua", op["script"]);
    }

    [Fact]
    public void Parse_Sets_Aliases_And_TypoFix() {
        var opts = App.InlineOperationOptions.Parse(new[] {
            "--module", "Foo",
            "--type", "lau", // typo handled => lua
            "--script", "a.lua"
        });
        var op = opts.BuildOperation();
        Assert.Equal("lua", op["script_type"]);
        Assert.Equal("a.lua", op["script"]);
    }

    [Fact]
    public void Parse_Args_List_And_Override() {
        var opts = App.InlineOperationOptions.Parse(new[] {
            "--game", "Foo", "--script", "a.py",
            "--arg", "one",
            "--args", "[2, 3]",
            "--set", "args=[\"x\", \"y\"]" // override
        });
        var op = opts.BuildOperation();
        Assert.True(op.ContainsKey("args"));
        var args = (op["args"] as IEnumerable<object?>)!.Select(x => x?.ToString()).ToArray();
        Assert.Equal(new[] { "x", "y" }, args);
    }

    [Fact]
    public void Parse_KeyValueForAnswers_And_AutoPrompt() {
        var opts = App.InlineOperationOptions.Parse(new[] {
            "--game", "Foo", "--script", "a.py",
            "--answer", "confirm=true",
            "--auto_prompt", "xyz=hello"
        });
        Assert.True(opts.PromptAnswers.TryGetValue("confirm", out var v) && v is bool b && b);
        Assert.True(opts.AutoPromptResponses.TryGetValue("xyz", out var s) && s == "hello");
    }

    [Theory]
    [InlineData("null", null)]
    [InlineData("true", true)]
    [InlineData("42", 42L)]
    [InlineData("3.14", 3.14)]
    [InlineData("'str'", "str")]
    [InlineData("\"str2\"", "str2")]
    public void ParseValueToken_Coercion_Works(string raw, object? expected) {
        var opts = App.InlineOperationOptions.Parse(new[] { "--game", "X", "--script", "b.py", "--set", $"k={raw}" });
        var op = opts.BuildOperation();
        Assert.True(op.TryGetValue("k", out var val));
        if (expected is double)
            Assert.Equal(Convert.ToDouble(expected), Convert.ToDouble(val));
        else
            Assert.Equal(expected, val);
    }

    [Fact]
    public void ParseArgsList_Supports_CommaSeparated() {
        var opts = App.InlineOperationOptions.Parse(new[] { "--game", "X", "--script", "b.py", "--args", "a,b, c" });
        var op = opts.BuildOperation();
        var args = (op["args"] as IEnumerable<object?>)!.Select(x => x?.ToString()).ToArray();
        Assert.Equal(new[] { "a", "b", "c" }, args);
    }
}
