# Particles Demo

A 2D particle engine showcase packaged as a UI Cartridge: a continuous additive
fountain, a click burst, and a pointer-following trail, all simulated and
rendered in C#, configured from a single TSX component via `useParticles` from
`onejs-react` (0.1.32+).

## Usage

1. Import this sample via the Package Manager (OneJS > Samples > Particles Demo).
2. Drag `ParticlesDemo.asset` into your JSRunner's **Cartridges** tab and click
   **Extract** (also happens automatically on first run).
3. Render the component from your app:

```tsx
import { render } from "onejs-react"
import { ParticlesDemo } from "./@cartridges/@singtaa/particlesDemo/particles-demo"

render(<ParticlesDemo />, __root)
```

See the Particles guide on onejs.com for the full API.

Note for OneJS developers: the source of truth for this component lives in the
dev container app (`Assets/Scenes/MainScene/App/~/examples/particles-demo.tsx`);
keep `particles-demo.tsx.txt` in sync when it changes.
