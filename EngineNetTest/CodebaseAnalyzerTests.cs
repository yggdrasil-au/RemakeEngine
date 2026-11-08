using CodebaseAnalyzer;
using Microsoft.CodeAnalysis.CSharp;

namespace EngineNetTest;

/// <summary>Checks analyzer classifications that inform refactoring decisions.</summary>
[TestClass]
public sealed class CodebaseAnalyzerTests {
    /// <summary>Implicit interface members are public, while explicit private methods and class defaults remain private.</summary>
    [TestMethod]
    public void Metrics_ClassifyImplicitInterfaceAccessibility() {
        FileStats Stats = Analyze("""
            interface IReader {
                void Read();
                static abstract void Open();
                private void Helper() { }
                string Name { get; }
            }
            class Reader { void Hidden() { } }
            """);

        Assert.AreEqual(1, Stats.FuncPub);
        Assert.AreEqual(1, Stats.FuncPubStat);
        Assert.AreEqual(2, Stats.FuncPriv);
        Assert.AreEqual(1, Stats.VarsPub);
        Assert.AreEqual(0, Stats.VarsPriv);
    }

    /// <summary>A dispatch switch keeps its raw complexity and is distinguished from branching inside a case.</summary>
    [TestMethod]
    public void Metrics_DistinguishDispatchFromControlFlow() {
        FileStats Stats = Analyze("""
            class Factory {
                int Dispatch(int Value) { switch (Value) { case 1: return 10; case 2: return 20; default: return 0; } }
                int Branch(int Value) { switch (Value) { case 1: if (Value > 0) { return 10; } break; } return 0; }
            }
            """);

        MethodStats Dispatch = Stats.MethodDetails.Single(Method => Method.Name == "Factory.Dispatch");
        MethodStats Branch = Stats.MethodDetails.Single(Method => Method.Name == "Factory.Branch");
        Assert.AreEqual(3, Dispatch.Complexity);
        Assert.AreEqual(2, Dispatch.DispatchCases);
        Assert.IsTrue(Dispatch.IsDispatchOnly);
        Assert.AreEqual(3, Branch.Complexity);
        Assert.IsFalse(Branch.IsDispatchOnly);
        Assert.AreEqual(2, Dispatch.Line);
    }

    /// <summary>Switch expressions and guarded patterns contribute to modern C# complexity.</summary>
    [TestMethod]
    public void Metrics_CountSwitchExpressionArmsAndGuards() {
        FileStats Stats = Analyze("""
            class Factory {
                int Dispatch(int Value) => Value switch { 1 => 10, 2 => 20, _ => 0 };
                int Guard(int Value) => Value switch { > 0 when Value < 10 => 1, _ => 0 };
            }
            """);

        Assert.AreEqual(3, Stats.MethodDetails[0].Complexity);
        Assert.IsTrue(Stats.MethodDetails[0].IsDispatchOnly);
        Assert.AreEqual(3, Stats.MethodDetails[1].Complexity);
        Assert.IsFalse(Stats.MethodDetails[1].IsDispatchOnly);
    }

    /// <summary>Type names include their containing type, avoiding collisions in combined partial-class totals.</summary>
    [TestMethod]
    public void Metrics_RetainContainingTypesInClassNames() {
        FileStats Stats = Analyze("namespace Test { struct Owner { class Nested { } } class Other { class Nested { } } }");

        Assert.AreEqual(2, Stats.NestedClasses);
        Assert.Contains("Test.Owner.Nested", Stats.ClassDetails.Select(Detail => Detail.Name).ToArray());
        Assert.Contains("Test.Other.Nested", Stats.ClassDetails.Select(Detail => Detail.Name).ToArray());
    }

    private static FileStats Analyze(string Source) {
        MetricWalker Walker = new();
        Walker.Visit(CSharpSyntaxTree.ParseText(Source).GetCompilationUnitRoot());
        return Walker.Stats;
    }
}
