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
/// Typed item for collection sync tests.
/// Simulates a game inventory item with mutable properties
/// that JS watches via useFrameSync selector mode.
/// </summary>
public class InteropTestItem {
    public int Id { get; set; }
    public string Name { get; set; }
    public int Durability { get; set; }
    public int StackCount { get; set; }
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
    static List<InteropTestItem> _inventory = new();

    public static InteropTestState GetState() => _state;
    public static List<string> GetItems() => _items;
    public static List<InteropTestItem> GetInventory() => _inventory;

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

    public static void AddInventoryItem(int id, string name, int durability, int stackCount) {
        _inventory.Add(new InteropTestItem {
            Id = id, Name = name, Durability = durability, StackCount = stackCount, Version = 1
        });
    }

    public static void SetItemDurability(int index, int durability) {
        if (index >= 0 && index < _inventory.Count) {
            _inventory[index].Durability = durability;
            _inventory[index].Version++;
        }
    }

    public static void SetItemName(int index, string name) {
        if (index >= 0 && index < _inventory.Count) {
            _inventory[index].Name = name;
            _inventory[index].Version++;
        }
    }

    public static void SetItemStackCount(int index, int count) {
        if (index >= 0 && index < _inventory.Count) {
            _inventory[index].StackCount = count;
            _inventory[index].Version++;
        }
    }

    public static void RemoveInventoryItem(int index) {
        if (index >= 0 && index < _inventory.Count)
            _inventory.RemoveAt(index);
    }

