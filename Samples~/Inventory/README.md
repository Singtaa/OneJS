# Inventory

A loot stash packaged as a UI Cartridge — the rarity-aura effect every ARPG,
looter-shooter and gacha game ships. Where `GameHUD` shows particles reacting to
gameplay events, this one shows particles driven by **data**.

| Effect | What it demonstrates |
|---|---|
| Rarity aura | One `<RarityAura>` component whose whole config comes from a rarity table. Common items mount no system at all — a conditional *component*, never a conditional hook. |
| Reroll | `useParticles(ref, cfg, [rarity])` — changing the tier recreates the system in place from the new data. |
| Legendary | Two emitters, two sprites, one system: soft glow on the built-in disc plus crisp shards on `Texture2D.whiteTexture`. Emitters sharing a texture share a draw, so this costs exactly two. |
| Drag trail | `space: "panel"` keeps spawned particles pinned where they were emitted, so the trail stays put instead of riding the element. One emitter per rarity means no system is ever rebuilt mid-drag. |

Every system here lives on its own bare `<View>`. A host is particle-owned — the
system assigns `style.unityMaterial` to it, which would cost the slot's rounded
border its antialiasing. Put chrome on a sibling, never on the host.

Requires OneJS 3.0.8+ and onejs-react 0.1.34+ (particle wire v2).

## Usage

1. Import this sample via the Package Manager (OneJS > Samples > Inventory).
2. Drag `Inventory.asset` into your JSRunner's **Cartridges** tab and click
   **Extract** (also happens automatically on first run).
3. Render the component from your app:

```tsx
import { render } from "onejs-react"
import { Inventory } from "@cartridges/@singtaa/inventory/inventory"

render(<Inventory />, __root)
```

The grid is laid out from `COLS` / `ROWS` / `SLOT` constants at the top of the
file. Rarity tiers, colors and aura configs all live in the `RARITY` table —
that table is the intended extension point.

See the Particles guide on onejs.com for the full API.

Note for OneJS developers: the source of truth for this component lives in the
dev container app (`Assets/Scenes/MainScene/App/~/examples/inventory.tsx`);
keep `inventory.tsx.txt` in sync when it changes.
