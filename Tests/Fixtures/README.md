# Test Fixtures

Test fixtures for OneJS tests. Unlike the old v2 approach that used GUIDs, fixtures are stored as plain text files that can be loaded via `Resources.Load<TextAsset>()`.

## Usage

```csharp
// Load fixture in test
var fixture = Resources.Load<TextAsset>("SimpleScript");
var code = fixture.text;
```

## Fixture Categories

### Simple Scripts (No Dependencies)

For basic tests that don't need React or npm packages:
- **SimpleScript.txt** - Basic console.log and global assignment
- **UICreation.txt** - Creates UI elements via CS proxy
- **EventTest.txt** - Tests event registration and dispatch

### React Apps (Require npm build)

For full integration tests that need React reconciler:
- Would require `npm install` + `npm run build` before testing
- Not currently implemented (use inline strings instead)

## Design Decisions

1. **Inline strings preferred** - Simple tests use `const string` for clarity
2. **TextAsset for complex fixtures** - Large/reusable code goes in Resources
3. **No GUIDs** - All references are by name, not Unity asset GUID
4. **Plain text format** - Files are `.txt` for Unity to import as TextAsset

## Adding New Fixtures

1. Create `.txt` file in `Resources/` folder
2. Unity auto-imports as TextAsset
3. Load with `Resources.Load<TextAsset>("FileName")` (no extension)
