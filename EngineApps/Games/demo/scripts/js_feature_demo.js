// example of explicitly referencing the API definitions when ide fails to pick them up
/// <reference path="../../../api_definitions/api_definitions.d.ts" />

// JavaScript feature showcase for the RemakeEngine demo module.
// Reimplemented from lua_feature_demo.lua to demonstrate JS script engine capabilities.
// Demonstrates the Jint C# interop APIs available from JsAction.cs

console.log("=== RemakeEngine JS API Comprehensive Demo ===");

/**
 * Basic argument parsing helper to mirror the Lua demo's parse_args.
 */
function parseArgs(args) {
    const opts = {};
    for (let i = 0; i < args.length; i++) {
        if (args[i].startsWith('--')) {
            const key = args[i].substring(2);
            const val = args[i + 1];
            if (val && !val.startsWith('--')) {
                opts[key] = val;
                i++;
            } else {
                opts[key] = true;
            }
        }
    }
    return opts;
}

// 1. Arguments and Basic Globals
console.log("--- Globals & Arguments ---");
const opts = parseArgs(argv || []);
const module_root = opts.module || Game_Root || '.';
const scratch_root = opts.scratch || (module_root + '/TMP/js-demo-comprehensive');
const note = opts.note || 'Comprehensive JS API demo';

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

// Diagnostics.Log is currently not exposed in JsAction.cs but planned
// if (typeof Diagnostics !== "undefined") {
//     Diagnostics.Log("This is a Diagnostics.Log message");
// }

// 3. Tool Resolution
console.log("--- Tool Resolution ---");
try {
    const testToolId = "vgmstream-cli";
    const resolvedTool = tool(testToolId, null);
    console.log("Resolved tool (" + testToolId + "): " + resolvedTool);

    const resolvedPath = ResolveToolPath("Blender", null);
    console.log("Resolved tool path (Blender): " + resolvedPath);
} catch (e) {
    console.error("Error resolving tool: " + e.message);
}

// 4. Progress System
console.log("--- Progress Tracking Examples ---");

// Script Progress (Main stage tracking)
progress.start(5, 'JS API Demo Execution');

progress.step('Initializing demo components');
// progress.add_steps(2); // Optionally add more steps dynamically

progress.step('Running PanelProgress simulations');

// Panel Progress (Visual background tasks)
console.log("--- Running PanelProgress Demo ---");
// progress.new(total, id, label)
const p = progress.new(100, "js-demo-idx", "Simulating JS background task...");
for (let i = 0; i < 100; i++) {
    // Calling C# methods exposed via Jint. PanelProgress.Update takes one parameter (amount)
    p.Update(1);
    // sdk.sleep(0.01); // Not yet implemented, but planned
}
p.Complete(); // Finish the panel task

progress.step('Concurrent Panels Demo');
const task1 = progress.new(50, 'worker-1', 'JS CPU Intensive Task');
const task2 = progress.new(50, 'worker-2', 'JS Network Download Simulation');

for (let i = 0; i < 50; i++) {
    task1.Update(1);
    task2.Update(1);
}
task1.Complete();
task2.Complete();

// 5. UPCOMING FEATURES (Commented out mapping to Lua SDK)
/*
progress.step('Testing Upcoming SDK Features (Planned)');

console.log("--- Upcoming SDK Path Joining ---");
// const demo_path = join(Game_Root, 'TMP', 'js-demo', 'test.txt');

console.log("--- Upcoming SDK File Operations ---");
// sdk.ensure_dir(scratch_root);
// if (sdk.path_exists(scratch_root)) { ... }

console.log("--- Upcoming SQLite Module ---");
// const db = sqlite.open(scratch_root + "/test.db");
// db.exec("CREATE TABLE test (id INTEGER PRIMARY KEY, name TEXT)");
*/

// 6. User Prompts (Interactive)
console.log("--- User Prompts ---");
console.log("Prompts are available but skipped for automated runs:");
// const userInput = prompt("Enter a message for the JS demo:", "js_demo_prompt", false);
// console.log("User said: " + userInput);

progress.step('Finalizing JS Demo');
progress.finish();

console.log("=== JS Demo Complete ===");


