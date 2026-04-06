namespace EngineNetTest;

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ProgramTests {

    private static object InvokeParseArguments(string[] args) {
        var method = typeof(EngineNet.Program).GetMethod("ParseArguments", BindingFlags.NonPublic | BindingFlags.Static);
        return method?.Invoke(null, [args]) ?? throw new System.Exception("Method ParseArguments not found");
    }

    private static object? GetPropertyValue(object obj, string propertyName) {
        var prop = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(obj);
    }

    private static string InvokeTryFindProjectRoot(string startDir) {
        var method = typeof(EngineNet.Program).GetMethod("TryFindProjectRoot", BindingFlags.NonPublic | BindingFlags.Static);
        return (string?)method?.Invoke(null, [startDir]) ?? string.Empty;
    }

    [TestMethod]
    public void ParseArguments_ShouldExtractExplicitRoot() {
        // Arrange
        string[] args = ["--root", "C:\\CustomRoot", "--gui"];

        // Act
        object result = InvokeParseArguments(args);

        // Assert
        Assert.AreEqual("C:\\CustomRoot", GetPropertyValue(result, "ExplicitRoot"));
        var remaining = (System.Collections.Generic.List<string>?)GetPropertyValue(result, "Remaining");
        Assert.IsNotNull(remaining);
        Assert.HasCount(1, remaining);
        Assert.AreEqual("--gui", remaining[0]);
    }

    [TestMethod]
    public void ParseArguments_ShouldHandleMissingRootValue_LeavesNull() {
        // Arrange
        string[] args = ["--root", "--tui"];

        // Act
        object result = InvokeParseArguments(args);

        // Assert
        // In the current implementation, --root with no following value results in ExplicitRoot being null
        // because i+1 is out of range.
        Assert.IsNull(GetPropertyValue(result, "ExplicitRoot"));

        var remaining = (System.Collections.Generic.List<string>?)GetPropertyValue(result, "Remaining");
        Assert.IsNotNull(remaining);
        Assert.HasCount(1, remaining);
        Assert.AreEqual("--tui", remaining[0]);
    }

    [TestMethod]
    public void ParseArguments_ShouldHandleNormalArgs() {
        // Arrange
        string[] args = ["--tui", "somefile.txt"];

        // Act
        object result = InvokeParseArguments(args);

        // Assert
        Assert.IsNull(GetPropertyValue(result, "ExplicitRoot"));
        var remaining = (System.Collections.Generic.List<string>?)GetPropertyValue(result, "Remaining");
        Assert.IsNotNull(remaining);
        Assert.HasCount(2, remaining);
    }

    [TestMethod]
    public void TryFindProjectRoot_ShouldReturnEmptyForInvalidPath() {
        // Arrange
        string startDir = "C:\\NonExistentPath_XYZ_123";

        // Act
        string result = InvokeTryFindProjectRoot(startDir);

        // Assert
        Assert.AreEqual(string.Empty, result);
    }
}