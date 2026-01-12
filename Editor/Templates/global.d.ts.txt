// OneJS runtime globals (provided by QuickJSBootstrap.js)
// Note: CS.* types and ES6 module declarations are provided by unity-types package

// Root UI element
declare const __root: CS.UnityEngine.UIElements.VisualElement;

// Event API for UI Toolkit elements
declare const __eventAPI: {
    addEventListener: (element: CS.UnityEngine.UIElements.VisualElement, eventType: string, callback: Function) => void;
    removeEventListener: (element: CS.UnityEngine.UIElements.VisualElement, eventType: string, callback: Function) => void;
    removeAllEventListeners: (element: CS.UnityEngine.UIElements.VisualElement) => void;
};

// Get System.Type from a Unity/C# class constructor
// Usage: go.AddComponent($typeof(MeshFilter))
// Note: With improved interop, you can often pass types directly: go.AddComponent(MeshFilter)
declare function $typeof<T>(type: { new(...args: any[]): T } | T): CS.System.Type;

// Extend GameObject.AddComponent to accept constructor types directly
declare namespace CS.UnityEngine {
    interface GameObject {
        AddComponent<T extends CS.UnityEngine.Component>(type: { new(...args: any[]): T }): T;
    }
}

// All C# objects wrapped by OneJS have these handle properties at runtime
// This allows VisualElement to be passed to render() without type errors
declare namespace CS.UnityEngine.UIElements {
    interface VisualElement {
        __csHandle: number;
        __csType: string;
    }
}

// C# object helpers
declare const __csHelpers: {
    newObject: (typeName: string, ...args: unknown[]) => unknown;
    callMethod: (obj: unknown, methodName: string, ...args: unknown[]) => unknown;
    callStatic: (typeName: string, methodName: string, ...args: unknown[]) => unknown;
    wrapObject: (typeName: string, handle: number) => unknown;
    releaseObject: (obj: unknown) => void;
};

// Console (provided by QuickJS)
declare const console: {
    log: (...args: unknown[]) => void;
    error: (...args: unknown[]) => void;
    warn: (...args: unknown[]) => void;
};

// Timers (provided by QuickJSBootstrap.js)
declare function setTimeout(callback: () => void, ms?: number): number;
declare function clearTimeout(id: number): void;
declare function setInterval(callback: () => void, ms?: number): number;
declare function clearInterval(id: number): void;
declare function requestAnimationFrame(callback: (timestamp: number) => void): number;
declare function cancelAnimationFrame(id: number): void;
declare function queueMicrotask(callback: () => void): void;

declare const performance: {
    now: () => number;
};

// StyleSheet API
declare function loadStyleSheet(path: string): boolean;
declare function compileStyleSheet(ussContent: string, name?: string): boolean;
declare function removeStyleSheet(name: string): boolean;
declare function clearStyleSheets(): number;

// FileSystem API - Path globals
/** Application.persistentDataPath - User-writable storage that persists across sessions */
declare const __persistentDataPath: string;
/** Application.streamingAssetsPath - Read-only assets bundled with the app */
declare const __streamingAssetsPath: string;
/** Application.dataPath - Path to game data folder (Editor: Assets, Build: Data) */
declare const __dataPath: string;
/** Application.temporaryCachePath - Temporary cache directory */
declare const __temporaryCachePath: string;

// FileSystem API - Functions
/**
 * Read a text file from an absolute path.
 * Works in Editor and standalone builds.
 * @param path - Absolute path to the file
 * @returns File contents
 * @throws If file doesn't exist or cannot be read
 * @example
 * const uss = await readTextFile(`${__persistentDataPath}/themes/dark.uss`);
 * compileStyleSheet(uss, "user-theme");
 */
declare function readTextFile(path: string): Promise<string>;

/**
 * Write text to a file at an absolute path.
 * Creates the file if it doesn't exist, overwrites if it does.
 * Automatically creates parent directories.
 * @param path - Absolute path to the file
 * @param content - Content to write
 * @example
 * await writeTextFile(`${__persistentDataPath}/prefs.json`, JSON.stringify(prefs));
 */
declare function writeTextFile(path: string, content: string): Promise<void>;

/**
 * Check if a file exists at the given path.
 * @param path - Absolute path to check
 * @returns True if file exists
 */
declare function fileExists(path: string): boolean;

/**
 * Check if a directory exists at the given path.
 * @param path - Absolute path to check
 * @returns True if directory exists
 */
declare function directoryExists(path: string): boolean;

/**
 * Delete a file at the given path.
 * @param path - Absolute path to the file
 * @returns True if file was deleted, false if it didn't exist
 */
declare function deleteFile(path: string): boolean;

/**
 * List files in a directory matching an optional pattern.
 * @param path - Directory path
 * @param pattern - Search pattern (e.g., "*.uss", "*.json"). Default is "*"
 * @param recursive - Search subdirectories. Default is false
 * @returns Array of file paths
 */
declare function listFiles(path: string, pattern?: string, recursive?: boolean): string[];
