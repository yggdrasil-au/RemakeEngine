// JavaScript feature showcase for the RemakeEngine demo module.
// Demonstrates the Jint C# interop APIs available from JsAction.cs

console.log("=== RemakeEngine JS API Comprehensive Demo ===");

// 1. Arguments and Basic Globals
console.log("--- Globals & Arguments ---");
console.log("Argument Count (argc): " + argc);
console.log("Arguments (argv): " + JSON.stringify(argv));
console.log("Game_Root: " + Game_Root);
console.log("Project_Root: " + Project_Root);
console.log("script_dir: " + script_dir);
console.log("DEBUG mode: " + DEBUG);

// 2. Logging and Diagnostics
console.log("--- Logging & Diagnostics ---");
console.log("This is a standard log (console.log)");
console.warn("This is a standard warning (console.warn)");
console.error("This is a standard error (console.error)");

warn("This is a direct engine warning (warn)");
error("This is a direct engine error (error)");

if (typeof Diagnostics !== "undefined") {
    Diagnostics.Log("This is a Diagnostics.Log message");
    Diagnostics.Trace("This is a Diagnostics.Trace message");
}

// 3. Tool Resolution
console.log("--- Tool Resolution ---");
try {
    var testToolId = "vgmstream-cli";
    var resolvedTool = tool(testToolId, null);
    console.log("Resolved tool (" + testToolId + "): " + resolvedTool);

    var resolvedPath = ResolveToolPath("Blender", null);
    console.log("Resolved tool path (Blender): " + resolvedPath);
} catch (e) {
    console.error("Error resolving tool: " + e.message);
}

// 4. Progress System
console.log("--- Progress Tracking Examples ---");

// Script Progress
progress.start(10, 'JS API Demo Execution');

progress.step('Initializing demo components');
progress.add_steps(2); // Optionally add more steps dynamically

progress.step('Running inner tasks');

// Panel Progress
console.log("--- Running PanelProgress Demo ---");
var p = progress.new(100, "js-demo-idx", "Simulating JS background task...");
for (var i = 0; i < 100; i++) {
    // Calling C# methods exposed via Jint. PanelProgress.Update takes one parameter (amount)
    p.Update(1);
}
p.Complete(); // Finish the panel task

// Finish Script Progress
progress.step('Finalizing JS Demo');
progress.finish();

// 5. User Prompts (Interactive)
console.log("--- User Prompts ---");
console.log("Skipping interactive prompts so script can run autonomously, but APIs are available:");
console.log("- prompt(message, id, secret)");
console.log("- color_prompt(message, color, id, secret)");
console.log("- colour_prompt(message, color, id, secret)");

console.log("=== JS Demo Complete ===");

