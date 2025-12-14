using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

/// <summary>
/// Playmode tests for UI Toolkit JS interop.
/// Tests element creation, properties, styles, and hierarchy manipulation.
/// Creates UIDocument and PanelSettings programmatically - no external assets required.
/// </summary>
[TestFixture]
public class UIToolkitJSPlaymodeTests {
    GameObject _go;
    UIDocument _uiDocument;
    PanelSettings _panelSettings;
    QuickJSContext _ctx;

    [UnitySetUp]
    public IEnumerator SetUp() {
        // Create PanelSettings at runtime
        _panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
        _panelSettings.themeStyleSheet =
            AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss");

        // Create GameObject with UIDocument
        _go = new GameObject("UIToolkitTestHost");
        _uiDocument = _go.AddComponent<UIDocument>();
        _uiDocument.panelSettings = _panelSettings;

        // Wait a frame for UIDocument to initialize
        yield return null;

        _ctx = new QuickJSContext();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _ctx?.Dispose();
        _ctx = null;

        if (_go != null) Object.Destroy(_go);
        if (_panelSettings != null) Object.Destroy(_panelSettings);

        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: Element Creation Tests

    [UnityTest]
    public IEnumerator Creation_VisualElement_Works() {
        var result = _ctx.Eval(@"
            var ve = new CS.UnityEngine.UIElements.VisualElement();
            ve.toString();
        ");
        StringAssert.Contains("VisualElement", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Creation_LabelWithText_Works() {
        var result = _ctx.Eval(@"
            var label = new CS.UnityEngine.UIElements.Label('Hello World');
            label.text;
        ");
        Assert.AreEqual("Hello World", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Creation_Button_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var btn = new CS.UnityEngine.UIElements.Button();
                btn.text = 'Click Me';
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Creation_TextField_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var tf = new CS.UnityEngine.UIElements.TextField();
                tf.label = 'Username';
                tf.value = 'test';
            ");
        });
        yield return null;
    }

    // MARK: Property Tests

    [UnityTest]
    public IEnumerator Property_Name_SetGet() {
        var result = _ctx.Eval(@"
            var ve = new CS.UnityEngine.UIElements.VisualElement();
            ve.name = 'TestElement';
            ve.name;
        ");
        Assert.AreEqual("TestElement", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Property_Tooltip_SetGet() {
        var result = _ctx.Eval(@"
            var ve = new CS.UnityEngine.UIElements.VisualElement();
            ve.tooltip = 'Hover text';
            ve.tooltip;
        ");
        Assert.AreEqual("Hover text", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Property_Visible_SetGet() {
        var result = _ctx.Eval(@"
            var ve = new CS.UnityEngine.UIElements.VisualElement();
            ve.visible = false;
            ve.visible;
        ");
        Assert.AreEqual("false", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Property_PickingMode_Set() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.pickingMode = CS.UnityEngine.UIElements.PickingMode.Ignore;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Property_AddCSSClass_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.AddToClassList('my-class');
                ve.AddToClassList('another-class');
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Property_ClassListContains_Works() {
        var result = _ctx.Eval(@"
            var ve = new CS.UnityEngine.UIElements.VisualElement();
            ve.AddToClassList('test-class');
            ve.ClassListContains('test-class');
        ");
        Assert.AreEqual("true", result);
        yield return null;
    }

    // MARK: Style Tests

    [UnityTest]
    public IEnumerator Style_GetStyleObject_Works() {
        var result = _ctx.Eval(@"
            var ve = new CS.UnityEngine.UIElements.VisualElement();
            var s = ve.style;
            s.toString();
        ");
        Assert.IsNotNull(result);
        Assert.AreNotEqual("null", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetWidthViaLengthStruct_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                var len = new CS.UnityEngine.UIElements.Length(100, CS.UnityEngine.UIElements.LengthUnit.Pixel);
                ve.style.width = len;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetWidthViaStyleLength_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                var styleLen = new CS.UnityEngine.UIElements.StyleLength(100);
                ve.style.width = styleLen;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetHeightDirectly_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.height = 50;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetBackgroundColor_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                var color = new CS.UnityEngine.Color(1, 0, 0, 1);
                ve.style.backgroundColor = color;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetFlexDirection_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.flexDirection = CS.UnityEngine.UIElements.FlexDirection.Row;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetMultipleStyles_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.flexGrow = 1;
                ve.style.flexShrink = 0;
                ve.style.alignItems = CS.UnityEngine.UIElements.Align.Center;
                ve.style.justifyContent = CS.UnityEngine.UIElements.Justify.SpaceBetween;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetPadding_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.paddingTop = 10;
                ve.style.paddingRight = 10;
                ve.style.paddingBottom = 10;
                ve.style.paddingLeft = 10;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetMargin_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.marginTop = 5;
                ve.style.marginRight = 5;
                ve.style.marginBottom = 5;
                ve.style.marginLeft = 5;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetBorderRadius_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.borderTopLeftRadius = 8;
                ve.style.borderTopRightRadius = 8;
                ve.style.borderBottomLeftRadius = 8;
                ve.style.borderBottomRightRadius = 8;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetDisplay_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.display = CS.UnityEngine.UIElements.DisplayStyle.Flex;
            ");
        });
        yield return null;
    }

    [UnityTest]
    public IEnumerator Style_SetPositionType_Works() {
        Assert.DoesNotThrow(() => {
            _ctx.Eval(@"
                var ve = new CS.UnityEngine.UIElements.VisualElement();
                ve.style.position = CS.UnityEngine.UIElements.Position.Absolute;
                ve.style.left = 10;
                ve.style.top = 20;
            ");
        });
        yield return null;
    }

    // MARK: Hierarchy Tests

    [UnityTest]
    public IEnumerator Hierarchy_AddChild_Works() {
        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

        _ctx.Eval($@"
            var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
            var child = new CS.UnityEngine.UIElements.Label('Child 1');
            child.name = 'child1';
            container.Add(child);
        ");

        Assert.AreEqual(1, testContainer.childCount);

        root.Remove(testContainer);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hierarchy_AddMultipleChildren_Works() {
        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

        _ctx.Eval($@"
            var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
            var child1 = new CS.UnityEngine.UIElements.Label('Child 1');
            var child2 = new CS.UnityEngine.UIElements.Label('Child 2');
            var child3 = new CS.UnityEngine.UIElements.Label('Child 3');
            container.Add(child1);
            container.Add(child2);
            container.Add(child3);
        ");

        Assert.AreEqual(3, testContainer.childCount);

        root.Remove(testContainer);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hierarchy_GetChildCount_ReturnsCorrect() {
        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        testContainer.Add(new Label("1"));
        testContainer.Add(new Label("2"));
        testContainer.Add(new Label("3"));
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

        var result = _ctx.Eval($@"
            var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
            container.childCount;
        ");

        Assert.AreEqual("3", result);

        root.Remove(testContainer);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hierarchy_InsertAtIndex_Works() {
        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        testContainer.Add(new Label("First") { name = "first" });
        testContainer.Add(new Label("Last") { name = "last" });
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

        _ctx.Eval($@"
            var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
            var inserted = new CS.UnityEngine.UIElements.Label('Inserted');
            inserted.name = 'inserted';
            container.Insert(1, inserted);
        ");

        Assert.AreEqual(3, testContainer.childCount);
        Assert.AreEqual("inserted", testContainer[1].name);

        root.Remove(testContainer);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hierarchy_RemoveAt_Works() {
        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        testContainer.Add(new Label("1"));
        testContainer.Add(new Label("2"));
        testContainer.Add(new Label("3"));
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);
        int countBefore = testContainer.childCount;

        _ctx.Eval($@"
            var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
            container.RemoveAt(0);
        ");

        Assert.AreEqual(countBefore - 1, testContainer.childCount);

        root.Remove(testContainer);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hierarchy_Clear_Works() {
        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        testContainer.Add(new Label("1"));
        testContainer.Add(new Label("2"));
        testContainer.Add(new Label("3"));
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

        _ctx.Eval($@"
            var container = __csHelpers.wrapObject('UnityEngine.UIElements.VisualElement', {containerHandle});
            container.Clear();
        ");

        Assert.AreEqual(0, testContainer.childCount);

        root.Remove(testContainer);
        yield return null;
    }

    [UnityTest]
    public IEnumerator Hierarchy_BringToFront_Works() {
        var root = _uiDocument.rootVisualElement;
        var testContainer = new VisualElement { name = "JSTestContainer" };
        root.Add(testContainer);

        int containerHandle = RegisterTestContainer(testContainer);

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

        Assert.AreEqual(2, testContainer.childCount);
        Assert.AreEqual("labelA", testContainer[1].name);

        root.Remove(testContainer);
        yield return null;
    }

    // MARK: Helper Methods

    int RegisterTestContainer(VisualElement container) {
        var method = typeof(QuickJSNative).GetMethod("RegisterObject",
            BindingFlags.NonPublic | BindingFlags.Static);
        if (method != null) {
            return (int)method.Invoke(null, new object[] { container });
        }
        throw new System.Exception("Could not find RegisterObject method");
    }
}