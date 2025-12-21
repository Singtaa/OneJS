# OneJS Tests

PlayMode tests for the OneJS runtime. Run via Unity Test Runner (Window > General > Test Runner).

## Test Files

| File | Purpose | Coverage |
|------|---------|----------|
| `QuickJSPlaymodeTests.cs` | Core QuickJS functionality | Eval, static calls, constructors, generics, async/await, structs |
| `QuickJSFastPathPlaymodeTests.cs` | Zero-allocation fast path | Property access, method calls, allocation verification |
| `QuickJSUIBridgePlaymodeTests.cs` | UI Toolkit integration | Event delegation, scheduling, Promise processing |
| `UIToolkitJSPlaymodeTests.cs` | React component rendering | Button, Label, TextField, state updates |
| `QuickJSStabilityTests.cs` | Stability & monitoring | Handle monitoring, task queue, buffer overflow, exceptions |

## Running Tests

1. Open Unity Test Runner: `Window > General > Test Runner`
2. Select `PlayMode` tab
3. Click `Run All` or select specific tests

## Test Categories

### Core Functionality (QuickJSPlaymodeTests)
- Basic eval and arithmetic
- Static method calls (`CS.UnityEngine.Debug.Log`)
- Constructor invocation (`new CS.UnityEngine.GameObject`)
- Generic types (`List<T>`, `Dictionary<K,V>`)
- Property and field access
- Indexer access (`list[0]`)
- Async/await with C# Tasks
- Struct serialization (Vector3, Color, custom)

### Fast Path (QuickJSFastPathPlaymodeTests)
- Zero-allocation property getters
- Zero-allocation method calls
- Time.deltaTime hot path
- Transform.position access

### UI Bridge (QuickJSUIBridgePlaymodeTests)
- Click event delegation
- Pointer events (down, up, move)
- Key events
- Change events (TextField, Toggle, Slider)
- Promise/microtask processing

### React Integration (UIToolkitJSPlaymodeTests)
- React component mounting
- Button click handlers
- Label text updates
- TextField value binding
- State-driven re-renders

### Stability & Monitoring (QuickJSStabilityTests)
- Handle count tracking (`GetHandleCount`, `GetPeakHandleCount`)
- Handle deduplication (same object = same handle)
- Task queue monitoring (`GetPendingTaskCount`, `GetPeakTaskQueueSize`)
- Buffer overflow detection (16KB limit warning)
- Exception context preservation
- Context disposal cleanup

## Test Helpers

### AsyncTestHelper
```csharp
Task<int> GetValueAsync(int value)                    // Immediate completion
Task<string> DelayedMessageAsync(string msg, int ms)  // Delayed completion
Task DoWorkAsync(int delayMs)                         // Void async
Task<int> FailingAsync(string errorMessage)           // Throws exception
Task<GameObject> CreateGameObjectAsync(string name)   // Returns Unity object
```

### Custom Test Structs
```csharp
TestCustomPoint { float x, y; string label; }
TestNestedStruct { Vector3 position; Color color; int id; }
TestPropertyStruct { float X { get; set; } float Y { get; set; } }
```

## Writing New Tests

Follow these patterns:

```csharp
[UnityTest]
public IEnumerator MyTest_Condition_ExpectedResult() {
    // Arrange
    var ctx = new QuickJSContext();

    // Act
    var result = ctx.Eval("1 + 1");

    // Assert
    Assert.AreEqual("2", result);

    // Cleanup
    ctx.Dispose();
    yield return null;
}
```

For expected log messages:
```csharp
LogAssert.Expect(LogType.Warning, new Regex(@"pattern"));
LogAssert.Expect(LogType.Error, "exact message");
```
