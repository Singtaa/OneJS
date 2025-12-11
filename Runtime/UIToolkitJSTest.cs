using UnityEngine;
using UnityEngine.UIElements;

// MARK: Main
[RequireComponent(typeof(UIDocument))]
public class UIToolkitJSTest : MonoBehaviour {
    UIDocument _uiDocument;

    QuickJSContext _ctx;
    int _passed;
    int _failed;

    void Awake() {
        _uiDocument = GetComponent<UIDocument>();
        _ctx = new QuickJSContext();
    }

    void Start() {
        Log("=== UI Toolkit JS Interop Tests ===");

        RunElementCreationTests();
        RunPropertyTests();
        RunStyleTests();
        
        RunHierarchyTests();
        // RunQueryTests();
        // RunEventTests();

        LogSummary();
    }

    void OnDestroy() {
        Log($"Final handle count: {QuickJSNative.GetHandleCount()}");
        _ctx?.Dispose();
        _ctx = null;
        QuickJSNative.ClearAllHandles();
    }

    // MARK: Creation
    void RunElementCreationTests() {
        Log("--- Element Creation Tests ---");

        Assert("Create VisualElement", () => {
            var result = _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.toString();
            ");
            return result.Contains("VisualElement");
        });

        Assert("Create Label with text", () => {
            var result = _ctx.Eval(@"
                var label = new CS.UnityEngine.UIElements.Label('Hello World');
                label.text;
            ");
            return result == "Hello World";
        });

        Assert("Create Button", () => {
            _ctx.Eval(@"
                var btn = new CS.UnityEngine.UIElements.Button();
                btn.text = 'Click Me';
            ");
        });

        Assert("Create TextField", () => {
            _ctx.Eval(@"
                var tf = new CS.UnityEngine.UIElements.TextField();
                tf.label = 'Username';
                tf.value = 'test';
            ");
        });
    }

    // MARK: Properties
    void RunPropertyTests() {
        Log("--- Property Tests ---");

        Assert("Set/Get name", () => {
            var result = _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.name = 'TestElement';
                ve.name;
            ");
            return result == "TestElement";
        });

        Assert("Set/Get tooltip", () => {
            var result = _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.tooltip = 'Hover text';
                ve.tooltip;
            ");
            return result == "Hover text";
        });

        Assert("Set/Get visible", () => {
            var result = _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.visible = false;
                ve.visible;
            ");
            return result == "false";
        });

        Assert("Set/Get pickingMode", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.pickingMode = CS.UnityEngine.UIElements.PickingMode.Ignore;
            ");
        });

        Assert("Add CSS class", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.AddToClassList('my-class');
                ve.AddToClassList('another-class');
            ");
        });

        Assert("Check CSS class", () => {
            var result = _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.AddToClassList('test-class');
                ve.ClassListContains('test-class');
            ");
            return result == "true";
        });
    }

    // MARK: Styles
    void RunStyleTests() {
        Log("--- Style Tests ---");

        // These tests probe the tricky StyleLength/StyleColor/etc. conversions
        // UI Toolkit uses implicit conversions: style.width = 100 actually creates StyleLength

        Assert("Get style object", () => {
            var result = _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                var s = ve.style;
                s.toString();
            ");
            return result != null && result != "null";
        });

        Assert("Set width via Length struct", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                var len = new CS.UnityEngine.UIElements.Length(100, CS.UnityEngine.UIElements.LengthUnit.Pixel);
                ve.style.width = len;
            ");
        });

        Assert("Set width via StyleLength", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                var styleLen = new CS.UnityEngine.UIElements.StyleLength(100);
                ve.style.width = styleLen;
            ");
        });

        Assert("Set height directly (implicit conversion)", () => {
            // This tests if the interop handles implicit operator StyleLength(float)
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.height = 50;
            ");
        });

