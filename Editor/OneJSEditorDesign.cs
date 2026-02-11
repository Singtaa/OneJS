using UnityEngine;

namespace OneJS.Editor {

/// <summary>
/// Centralized design tokens for OneJS editor UIs. Use these instead of hardcoding colors or text.
/// </summary>
public static class OneJSEditorDesign {

    /// <summary>
    /// Color palette for the OneJS editor UI. All values are in 0-1 range.
    /// </summary>
    public static class Colors {

        // --- Surfaces ---
        /// <summary>Tab content background, active tab, info box content.</summary>
        public static readonly Color ContentBg = new Color(0.22f, 0.22f, 0.22f);
        /// <summary>Styled box (status block) background.</summary>
        public static readonly Color BoxBg = new Color(0.24f, 0.24f, 0.24f);
        /// <summary>Info message container background.</summary>
        public static readonly Color InfoBoxBg = new Color(0.28f, 0.28f, 0.28f);
        /// <summary>Inactive tab background.</summary>
        public static readonly Color TabInactive = new Color(0.2f, 0.2f, 0.2f);
        /// <summary>Tab hover state.</summary>
        public static readonly Color TabHover = new Color(0.26f, 0.26f, 0.26f);
        /// <summary>Cartridge/row item background.</summary>
        public static readonly Color RowBg = new Color(0.22f, 0.22f, 0.22f);

        // --- Borders ---
        /// <summary>Standard border color for tabs, boxes, dividers.</summary>
        public static readonly Color Border = new Color(0.14f, 0.14f, 0.14f);

        // --- Text ---
        /// <summary>Muted text (secondary labels, empty placeholders).</summary>
        public static readonly Color TextMuted = new Color(0.6f, 0.6f, 0.6f);
        /// <summary>Dim text (index labels, separators, arrows).</summary>
        public static readonly Color TextDim = new Color(0.5f, 0.5f, 0.5f);
        /// <summary>Info/message text (help boxes, descriptions).</summary>
        public static readonly Color TextInfo = new Color(0.72f, 0.72f, 0.72f);
        /// <summary>Neutral status text (e.g., Not Initialized).</summary>
        public static readonly Color TextNeutral = new Color(0.7f, 0.7f, 0.7f);

        // --- Status ---
        /// <summary>Success / running / extracted (green).</summary>
        public static readonly Color StatusSuccess = new Color(0.4f, 0.8f, 0.4f);
        /// <summary>Alternative success (type gen).</summary>
        public static readonly Color StatusSuccessAlt = new Color(0.2f, 0.8f, 0.2f);
        /// <summary>Warning / loading (yellow-amber).</summary>
        public static readonly Color StatusWarning = new Color(0.8f, 0.6f, 0.2f);
        /// <summary>Error / failure (red).</summary>
        public static readonly Color StatusError = new Color(0.8f, 0.4f, 0.4f);
        /// <summary>Stopped state (cyan).</summary>
        public static readonly Color StatusStopped = new Color(0.28f, 0.82f, 0.82f);
        /// <summary>Running state (green).</summary>
        public static readonly Color StatusRunning = new Color(0.2f, 0.8f, 0.2f);

        // --- Buttons ---
        /// <summary>Primary action button background (e.g., Initialize, Remove Settings).</summary>
        public static readonly Color ButtonPrimaryBg = new Color(0.72f, 0.72f, 0.72f);
        /// <summary>Primary button hover.</summary>
        public static readonly Color ButtonPrimaryHover = new Color(0.82f, 0.82f, 0.82f);
        /// <summary>Primary button text (dark on light).</summary>
        public static readonly Color ButtonPrimaryText = new Color(0.18f, 0.18f, 0.18f);
        /// <summary>Danger/remove button background.</summary>
        public static readonly Color ButtonDanger = new Color(0.5f, 0.2f, 0.2f);

        // --- UICartridgeEditor specific (blue theme) ---
        public static readonly Color CartridgeHeaderBg = new Color(0.18f, 0.28f, 0.38f);
        public static readonly Color CartridgePathPreview = new Color(0.7f, 0.85f, 1f);
        public static readonly Color CartridgePathWarning = new Color(0.8f, 0.6f, 0.4f);
        public static readonly Color CartridgeAddBtn = new Color(0.2f, 0.4f, 0.3f);
        public static readonly Color CartridgeRemoveBtn = new Color(0.4f, 0.2f, 0.2f);

        // --- JSPadEditor specific ---
        public static readonly Color TextInputBg = new Color(0.15f, 0.15f, 0.15f);
        public static readonly Color Separator = new Color(0.3f, 0.3f, 0.3f);
        public static readonly Color ErrorText = new Color(0.9f, 0.3f, 0.3f);
    }

    /// <summary>
    /// Repeated text labels and messages for the OneJS editor UI.
    /// </summary>
    public static class Texts {

        // --- Status ---
        public const string Status = "Status";
        public const string Stopped = "Stopped";
        public const string Running = "Running";
        public const string Loading = "Loading...";
        public const string NotInitialized = "Not Initialized";
        public const string NotValid = "Not Valid";

        // --- Actions ---
        public const string Reload = "Reload";
        public const string Rebuild = "Rebuild";
        public const string Rebuilding = "Rebuilding...";
        public const string InitializeProject = "Initialize Project";
        public const string RemoveSettings = "Remove Settings";

        // --- Tabs ---
        public const string TabProject = "Project";
        public const string TabUI = "UI";
        public const string TabCartridges = "Cartridges";
        public const string TabBuild = "Build";

        // --- Section headers ---
        public const string BuildOutput = "Build Output";
        public const string TypeGeneration = "Type Generation";
        public const string Scaffolding = "Scaffolding";
        public const string PanelSettings = "Panel Settings";
        public const string Stylesheets = "Stylesheets";
        public const string Preloads = "Preloads";
        public const string Globals = "Globals";
        public const string LiveReload = "Live Reload";
        public const string UICartridges = "UI Cartridges";

        // --- Empty states ---
        public const string NoStylesheets = "No stylesheets. Click + to add one.";
        public const string NoPreloads = "No preloads. Click + to add one.";
        public const string NoGlobals = "No globals. Click + to add one.";
        public const string NoCartridges = "No cartridges. Click + to add one.";
        public const string NoSettings = "No settings.";

        // --- Buttons ---
        public const string GenerateTypesNow = "Generate Types Now";
        public const string ResetToDefaults = "Reset to Defaults";
        public const string ExtractAll = "Extract All";
        public const string DeleteAllExtracted = "Delete All Extracted";

        // --- Watcher ---
        public const string Watcher = "Watcher: ";
        public const string WatcherStarting = "Starting...";
        public const string WatcherIdle = "Idle (enter Play Mode to run)";
        public const string NonInitializedYet = "Not initialized yet";

        // --- Misc ---
        public const string ProjectFolder = "Project Folder: ";
        public const string LastReload = "Last Reload: ";
        public const string Output = "Output: ";
        public const string Extracted = "Extracted";
        public const string NotExtracted = "Not extracted";
        public const string NoSlug = "No slug";
        public const string NotGenerated = "Not generated";

        // --- JSPad specific ---
        public const string Processing = "Processing...";
        public const string Ready = "Ready";
        public const string NotBuilt = "Not built";
        public const string NoModules = "No additional modules. Click + to add one.";
    }
}

}
