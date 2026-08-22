# The OneJS design paradigm

How to decide what belongs in JavaScript, what belongs in C#, and what shape the
boundary between them takes.

This is not style guidance. Every rule here exists because breaking it produces
a specific, measured failure, and each one names it.

## The thesis

**JavaScript orchestrates. C# computes.** Unity's engine does the heavy lifting:
physics, rendering, audio, animation. JavaScript configures it, drives it, reacts
to it, and decides what happens next.

The line is not "never call into C# per frame" -- calling into C# is exactly what
JavaScript is for. The line is **never write the simulation in JavaScript**.

This is not a performance preference. It is what makes a OneJS app *portable*.

## Why portability forces it

OneJS runs on two very different engines:

| Platform | Engine | Character |
|---|---|---|
| WebGL | the browser's V8 or SpiderMonkey | JIT compiled |
| Desktop, mobile, console | QuickJS | interpreted |

An interpreter is roughly one to two orders of magnitude slower than a JIT on
numeric hot loops. So a game written with its simulation in JavaScript can run
beautifully in a browser and miss frame budget everywhere else. It will not fail
loudly; it will just be slow on the platforms you shipped to last.

Put the simulation in C# and the gap stops mattering, because the hot loop was
never in JavaScript. JS does the same small amount of work on both engines.

**The corollary:** a browser API that does real work is a trap, however
convenient. WebAudio is the clearest example. It exists only on WebGL, so a game
built on it cannot leave the browser at all.

**The subtler corollary**, and the one that decides most designs: what degrades
on an interpreter is *JavaScript computation*, not the crossings. A crossing is
mostly bridge work -- reflection, marshalling, a C# call -- and that cost is
similar on both engines. A `for` loop doing vector maths is pure JavaScript, and
that is what runs one to two orders of magnitude slower.

So the thing to move into C# is the **loop**, not the **call**.

## What a crossing actually costs

Measured in the Play container on V8, 20,000 iterations each:

| Operation | Per call |
|---|---:|
| `transform.position = new Vector3(...)` | 4.44 us |
| `Mathf.Sin(x)`, static, primitives | 4.76 us |
| Property read (`Time.realtimeSinceStartup`) | 2.03 us |
| The same work in pure JavaScript | 0.01 us |

A frame at 60fps is 16.67 ms. Spending 10% of it on crossings buys roughly **350
per frame**. So:

- Driving 20 GameObjects from JavaScript every frame costs about **0.09 ms**, or
  half a percent of the frame. That is fine, and it is the kind of thing
  JavaScript is *for*.
- A few hundred per frame is a real but affordable budget, worth knowing you are
  spending.
- Thousands per frame is not affordable on any engine.
- Simulating a thousand bodies **in JavaScript** is a different failure: the
  arithmetic itself is the cost, and that is what an interpreter multiplies.

Re-measure rather than trusting these numbers after a bridge change; the
benchmark is a few lines of `performance.now()` around a loop.

## The four rules

### 1. Config crosses once

A system is described by a value that crosses the boundary one time, not by a
sequence of setter calls. The 2D particle engine is the reference: an entire
emitter configuration goes over as one versioned JSON document, parsed by
`ParticleWire.cs`, and nothing crosses again while it runs.

*Breaking it looks like:* a hundred property sets on startup, each a separate
reflection call, and a visible hitch when the system is created.

### 2. Steady state scales with what the game does, not with what it contains

A system that is running but uninteresting should not cost JavaScript anything.
Emission, integration and collision happen C#-side; JavaScript hears about it
when something happens that it asked to hear about.

The distinction is between per-*entity* and per-*event* cost. Driving twenty
things the player is watching is orchestration and belongs in JS. Ticking a
thousand particles is simulation and does not.

*Breaking it looks like:* cost that grows with the size of the world rather than
with the amount of gameplay. A thousand bodies at 60Hz is 60,000 crossings a
second, which no engine affords.

### 3. Bulk data moves as one buffer, never per object