    public static void Reset() {
        _state = null;
        _items.Clear();
        _inventory.Clear();
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

    // =========================================================================
    // MARK: Collection Sync — Typed Item Tests
    //
    // These tests validate the exact interop contract that useFrameSync's
    // selector mode + toArray rely on for the parent/child collection pattern:
    //   - Parent watches list.Count via selector → re-renders on add/remove
    //   - Each child watches its own item's properties → re-renders on mutation
    //   - Item proxies are cached (same C# object → same JS reference)
    //   - Extracted primitives from item proxies detect changes via Object.is
    // =========================================================================

    [UnityTest]
    public IEnumerator TypedCollection_ItemProxyCaching_SameReference() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);

        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var item0a = inv[0];
            var item0b = inv[0];
            var item1 = inv[1];
            (item0a === item0b) + '|' + (item0a === item1);
        ");
        Assert.AreEqual("true|false", result,
            "Same list index should return same proxy; different indices should differ");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_ItemPropertyRead_Works() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 3);

        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var sword = inv[0];
            var shield = inv[1];
            sword.Id + '|' + sword.Name + '|' + sword.Durability + '|' + sword.StackCount
                + '||' + shield.Id + '|' + shield.Name + '|' + shield.Durability + '|' + shield.StackCount;
        ");
        Assert.AreEqual("1|Sword|100|1||2|Shield|80|3", result,
            "All item properties should be readable through proxy");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_ItemPropertyChange_VisibleThroughProxy() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);

        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var sword = inv[0];
            var durBefore = sword.Durability;
            var verBefore = sword.Version;

            CS.InteropTestHelper.SetItemDurability(0, 75);

            var durAfter = sword.Durability;
            var verAfter = sword.Version;
            durBefore + '|' + durAfter + '|' + verBefore + '|' + verAfter;
        ");
        Assert.AreEqual("100|75|1|2", result,
            "Mutating item in C# should be visible through the same JS proxy");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_ItemDepsDetectChange_OnlyAffectedItem() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);

        // This simulates the exact pattern each child component uses:
        // selector: (item) => [item.Name, item.Durability, item.StackCount]
        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();

            function itemDeps(item) {
                return [item.Name, item.Durability, item.StackCount];
            }

            var swordDeps1 = itemDeps(inv[0]);
            var shieldDeps1 = itemDeps(inv[1]);

            // Only change the sword's durability
            CS.InteropTestHelper.SetItemDurability(0, 50);

            var swordDeps2 = itemDeps(inv[0]);
            var shieldDeps2 = itemDeps(inv[1]);

            function depsChanged(a, b) {
                return a.some(function(val, i) { return !Object.is(val, b[i]); });
            }

            var swordChanged = depsChanged(swordDeps1, swordDeps2);
            var shieldChanged = depsChanged(shieldDeps1, shieldDeps2);
            swordChanged + '|' + shieldChanged;
        ");
        Assert.AreEqual("true|false", result,
            "Only the mutated item's deps should change — " +
            "this is what makes per-child re-rendering work");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_VersionStamp_CatchesAnyChange() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);

        // Version stamp pattern: selector: (item) => [item.Version]
        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();

            var swordVer1 = inv[0].Version;
            var shieldVer1 = inv[1].Version;

            CS.InteropTestHelper.SetItemDurability(0, 50);

            var swordVer2 = inv[0].Version;
            var shieldVer2 = inv[1].Version;

            var swordChanged = !Object.is(swordVer1, swordVer2);
            var shieldChanged = !Object.is(shieldVer1, shieldVer2);
            swordChanged + '|' + shieldChanged + '|' + swordVer1 + '|' + swordVer2;
        ");
        Assert.AreEqual("true|false|1|2", result,
            "Version stamp should increment only on the mutated item");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_CountChange_DetectedByParentDeps() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);

        // Parent pattern: selector: (inv) => [inv.Count]
        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var countBefore = inv.Count;

            CS.InteropTestHelper.AddInventoryItem(3, 'Potion', 1, 5);

            var countAfter = inv.Count;

            var countChanged = !Object.is(countBefore, countAfter);
            countChanged + '|' + countBefore + '|' + countAfter;
        ");
        Assert.AreEqual("true|2|3", result,
            "Adding an item should change Count — detected by parent selector");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_ItemMutation_DoesNotChangeCount() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);

        // Verifies the parent does NOT re-render when only an item property changes
        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var countBefore = inv.Count;

            CS.InteropTestHelper.SetItemDurability(0, 50);
            CS.InteropTestHelper.SetItemName(1, 'Broken Shield');

            var countAfter = inv.Count;
            var countChanged = !Object.is(countBefore, countAfter);
            '' + countChanged;
        ");
        Assert.AreEqual("false", result,
            "Item property mutations should NOT change Count — parent stays stable");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_RemoveItem_ChangesCount() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);
        InteropTestHelper.AddInventoryItem(3, "Potion", 1, 5);

        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var countBefore = inv.Count;

            CS.InteropTestHelper.RemoveInventoryItem(1);

            var countAfter = inv.Count;
            var remaining0 = inv[0].Name;
            var remaining1 = inv[1].Name;
            countBefore + '|' + countAfter + '|' + remaining0 + '|' + remaining1;
        ");
        Assert.AreEqual("3|2|Sword|Potion", result,
            "Removing an item should change Count and shift indices");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_ToArrayLoop_ProducesJSArray() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);
        InteropTestHelper.AddInventoryItem(3, "Potion", 1, 5);

        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var jsArr = [];
            for (var i = 0; i < inv.Count; i++) {
                jsArr.push(inv[i]);
            }

            // Verify it's a real JS array with array methods
            var names = jsArr.map(function(item) { return item.Name; });
            var highDur = jsArr.filter(function(item) { return item.Durability > 50; });
            jsArr.length + '|' + names.join(',') + '|' + highDur.length;
        ");
        Assert.AreEqual("3|Sword,Shield,Potion|2", result,
            "toArray loop should produce a real JS array with working map/filter");
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_FullParentChildPattern_EndToEnd() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);
        InteropTestHelper.AddInventoryItem(2, "Shield", 80, 1);

        // Simulates the complete pattern:
        //   Parent: selector = (inv) => [inv.Count]
        //   Children: selector = (item) => [item.Name, item.Durability, item.StackCount]
        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();

            function parentDeps() { return [inv.Count]; }
            function childDeps(item) { return [item.Name, item.Durability, item.StackCount]; }
            function toArray(coll) {
                var arr = [];
                for (var i = 0; i < coll.Count; i++) arr.push(coll[i]);
                return arr;
            }
            function depsChanged(a, b) {
                if (a.length !== b.length) return true;
                return a.some(function(v, i) { return !Object.is(v, b[i]); });
            }

            // --- Snapshot 1: initial state ---
            var pDeps1 = parentDeps();
            var items1 = toArray(inv);
            var cDeps1_0 = childDeps(items1[0]);
            var cDeps1_1 = childDeps(items1[1]);

            // --- Mutation: change only Sword's durability ---
            CS.InteropTestHelper.SetItemDurability(0, 50);

            var pDeps2 = parentDeps();
            var cDeps2_0 = childDeps(inv[0]);
            var cDeps2_1 = childDeps(inv[1]);

            var parentChanged = depsChanged(pDeps1, pDeps2);
            var child0Changed = depsChanged(cDeps1_0, cDeps2_0);
            var child1Changed = depsChanged(cDeps1_1, cDeps2_1);

            // --- Mutation: add a new item ---
            CS.InteropTestHelper.AddInventoryItem(3, 'Potion', 1, 5);

            var pDeps3 = parentDeps();
            var parentChangedAfterAdd = depsChanged(pDeps2, pDeps3);

            JSON.stringify({
                parentChangedOnMutation: parentChanged,
                swordChildChanged: child0Changed,
                shieldChildChanged: child1Changed,
                parentChangedOnAdd: parentChangedAfterAdd,
                newCount: inv.Count,
                newItemName: inv[2].Name
            });
        ");

        StringAssert.Contains("\"parentChangedOnMutation\":false", result);
        StringAssert.Contains("\"swordChildChanged\":true", result);
        StringAssert.Contains("\"shieldChildChanged\":false", result);
        StringAssert.Contains("\"parentChangedOnAdd\":true", result);
        StringAssert.Contains("\"newCount\":3", result);
        StringAssert.Contains("\"newItemName\":\"Potion\"", result);
        yield return null;
    }

    [UnityTest]
    public IEnumerator TypedCollection_MultiplePropertyChanges_AllDetected() {
        InteropTestHelper.AddInventoryItem(1, "Sword", 100, 1);

        var result = _ctx.Eval(@"
            var inv = CS.InteropTestHelper.GetInventory();
            var sword = inv[0];

            var deps1 = [sword.Name, sword.Durability, sword.StackCount, sword.Version];

            CS.InteropTestHelper.SetItemName(0, 'Broken Sword');
            CS.InteropTestHelper.SetItemStackCount(0, 2);
            // Note: SetItemName and SetItemStackCount each bump Version,
            // so Version goes from 1 → 2 → 3

            var deps2 = [sword.Name, sword.Durability, sword.StackCount, sword.Version];

            var changes = [];
            var labels = ['Name', 'Durability', 'StackCount', 'Version'];
            for (var i = 0; i < deps1.length; i++) {
                if (!Object.is(deps1[i], deps2[i])) changes.push(labels[i]);
            }
            changes.join(',') + '|' + deps2[0] + '|' + deps2[2] + '|' + deps2[3];
        ");
        Assert.AreEqual("Name,StackCount,Version|Broken Sword|2|3", result,
            "Multiple property changes should all be detected; " +
            "Durability was not changed so it should not appear");
        yield return null;
    }
}
