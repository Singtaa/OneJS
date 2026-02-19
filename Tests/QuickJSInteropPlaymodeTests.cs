using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// C# helper class for interop tests.
/// Provides a mutable object that JS can read/modify
/// to test proxy caching and property change detection.
/// </summary>
public class InteropTestState {
    public string Name { get; set; }
    public int Version { get; set; }
}

/// <summary>
/// Static helper for interop tests.
/// Exposes state and collections via methods (not PascalCase properties)
/// because the bootstrap path proxy treats uppercase-first property access
/// as type path segments. Method calls use the apply handler which correctly
/// falls back to static method invocation.
/// </summary>
public static class InteropTestHelper {
    static InteropTestState _state;
    static List<string> _items = new();

    public static InteropTestState GetState() => _state;
    public static List<string> GetItems() => _items;

    public static void Init(string name, int version) {
        _state = new InteropTestState { Name = name, Version = version };
    }

    public static void SetName(string name) {
        if (_state != null) _state.Name = name;
    }

    public static void SetVersion(int version) {
        if (_state != null) _state.Version = version;
    }

    public static void ClearItems() => _items.Clear();
    public static void AddItem(string item) => _items.Add(item);

    public static void Reset() {
        _state = null;
        _items.Clear();
    }
}

/// <summary>
/// Playmode tests for C# interop patterns used by useFrameSync (selector mode) and toArray.
/// Validates proxy caching, property change detection, and collection access
/// through the QuickJS bootstrap proxy layer.
///
/// Key design notes:
/// - Static members are accessed via method calls (GetState(), GetItems()) because
///   the CS path proxy treats PascalCase property access as type path segments.
/// - Instance properties (Name, Version) on handle-based proxies work with any case
///   because the handle proxy resolves all properties through C# reflection.
/// - List&lt;T&gt; supports .Count and numeric indexer [i] via get_Item.
/// </summary>
[TestFixture]
public class QuickJSInteropPlaymodeTests {
    QuickJSContext _ctx;