When JavaScript genuinely needs many values, they travel as a flat typed array,
not as properties read one at a time. `PainterBridge` is the reference: a whole
vector drawing becomes one numeric command buffer replayed in C# with direct
typed calls, no reflection, structs built C#-side.

*Breaking it looks like:* `for (const b of bodies) b.position.x` — two crossings
per body per frame, plus a `Vector2` boxed for each.

### 4. Commands batch into one crossing

Many operations become one call. `StyleBridge.ApplyStyles` takes an entire style
update in a single crossing rather than one per property.

*Breaking it looks like:* an API that reads naturally and is unusably slow, which
is the worst kind because it passes review.

## What this means for a wrapper

A wrapper is **not** a one-to-one mirror of a Unity class. `Rigidbody2D` exposed
to JavaScript with `position`, `velocity` and `AddForce` obeys none of the rules
above and will be slow on every platform that matters.

The shape that works owns both the simulation **and its binding to what you
see**, in C#:

```
JS  ->  describe the world once      (bodies, shapes, materials, which element
                                      each body drives)
C#  ->  simulate, and write the      (no JS involved)
        results straight onto the
        visual elements
C#  ->  raise events JS asked for    (collisions, triggers, sleep)
JS  ->  react, and change config     (one crossing, only when something changes)
```

The test for a proposed API: **does the per-frame JavaScript cost grow with the
size of the world, or with the amount of gameplay?** Growing with gameplay is
correct. Growing with the world means the simulation leaked into JavaScript.

A hundred bodies the player never looks at should cost JS nothing. Twenty the
player is dragging around can cost twenty crossings, and that is a rounding
error.

## What JavaScript is genuinely for

Not a consolation prize. This is most of a game:

- What happens next: rules, scoring, progression, win and lose conditions
- UI, which is React over UI Toolkit and belongs in JS entirely
- Reacting to events the engine raises
- Composition: which systems exist, how they are configured, when they change
- Anything that runs on interaction rather than on a clock

Wordle is 100% JavaScript and correct to be. It has no per-frame work.

## Portable by default

The container shadows the globals a game must not reach. That list is a
**portability contract**, not only a sandbox:

- **Allowed**, because OneJS implements them on every platform: `fetch`,
  `localStorage`, `performance`, `requestAnimationFrame`, `setTimeout`, `URL`,
  `btoa` and `atob`
- **Shadowed**, because they exist only in a browser: `document`, `window`,
  `AudioContext`, `WebSocket`, `XMLHttpRequest`, `Worker`, `indexedDB`,
  `location`, `navigator`
- **Given a seam** where a real need has no portable API: audio is the case, and
  `oj.audio` over Unity's `AudioSource` is the answer rather than WebAudio

If a game runs on OneJS Play, it runs everywhere OneJS runs. That promise is
worth more than any single browser convenience, and it is only true if the
boundary is enforced rather than documented.

## The seam pattern

When a capability needs different implementations per host, inject the
implementation rather than branching inside the caller. OneJS uses this shape in
four places already: the input backend, the esbuild filesystem provider, the
`oj` runtime object, and the stage presenter.

```ts
setInputBackend(backend)   // the host supplies it; callers never branch
```

A new capability that differs across hosts gets a seam, not an `if`.

## Budgets worth knowing

- **Container download**: listing a module in the manifest is nearly free, because
  engine code stripping removes what nothing references (measured: adding
  Physics2D, PhysX, Audio, Animation and AssetBundle cost 0.03 MB). Preserving
  those modules in `link.xml` is what makes them reachable from JavaScript by
  name, and that is what costs: the same four modules came to **0.71 MB**. That
  is the right way to expose a built-in assembly to JS, and the price is per
  module, once, for everyone.
- **Native callback table**: 4096 slots. Every JS function assigned to a C#
  delegate takes one.
- **Handles**: warned at 10,000 live; a leak here is usually a system that
  configures per frame instead of once.
