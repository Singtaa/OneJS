## Preliminary V8 build for OneJS (via ClearScript)

> This is work in progress and is not yet ready for normal use. Expect lots of bugs and missing features (compared with Jint). Goal here is to have seamless transition between the 2 backends (Jint and V8).

To Enable V8, use the "Tools/OneJS/Enable V8" menu item. Remember to flush (delete) your ScriptLib folder after pulling the latest changes.

### Some quick notes about ClearScript

 * Auto conversion between C# and JS types are actually very costly in ClearScript (often times more so than Jint). But as long as you keep them off any hot path, you should be able to reap the full benefits of V8's performance.
 * Jint supports Operator Overloading; ClearScript V8 cannot. So you cannot quickly add 2 vectors together using just a plus sign like you can with Jint.
 * In ClearScript V8, the handling of member access on parent or base types is more restrictive compared to Jint. Specifically, in a scenario where you have a Child class inheriting from a Parent class, ClearScript V8 enforces stricter access rules. If you obtain an instance of Child while referencing it as its Parent type, ClearScript V8 limits your access to only the members defined in the Parent class. In contrast, Jint allows access in this aspect.
 * Event handling is different in ClearScript. It's still pretty straightforward. But we should probably refactor some of  `useEventfulState` and related functionalities to accommodate seamless switching between Jint and V8.

### Sample Test Code

https://github.com/DragonGround/ScriptLib/assets/666527/1773a6ef-ca29-455b-b0af-478fe6593f9d

Here's a simple test code you can use to test Jint vs V8 performance.

```tsx
import { render, h } from "preact"
import { useEffect, useState } from "preact/hooks"

const Counter = () => {
    const [count, setCount] = useState(0)

    useEffect(() => {
        const interval = setInterval(() => {
            setCount(count => count + 1)
        }, 50)

        return () => clearInterval(interval)
    }, [])

    return <div class="w-10 h-10 bg-orange-200 m-2 justify-center items-center">{count+""}</div>
}

const Container = () => {
    const vals = Array.from(Array(100)).map((n, i) => i)

    return <div><div><div><div class="flex flex-row flex-wrap">
        {vals.map((val, i) => <Counter key={i.toString()} />)}
    </div></div></div></div>
}

render(<Container />, document.body)
```