    [UnitySetUp]
    public IEnumerator SetUp() {
        InteropTestHelper.Reset();
        _ctx = new QuickJSContext();
        yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown() {
        _ctx?.Dispose();
        _ctx = null;
        InteropTestHelper.Reset();
        QuickJSNative.ClearAllHandles();
        yield return null;
    }

    // MARK: Proxy Identity Tests

    [UnityTest]
    public IEnumerator ProxyCache_SameMethodCall_ReturnsSameProxy() {
        InteropTestHelper.Init("Town", 1);

        var result = _ctx.Eval(@"
            var a = CS.InteropTestHelper.GetState();
            var b = CS.InteropTestHelper.GetState();
            a === b;
        ");
        Assert.AreEqual("true", result, "Same C# object should return same JS proxy (proxy caching)");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ProxyCache_PropertyChangeVisibleThroughSameProxy() {
        InteropTestHelper.Init("Town A", 1);

        var result = _ctx.Eval(@"
            var state = CS.InteropTestHelper.GetState();
            var nameBefore = state.Name;
            CS.InteropTestHelper.SetName('Town B');
            var nameAfter = state.Name;
            nameBefore + '|' + nameAfter;
        ");
        Assert.AreEqual("Town A|Town B", result,
            "Reading a property through the same proxy should reflect C# changes");
        yield return null;
    }

    // MARK: Change Detection Pattern Tests

    [UnityTest]
    public IEnumerator ChangeDetection_ObjectIs_TrueForSameProxy() {
        InteropTestHelper.Init("Town A", 1);

        var result = _ctx.Eval(@"
            var state1 = CS.InteropTestHelper.GetState();
            CS.InteropTestHelper.SetName('Town B');
            CS.InteropTestHelper.SetVersion(2);
            var state2 = CS.InteropTestHelper.GetState();
            Object.is(state1, state2);
        ");
        Assert.AreEqual("true", result,
            "Object.is should return true for same proxy even after property changes — " +
            "this is the core limitation that useFrameSync's selector mode addresses");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ChangeDetection_ExtractedPrimitives_DetectChanges() {
        InteropTestHelper.Init("Town A", 1);

        var result = _ctx.Eval(@"
            var name1 = CS.InteropTestHelper.GetState().Name;
            var ver1 = CS.InteropTestHelper.GetState().Version;

            CS.InteropTestHelper.SetName('Town B');
            CS.InteropTestHelper.SetVersion(2);

            var name2 = CS.InteropTestHelper.GetState().Name;
            var ver2 = CS.InteropTestHelper.GetState().Version;

            var nameChanged = !Object.is(name1, name2);
            var verChanged = !Object.is(ver1, ver2);
            nameChanged + '|' + verChanged;
        ");
        Assert.AreEqual("true|true", result,
            "Extracted primitive values should detect changes via Object.is — " +
            "this is the pattern useFrameSync's selector mode relies on");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ChangeDetection_UnchangedPrimitives_StaySame() {
        InteropTestHelper.Init("Town A", 1);

        var result = _ctx.Eval(@"
            var name1 = CS.InteropTestHelper.GetState().Name;
            var ver1 = CS.InteropTestHelper.GetState().Version;

            // Don't change anything
            var name2 = CS.InteropTestHelper.GetState().Name;
            var ver2 = CS.InteropTestHelper.GetState().Version;

            var nameChanged = !Object.is(name1, name2);
            var verChanged = !Object.is(ver1, ver2);
            nameChanged + '|' + verChanged;
        ");
        Assert.AreEqual("false|false", result,
            "Unchanged primitive values should be equal via Object.is");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ChangeDetection_DepsArrayPattern_Works() {
        InteropTestHelper.Init("Town A", 1);

        var result = _ctx.Eval(@"
            function extractDeps() {
                var state = CS.InteropTestHelper.GetState();
                return [state ? state.Name : null, state ? state.Version : null];
            }

            var deps1 = extractDeps();

            CS.InteropTestHelper.SetName('Town B');

            var deps2 = extractDeps();

            var changed = deps1.some(function(val, i) { return !Object.is(val, deps2[i]); });
            changed + '|' + deps1[0] + '|' + deps2[0];
        ");
        Assert.AreEqual("true|Town A|Town B", result,
            "Deps array comparison should detect property changes");
        yield return null;
    }

    [UnityTest]
    public IEnumerator ChangeDetection_DepsArrayPattern_StableWhenUnchanged() {
        InteropTestHelper.Init("Town A", 1);

        var result = _ctx.Eval(@"
            function extractDeps() {
                var state = CS.InteropTestHelper.GetState();
                return [state ? state.Name : null, state ? state.Version : null];
            }

            var deps1 = extractDeps();
            var deps2 = extractDeps();

            var changed = deps1.some(function(val, i) { return !Object.is(val, deps2[i]); });
            '' + changed;
        ");
        Assert.AreEqual("false", result,
            "Deps array comparison should be stable when nothing changed");
        yield return null;
    }

    // MARK: Null Safety Tests

    [UnityTest]
    public IEnumerator NullSafety_NullState_ReturnsNull() {
        // State is null (Reset was called in SetUp)
        var result = _ctx.Eval(@"
            var state = CS.InteropTestHelper.GetState();
            '' + state;
        ");
        Assert.AreEqual("null", result, "Null C# property should return null in JS");
        yield return null;
    }

    [UnityTest]
    public IEnumerator NullSafety_OptionalChaining_Works() {
        var result = _ctx.Eval(@"
            var state = CS.InteropTestHelper.GetState();
            var name = state?.Name ?? 'default';
            name;
        ");
        Assert.AreEqual("default", result,
            "Optional chaining with nullish coalescing should work on null state");
        yield return null;
    }

    [UnityTest]
    public IEnumerator NullSafety_StateTransitionFromNullToObject() {
        var result = _ctx.Eval(@"
            var stateBefore = CS.InteropTestHelper.GetState();
            var before = stateBefore?.Name ?? 'none';
            CS.InteropTestHelper.Init('Village', 1);
            var stateAfter = CS.InteropTestHelper.GetState();
            var after = stateAfter?.Name ?? 'none';
            before + '|' + after;
        ");
        Assert.AreEqual("none|Village", result,
            "Should handle transition from null to valid state");
        yield return null;
    }

    // MARK: Collection Access Tests (toArray pattern)

    [UnityTest]
    public IEnumerator Collection_ListCount_Works() {
        InteropTestHelper.AddItem("sword");
        InteropTestHelper.AddItem("shield");
        InteropTestHelper.AddItem("potion");

        var result = _ctx.Eval(@"
            var items = CS.InteropTestHelper.GetItems();
            '' + items.Count;
        ");
        Assert.AreEqual("3", result, "List.Count should be accessible from JS");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Collection_ListIndexer_Works() {
        InteropTestHelper.AddItem("sword");
        InteropTestHelper.AddItem("shield");
        InteropTestHelper.AddItem("potion");

        var result = _ctx.Eval(@"
            var items = CS.InteropTestHelper.GetItems();
            items[0] + '|' + items[1] + '|' + items[2];
        ");
        Assert.AreEqual("sword|shield|potion", result,
            "List numeric indexer should work from JS");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Collection_EmptyList_CountIsZero() {
        var result = _ctx.Eval(@"
            var items = CS.InteropTestHelper.GetItems();
            '' + items.Count;
        ");
        Assert.AreEqual("0", result, "Empty list should have Count = 0");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Collection_ToJSArrayLoop_Works() {
        InteropTestHelper.AddItem("a");
        InteropTestHelper.AddItem("b");
        InteropTestHelper.AddItem("c");

        var result = _ctx.Eval(@"
            var csList = CS.InteropTestHelper.GetItems();
            var jsArr = [];
            for (var i = 0; i < csList.Count; i++) {
                jsArr.push(csList[i]);
            }
            jsArr.length + '|' + jsArr.join(',');
        ");
        Assert.AreEqual("3|a,b,c", result,
            "Manual for-loop conversion to JS array should work (toArray pattern)");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Collection_JSArrayHasMapFilterEtc() {
        InteropTestHelper.AddItem("hello");
        InteropTestHelper.AddItem("world");

        var result = _ctx.Eval(@"
            var csList = CS.InteropTestHelper.GetItems();
            var jsArr = [];
            for (var i = 0; i < csList.Count; i++) jsArr.push(csList[i]);

            // Now we can use JS array methods
            var mapped = jsArr.map(function(s) { return s.toUpperCase(); });
            var filtered = jsArr.filter(function(s) { return s.length > 4; });
            mapped.join(',') + '|' + filtered.join(',');
        ");
        Assert.AreEqual("HELLO,WORLD|hello,world", result,
            "After conversion, JS array methods (map, filter) should work");
        yield return null;
    }

    [UnityTest]
    public IEnumerator Collection_LiveUpdates_ReflectedInProxy() {
        InteropTestHelper.AddItem("initial");

        var result = _ctx.Eval(@"
            var items = CS.InteropTestHelper.GetItems();
            var countBefore = items.Count;
            CS.InteropTestHelper.AddItem('added');
            var countAfter = items.Count;
            countBefore + '|' + countAfter + '|' + items[1];
        ");
        Assert.AreEqual("1|2|added", result,
            "Collection changes should be visible through the same proxy");
        yield return null;
    }

    // MARK: Combined Patterns (simulating useFrameSync selector + toArray workflow)

    [UnityTest]
    public IEnumerator Combined_FullWorkflow_ProxyWithDepsAndCollection() {
        InteropTestHelper.Init("Tavern", 1);
        InteropTestHelper.AddItem("ale");
        InteropTestHelper.AddItem("bread");

        var result = _ctx.Eval(@"
            function getDeps() {
                var state = CS.InteropTestHelper.GetState();
                var items = CS.InteropTestHelper.GetItems();
                return [
                    state ? state.Name : null,
                    state ? state.Version : null,
                    items.Count
                ];
            }

            // Simulate toArray
            function toArray(coll) {
                var result = [];
                for (var i = 0; i < coll.Count; i++) result.push(coll[i]);
                return result;
            }

            var deps1 = getDeps();
            var items1 = toArray(CS.InteropTestHelper.GetItems());

            // Simulate game state change
            CS.InteropTestHelper.SetName('Market');
            CS.InteropTestHelper.SetVersion(2);
            CS.InteropTestHelper.AddItem('cheese');

            var deps2 = getDeps();
            var items2 = toArray(CS.InteropTestHelper.GetItems());

            var changed = deps1.some(function(val, i) { return !Object.is(val, deps2[i]); });

            JSON.stringify({
                changed: changed,
                nameBefore: deps1[0],
                nameAfter: deps2[0],
                itemsBefore: items1.join(','),
                itemsAfter: items2.join(',')
            });
        ");

        StringAssert.Contains("\"changed\":true", result);
        StringAssert.Contains("\"nameBefore\":\"Tavern\"", result);
        StringAssert.Contains("\"nameAfter\":\"Market\"", result);
        StringAssert.Contains("\"itemsBefore\":\"ale,bread\"", result);
        StringAssert.Contains("\"itemsAfter\":\"ale,bread,cheese\"", result);
        yield return null;
    }
}
