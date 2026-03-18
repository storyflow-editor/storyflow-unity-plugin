# StoryFlow for Unity

Import and run [StoryFlow Editor](https://storyflow-editor.com) projects in Unity. Build interactive dialogues, branching narratives, and variable-driven story logic using a visual node-based editor, then play them at runtime in your Unity game.

## Requirements

- **Unity 2022.3 LTS** or newer (including Unity 6)
- **Newtonsoft.Json** (`com.unity.nuget.newtonsoft-json` 3.2.1+)
- **TextMeshPro** (`com.unity.textmeshpro` 3.0.6+)

Both dependencies are resolved automatically by the Unity Package Manager.

## Installation

1. Open your Unity project
2. Go to **Window > Package Manager**
3. Click the **+** button and select **Add package from git URL...**
4. Enter: `https://github.com/StoryFlowEditor/storyflow-unity.git`
5. Click **Add**

### A Note on `.meta` Files

This package does not include `.meta` files -- Unity generates them automatically when the package is first imported or resolved. After importing, commit the entire package folder (including the generated `.meta` files) to your project's version control. If you distribute via `.unitypackage`, `.meta` files are bundled automatically.

## Quick Start

1. **Export your project** from StoryFlow Editor as JSON (File > Export > JSON)
2. **Import into Unity** via **StoryFlow > Import Project** in the menu bar, then select the exported folder
3. **Add a StoryFlowComponent** to any GameObject in your scene
4. **Assign the imported Project Asset** in the Inspector
5. **Call `StartDialogue()`** from a script or hook it up to a trigger:

```csharp
var storyFlow = GetComponent<StoryFlowComponent>();
storyFlow.StartDialogue();
```

6. **Subscribe to events** to drive your UI:

```csharp
storyFlow.OnDialogueUpdated += state =>
{
    // Update your dialogue UI with state.Text, state.Options, state.Character, etc.
};

storyFlow.OnDialogueEnded += () =>
{
    // Hide dialogue UI
};
```

Or use the built-in **StoryFlowDefaultDialogueUI** component for a ready-made uGUI dialogue panel.

## Features

- **160+ node types** -- dialogue, branching, boolean/integer/float/string/enum logic, arrays, characters, audio, images, and more
- **Variable system** with live text interpolation (`{varname}`, `{Character.Name}`)
- **RunScript / RunFlow** for modular, nested story structures with parameter passing
- **ForEach loops** across all array types
- **Save / Load** -- serialize and restore full execution state
- **Once-only options** tracked across components
- **Live Sync** -- WebSocket connection to StoryFlow Editor for real-time updates during development
- **Custom Inspector** for StoryFlowComponent with runtime debugging
- **Project Settings** integration for global configuration

## API Overview

| Class | Purpose |
|---|---|
| `StoryFlowComponent` | MonoBehaviour that runs a single dialogue instance. Add to any GameObject. Provides `StartDialogue()`, `SelectOption()`, `SetVariable()`, and event callbacks. |
| `StoryFlowManager` | Singleton manager for global state, once-only option tracking, and multi-component coordination. |
| `StoryFlowDialogueUI` | Abstract base class for custom dialogue UI implementations. |
| `StoryFlowProjectAsset` | ScriptableObject holding an imported project (scripts, variables, characters, assets). |
| `StoryFlowSaveData` | Serializable save data for persisting dialogue state. |

## Samples

Import samples from the Package Manager window (select the StoryFlow package, expand the Samples section):

- **Basic Dialogue** -- Minimal setup to get dialogue running
- **Branching Story** -- Variables and conditional branching
- **Custom UI** -- Implement your own dialogue UI

## Documentation

Full documentation: [storyflow-editor.com/integrations/unity](https://storyflow-editor.com/integrations/unity)

## License

MIT -- see [LICENSE](LICENSE) for details.
