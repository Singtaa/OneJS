using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace OneJS.Editor {
    /// <summary>
    /// The inspector of a runner linked to a root cart (Specs/ROOT_CARTS.md, section 4 and the
    /// mockups in Specs/root-carts), and the cart picker the legacy inspector shows a new runner
    /// when the project root holds carts. A runner with no cart set gets the legacy inspector,
    /// built by JSRunnerEditor.cs exactly as before carts.
    /// </summary>
    public partial class JSRunnerEditor {
        const string CartTabPrefKey = "JSRunner.ActiveCartTab";

        /// <summary>What the legacy inspector says to a runner with no Panel Settings.</summary>
        const string NotInitializedMessage = "Initialize a new OneJS project, or assign a Panel Settings asset. The assigned asset’s folder becomes the active project folder for this runner.";

        VisualElement _root;
        bool _cartMode;

        // Legacy inspector: the empty state's message and its cart picker
        Label _notInitMessage;
        VisualElement _emptyCartPicker;

        // Cart inspector
        Label _cartStatusLabel;
        VisualElement _cartStatusLines;
        Button _cartStatusButton;
        Action _cartStatusAction;
        string _cartStatusKey;
        Button[] _cartFolderButtons;
        Label _cartReloadLabel;
        Label _cartWatcherLabel;
        Label _cartFolderLabel;
        VisualElement _cartRunningLines;
        Button _cartPickerButton;
        Label _cartInfoLabel;
        VisualElement _cartProblems;
        string _cartPickerKey;

        /// <summary>Builds the other inspector after the runner changed kind.</summary>
        void RebuildInspector() {
            _root.Clear();
            _cartMode = _target.IsCart;
            _statusLabel = null;
            _cartStatusLabel = null;
            _cartStatusKey = null;
            _cartPickerKey = null;
            if (_cartMode) {
                BuildCartInspector(_root);
            } else {
                _activeTab = EditorPrefs.GetInt(TabPrefKey, 0);
                BuildLegacyInspector(_root);
            }
            AddKeepUIDocumentNote(_root);
            _root.Bind(serializedObject);
        }

        // The cart inspector

        void BuildCartInspector(VisualElement root) {
            _activeTab = Mathf.Clamp(EditorPrefs.GetInt(CartTabPrefKey, 0), 0, 2);

            root.Add(CreateCartStatusSection());

            var actions = CreateRow();
            actions.style.marginTop = 8;
            _reloadButton = new Button(() => _target.ForceReload()) { text = OneJSEditorDesign.Texts.Reload, tooltip = "Reload/Rerun this JavaScript runtime" };
            _buildButton = new Button(RunCartRebuild) { text = OneJSEditorDesign.Texts.Rebuild, tooltip = "Delete the cart's node_modules, install it again, then build" };
            var openFolder = new Button(OpenWorkingDirectory) { text = "Open Folder", tooltip = "Open the cart's folder in the file explorer." };
            var openTerminal = new Button(OpenTerminal) { text = "Open Terminal", tooltip = "Open a terminal in the cart's folder" };
            _cartFolderButtons = new[] { openFolder, openTerminal };
            foreach (var b in new[] { _reloadButton, _buildButton, openFolder, openTerminal }) {
                b.style.height = 24;
                b.style.flexGrow = 1;
                actions.Add(b);
            }
            root.Add(actions);

            CreateInfoBoxWithLabel(out _buildOutputContainer, out _buildOutputLabel);
            _buildOutputContainer.style.marginTop = 5;
            _buildOutputContainer.style.display = DisplayStyle.None;
            root.Add(_buildOutputContainer);

            var tabs = new VisualElement();
            tabs.style.flexDirection = FlexDirection.Row;
            tabs.style.marginTop = 8;
            _tabBarContainer = CreateTabBar(tabs,
                new[] { OneJSEditorDesign.Texts.TabCart, OneJSEditorDesign.Texts.TabUI, OneJSEditorDesign.Texts.TabBuild },
                ShowCartTab);
            root.Add(_tabBarContainer);

            _tabContent = CreateTabContent();
            root.Add(_tabContent);
            ShowCartTab(_activeTab);

            UpdateCartUI();
        }

        void ShowCartTab(int index) {
            _activeTab = index;
            EditorPrefs.SetInt(CartTabPrefKey, index);
            UpdateTabStyles();
            _tabContent.Clear();
            _cartPickerKey = null;
            switch (index) {
                case 0: BuildCartTab(_tabContent); break;
                case 1: BuildCartUITab(_tabContent); break;
                case 2: BuildCartBuildTab(_tabContent); break;
            }
            _tabContent.Bind(serializedObject);
        }

        VisualElement CreateCartStatusSection() {
            var box = CreateStyledBox();
            box.style.marginTop = 2;

            var split = CreateRow();
            split.style.alignItems = Align.Center;
            var left = new VisualElement();
            left.style.flexGrow = 1;
            left.style.flexShrink = 1;
            left.style.minWidth = 0;
            left.style.marginRight = 12;

            var statusRow = CreateRow();
            statusRow.Add(CreateLabel(OneJSEditorDesign.Texts.Status, 50, true));
            _cartStatusLabel = new Label();
            statusRow.Add(_cartStatusLabel);
            left.Add(statusRow);

            _cartStatusLines = new VisualElement();
            left.Add(_cartStatusLines);

            _cartRunningLines = new VisualElement();
            _cartReloadLabel = SmallLabel();
            _cartWatcherLabel = SmallLabel();
            _cartWatcherLabel.enableRichText = true;
            _cartFolderLabel = SmallLabel();
            _cartFolderLabel.enableRichText = true;
            _cartRunningLines.Add(_cartReloadLabel);
            _cartRunningLines.Add(_cartWatcherLabel);
            _cartRunningLines.Add(_cartFolderLabel);
            left.Add(_cartRunningLines);
            split.Add(left);

            _cartStatusButton = PrimaryButton("", () => _cartStatusAction?.Invoke());
            split.Add(_cartStatusButton);
            box.Add(split);
            return box;
        }

        void BuildCartTab(VisualElement container) {
            container.Add(CreateCartPickerRow());
            _cartInfoLabel = SmallLabel();
            _cartInfoLabel.style.marginLeft = 120;
            _cartInfoLabel.style.marginBottom = 4;
            container.Add(_cartInfoLabel);
            _cartProblems = new VisualElement();
            container.Add(_cartProblems);
            AddSpacer(container, 6);
            BuildProjectTab(container);
            UpdateCartPicker();
        }

        void BuildCartUITab(VisualElement container) {
            var psRow = CreateRow();
            var psField = new PropertyField(serializedObject.FindProperty("_panelSettings"), OneJSEditorDesign.Texts.PanelSettings);
            psField.style.flexGrow = 1;
            psRow.Add(psField);
            var create = new Button(() => {
                _target.CreateCartPanelSettings();
                serializedObject.Update();
                ShowCartTab(1);
            }) { text = OneJSEditorDesign.Texts.CreatePanelSettings, tooltip = "Create a Panel Settings at Assets/{cart folder}/PanelSettings.asset and assign it" };
            create.style.display = _target.PanelSettingsAsset == null ? DisplayStyle.Flex : DisplayStyle.None;
            psRow.Add(create);
            psField.RegisterValueChangeCallback(_ => create.style.display = _target.PanelSettingsAsset == null ? DisplayStyle.Flex : DisplayStyle.None);
            container.Add(psRow);

            var note = SmallLabel();
            note.text = OneJSEditorDesign.Texts.CartPanelSettingsNote;
            note.style.marginBottom = 4;
            container.Add(note);
            AddSpacer(container);
            BuildUITab(container);
        }

        void BuildCartBuildTab(VisualElement container) {
            AddSectionHeader(container, OneJSEditorDesign.Texts.BuildOutput);
            var cart = _target.CartFolder;
            var folder = cart != null ? cart.Folder : "{folder}";
            AddPathRow(container, OneJSEditorDesign.Texts.CartBuildEditor, $"{RootCarts.EditorBuildRoot}/{folder}/app.js.txt");
            AddPathRow(container, OneJSEditorDesign.Texts.CartBuildPlayer, $"{RootCarts.PlayerBuildRoot}/{folder}/");
            AddPathRow(container, OneJSEditorDesign.Texts.CartBuildFiles, $"StreamingAssets/{RootCarts.PlayerFilesRoot}/{folder}/");
            AddSpacer(container, 6);
            container.Add(new PropertyField(serializedObject.FindProperty("_includeSourceMap"), "Include Source Map"));
            container.Add(new PropertyField(serializedObject.FindProperty("_excludeFromBuild"), "Exclude From Build"));
            var help = CreateInfoBox(OneJSEditorDesign.Texts.CartBuildNote);
            help.style.marginTop = 6;
            container.Add(help);
        }

        void AddPathRow(VisualElement container, string label, string path) {
            var row = CreateRow();
            row.style.marginBottom = 2;
            row.Add(CreateLabel(label, 120));
            var value = new Label(path);
            value.style.color = OneJSEditorDesign.Colors.TextInfo;
            value.style.whiteSpace = WhiteSpace.Normal;
            value.style.flexShrink = 1;
            row.Add(value);
            container.Add(row);
        }

        // The picker

        /// <summary>The Cart field: a menu of what the scan found, each name with its folder.</summary>
        VisualElement CreateCartPickerRow() {
            var row = CreateRow();
            row.Add(CreateLabel(OneJSEditorDesign.Texts.Cart, 120));
            _cartPickerButton = new Button();
            _cartPickerButton.style.flexGrow = 1;
            _cartPickerButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            _cartPickerButton.style.height = 20;
            _cartPickerButton.style.marginLeft = 0;
            var button = _cartPickerButton;
            _cartPickerButton.clicked += () => ShowCartMenu(button);
            row.Add(_cartPickerButton);
            return row;
        }

        void ShowCartMenu(VisualElement anchor) {
            RootCarts.Invalidate();
            var carts = RootCarts.All;
            var current = _target.IsCart ? _target.ResolveCart() : default;
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(OneJSEditorDesign.Texts.CartsAtRoot));
            foreach (var cart in carts.Where(c => c.Usable)) {
                var name = cart.Name;
                var on = _target.IsCart && current.Outcome == RootCarts.Outcome.Found && current.Cart.FullPath == cart.FullPath;
                menu.AddItem(new GUIContent(MenuText(cart.Name, cart.Folder)), on, () => PickCart(name));
            }
            var unusable = carts.Where(c => !c.Usable).ToList();
            if (unusable.Count > 0) {
                menu.AddSeparator("");
                foreach (var cart in unusable) menu.AddDisabledItem(new GUIContent(MenuText("(no name)", cart.Folder)));
            }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("None"), !_target.IsCart, () => PickCart(null));
            menu.DropDown(anchor.worldBound);
        }

        /// <summary>A menu line: GenericMenu reads a slash as a submenu, so the folder's is a look-alike.</summary>
        static string MenuText(string name, string folder) =>
            $"{name.Replace('/', '∕')}      {folder}∕";

        void PickCart(string name) {
            _target.SetCart(name);
            serializedObject.Update();
            // UpdateDynamicUI swaps the inspector when the runner changed kind.
        }

        /// <summary>The picker's text and the lines under it, redrawn only when what they say changes.</summary>
        void UpdateCartPicker() {
            if (_cartPickerButton == null) return;
            var carts = RootCarts.All;
            var resolution = _target.IsCart ? _target.ResolveCart() : default;
            var key = string.Join("|", carts.Select(c => c.FullPath + ":" + c.Name + ":" + c.Problem)) +
                "#" + _target.Cart + "#" + (_target.IsCart ? resolution.Outcome.ToString() : "none");
            if (key == _cartPickerKey) return;
            _cartPickerKey = key;

            var color = OneJSEditorDesign.Colors.TextNeutral;
            string text;
            if (!_target.IsCart) {
                text = $"None ({carts.Count(c => c.Usable)} found)";
                color = OneJSEditorDesign.Colors.TextMuted;
            } else if (resolution.Outcome == RootCarts.Outcome.Found) {
                text = resolution.Cart.Name;
            } else if (resolution.Outcome == RootCarts.Outcome.Duplicate) {
                text = $"{_target.Cart} ({resolution.Matches.Count} found)";
                color = OneJSEditorDesign.Colors.StatusError;
            } else {
                text = $"{_target.Cart} (not found)";
                color = OneJSEditorDesign.Colors.StatusWarning;
            }
            _cartPickerButton.text = text + "  ▾";
            _cartPickerButton.style.color = color;

            if (_cartInfoLabel != null) {
                var cart = resolution.Outcome == RootCarts.Outcome.Found ? resolution.Cart : null;
                _cartInfoLabel.style.display = cart != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (cart != null) {
                    var parts = new List<string>();
                    if (!string.IsNullOrEmpty(cart.Version)) parts.Add(cart.Version);
                    parts.Add(cart.OjJson);
                    parts.Add("entry " + cart.Entry);
                    _cartInfoLabel.text = string.Join(" · ", parts);
                }
            }
            if (_cartProblems != null) {
                _cartProblems.Clear();
                foreach (var cart in carts.Where(c => !c.Usable)) {
                    var box = CreateInfoBox(cart.Problem);
                    box.style.marginTop = 4;
                    _cartProblems.Add(box);
                }
            }
        }

        /// <summary>
        /// The legacy inspector's empty state, once the project root holds carts: the picker
        /// and the sentence that offers both. A project with no carts never sees either.
        /// </summary>
        void UpdateEmptyCartPicker(bool empty) {
            if (_emptyCartPicker == null) return;
            var offer = empty && RootCarts.All.Count > 0;
            _emptyCartPicker.style.display = offer ? DisplayStyle.Flex : DisplayStyle.None;
            if (_notInitMessage != null) {
                var text = offer ? OneJSEditorDesign.Texts.PickCartOrInitialize : NotInitializedMessage;
                if (_notInitMessage.text != text) _notInitMessage.text = text;
            }
            if (offer) UpdateCartPicker();
        }

        // Status

        void UpdateCartUI() {
            if (_cartStatusLabel == null) return;
            serializedObject.Update();
            var resolution = _target.ResolveCart();
            var cart = resolution.Cart;

            string status;
            Color color;
            var lines = new List<string>();
            string action = null;
            Action run = null;
            var running = false;

            if (resolution.Outcome == RootCarts.Outcome.Missing) {
                status = OneJSEditorDesign.Texts.NoCart;
                color = OneJSEditorDesign.Colors.StatusWarning;
                lines.Add($"No cart named <b>{_target.Cart}</b> in this project.");
                var renamed = resolution.RenamedTo;
                if (renamed != null) {
                    lines.Add($"{renamed.OjJson} was \"{_target.Cart}\" and now says <b>\"{renamed.Name}\"</b>.");
                    action = $"Link to {renamed.Name}";
                    var to = renamed.Name;
                    run = () => PickCart(to);
                }
            } else if (resolution.Outcome == RootCarts.Outcome.Duplicate) {
                status = OneJSEditorDesign.Texts.TwoCarts;
                color = OneJSEditorDesign.Colors.StatusError;
                lines.Add($"{(resolution.Matches.Count == 2 ? "Two" : resolution.Matches.Count.ToString())} carts are named <b>{_target.Cart}</b>:");
                foreach (var m in resolution.Matches) lines.Add("    " + m.OjJson);
                lines.Add("A runner links by name, so it runs neither. Rename one in its oj.json.");
            } else if (_target.PanelSettingsAsset == null) {
                status = OneJSEditorDesign.Texts.NoPanelSettings;
                color = OneJSEditorDesign.Colors.StatusWarning;
                lines.Add($"A cart runner needs a Panel Settings for its panel. Create makes Assets/{cart.Folder}/PanelSettings.asset, or assign any in the UI tab.");
                action = OneJSEditorDesign.Texts.CreatePanelSettings;
                run = () => { _target.CreateCartPanelSettings(); serializedObject.Update(); };
            } else if (CartRunners.NeedsSetUp(cart)) {
                status = OneJSEditorDesign.Texts.NotSetUp;
                color = OneJSEditorDesign.Colors.StatusWarning;
                var problem = CartRunners.SetUpProblem(cart);
                if (problem != null) {
                    lines.Add($"{problem.Replace(cart.Name, $"<b>{cart.Name}</b>")} Set Up runs npx ojplay init and npm install in {cart.Folder}/, which is what any machine does with a cart.");
                } else {
                    lines.Add($"<b>{cart.Name}</b> still holds the files OneJS's setup wrote into it before it moved to the project root. " +
                        $"Set Up runs npx {MoveToProjectRoot.Ojplay} init --from-unity in {cart.Folder}/ to take them out, then npm install.");
                }
                action = CartRunners.IsBusy(cart) ? OneJSEditorDesign.Texts.SettingUp : OneJSEditorDesign.Texts.SetUp;
                var c = cart;
                if (!CartRunners.IsBusy(cart)) run = () => CartRunners.SetUp(c);
            } else {
                running = true;
                if (Application.isPlaying) {
                    status = _target.IsRunning ? OneJSEditorDesign.Texts.Running : OneJSEditorDesign.Texts.Loading;
                    color = _target.IsRunning ? OneJSEditorDesign.Colors.StatusRunning : OneJSEditorDesign.Colors.StatusWarning;
                } else {
                    status = OneJSEditorDesign.Texts.Stopped;
                    color = OneJSEditorDesign.Colors.StatusStopped;
                }
            }

            _cartStatusLabel.text = status;
            _cartStatusLabel.style.color = color;

            var key = string.Join("\n", lines) + "#" + action + "#" + (run != null);
            if (key != _cartStatusKey) {
                _cartStatusKey = key;
                _cartStatusLines.Clear();
                foreach (var line in lines) {
                    var label = SmallLabel();
                    label.enableRichText = true;
                    label.text = line;
                    _cartStatusLines.Add(label);
                }
                _cartStatusAction = run;
                _cartStatusButton.text = action ?? "";
                _cartStatusButton.style.display = action != null ? DisplayStyle.Flex : DisplayStyle.None;
                _cartStatusButton.SetEnabled(run != null);
            }

            _cartRunningLines.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            if (running) {
                var showReloads = _target.IsRunning && _target.ReloadCount > 0;
                _cartReloadLabel.style.display = showReloads ? DisplayStyle.Flex : DisplayStyle.None;
                if (showReloads) _cartReloadLabel.text = $"Reloads: {_target.ReloadCount} · {OneJSEditorDesign.Texts.LastReload}{FormatTimeAgo(_target.LastReloadTime)}";

                string watcher;
                if (NodeWatcherManager.IsStarting(cart.FullPath)) watcher = Colored(OneJSEditorDesign.Texts.WatcherStarting, OneJSEditorDesign.Colors.StatusWarning);
                else if (NodeWatcherManager.IsRunning(cart.FullPath)) watcher = Colored(OneJSEditorDesign.Texts.Running, OneJSEditorDesign.Colors.StatusSuccess) + OneJSEditorDesign.Texts.CartWatcherCommand;
                else watcher = OneJSEditorDesign.Texts.WatcherStarting;
                _cartWatcherLabel.text = OneJSEditorDesign.Texts.Watcher + watcher;
                _cartFolderLabel.text = $"Cart: <b>{cart.Name}</b> in {cart.Folder}/";
            }

            _reloadButton?.SetEnabled(_target.IsRunning);
            if (_cartFolderButtons != null) foreach (var b in _cartFolderButtons) b.SetEnabled(cart != null);
            if (_buildButton != null) {
                _buildButton.SetEnabled(!_buildInProgress && cart != null);
                _buildButton.text = _buildInProgress ? OneJSEditorDesign.Texts.Rebuilding : OneJSEditorDesign.Texts.Rebuild;
            }
            if (_buildOutputContainer != null) {
                _buildOutputContainer.style.display = string.IsNullOrEmpty(_buildOutput) ? DisplayStyle.None : DisplayStyle.Flex;
                _buildOutputLabel.text = _buildOutput ?? "";
            }
            UpdateCartPicker();
        }

        static string Colored(string text, Color color) => $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        /// <summary>
        /// Rebuild for a cart, as for an app: stop the watcher, delete node_modules, install,
        /// then one build into Library; the watcher comes back on the next editor tick.
        /// </summary>
        void RunCartRebuild() {
            var cart = _target.CartFolder;
            if (cart == null) return;
            NodeWatcherManager.StopWatcher(cart.FullPath);
            var nodeModules = Path.Combine(cart.FullPath, "node_modules");
            _buildInProgress = true;
            if (Directory.Exists(nodeModules)) {
                _buildOutput = "Deleting node_modules...";
                try {
                    Directory.Delete(nodeModules, true);
                } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
                    // Expected failure: a file is locked, usually by a process still exiting.
                    _buildInProgress = false;
                    _buildOutput = $"Failed to delete node_modules: {ex.Message}";
                    return;
                }
            }
            _buildOutput = "Setting up...";
            CartRunners.SetUp(cart, ok => {
                if (!ok) {
                    _buildInProgress = false;
                    _buildOutput = "Set Up failed; see the Console.";
                    return;
                }
                _buildOutput = "Building...";
                CartRunners.BuildOnce(cart, built => {
                    _buildInProgress = false;
                    _buildOutput = built ? "Rebuild completed successfully!" : "Build failed; see the Console.";
                });
            });
        }

        // Small helpers

        static Label SmallLabel() {
            var label = new Label();
            label.style.marginTop = 4;
            label.style.fontSize = 11;
            label.style.color = OneJSEditorDesign.Colors.TextMuted;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        static Button PrimaryButton(string text, Action onClick) {
            var button = new Button(onClick) { text = text };
            button.style.height = 22;
            button.style.flexShrink = 0;
            button.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryBg;
            button.style.color = OneJSEditorDesign.Colors.ButtonPrimaryText;
            button.RegisterCallback<MouseEnterEvent>(_ => button.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryHover);
            button.RegisterCallback<MouseLeaveEvent>(_ => button.style.backgroundColor = OneJSEditorDesign.Colors.ButtonPrimaryBg);
            return button;
        }
    }
}
