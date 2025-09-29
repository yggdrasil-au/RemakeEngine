using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EngineNet.Interface.CLI;
using Xunit;

namespace EngineNet.Tests;

public sealed class TerminalUtilsTests {
    [Fact]
    public void WriteColored_Writes_And_Restores_Color() {
        var prev = Console.ForegroundColor;
        StringBuilder sb = new StringBuilder();
        TextWriter prevOut = Console.Out;
        try {
            using StringWriter sw = new StringWriter(sb);
            Console.SetOut(sw);
            TerminalUtils.WriteColored("hello", ConsoleColor.Yellow);
            sw.Flush();
            Assert.Contains("hello", sb.ToString());
            Assert.Equal(prev, Console.ForegroundColor);
        } finally {
            Console.SetOut(prevOut);
            Console.ForegroundColor = prev;
        }
    }

    [Fact]
    public void OnOutput_Writes_To_Stdout_And_Stderr_Colored() {
        StringBuilder sb = new StringBuilder();
        TextWriter prevOut = Console.Out;
        try {
            using StringWriter sw = new StringWriter(sb);
            Console.SetOut(sw);
            TerminalUtils.OnOutput("line1", "stdout");
            TerminalUtils.OnOutput("line2", "stderr");
            sw.Flush();
            String all = sb.ToString();
            Assert.Contains("line1", all);
            Assert.Contains("line2", all);
        } finally {
            Console.SetOut(prevOut);
        }
    }

    [Fact]
    public void OnEvent_Print_And_Prompt_Warning_Error() {
        StringBuilder sb = new StringBuilder();
        TextWriter prevOut = Console.Out;
        try {
            using StringWriter sw = new StringWriter(sb);
            Console.SetOut(sw);
            TerminalUtils.OnEvent(new Dictionary<string, object?> { {"event", "print"}, {"message", "M"}, {"color", "green"}, {"newline", true} });
            TerminalUtils.OnEvent(new Dictionary<string, object?> { {"event", "prompt"}, {"message", "Your name?"} });
            TerminalUtils.OnEvent(new Dictionary<string, object?> { {"event", "warning"}, {"message", "Be careful"} });
            TerminalUtils.OnEvent(new Dictionary<string, object?> { {"event", "error"}, {"message", "Oops"} });
            sw.Flush();
            String all = sb.ToString();
            Assert.Contains("M", all);
            Assert.Contains("? Your name?", all);
            Assert.Contains("Be careful", all);
            Assert.Contains("Oops", all);
        } finally {
            Console.SetOut(prevOut);
        }
    }

    [Fact]
    public void StdinProvider_ReadsLine() {
        // Replace Console.In with a preloaded string
        var prevIn = Console.In;
        try {
            using StringReader sr = new StringReader("answer\n");
            Console.SetIn(sr);
            string? ans = TerminalUtils.StdinProvider();
            Assert.Equal("answer", ans);
        } finally {
            Console.SetIn(prevIn);
        }
    }
}
