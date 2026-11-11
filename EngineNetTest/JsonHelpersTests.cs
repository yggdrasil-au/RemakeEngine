using System.Reflection;
using System.Text;
using System.Text.Json;
using EngineNet.Shared.Serialization.Json;

namespace EngineNetTest;

[TestClass]
public sealed class JsonHelpersTests {

    [TestMethod]
    public void LoadJsonFile_SupportsJsoncCommentsTrailingCommasBomAndUrls() {
        string tempDir = Path.Combine(path1: Path.GetTempPath(), path2: $"RemakeEngine_Jsonc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path: tempDir);
        string filePath = Path.Combine(path1: tempDir, path2: "sample.jsonc");

        try {
            string jsonc = """
            // Leading file comment with // slashes and /* markers
            {
              /* Multi-line block
                 comment before properties */
              "Modules": {
                "Lego_Batman": {
                  "id": "6",
                  "path": "EngineApps/Games/Lego/Games/Lego_Batman", // trailing comma next
                  //"url": "https://github.com/Superposition28/Lego_Batman.git"
                },
                "ValidUrl": {
                  "id": "7",
                  "url": "https://example.com/a//b/*not-a-comment*/",
                  "tags": [
                    "alpha",
                    "beta", // trailing comma in array
                  ],
                },
              },
            }
            """;

            // Write explicitly with a UTF-8 BOM
            File.WriteAllText(path: filePath, contents: jsonc, encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Dictionary<string, object?> loaded = JsonHelpers.LoadJsonFile(filePath: filePath);

            Assert.IsTrue(condition: loaded.TryGetValue(key: "modules", out object? modulesObj));
            Dictionary<string, object?> modules = (Dictionary<string, object?>)modulesObj!;

            Assert.IsTrue(condition: modules.TryGetValue(key: "lego_batman", out object? batmanObj));
            Dictionary<string, object?> batman = (Dictionary<string, object?>)batmanObj!;
            Assert.AreEqual(expected: "6", actual: batman[key: "ID"]?.ToString());
            Assert.IsFalse(condition: batman.ContainsKey(key: "url"));

            Dictionary<string, object?> validUrl = (Dictionary<string, object?>)modules[key: "ValidUrl"]!;
            Assert.AreEqual(expected: "https://example.com/a//b/*not-a-comment*/", actual: validUrl[key: "url"]?.ToString());

            List<object?> tags = (List<object?>)validUrl[key: "tags"]!;
            Assert.HasCount(expected: 2, collection: tags);
            Assert.AreEqual(expected: "alpha", actual: tags[index: 0]?.ToString());
            Assert.AreEqual(expected: "beta", actual: tags[index: 1]?.ToString());
        } finally {
            if (Directory.Exists(path: tempDir)) {
                Directory.Delete(path: tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void LoadJsonFile_ReturnsEmptyCaseInsensitiveDictionaryForEmptyCommentOnlyNonObjectAndMalformed() {
        string tempDir = Path.Combine(path1: Path.GetTempPath(), path2: $"RemakeEngine_JsonEdge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path: tempDir);

        try {
            string emptyFile = Path.Combine(path1: tempDir, path2: "empty.jsonc");
            string commentOnlyFile = Path.Combine(path1: tempDir, path2: "comments_only.jsonc");
            string arrayRootFile = Path.Combine(path1: tempDir, path2: "array.jsonc");
            string malformedFile = Path.Combine(path1: tempDir, path2: "malformed.jsonc");

            File.WriteAllText(path: emptyFile, contents: string.Empty);
            File.WriteAllText(path: commentOnlyFile, contents: """
            // Comment only JSONC
            /* Second comment */
            """);
            File.WriteAllText(path: arrayRootFile, contents: """[ 1, 2, ]""");
            File.WriteAllText(path: malformedFile, contents: """{ "unclosed": """);

            string[] cases = [
                emptyFile,
                commentOnlyFile,
                arrayRootFile,
                malformedFile,
                Path.Combine(path1: tempDir, path2: "does_not_exist.jsonc"),
                string.Empty
            ];

            foreach (string path in cases) {
                Dictionary<string, object?> dict = JsonHelpers.LoadJsonFile(filePath: path);
                Assert.IsEmpty(collection: dict);
                Assert.AreEqual(expected: StringComparer.OrdinalIgnoreCase, actual: dict.Comparer);
            }
        } finally {
            if (Directory.Exists(path: tempDir)) {
                Directory.Delete(path: tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ParseFileToPlainObject_SupportsArraysAndCommentOnlyFiles() {
        string tempDir = Path.Combine(path1: Path.GetTempPath(), path2: $"RemakeEngine_JsonPlain_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path: tempDir);

        try {
            string arrayFile = Path.Combine(path1: tempDir, path2: "ops.jsonc");
            File.WriteAllText(path: arrayFile, contents: """
            [
              // First item
              { "Name": "Extract", "id": 1, },
              /* Second item */
              { "Name": "Build", "id": 2, },
            ]
            """);

            object? plain = JsonHelpers.ParseFileToPlainObject(filePath: arrayFile);
            Assert.IsInstanceOfType<List<object?>>(value: plain);

            List<object?> items = (List<object?>)plain!;
            Assert.HasCount(expected: 2, collection: items);

            Dictionary<string, object?> first = (Dictionary<string, object?>)items[index: 0]!;
            Assert.AreEqual(expected: "Extract", actual: first[key: "name"]?.ToString());

            string commentOnlyFile = Path.Combine(path1: tempDir, path2: "empty_comments.jsonc");
            File.WriteAllText(path: commentOnlyFile, contents: "// only comments\n");
            object? commentOnlyResult = JsonHelpers.ParseFileToPlainObject(filePath: commentOnlyFile);
            Assert.IsInstanceOfType<Dictionary<string, object?>>(value: commentOnlyResult);
            Assert.IsEmpty(collection: (Dictionary<string, object?>)commentOnlyResult!);
        } finally {
            if (Directory.Exists(path: tempDir)) {
                Directory.Delete(path: tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ParseDocument_And_HasJsonContent_HandleBomCommentsAndTrailingCommas() {
        string withBomAndComments = "\uFEFF// header\n{ \"Url\": \"https://a.test//b\", /* note */ \"List\": [1, 2,], }";
        using JsonDocument doc = JsonHelpers.ParseDocument(json: withBomAndComments);
        Assert.AreEqual(expected: "https://a.test//b", actual: doc.RootElement.GetProperty(propertyName: "Url").GetString());
        Assert.AreEqual(expected: 2, actual: doc.RootElement.GetProperty(propertyName: "List").GetArrayLength());

        Assert.IsFalse(condition: JsonHelpers.HasJsonContent(json: "// just a comment\n/* block */   "));
        Assert.IsFalse(condition: JsonHelpers.HasJsonContent(json: "// eof comment without newline"));
        Assert.IsFalse(condition: JsonHelpers.HasJsonContent(json: "/* a */ /* b */"));
        Assert.IsNull(value: JsonHelpers.ParseDocumentOrNullIfEmpty(json: "\uFEFF  // comment only"));
        Assert.IsTrue(condition: JsonHelpers.HasJsonContent(json: "{}"));
        Assert.IsTrue(condition: JsonHelpers.HasJsonContent(json: "true"));
        Assert.IsTrue(condition: JsonHelpers.HasJsonContent(json: "123"));
        Assert.IsTrue(condition: JsonHelpers.HasJsonContent(json: "\"text\""));

        Assert.Throws<JsonException>(action: () => JsonHelpers.HasJsonContent(json: "/* unclosed"));
        Assert.Throws<JsonException>(action: () => JsonHelpers.HasJsonContent(json: "// ok\n/"));

        Dictionary<string, int>? deserialized = JsonHelpers.Deserialize<Dictionary<string, int>>(
            json: "\uFEFF// config\n{ \"One\": 1, /* trailing */ }"
        );
        Assert.IsNotNull(value: deserialized);
        Assert.AreEqual(expected: 1, actual: deserialized[key: "One"]);
        Assert.IsNull(value: JsonHelpers.Deserialize<Dictionary<string, int>>(json: "// comment only"));
    }

    [TestMethod]
    public void InternalToolRegistry_Assemble_SupportsJsonAndJsoncFragments() {
        string tempRoot = Path.Combine(path1: Path.GetTempPath(), path2: $"RemakeEngine_ToolsReg_{Guid.NewGuid():N}");
        string toolDir = Path.Combine(path1: tempRoot, path2: "SampleTool");
        Directory.CreateDirectory(path: toolDir);

        try {
            File.WriteAllText(path: Path.Combine(path1: toolDir, path2: "1.json"), contents: """
            {
              "1.0": {
                "win-x64": {
                  "url": "https://example.com/tool-win.zip",
                  "sha256": "abc"
                }
              }
            }
            """);

            File.WriteAllText(path: Path.Combine(path1: toolDir, path2: "2.jsonc"), contents: """
            {
              // JSONC version entry with trailing commas
              "2.0": {
                "linux-x64": {
                  "url": "https://example.com/tool-linux.tar.gz",
                  "sha256": "def",
                },
              },
            }
            """);

            File.WriteAllText(path: Path.Combine(path1: toolDir, path2: "notes.jsonc"), contents: "// placeholder comment-only fragment\n");

            Type? registryType = typeof(EngineNet.Core.ExternalTools.JsonToolResolver).Assembly.GetType(name: "EngineNet.Core.ExternalTools.InternalToolRegistry");
            Assert.IsNotNull(value: registryType);

            MethodInfo? assembleMethod = registryType.GetMethod(name: "Assemble", bindingAttr: BindingFlags.NonPublic | BindingFlags.Static, types: [typeof(string)]);
            Assert.IsNotNull(value: assembleMethod);

            Dictionary<string, object?>? assembled = (Dictionary<string, object?>?)assembleMethod.Invoke(obj: null, parameters: [tempRoot]);
            Assert.IsNotNull(value: assembled);
            Assert.IsTrue(condition: assembled.TryGetValue(key: "sampletool", out object? toolObj));

            Dictionary<string, object?> versions = (Dictionary<string, object?>)toolObj!;
            Assert.IsTrue(condition: versions.ContainsKey(key: "1.0"));
            Assert.IsTrue(condition: versions.ContainsKey(key: "2.0"));
        } finally {
            if (Directory.Exists(path: tempRoot)) {
                Directory.Delete(path: tempRoot, recursive: true);
            }
        }
    }
}
