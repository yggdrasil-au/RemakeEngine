// ------------------------------------------------------------
// File: EngineNet.Tests/EngineConfigTests.cs
// Purpose: Unit tests for EngineNet.EngineConfig using xUnit.
// Notes:
//  - These tests validate JSON parsing, case-insensitive keys,
//    nested object handling, reload behavior, and error handling.
// ------------------------------------------------------------


namespace EngineNet.Tests {
    /// <summary>
    /// Test suite for <see cref="EngineNet.EngineConfig"/>.
    /// Each test is a self-contained scenario that verifies a specific behavior.
    /// </summary>
    public class EngineConfigTests {
        /// <summary>
        /// Verifies that the loader:
        /// 1) Parses nested JSON objects,
        /// 2) Applies case-insensitive keys,
        /// 3) Converts JSON numbers to Int64 (when they fit),
        /// 4) Converts booleans correctly.
        /// </summary>
        [Fact]
        public void LoadJsonFile_ParsesNested() {
            // ARRANGE (Step 1): Create a temporary file with nested JSON content.
            // The TempFile helper writes content to a unique path and deletes it on Dispose.
            using TempFile tmp = new TempFile("{\n  \"Foo\": { \"Bar\": 123 },\n  \"Flag\": true\n}\n");

            // ARRANGE (Step 2): Construct EngineConfig pointing at the temp file path.
            // The constructor will call Reload(), which loads the JSON immediately.
            Core.EngineConfig cfg = new Core.EngineConfig(tmp.Path);

            // ASSERT (Step 3): The root key "Foo" should be present (case-insensitive), normalized by our dictionary comparer.
            Assert.True(cfg.Data.ContainsKey("foo"));

            // ASSERT (Step 4): The "foo" value should be a dictionary (nested object), not a string or number.
            Dictionary<string, object?> foo = Assert.IsType<Dictionary<string, object?>>(cfg.Data["foo"]!);

            // ASSERT (Step 5): Inside "foo", key "bar" should be 123 as Int64 (xUnit shows it as 123L).
            Assert.Equal(123L, foo["bar"]);

            // ASSERT (Step 6): "Flag" should be parsed as boolean true.
            Assert.Equal(true, cfg.Data["flag"]);
        }

        /// <summary>
        /// Verifies that calling <see cref="EngineNet.EngineConfig.Reload"/>:
        /// 1) Re-reads the file,
        /// 2) Reflects updated values,
        /// 3) Adds newly introduced keys,
        /// 4) Maintains case-insensitive access (e.g., "FOO" vs "foo").
        /// </summary>
        [Fact]
        public void Reload_ReflectsLatestFileContents() {
            // ARRANGE (Step 1): Create a file with initial content.
            using TempFile tmp = new TempFile("{\n  \"Foo\": \"Alpha\",\n  \"Flag\": false\n}\n");

            // ARRANGE (Step 2): Load the config.
            Core.EngineConfig cfg = new Core.EngineConfig(tmp.Path);

            // ASSERT (Step 3): Confirm initial value is present before reload.
            Assert.Equal("Alpha", cfg.Data["foo"]);

            // ACT (Step 4): Overwrite the file with new content to simulate an external change.
            File.WriteAllText(tmp.Path, "{\n  \"Foo\": \"Beta\",\n  \"Flag\": true,\n  \"Extra\": 5\n}\n");

            // ACT (Step 5): Reload to pick up the latest file content.
            cfg.Reload();

            // ASSERT (Step 6): New value should be visible (also check case-insensitive retrieval).
            Assert.Equal("Beta", cfg.Data["FOO"]);

            // ASSERT (Step 7): Flag changed from false to true.
            Assert.Equal(true, cfg.Data["flag"]);

            // ASSERT (Step 8): New key "Extra" is added and parsed as Int64.
            Assert.Equal(5L, cfg.Data["extra"]);
        }

        /// <summary>
        /// Verifies that <see cref="EngineNet.EngineConfig.LoadJsonFile(string)"/> returns an empty dictionary
        /// when the file is missing or contains invalid JSON, instead of throwing.
        /// </summary>
        [Fact]
        public void LoadJsonFile_ReturnsEmpty_WhenMissingOrInvalid() {
            // ARRANGE (Step 1): Build a temp path that does not exist.
            string missingPath = Path.Combine(Path.GetTempPath(), "engcfg_missing_" + Guid.NewGuid().ToString("N") + ".json");

            // ACT (Step 2): Attempt to load a missing file.
            Dictionary<string, object?> missing = Core.EngineConfig.LoadJsonFile(missingPath);

            // ASSERT (Step 3): We should get an empty dictionary rather than an exception or null.
            Assert.Empty(missing);

            // ARRANGE (Step 4): Create a file with deliberately invalid JSON.
            using TempFile tmp = new TempFile("{ invalid json");

            // ACT (Step 5): Attempt to load invalid JSON.
            Dictionary<string, object?> invalid = Core.EngineConfig.LoadJsonFile(tmp.Path);

            // ASSERT (Step 6): Again, should get an empty dictionary.
            Assert.Empty(invalid);
        }

        /// <summary>
        /// Small helper that writes content to a unique temp file and deletes it when disposed.
        /// This keeps tests isolated and leaves no files behind on success.
        /// </summary>
        private sealed class TempFile:IDisposable {
            /// <summary>Full file system path to the created temp file.</summary>
            public string Path {
                get;
            }

            /// <summary>
            /// Creates the file and writes the supplied content.
            /// </summary>
            public TempFile(string content) {
                Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enginenet_cfg_" + Guid.NewGuid().ToString("N") + ".json");
                File.WriteAllText(Path, content); // Step: Write supplied test JSON content
            }

            /// <summary>
            /// Deletes the file on dispose; errors during deletion are swallowed to keep teardown robust.
            /// </summary>
            public void Dispose() {
                try {
                    File.Delete(Path); // Step: Best-effort cleanup
                } catch { /* Ignore cleanup errors to avoid masking test results */ }
            }
        }
    }
}
