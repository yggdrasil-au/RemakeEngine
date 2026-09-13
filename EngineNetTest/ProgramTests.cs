namespace EngineNetTest;

using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class ProgramTests {

    private static object InvokeParseArguments(string[] args) {
        MethodInfo? method = typeof(EngineNet.Program).GetMethod(name: "ParseArguments", bindingAttr: BindingFlags.NonPublic | BindingFlags.Static);
        return method?.Invoke(obj: null, parameters: [args]) ?? throw new System.Exception(message: "Method ParseArguments not found");
    }

    private static object? GetPropertyValue(object obj, string propertyName) {
        PropertyInfo? prop = obj.GetType().GetProperty(name: propertyName, bindingAttr: BindingFlags.Public | BindingFlags.Instance);
        return prop?.GetValue(obj: obj);
    }

    private static string InvokeTryFindProjectRoot(string startDir) {
        MethodInfo? method = typeof(EngineNet.Program).GetMethod(name: "TryFindProjectRoot", bindingAttr: BindingFlags.NonPublic | BindingFlags.Static);
        return (string?)method?.Invoke(obj: null, parameters: [startDir]) ?? string.Empty;
    }

    [TestMethod]
    public void ParseArguments_ShouldExtractExplicitRoot() {
        // Arrange
        string[] args = ["--root", "C:\\CustomRoot", "--gui"];

        // Act
        object result = InvokeParseArguments(args: args);

        // Assert
        Assert.AreEqual(expected: "C:\\CustomRoot", actual: GetPropertyValue(obj: result, propertyName: "ExplicitRoot"));
        List<string>? remaining = (System.Collections.Generic.List<string>?)GetPropertyValue(obj: result, propertyName: "Remaining");
        Assert.IsNotNull(value: remaining);
        Assert.HasCount(expected: 1, collection: remaining);
        Assert.AreEqual(expected: "--gui", actual: remaining[index: 0]);
    }

    [TestMethod]
    public void ParseArguments_ShouldHandleMissingRootValue_LeavesNull() {
        // Arrange
        string[] args = ["--root", "--tui"];

        // Act
        object result = InvokeParseArguments(args: args);

        // Assert
        // In the current implementation, --root with no following value results in ExplicitRoot being null
        // because i+1 is out of range.
        Assert.IsNull(value: GetPropertyValue(obj: result, propertyName: "ExplicitRoot"));

        List<string>? remaining = (System.Collections.Generic.List<string>?)GetPropertyValue(obj: result, propertyName: "Remaining");
        Assert.IsNotNull(value: remaining);
        Assert.HasCount(expected: 1, collection: remaining);
        Assert.AreEqual(expected: "--tui", actual: remaining[index: 0]);
    }

    [TestMethod]
    public void ParseArguments_ShouldHandleNormalArgs() {
        // Arrange
        string[] args = ["--tui", "somefile.txt"];

        // Act
        object result = InvokeParseArguments(args: args);

        // Assert
        Assert.IsNull(value: GetPropertyValue(obj: result, propertyName: "ExplicitRoot"));
        List<string>? remaining = (System.Collections.Generic.List<string>?)GetPropertyValue(obj: result, propertyName: "Remaining");
        Assert.IsNotNull(value: remaining);
        Assert.HasCount(expected: 2, collection: remaining);
    }

    [TestMethod]
    public void TryFindProjectRoot_ShouldReturnEmptyForInvalidPath() {
        // Arrange
        string startDir = "C:\\NonExistentPath_XYZ_123";

        // Act
        string result = InvokeTryFindProjectRoot(startDir: startDir);

        // Assert
        Assert.AreEqual(expected: string.Empty, actual: result);
    }
}