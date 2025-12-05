using UnityEngine;

public class QuickJSTest : MonoBehaviour {
    QuickJSNative.Context _ctx;

    void Awake() {
        _ctx = new QuickJSNative.Context();
    }

    void Start() {
        var r1 = _ctx.Eval("1 + 2");
        Debug.Log("QuickJS 1+2 = " + r1);

        var script = @"
            function add(a, b) { return a + b; }
            add(10, 32);
        ";
        var r2 = _ctx.Eval(script, "test.js");
        Debug.Log("QuickJS add = " + r2);

        _ctx.Eval("console.log('hello from JS');", "log_test.js");

        // Static call via helpers
        _ctx.Eval("__csHelpers.callStatic('UnityEngine.Debug', 'Log', 'hello from __csHelpers.callStatic');",
            "invoke_test.js");

        // Phase 3: CS.* + property chain
        var interopScript = @"
            var go = new CS.UnityEngine.GameObject('From JS');
            console.log('Created GameObject, handle wrapper:', go);

            var v = new CS.UnityEngine.Vector3(10, 10, 0);
            console.log('Vector3 from JS:', v);

            // This should go through GetProp/SetProp on C#
            go.transform.position = v;

            go.SetActive(false);
            CS.UnityEngine.Debug.Log(""ggg"");
            CS.UnityEngine.Debug.Log(CS.UnityEngine.Time.deltaTime); // doesn't work yet

            console.log('Finished go.transform.position = v in JS');
        ";
        _ctx.Eval(interopScript, "gameobject_prop_test.js");
    }

    void OnDestroy() {
        _ctx?.Dispose();
        _ctx = null;
    }
}