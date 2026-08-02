# Game HUD

A fake ARPG HUD packaged as a UI Cartridge, built entirely from particle effects
that ship in real games. Where `ParticlesDemo` teaches the API, this one shows
what you actually build with it — every piece is meant to be lifted straight
into a project.

| Effect | What it demonstrates |
|---|---|
| Loot collect | `attract` — coins burst outward, then converge on the gold counter and arrive exactly as they expire. Retargeted from the counter's live layout on every drop. |
| Ability bar | Two emitters per slot: an idle shimmer while castable, and a ring burst the instant the cooldown ends. `start()`/`stop()`/`burst()` — the system is never rebuilt. |
| XP bar | An emitter moved to the leading edge of the fill each tick (one interop crossing per frame). |
| Level up | `tintPalette` for multicolored confetti from **one** emitter, `aspect` for paper strips instead of dots, `edge: "stick"` so they settle on the floor. |
| Low health | Emission `rate` driven from HP without recreating the system. |

Requires OneJS 3.0.8+ and onejs-react 0.1.34+ (particle wire v2).

## Usage

1. Import this sample via the Package Manager (OneJS > Samples > Game HUD).
2. Drag `GameHUD.asset` into your JSRunner's **Cartridges** tab and click
   **Extract** (also happens automatically on first run).
3. Render the component from your app:

```tsx
import { render } from "onejs-react"
import { GameHUD } from "@cartridges/@singtaa/gameHud/game-hud"

render(<GameHUD />, __root)
```

The HUD is laid out at a fixed 960x540, which fills a 1080p screen through the
default PanelSettings (`ConstantPixelSize`, scale 2). Adjust `HUD_W` / `HUD_H`
at the top of the file to match your own panel.

See the Particles guide on onejs.com for the full API.

Note for OneJS developers: the source of truth for this component lives in the
dev container app (`Assets/Scenes/MainScene/App/~/examples/game-hud.tsx`);
keep `game-hud.tsx.txt` in sync when it changes.
