# Test Fixtures

Test fixtures for OneJS tests. Unlike the old v2 approach that used GUIDs, fixtures are stored as plain text files that can be loaded via `Resources.Load<TextAsset>()`.

## Usage

```csharp
// Load fixture in test
var fixture = Resources.Load<TextAsset>("SimpleScript");
var code = fixture.text;
```

## Contents

- **`PanelHost.cs`**: a real UI Toolkit panel rendering into a RenderTexture, for tests that need resolved layout or readable pixels (`ParticleTests`, `ShaderFXTests`). Yield two frames after adding children before asserting on geometry.
- **`Resources/*.txt`**: plain scripts with no dependencies: `SimpleScript` (console.log and a global), `UICreation` (UI elements via the CS proxy), `EventTest` (event registration and dispatch), `WebGLTest` (WebGL bridge validation). No test loads these four today; they are kept as starting points.
- **`Resources/TestCustomElement.txt`**: prebuilt React bundle loaded by `CustomElementPlaymodeTests`.
- **`CustomElement~/`**: the source of that bundle. Rebuild after changing it or onejs-react: `cd CustomElement~ && npm install && npm run build` (writes `../Resources/TestCustomElement.txt`). Its `onejs-react` dependency is a `file:` link to the container's `JSModules/onejs-react`, so it builds only inside the OneJSv3Container checkout.

## Design Decisions

1. **Inline strings preferred**: Simple tests use `const string` for clarity
2. **TextAsset for complex fixtures**: Large/reusable code goes in Resources
3. **No GUIDs**: All references are by name, not Unity asset GUID
4. **Plain text format**: Files are `.txt` for Unity to import as TextAsset

## Adding New Fixtures

1. Create `.txt` file in `Resources/` folder
2. Unity auto-imports as TextAsset
3. Load with `Resources.Load<TextAsset>("FileName")` (no extension)
