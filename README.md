# StoryFlow for Unity

Runtime plugin for [StoryFlow Editor](https://storyflow-editor.com) - a visual node editor for creating interactive stories and dialogue systems.

## Features

- 160+ node types - dialogue, branching, variables, arrays, characters, audio, images
- Zero-setup workflow - auto-creating manager, auto-project discovery, auto-fallback UI
- Live text interpolation - `{varname}`, `{Character.Name}`
- RunScript / RunFlow - nested scripts with parameters, outputs, and exit flows
- ForEach loops across all array types
- Audio advance-on-end with optional skip
- Character variables with built-in Name/Image field support
- Save/Load with slot-based persistence
- WebSocket Live Sync with auto-reconnect

## Requirements

- Unity 2022.3 LTS or newer (including Unity 6)
- Newtonsoft.Json and TextMeshPro (resolved automatically by Package Manager)
- StoryFlow Editor (for creating and exporting projects)

## Installation

Open your Unity project, go to **Window > Package Manager**, click **+** > **Add package from git URL**, and enter:

```
https://github.com/storyflow-editor/storyflow-unity-plugin.git
```

## Quick Start

1. **Import your project** - go to **StoryFlow > Import Project**, select your exported `build/` folder
2. **Add a StoryFlowComponent** to any GameObject
3. **Call `StartDialogue()`** from your game code:

```csharp
GetComponent<StoryFlowComponent>().StartDialogue();
```

A built-in dialogue UI appears automatically. No manager setup, no UI wiring needed.

## Live Sync

Connect to the StoryFlow Editor for real-time updates during development:

1. Open **StoryFlow > Live Sync** in Unity
2. Click **Connect** (default port: 9000)
3. Click **Sync** - or sync from the editor

Changes sync automatically when you save in the editor.

## Customizing the UI

The built-in UI is for prototyping. For production:

- **Extend `StoryFlowDialogueUI`** - override `HandleDialogueUpdated`, assign to the component's Dialogue UI field
- **Subscribe to events directly** - `OnDialogueUpdated`, `OnDialogueEnded`, etc.

```csharp
storyFlow.OnDialogueUpdated += state =>
{
    myText.text = state.Text;
    // Build option buttons from state.Options
};
```

## Save & Load

```csharp
StoryFlowManager.Instance.SaveToSlot("slot1");
StoryFlowManager.Instance.LoadFromSlot("slot1");
```

## Documentation

Full documentation at [storyflow-editor.com/integrations/unity](https://storyflow-editor.com/integrations/unity).

## Contributing

Contributions are welcome! Please read the guidelines below before submitting.

### Branch Structure

- **`main`** - latest stable release. This is what users install.
- **`dev`** - active development. All changes go here first.

### How to Contribute

1. Fork this repository
2. Create a feature branch from `dev` (`git checkout -b my-feature dev`)
3. Make your changes and commit
4. Open a Pull Request targeting the `dev` branch
5. We'll review and merge when ready

Please open an [issue](https://github.com/storyflow-editor/storyflow-unity-plugin/issues) first for large changes so we can discuss the approach.

## Changelog

See the full version history at [storyflow-editor.com/integrations/unity/changelog](https://storyflow-editor.com/integrations/unity/changelog/).

## License

[MIT](LICENSE)
