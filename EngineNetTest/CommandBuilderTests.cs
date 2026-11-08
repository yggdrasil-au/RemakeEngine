using EngineNet.Core.Data;
using EngineNet.Core.Services.CommandService;

namespace EngineNetTest;

/// <summary>
/// Characterizes the command contract used by manifest, CLI, and script operations.
/// </summary>
[TestClass]
public sealed class CommandBuilderTests {
    /// <summary>Missing scripts return before mutating any prompt answers.</summary>
    [TestMethod]
    public void BuildCommand_WithoutScript_DoesNotSeedDefaults() {
        PromptAnswers Answers = new();
        Dictionary<string, object?> Operation = new() {
            ["prompts"] = new List<object?> { Prompt("Enabled", "confirm", "default", true) }
        };

        CollectionAssert.AreEqual(Array.Empty<string>(), Build(Operation, Answers));
        Assert.IsEmpty(Answers);
    }

    /// <summary>Argument tokens retain order, whitespace, and empty strings without shell quoting.</summary>
    [TestMethod]
    public void BuildCommand_PreservesArgumentTokensAndInterpreterDefaults() {
        Dictionary<string, object?> Operation = new() {
            ["script"] = "a script.py",
            ["args"] = new System.Collections.ArrayList { "", "two words", null, 42, "a\"b", "{{Game_Root}}" }
        };

        CollectionAssert.AreEqual(
            new[] { "python", "a script.py", "", "two words", "42", "a\"b", "{{Game_Root}}" },
            Build(Operation)
        );
        Operation["script_type"] = "LuA";
        Operation["args"] = new[] { "--flag", "value" };
        CollectionAssert.AreEqual(new[] { "lua", "a script.py", "--flag", "value" }, Build(Operation));
    }

    /// <summary>Conditions can refer to later defaults, including defaults of inactive prompts.</summary>
    [TestMethod]
    public void BuildCommand_SeedsAllDefaultsBeforeEvaluatingConditions() {
        Dictionary<string, object?> Conditional = Prompt("Output", "text", "default", "target");
        Conditional["condition"] = "Enabled";
        Conditional["cli_arg_prefix"] = "--output";
        Dictionary<string, object?> Enabled = Prompt("Enabled", "confirm", "default", true);
        Enabled["condition"] = "Missing";
        PromptAnswers Answers = new();
        Dictionary<string, object?> Operation = new() {
            ["script"] = "build.py",
            ["prompts"] = new List<object?> { Conditional, Enabled }
        };

        CollectionAssert.AreEqual(new[] { "python", "build.py", "--output", "target" }, Build(Operation, Answers));
        Assert.AreEqual(true, Answers["enabled"]);
        Assert.AreEqual("target", Answers["output"]);
    }

    /// <summary>Existing answers, including null, win over defaults; conditions require an actual true bool.</summary>
    [TestMethod]
    public void BuildCommand_PreservesExplicitAnswersAndStrictBooleanConditions() {
        Dictionary<string, object?> Text = Prompt("Output", "text", "default", "default-output");
        Text["cli_arg"] = "--output";
        Dictionary<string, object?> Conditional = Prompt("Verbose", "confirm", "cli_arg", "--verbose");
        Conditional["condition"] = "Enabled";
        PromptAnswers Answers = new() { ["output"] = null, ["Verbose"] = true, ["Enabled"] = "true" };
        Dictionary<string, object?> Operation = new() {
            ["script"] = "build.py",
            ["prompts"] = new List<object?> { Text, Conditional }
        };

        CollectionAssert.AreEqual(new[] { "python", "build.py" }, Build(Operation, Answers));
        Assert.IsNull(Answers["Output"]);
        Answers["Enabled"] = true;
        CollectionAssert.AreEqual(new[] { "python", "build.py", "--verbose" }, Build(Operation, Answers));
    }

    /// <summary>Prompt types preserve prefix precedence, repeated flags, and checkbox item conversion.</summary>
    [TestMethod]
    public void BuildCommand_MapsConfirmCheckboxAndTextInManifestOrder() {
        Dictionary<string, object?> Text = Prompt("Text", "text", "cli_arg_prefix", "--preferred");
        Text["cli_arg"] = "--fallback";
        Dictionary<string, object?> Operation = new() {
            ["script"] = "build.py",
            ["args"] = new List<string> { "--verbose" },
            ["prompts"] = new List<object?> {
                Prompt("Confirm", "confirm", "cli_arg", "--verbose"),
                Prompt("Items", "checkbox", "cli_prefix", "--items"),
                Text,
                Prompt("Fallback", "text", "cli_arg", "--fallback"),
                Prompt("Empty", "text", "cli_arg", "--empty")
            }
        };
        PromptAnswers Answers = new() {
            ["Confirm"] = true,
            ["Items"] = new List<object?> { "first", null, 7, "" },
            ["Text"] = " value ",
            ["Fallback"] = 99,
            ["Empty"] = "  "
        };

        CollectionAssert.AreEqual(
            new[] { "python", "build.py", "--verbose", "--verbose", "--items", "first", "7", "", "--preferred", " value ", "--fallback", "99" },
            Build(Operation, Answers)
        );
        Answers["Items"] = new List<object?>();
        Assert.Contains("--items", Build(Operation, Answers));
    }

    /// <summary>Malformed entries and unknown or differently cased prompt types remain ignored.</summary>
    [TestMethod]
    public void BuildCommand_IgnoresMalformedAndUnsupportedPrompts() {
        Dictionary<string, object?> Operation = new() {
            ["script"] = null,
            ["prompts"] = new List<object?> {
                null, "bad", new Dictionary<string, object?> { ["Name"] = "NoType", ["default"] = true },
                Prompt("Unknown", "Text", "cli_arg", "--unknown"),
                Prompt("Items", "checkbox", "cli_prefix", "--items")
            }
        };
        PromptAnswers Answers = new() { ["Unknown"] = "text", ["Items"] = new List<string> { "one" } };

        CollectionAssert.AreEqual(new[] { "python", "" }, Build(Operation, Answers));
        Assert.IsFalse(Answers.ContainsKey("NoType"));
    }

    /// <summary>The builder validates selection but does not look up games or expand placeholders itself.</summary>
    [TestMethod]
    public void BuildCommand_RejectsEmptyGameSelection() {
        CommandService Service = new();
        ArgumentException Error = Assert.ThrowsExactly<ArgumentException>(() => Service.BuildCommand(
            " ", new GameModules(), new Dictionary<string, object?>(), new Dictionary<string, object?>(), new PromptAnswers()
        ));
        Assert.AreEqual("currentGame", Error.ParamName);
    }

    private static List<string> Build(Dictionary<string, object?> Operation, PromptAnswers? Answers = null) {
        return new CommandService().BuildCommand("module", new GameModules(), new Dictionary<string, object?>(), Operation, Answers ?? new PromptAnswers());
    }

    private static Dictionary<string, object?> Prompt(string Name, string Type, string Key, object? Value) {
        return new Dictionary<string, object?> { ["Name"] = Name, ["type"] = Type, [Key] = Value };
    }
}