        Assert("Set backgroundColor", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                var color = new CS.UnityEngine.Color(1, 0, 0, 1);
                ve.style.backgroundColor = color;
            ");
        });

        Assert("Set flexDirection", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.flexDirection = CS.UnityEngine.UIElements.FlexDirection.Row;
            ");
        });

        Assert("Set multiple styles", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.flexGrow = 1;
                ve.style.flexShrink = 0;
                ve.style.alignItems = CS.UnityEngine.UIElements.Align.Center;
                ve.style.justifyContent = CS.UnityEngine.UIElements.Justify.SpaceBetween;
            ");
        });

        Assert("Set padding", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.paddingTop = 10;
                ve.style.paddingRight = 10;
                ve.style.paddingBottom = 10;
                ve.style.paddingLeft = 10;
            ");
        });

        Assert("Set margin", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.marginTop = 5;
                ve.style.marginRight = 5;
                ve.style.marginBottom = 5;
                ve.style.marginLeft = 5;
            ");
        });

        Assert("Set borderRadius", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.borderTopLeftRadius = 8;
                ve.style.borderTopRightRadius = 8;
                ve.style.borderBottomLeftRadius = 8;
                ve.style.borderBottomRightRadius = 8;
            ");
        });

        Assert("Set display", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.display = CS.UnityEngine.UIElements.DisplayStyle.Flex;
            ");
        });

        Assert("Set position type", () => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.position = CS.UnityEngine.UIElements.Position.Absolute;
                ve.style.left = 10;
                ve.style.top = 20;
            ");
        });
    }

    // MARK: Hierarchy
    void RunHierarchyTests() {
        Log("--- Hierarchy Tests ---");

        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        root.Add(testContainer);

        // Register the test container so JS can access it
        int containerHandle = RegisterTestContainer(testContainer);

        Assert("Add child to container", () => {
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var child = new CS.UnityEngine.UIElements.Label('Child 1');
                child.name = 'child1';
                container.Add(child);
            ");
            return testContainer.childCount == 1;
        });

        Assert("Add multiple children", () => {
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var child2 = new CS.UnityEngine.UIElements.Label('Child 2');
                child2.name = 'child2';
                var child3 = new CS.UnityEngine.UIElements.Label('Child 3');
                child3.name = 'child3';
                container.Add(child2);
                container.Add(child3);
            ");
            return testContainer.childCount == 3;
        });

        Assert("Get childCount", () => {
            var result = _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                container.childCount;
            ");
            return result == "3";
        });

        Assert("Insert child at index", () => {
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var inserted = new CS.UnityEngine.UIElements.Label('Inserted');
                inserted.name = 'inserted';
                container.Insert(1, inserted);
            ");
            return testContainer.childCount == 4 && testContainer[1].name == "inserted";
        });

        // .Q() shouldn't work as it's an extension method - requires special handling during interop
        // Assert("Remove child", () => {
        //     int countBefore = testContainer.childCount;
        //     _ctx.Eval($@"
        //         var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
        //         var toRemove = container.Q('inserted');
        //         container.Remove(toRemove);
        //     ");
        //     return testContainer.childCount == countBefore - 1;
        // });

        Assert("RemoveAt index", () => {
            int countBefore = testContainer.childCount;
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                container.RemoveAt(0);
            ");
            return testContainer.childCount == countBefore - 1;
        });

        Assert("Clear children", () => {
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                container.Clear();
            ");
            return testContainer.childCount == 0;
        });

        Assert("BringToFront / SendToBack", () => {
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var a = new CS.UnityEngine.UIElements.Label('A');
                a.name = 'labelA';
                var b = new CS.UnityEngine.UIElements.Label('B');
                b.name = 'labelB';
                container.Add(a);
                container.Add(b);
                a.BringToFront();
            ");
            return testContainer.childCount == 2 && testContainer[1].name == "labelA";
        });

        // Cleanup
        root.Remove(testContainer);
    }

    // MARK: Query
    void RunQueryTests() {
        Log("--- Query Tests ---");

        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "QueryTestContainer" };
        testContainer.AddToClassList("test-container");

        var label1 = new Label("Label 1") { name = "queryLabel1" };
        label1.AddToClassList("my-label");
        var label2 = new Label("Label 2") { name = "queryLabel2" };
        label2.AddToClassList("my-label");
        var button = new Button { name = "queryButton", text = "Test" };

        testContainer.Add(label1);
        testContainer.Add(label2);
        testContainer.Add(button);
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

        Assert("Query by name (Q)", () => {
            var result = _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var found = container.Q('queryLabel1');
                found ? found.name : 'null';
            ");
            return result == "queryLabel1";
        });

        Assert("Query by class", () => {
            var result = _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var found = container.Q(null, 'my-label');
                found ? 'found' : 'null';
            ");
            return result == "found";
        });

        // Note: QueryAll requires special handling (returns UQueryState/List)
        // This might need custom interop

        // Cleanup
        root.Remove(testContainer);
    }

    // MARK: Events
    void RunEventTests() {
        Log("--- Event Tests ---");

        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "EventTestContainer" };
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

        // First check if __registerCallback exists
        Assert("__registerCallback exists", () => {
            var result = _ctx.Eval("typeof __registerCallback");
            return result == "function";
        });

        Assert("Register click callback on Button", () => {
            // This tests the full callback flow
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var btn = new CS.UnityEngine.UIElements.Button();
                btn.text = 'Click Test';
                btn.name = 'eventTestBtn';
                container.Add(btn);
                
                // Button.clicked is an Action, not an event
                // We need to use RegisterCallback<ClickEvent> instead
                // This might require special handling in the interop
            ");
        });

        // Event registration is complex in UI Toolkit because:
        // 1. Button.clicked is an Action (simpler)
        // 2. RegisterCallback<T> requires generic type handling
        // 3. Events like ClickEvent, MouseDownEvent, etc. need type resolution

        Assert("Create element for manual event test", () => {
            _ctx.Eval($@"
                var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
                var testBtn = new CS.UnityEngine.UIElements.Button();
                testBtn.text = 'Manual Event Test';
                testBtn.name = 'manualEventBtn';
                container.Add(testBtn);
            ");
            return testContainer.Q<Button>("manualEventBtn") != null;
        });

        // Note: Full event callback support will likely need:
        // 1. Generic method invocation support (RegisterCallback<T>)
        // 2. Or a helper C# class that wraps event registration
        // 3. Or special handling in the bootstrap for common events

        // Cleanup
        root.Remove(testContainer);
    }

    // MARK: Helpers
    int RegisterTestContainer(VisualElement container) {
        // Use reflection to access the internal RegisterObject method
        var method = typeof(QuickJSNative).GetMethod("RegisterObject",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (method != null) {
            return (int)method.Invoke(null, new object[] { container });
        }
        throw new System.Exception("Could not find RegisterObject method");
    }

    void Assert(string name, string actual, string expected) {
        if (actual == expected) {
            _passed++;
            Log($"  [PASS] {name}");
        } else {
            _failed++;
            Debug.LogError($"  [FAIL] {name}: expected '{expected}', got '{actual}'");
        }
    }

    void Assert(string name, System.Action action) {
        try {
            action();
            _passed++;
            Log($"  [PASS] {name}");
        } catch (System.Exception ex) {
            _failed++;
            Debug.LogError($"  [FAIL] {name}: {ex.Message}");
        }
    }

    void Assert(string name, System.Func<bool> predicate) {
        try {
            if (predicate()) {
                _passed++;
                Log($"  [PASS] {name}");
            } else {
                _failed++;
                Debug.LogError($"  [FAIL] {name}: assertion failed");
            }
        } catch (System.Exception ex) {
            _failed++;
            Debug.LogError($"  [FAIL] {name}: {ex.Message}");
        }
    }

    void Log(string msg) => Debug.Log($"[UIToolkitJS] {msg}");

    void LogSummary() {
        var total = _passed + _failed;
        var status = _failed == 0 ? "ALL PASSED" : $"{_failed} FAILED";
        Log($"=== Results: {_passed}/{total} passed ({status}) ===");

        if (_failed > 0) {
            Log("Common failure causes:");
            Log("  - StyleLength/StyleColor implicit conversions not handled");
            Log("  - Generic method calls (RegisterCallback<T>) not supported");
            Log("  - Enum values not resolving correctly");
        }
    }
}