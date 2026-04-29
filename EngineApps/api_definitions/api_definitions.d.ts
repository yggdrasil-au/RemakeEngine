/**
 *  Engine Scripting Environment Type Definitions (Jint)
 *  This file provides autocompletion and type checking for the custom engine API.
 *
 *  IDE hint functionallity tested in vscode with microsoft ESLint extension
 */

// =============================================================================
// 1. Custom Objects & Interfaces
// =============================================================================

/**
 * Represents a long-running background task whose progress is displayed in the lower TUI/GUI panel.
 */
declare interface PanelProgress {
    /**
     * The total number of generic "items" to process.
     */
    Total: number;

    /**
     * The current number of items completed.
     */
    readonly Current: number;

    /**
     * The unique identifier for this progress tracking panel.
     */
    readonly Id: string;

    Label: string | null;

    /**
     * Signal that the background task is fully complete and destroy the UI element.
     */
    Complete(): void;

    /**
     * Disposes the progress handle (same as Complete()).
     */
    Dispose(): void;

    /**
     * Manually add to the internal processed counter.
     * @param inc Amount to increment (defaults to 1).
     */
    Update(inc?: number): void;

    /**
     * Sets the total number of items to process.
     * @param total The total count.
     */
    SetTotal(total: number): void;

    /**
     * Sets the label for the panel.
     * @param label The label text.
     */
    SetLabel(label: string): void;
}

/**
 * Script execution specific progress model for overarching operation checkpoints
 */
declare interface ScriptProgress {
    /**
     * Update the overarching script progress by `inc` ticks. Emits the visual label if provided.
     * @param inc Tick amount.
     * @param label Optional text describing the current tick.
     */
    Update(inc: number, label?: string): void;

    /**
     * Add to the total available ticks in the script progress limit before it is considered done.
     * @param total The total to set.
     */
    SetTotal(total: number): void;

    /**
     * Call to indicate script sequence is perfectly complete.
     */
    Complete(): void;
}

/**
 * Fully controllable console progress panel.
 */
declare interface ConsoleProgress {
    /** Returns the progress id. */
    GetId(): string;

    /** Returns the total count. */
    GetTotal(): number;

    /** Returns the processed count. */
    GetProcessed(): number;

    /** Returns the ok count. */
    GetOk(): number;

    /** Returns the skip count. */
    GetSkip(): number;

    /** Returns the error count. */
    GetErr(): number;

    /** Returns the current label. */
    GetLabel(): string;

    /** Sets the total count. */
    SetTotal(total: number): void;

    /** Sets the label text. */
    SetLabel(label: string): void;

    /** Sets the counter values. */
    SetStats(processed: number, ok: number, skip: number, err: number): void;

    /** Updates the counters by the provided increments. */
    Update(processed?: number, ok?: number, skip?: number, err?: number): void;

    /** Adds an active job entry. */
    AddJob(tool: string, file: string): void;

    /** Removes an active job entry by file name. */
    RemoveJob(file: string): void;

    /** Completes and closes the panel. */
    Complete(): void;

    /** Disposes the progress handle (same as Complete()). */
    Dispose(): void;
}

// =============================================================================
// 2. Global Variables
// =============================================================================

/**
 * Argument variables passed from the execution CLI/GUI.
 */
declare const argv: string[];

/**
 * Total list size of parsed arguments
 */
declare const argc: number;

/**
 * Absolute system path pointing to the currently processing Game Module.
 */
declare const Game_Root: string;

/**
 * Absolute system path pointing to the host RemakeEngine solution container root.
 */
declare const Project_Root: string;

/**
 * The directory that fundamentally encloses the script natively executing.
 */
declare const script_dir: string;

/**
 * Flag denoting if the main `.NET` engine context is currently running within a Debug configuration block.
 */
declare const DEBUG: boolean;

// =============================================================================
// 3. Global Functions
// =============================================================================

/**
 * Raise a stylized standard WARNING using the Engine SDK pipeline.
 * @param message The message to print.
 */
declare function warn(message: string): void;

/**
 * Raise a stylized standard ERROR using the Engine SDK pipeline.
 * @param message The message to print.
 */
declare function error(message: string): void;

/**
 * Halt the script synchronously and prompt the user directly via standard I/O streams or GUI box.
 * @param message The user-facing question/request.
 * @param id Optional identifier for cached telemetry tracking (default `"q1"`).
 * @param secret If true, conceals the typed keys entirely (used for sensitive variables).
 * @returns The string implicitly written by the user.
 */
declare function prompt(message: string, id?: string, secret?: boolean): string;

/**
 * Color-variated alternative of the built-in script prompter object.
 * @param message The user-facing question/request.
 * @param color Defined standard .NET string color type (Eg `"darkred"`, `"cyan"`).
 * @param id Optional identifier.
 * @param secret Optionally conceal characters.
 * @returns The string implicitly written by the user.
 */
declare function color_prompt(message: string, color: string, id?: string, secret?: boolean): string;

/**
 * AU/UK English spelling mapping for color prompts.
 */
declare function colour_prompt(message: string, color: string, id?: string, secret?: boolean): string;

/**
 * Retrieves the fully qualified path of an internally managed third-party external Tool.
 * Required for calling tools packaged directly into `GlobalTools.json`.
 * @param id Exact casing tool ID as present in the game module or engine scope (e.g., `'Godot'`, `'Blender'`, `'vgmstream-cli'`).
 * @param ver Specific semantic version string to look up. `null` to query largest version available.
 * @returns Built path resolving execution root of Tool.
 */
declare function tool(id: string, ver?: string | null): string;

/**
 * Duplicate of `tool`. Retrieves the fully qualified path of an internally managed third-party external Tool.
 * @param id Tool ID.
 * @param ver Version limit.
 * @returns Resolving executable Tool path.
 */
declare function ResolveToolPath(id: string, ver?: string | null): string;

// =============================================================================
// 4. Exposed Standard Objects
// =============================================================================

/**
 * Standard output logger mapping to the engine's internal streams.
 */
declare namespace console {
    /**
     * Prints a standard log line to the engine output.
     * @param message Message to output.
     */
    function log(message: any): void;

    /**
     * Prints a warning line to the engine output.
     * @param message Warning message to output.
     */
    function warn(message: any): void;

    /**
     * Prints an error line to the engine output.
     * @param message Error message to output.
     */
    function error(message: any): void;
}

declare namespace Diagnostics {
    /** Write pure core-engine logging traces dynamically. */
    function Log(message: string): void;

    /** Execute pure framework stack tracing routines. */
    function Trace(message: string): void;
}

declare namespace progress {
    /**
     * Script-wide progress helpers (GUI only).
     */
    namespace script {
        function start(total: number, label?: string): ScriptProgress;
        function step(label?: string): void;
        function add_steps(count: number): void;
        function finish(): void;
    }

    /**
     * Standard panel progress helpers.
     */
    namespace panel {
        function _new(total: number, id?: string, label?: string): PanelProgress;
        export { _new as new };
    }

    /**
     * Advanced console panel helpers.
     */
    namespace console {
        function _new(total: number, id?: string, label?: string): ConsoleProgress;
        export { _new as new };
    }
}

