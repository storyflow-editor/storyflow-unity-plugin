# StoryFlow for Unity

Runtime plugin for [StoryFlow Editor](https://storyflow-editor.com) — a visual node editor for creating interactive stories and dialogue systems.

## Requirements

- **Unity 2022.3 LTS** or newer (including Unity 6)
- **Newtonsoft.Json** and **TextMeshPro** (resolved automatically by Package Manager)

## Installation

Open your Unity project, go to **Window > Package Manager**, click **+** > **Add package from git URL**, and enter:

```
https://github.com/StoryFlowEditor/storyflow-unity.git
```

## Quick Start

1. **Import your project** — go to **StoryFlow > Import Project**, select your exported `build/` folder
2. **Add a StoryFlowComponent** to any GameObject (e.g., an NPC)
3. **Call `StartDialogue()`** from your game code:

```csharp
GetComponent<StoryFlowComponent>().StartDialogue();
```

That's it. A built-in dialogue UI appears automatically. No manager setup, no UI wiring needed.

Optionally assign a specific **Script** asset on the component in the Inspector. If left empty, the project's startup script runs.

## Live Sync

Connect to the StoryFlow Editor for real-time updates during development:

1. Open **StoryFlow > Live Sync** in Unity
2. Click **Connect** (default port: 9000)
3. Click **Sync** — or sync from the editor

Changes sync automatically when you save in the editor.

## Customizing the UI

The built-in UI is for prototyping. For production, either:

- **Extend `StoryFlowDialogueUI`** — override `HandleDialogueUpdated`, assign to the component's **Dialogue UI** field
- **Subscribe to events directly** — `OnDialogueUpdated`, `OnDialogueEnded`, etc.

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

## Features

- 160+ node types — dialogue, branching, variables, arrays, characters, audio, images
- Zero-setup workflow — auto-creating manager, auto-project discovery, auto-fallback UI
- Live text interpolation — `{varname}`, `{Character.Name}`
- RunScript / RunFlow — nested scripts with parameters, outputs, and exit flows
- ForEach loops across all array types
- Audio advance-on-end with optional skip
- Character variables with built-in Name/Image field support
- Save/Load with slot-based persistence
- WebSocket Live Sync with auto-reconnect
- Toolbar button for quick Connect/Sync

## Documentation

Full documentation at [storyflow-editor.com/integrations/unity](https://storyflow-editor.com/integrations/unity).

## License

[MIT](LICENSE)
