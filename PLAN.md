# StoryFlow Unity Plugin — Implementation Plan

## Overview

A Unity package for importing and running StoryFlow Editor projects. Feature parity with the Unreal Engine plugin (v1.0.3), adapted to Unity conventions and APIs.

The plugin lets game developers import `.storyflow` projects exported as JSON from StoryFlow Editor, then run interactive dialogues, branching logic, and variable systems at runtime — all controllable from C# scripts or the Unity Inspector.

**Supported Unity Versions**: 2022.3 LTS and newer (including Unity 6)

---

## Table of Contents

1. [Package Structure](#1-package-structure)
2. [Module Breakdown](#2-module-breakdown)
3. [Data Model](#3-data-model)
4. [Import System](#4-import-system)
5. [Runtime Execution](#5-runtime-execution)
6. [Dialogue System](#6-dialogue-system)
7. [Variable System](#7-variable-system)
8. [Character System](#8-character-system)
9. [Audio System](#9-audio-system)
10. [Save & Load](#10-save--load)
11. [Script & Flow Nesting](#11-script--flow-nesting)
12. [UI Integration](#12-ui-integration)
13. [Live Sync (Editor)](#13-live-sync-editor)
14. [Public API Surface](#14-public-api-surface)
15. [Unreal → Unity Mapping](#15-unreal--unity-mapping)
16. [Implementation Phases](#16-implementation-phases)

---

## 1. Package Structure

```
com.storyflow.unity/
├── package.json                          # Unity Package Manager manifest
├── LICENSE
├── README.md
├── CHANGELOG.md
│
├── Runtime/
│   ├── StoryFlow.Runtime.asmdef
│   │
│   ├── Core/
│   │   ├── StoryFlowManager.cs           # Singleton manager (replaces UE Subsystem)
│   │   ├── StoryFlowComponent.cs         # MonoBehaviour — main runtime per-actor
│   │   └── StoryFlowSettings.cs          # ScriptableObject — project-wide config
│   │
│   ├── Data/
│   │   ├── StoryFlowProjectAsset.cs      # ScriptableObject — root project
│   │   ├── StoryFlowScriptAsset.cs       # ScriptableObject — single .sfe script
│   │   ├── StoryFlowCharacterAsset.cs    # ScriptableObject — character definition
│   │   ├── StoryFlowTypes.cs             # Enums, node types, variable types
│   │   ├── StoryFlowVariant.cs           # Type-safe variant value container
│   │   ├── StoryFlowVariable.cs          # Variable definition (id, name, type, value)
│   │   ├── StoryFlowNode.cs              # Node data (id, type, data dict)
│   │   ├── StoryFlowConnection.cs        # Edge (source, target, handles)
│   │   ├── StoryFlowDialogueState.cs     # Current dialogue snapshot for UI
│   │   ├── StoryFlowCharacterData.cs     # Resolved runtime character data
│   │   ├── StoryFlowHandles.cs           # Handle string builders & constants
│   │   └── StoryFlowSaveData.cs          # Serializable save data container
│   │
│   ├── Execution/
│   │   ├── StoryFlowExecutionContext.cs   # Full runtime state machine
│   │   ├── StoryFlowEvaluator.cs          # Recursive expression evaluator
│   │   ├── StoryFlowNodeDispatcher.cs     # Node type → handler dispatch table
│   │   ├── NodeHandlers/                  # One handler per node category
│   │   │   ├── DialogueNodeHandler.cs
│   │   │   ├── BranchNodeHandler.cs
│   │   │   ├── ControlFlowNodeHandler.cs  # Start, End, RunScript, RunFlow, EntryFlow
│   │   │   ├── BooleanNodeHandler.cs      # Get/Set/And/Or/Not/Equal
│   │   │   ├── IntegerNodeHandler.cs
│   │   │   ├── FloatNodeHandler.cs
│   │   │   ├── StringNodeHandler.cs
│   │   │   ├── EnumNodeHandler.cs
│   │   │   ├── ConversionNodeHandler.cs   # Type casting nodes
│   │   │   ├── ArrayNodeHandler.cs        # All array operations + forEach
│   │   │   ├── MediaNodeHandler.cs        # Image, Audio, Character set/get
│   │   │   └── CharacterVarNodeHandler.cs
│   │   └── CallStack.cs                   # Script & flow call frames
│   │
│   ├── UI/
│   │   ├── StoryFlowDialogueUI.cs         # Abstract MonoBehaviour base for dialogue UI
│   │   └── StoryFlowDefaultDialogueUI.cs  # Optional simple default implementation
│   │
│   └── Utilities/
│       ├── StoryFlowPathNormalizer.cs     # Path normalization helpers
│       ├── StoryFlowInterpolation.cs      # {varname} text interpolation
│       └── StoryFlowSaveHelpers.cs        # JSON serialization for save/load
│
├── Editor/
│   ├── StoryFlow.Editor.asmdef
│   │
│   ├── Import/
│   │   ├── StoryFlowImporter.cs           # JSON → ScriptableObject conversion
│   │   ├── StoryFlowImporterWindow.cs     # EditorWindow UI for import
│   │   └── StoryFlowAssetPostprocessor.cs # Optional: auto-reimport on file change
│   │
│   ├── LiveSync/
│   │   ├── StoryFlowLiveSyncServer.cs     # WebSocket client to StoryFlow Editor
│   │   └── StoryFlowLiveSyncWindow.cs     # Editor window for connection status
│   │
│   ├── Inspectors/
│   │   ├── StoryFlowComponentEditor.cs    # Custom inspector for StoryFlowComponent
│   │   ├── StoryFlowProjectAssetEditor.cs # Custom inspector for project assets
│   │   └── StoryFlowSettingsProvider.cs   # Project Settings integration
│   │
│   └── Utilities/
│       └── StoryFlowEditorHelpers.cs
│
├── Samples~/
│   ├── BasicDialogue/                     # Minimal setup example
│   ├── BranchingStory/                    # Variables + branching example
│   └── CustomUI/                          # Custom dialogue UI example
│
└── Tests/
    ├── Runtime/
    │   ├── StoryFlow.Tests.Runtime.asmdef
    │   ├── ExecutionContextTests.cs
    │   ├── EvaluatorTests.cs
    │   ├── NodeHandlerTests.cs
    │   ├── VariableSystemTests.cs
    │   ├── InterpolationTests.cs
    │   └── SaveLoadTests.cs
    │
    └── Editor/
        ├── StoryFlow.Tests.Editor.asmdef
        └── ImporterTests.cs
```

---

## 2. Module Breakdown

### Runtime Assembly (`StoryFlow.Runtime`)

Contains everything needed at runtime. No UnityEditor references. Ships with builds.

| Area | Responsibility |
|------|---------------|
| **Core** | Manager singleton, per-actor component, project settings |
| **Data** | ScriptableObject assets, type definitions, serializable structs |
| **Execution** | Node graph traversal, expression evaluation, call stacks |
| **UI** | Abstract dialogue UI base class + optional default |
| **Utilities** | Path normalization, text interpolation, save helpers |

### Editor Assembly (`StoryFlow.Editor`)

Editor-only tooling. Stripped from builds.

| Area | Responsibility |
|------|---------------|
| **Import** | JSON → ScriptableObject conversion, media import |
| **LiveSync** | WebSocket connection to StoryFlow Editor for hot reload |
| **Inspectors** | Custom editors for components and assets |

---

## 3. Data Model

### 3.1 Node Types Enum

```csharp
public enum StoryFlowNodeType
{
    // Control flow
    Start, End, Branch, RunScript, RunFlow, EntryFlow,

    // Dialogue
    Dialogue,

    // Boolean
    GetBool, SetBool, AndBool, OrBool, NotBool, EqualBool,

    // Integer
    GetInt, SetInt, PlusInt, MinusInt, MultiplyInt, DivideInt,
    RandomInt, GreaterInt, GreaterOrEqualInt, LessInt, LessOrEqualInt, EqualInt,

    // Float
    GetFloat, SetFloat, PlusFloat, MinusFloat, MultiplyFloat, DivideFloat,
    RandomFloat, GreaterFloat, GreaterOrEqualFloat, LessFloat, LessOrEqualFloat, EqualFloat,

    // String
    GetString, SetString, ConcatenateString, EqualString, ContainsString,
    ToUpperCase, ToLowerCase,

    // Enum
    GetEnum, SetEnum, EqualEnum, SwitchOnEnum, RandomBranch,

    // Conversions
    IntToBoolean, FloatToBoolean, IntToString, FloatToString,
    StringToInt, StringToFloat, IntToFloat, FloatToInt, IntToEnum,

    // Arrays (Boolean, Integer, Float, String, Image, Character, Audio)
    // Each array type has: Get, Set, GetElement, SetElement, GetRandom,
    // Add, Remove, Clear, Length, Contains, FindIndex
    // Plus forEach loops for each type

    // Media
    GetImage, SetImage, SetBackgroundImage,
    GetAudio, SetAudio, PlayAudio,
    GetCharacter, SetCharacter,

    // Character variables
    GetCharacterVar, SetCharacterVar
}
```

### 3.2 StoryFlowVariant (Type-Safe Value Container)

```csharp
[System.Serializable]
public struct StoryFlowVariant
{
    // Private backing fields
    private StoryFlowVariableType type;
    private bool boolValue;
    private int intValue;
    private float floatValue;
    private string stringValue;
    private string enumValue;
    private List<StoryFlowVariant> arrayValue;

    // Typed getters/setters
    // Static factory methods: Bool(), Int(), Float(), String(), Enum()
    // ToString() for display
    // Reset()
}
```

### 3.3 StoryFlowDialogueState (UI Snapshot)

```csharp
[System.Serializable]
public class StoryFlowDialogueState
{
    public string NodeId;
    public string Title;           // Interpolated
    public string Text;            // Interpolated
    public Sprite Image;           // Resolved sprite (or Texture2D)
    public AudioClip Audio;        // Resolved audio clip
    public StoryFlowCharacterData Character;
    public List<StoryFlowTextBlock> TextBlocks;
    public List<StoryFlowOption> Options;    // Pre-filtered visible options
    public bool IsValid;
    public bool CanAdvance;        // True if no options (narrative-only)

    // Audio settings from node
    public bool AudioLoop;
    public bool AudioReset;
}
```

### 3.4 Connection Handle Format

Matches StoryFlow Editor exactly (same as Unreal plugin):

| Handle | Format |
|--------|--------|
| Default output | `source-{nodeId}-` |
| Branch true/false | `source-{nodeId}-true`, `source-{nodeId}-false` |
| Flow-through (Set*) | `source-{nodeId}-1` |
| Data output | `source-{nodeId}-boolean-`, `source-{nodeId}-integer-`, etc. |
| Dialogue option | `source-{nodeId}-{optionId}` |
| Default input | `target-{nodeId}-` |
| Typed input | `target-{nodeId}-boolean`, `target-{nodeId}-integer`, etc. |
| Loop body/complete | `source-{nodeId}-loopBody`, `source-{nodeId}-loopCompleted` |

---

## 4. Import System

### 4.1 Import Pipeline

```
StoryFlow Editor → Export JSON → Unity Import Window → ScriptableObject Assets
```

**Input**: A build/export folder from StoryFlow Editor containing:
- `project.json` — project metadata, global variables, characters
- `scripts/*.json` — individual .sfe script data (nodes, edges, variables)
- `characters.json` — character definitions
- `assets/` — media files (images, audio)

**Output** (under `Assets/StoryFlow/{ProjectName}/`):
- `SF_Project.asset` — `StoryFlowProjectAsset` (root)
- `Scripts/` — one `StoryFlowScriptAsset` per .sfe file
- `Characters/` — one `StoryFlowCharacterAsset` per character
- `Media/Images/` — imported `Sprite` or `Texture2D` assets
- `Media/Audio/` — imported `AudioClip` assets

### 4.2 Importer Logic

```csharp
public class StoryFlowImporter
{
    // Main entry point
    public static StoryFlowProjectAsset ImportProject(string buildDirectory, string outputPath);

    // Individual parsers (from JSON)
    public static Dictionary<string, StoryFlowNode> ParseNodes(JObject scriptJson);
    public static List<StoryFlowConnection> ParseConnections(JArray edgesJson);
    public static Dictionary<string, StoryFlowVariable> ParseVariables(JObject varsJson);
    public static Dictionary<string, string> ParseStrings(JObject stringsJson);
    public static Dictionary<string, StoryFlowCharacterDef> ParseCharacters(JObject charsJson);

    // Media import
    public static Texture2D ImportImage(string sourcePath, string outputPath);
    public static AudioClip ImportAudio(string sourcePath, string outputPath);
}
```

### 4.3 Import Window (`EditorWindow`)

- Folder picker for JSON export directory
- Output path selector (defaults to `Assets/StoryFlow/`)
- Import button with progress bar
- Re-import support (updates existing assets in place)
- Warning display for MP3 files (recommend WAV)

### 4.4 Connection Indexing

On import (or on `OnEnable`), `StoryFlowScriptAsset` builds lookup indices:

```csharp
// Fast edge lookup by source handle
Dictionary<string, StoryFlowConnection> sourceHandleIndex;

// All edges from a source node
Dictionary<string, List<StoryFlowConnection>> sourceNodeIndex;

// All edges targeting a node
Dictionary<string, List<StoryFlowConnection>> targetNodeIndex;
```

---

## 5. Runtime Execution

### 5.1 StoryFlowExecutionContext

The core state machine. One instance per `StoryFlowComponent`.

```csharp
public class StoryFlowExecutionContext
{
    // Current execution position
    public StoryFlowScriptAsset CurrentScript { get; private set; }
    public string CurrentNodeId { get; private set; }

    // Execution state flags
    public bool IsWaitingForInput { get; private set; }
    public bool IsExecuting { get; private set; }
    public bool IsPaused { get; private set; }
    public bool IsEnteringDialogueViaEdge { get; set; }

    // Stacks
    private List<CallFrame> callStack;        // RunScript nesting (max 20)
    private List<FlowFrame> flowCallStack;    // RunFlow depth tracking (max 50)
    private List<LoopContext> loopStack;       // forEach nesting

    // Variables (three scopes)
    private Dictionary<string, StoryFlowVariable> localVariables;
    private Dictionary<string, StoryFlowVariable> externalGlobalVariables;  // Shared ref
    private Dictionary<string, StoryFlowCharacterAsset> externalCharacters; // Shared ref

    // Per-node isolated runtime state (cached evaluations, loop counters)
    private Dictionary<string, NodeRuntimeState> nodeRuntimeStates;

    // Current dialogue
    public StoryFlowDialogueState CurrentDialogueState { get; private set; }
    public Sprite PersistentBackgroundImage { get; set; }

    // Depth guards
    private int evaluationDepth;   // Max 100
    private int processingDepth;   // Max 1000

    // Name → ID lazy indices for variable lookup
    private Dictionary<string, string> localVariableNameIndex;
    private Dictionary<string, string> globalVariableNameIndex;
}
```

### 5.2 Node Dispatch

Static dispatch table mapping `StoryFlowNodeType` → handler delegate:

```csharp
public static class StoryFlowNodeDispatcher
{
    private static readonly Dictionary<StoryFlowNodeType, Action<StoryFlowComponent, StoryFlowNode>>
        dispatchTable = new()
    {
        { StoryFlowNodeType.Start,     ControlFlowNodeHandler.HandleStart },
        { StoryFlowNodeType.End,       ControlFlowNodeHandler.HandleEnd },
        { StoryFlowNodeType.Dialogue,  DialogueNodeHandler.Handle },
        { StoryFlowNodeType.Branch,    BranchNodeHandler.Handle },
        { StoryFlowNodeType.SetBool,   BooleanNodeHandler.HandleSet },
        // ... 100+ entries
    };

    public static void ProcessNode(StoryFlowComponent component, StoryFlowNode node);
}
```

### 5.3 Expression Evaluator

Recursive, side-effect-free evaluation of expression node chains:

```csharp
public static class StoryFlowEvaluator
{
    public static bool EvaluateBoolean(StoryFlowExecutionContext ctx, string nodeId, string inputHandle);
    public static int EvaluateInteger(StoryFlowExecutionContext ctx, string nodeId, string inputHandle);
    public static float EvaluateFloat(StoryFlowExecutionContext ctx, string nodeId, string inputHandle);
    public static string EvaluateString(StoryFlowExecutionContext ctx, string nodeId, string inputHandle);
    public static string EvaluateEnum(StoryFlowExecutionContext ctx, string nodeId, string inputHandle);
    public static List<StoryFlowVariant> EvaluateBoolArray(StoryFlowExecutionContext ctx, ...);
    // ... etc for all types

    // Boolean chain pre-caching
    public static void ProcessBooleanChain(StoryFlowExecutionContext ctx, string nodeId);

    // Option visibility evaluation
    public static bool EvaluateOptionVisibility(StoryFlowExecutionContext ctx, string nodeId, string optionId);
}
```

### 5.4 Execution Flow

```
StartDialogue()
  → Initialize context with script
  → ProcessNode("0")  // Start node
    → ProcessNextNode()  // Follow edge
      → ProcessNode(targetId)
        → [dispatch to handler]
          → If Dialogue: build state, wait for input
          → If Set*: update variable, find next edge (or return to dialogue)
          → If Branch: evaluate boolean, follow true/false edge
          → If End: pop call stack or finish
          → If RunScript: push frame, switch script
          → If RunFlow: find EntryFlow, continue
          → ...
```

### 5.5 Critical Behaviors (Must Match Unreal Plugin)

1. **Set\* nodes with no outgoing edge**:
   - Check forEach loop → continue iteration
   - Check if came from Dialogue → return to re-render dialogue

2. **Dialogue fresh entry detection**:
   - `IsEnteringDialogueViaEdge` = true when arriving via normal edge
   - Audio only plays on fresh entry, not on re-render from Set* return

3. **BuildDialogueState order**:
   - Resolve character FIRST
   - Update `CurrentDialogueState.Character`
   - THEN interpolate text (so `{Character.Name}` works)

4. **Character path normalization**:
   - Normalize: `path.ToLower().Replace("/", "\\")`
   - Must match at storage AND lookup time

---

## 6. Dialogue System

### 6.1 Building Dialogue State

```csharp
private StoryFlowDialogueState BuildDialogueState(StoryFlowNode dialogueNode)
{
    var state = new StoryFlowDialogueState();
    state.NodeId = dialogueNode.Id;

    // 1. Resolve character FIRST
    state.Character = ResolveCharacter(dialogueNode);

    // 2. Get raw title/text from string table
    string rawTitle = GetString(dialogueNode, "title");
    string rawText = GetString(dialogueNode, "text");

    // 3. Interpolate variables (now {Character.Name} is available)
    state.Title = InterpolateVariables(rawTitle);
    state.Text = InterpolateVariables(rawText);

    // 4. Resolve image (with persistence logic)
    state.Image = ResolveDialogueImage(dialogueNode);

    // 5. Resolve audio
    state.Audio = ResolveDialogueAudio(dialogueNode);

    // 6. Build text blocks (non-interactive segments)
    state.TextBlocks = BuildTextBlocks(dialogueNode);

    // 7. Build visible options (filtered by once-only + visibility)
    state.Options = BuildVisibleOptions(dialogueNode);

    // 8. Determine advance capability
    state.CanAdvance = state.Options.Count == 0;

    state.IsValid = true;
    return state;
}
```

### 6.2 Options

```csharp
[System.Serializable]
public class StoryFlowOption
{
    public string Id;
    public string Text;           // Interpolated
    public bool IsOnceOnly;       // Hide after selection
    public bool IsSelected;       // Already used (for tracking)
    // Future: typed input options (string, int, float, bool, enum)
}
```

### 6.3 Text Blocks

Non-interactive dialogue segments that always display (titles, narration, etc.):

```csharp
[System.Serializable]
public class StoryFlowTextBlock
{
    public string Id;
    public string Text;           // Interpolated
}
```

### 6.4 Once-Only Tracking

Shared across all `StoryFlowComponent` instances via `StoryFlowManager`:
- Key format: `{nodeId}-{optionId}`
- `HashSet<string> usedOnceOnlyOptions`
- Persisted in save data

---

## 7. Variable System

### 7.1 Three Scopes

| Scope | Owner | Lifetime | Shared? |
|-------|-------|----------|---------|
| **Local** | Script | Per-script execution | No |
| **Global** | StoryFlowManager | Game session | Yes, across all components |
| **Character** | Character asset (runtime copy) | Game session | Yes |

### 7.2 Public Variable API (on StoryFlowComponent)

```csharp
// Boolean
public bool GetBoolVariable(string name);
public void SetBoolVariable(string name, bool value);

// Integer
public int GetIntVariable(string name);
public void SetIntVariable(string name, int value);

// Float
public float GetFloatVariable(string name);
public void SetFloatVariable(string name, float value);

// String
public string GetStringVariable(string name);
public void SetStringVariable(string name, string value);

// Enum
public string GetEnumVariable(string name);
public void SetEnumVariable(string name, string value);

// Character variables
public StoryFlowVariant GetCharacterVariable(string characterPath, string varName);
public void SetCharacterVariable(string characterPath, string varName, StoryFlowVariant value);

// Reset
public void ResetVariables();
```

### 7.3 Variable Interpolation

```csharp
// Input: "Hello {playerName}, you have {gold} gold."
// Output: "Hello Aria, you have 150 gold."

// Special: {Character.Name} → current dialogue character's display name
// Special: {Character.VarName} → character variable value

public static string InterpolateVariables(string text, StoryFlowExecutionContext ctx)
{
    return Regex.Replace(text, @"\{([^}]+)\}", match => {
        string varName = match.Groups[1].Value;
        // Check Character.X pattern first
        // Then check local variables
        // Then check global variables
        // Return original {varName} if not found
    });
}
```

### 7.4 Array Support

All array types: Boolean, Integer, Float, String, Image, Character, Audio.

Operations per type:
- Get/Set (whole array)
- GetElement, SetElement (by index)
- GetRandom
- Add, Remove, Clear
- Length, Contains, FindIndex
- ForEach loop iteration

---

## 8. Character System

### 8.1 Character Asset

```csharp
[CreateAssetMenu(menuName = "StoryFlow/Character Asset")]
public class StoryFlowCharacterAsset : ScriptableObject
{
    public string CharacterName;          // String table key
    public string ImageAssetKey;          // Reference to image asset
    public List<StoryFlowVariable> Variables;  // Per-character variables
    public string CharacterPath;          // Normalized path for lookup

    // Resolved at runtime
    [System.NonSerialized] public Sprite ResolvedImage;
    [System.NonSerialized] public string ResolvedName;
}
```

### 8.2 Runtime Character Data

```csharp
[System.Serializable]
public class StoryFlowCharacterData
{
    public string Name;                   // Resolved display name
    public Sprite Image;                  // Loaded sprite
    public Dictionary<string, StoryFlowVariant> Variables;  // Mutable runtime copies
}
```

### 8.3 Path Normalization (Critical)

```csharp
public static string NormalizeCharacterPath(string path)
{
    return path.ToLowerInvariant().Replace("/", "\\");
}
```

Must be applied at:
- Import time (when storing in dictionary)
- Runtime lookup (when finding character by path)

---

## 9. Audio System

### 9.1 Unity Audio Approach

Uses Unity's built-in audio system (`AudioSource`) instead of Unreal's `UAudioComponent`.

```csharp
// On StoryFlowComponent
[Header("Audio Settings")]
public bool StopAudioOnDialogueEnd = true;
public AudioMixerGroup DialogueAudioMixerGroup;
[Range(0f, 2f)]
public float DialogueVolumeMultiplier = 1f;

// Internal
private AudioSource dialogueAudioSource;  // Created on demand
```

### 9.2 Audio Behavior (Matching Unreal)

- **Fresh entry** (`IsEnteringDialogueViaEdge = true`):
  - If node has audio → play it (loop if `audioLoop` is set)
  - If node has no audio and `audioReset` is true → stop current audio
  - Else → keep current audio playing

- **Returning from Set\* node** (`IsEnteringDialogueViaEdge = false`):
  - Skip all audio logic (re-render only)

- **Dialogue end**: Stop audio if `StopAudioOnDialogueEnd` is true

- **Looping**: `AudioSource.loop = true` when `audioLoop` is set

- **PlayAudio node**: Fires `OnAudioPlayRequested` event for game code to handle

### 9.3 Import Notes

- WAV files: Import directly as `AudioClip`
- MP3 files: Unity natively supports MP3 import (unlike Unreal), so no decoder needed
- OGG files: Also natively supported
- Show info message recommending WAV for best compatibility

---

## 10. Save & Load

### 10.1 Save Data Structure

```csharp
[System.Serializable]
public class StoryFlowSaveData
{
    public Dictionary<string, StoryFlowVariableData> GlobalVariables;
    public Dictionary<string, StoryFlowCharacterSaveData> RuntimeCharacters;
    public HashSet<string> UsedOnceOnlyOptions;
    public string Version;
}
```

### 10.2 API (on StoryFlowManager)

```csharp
public void SaveToSlot(string slotName);
public bool LoadFromSlot(string slotName);
public bool DoesSaveExist(string slotName);
public void DeleteSave(string slotName);
```

### 10.3 Implementation

- Serialize to JSON via `JsonUtility` or Newtonsoft.Json
- Store in `Application.persistentDataPath/StoryFlow/Saves/{slotName}.json`
- Guard: refuse to load while dialogue is active
- Reset functions: `ResetGlobalVariables()`, `ResetRuntimeCharacters()`, `ResetAllState()`

### 10.4 What Gets Saved

| Saved | Not Saved |
|-------|-----------|
| Global variables | Local variables |
| Runtime character variable state | Execution position |
| Once-only option tracking | Mid-dialogue state |
| | Audio playback state |

---

## 11. Script & Flow Nesting

### 11.1 RunScript (Call Semantics)

1. Evaluate input parameters
2. Push call frame: `{ scriptPath, returnNodeId, savedLocalVars, savedFlowStack }`
3. Load target script, initialize its local variables with parameters
4. Process from start node ("0")
5. On END: pop frame, restore previous script, collect output variables
6. Continue from return node's exit edge

**Max depth**: 20

### 11.2 RunFlow (Jump Semantics)

1. Find matching `EntryFlow` node by flow ID in current script
2. Push flow frame (for depth tracking only)
3. Continue execution from entry node
4. No return — flow is a one-way in-script jump

**Max depth**: 50

### 11.3 Cross-Script Flow Stack

When RunScript pushes a call frame, the current flow stack is saved and restored on return. Each script has its own flow context.

---

## 12. UI Integration

### 12.1 Two Approaches (Same as Unreal)

**Approach A: Extend Base Class**

```csharp
public abstract class StoryFlowDialogueUI : MonoBehaviour
{
    protected StoryFlowComponent storyFlowComponent;

    public void InitializeWithComponent(StoryFlowComponent component);

    // Override these in your implementation
    protected virtual void OnDialogueStarted() { }
    protected virtual void OnDialogueUpdated(StoryFlowDialogueState state) { }
    protected virtual void OnDialogueEnded() { }
    protected virtual void OnVariableChanged(string varName, StoryFlowVariant value) { }
}
```

**Approach B: Manual Event Binding**

```csharp
// Subscribe to events directly
storyFlowComponent.OnDialogueStarted += HandleDialogueStarted;
storyFlowComponent.OnDialogueUpdated += HandleDialogueUpdated;
storyFlowComponent.OnDialogueEnded += HandleDialogueEnded;
```

### 12.2 Default Dialogue UI (Optional)

A simple uGUI-based dialogue panel included in Samples:
- Title text (TextMeshPro)
- Body text with variable interpolation
- Character portrait (Image)
- Option buttons (dynamically created)
- Continue button (for narrative-only dialogues)
- Background image panel

### 12.3 UI Toolkit Support

Provide a second sample using UI Toolkit (USS/UXML) for developers preferring the newer UI system.

---

## 13. Live Sync (Editor)

### 13.1 WebSocket Client

Editor-only feature connecting to StoryFlow Editor's WebSocket server:

```csharp
[InitializeOnLoad]
public class StoryFlowLiveSyncServer
{
    // Connects to StoryFlow Editor at configurable port
    // Receives JSON updates when scripts change
    // Applies changes to ScriptableObject assets in memory
    // Triggers re-import of modified scripts
}
```

### 13.2 Live Sync Window

- Connection status indicator
- Auto-connect toggle
- Manual connect/disconnect buttons
- Log of received updates

---

## 14. Public API Surface

### 14.1 StoryFlowComponent (Main API)

```csharp
public class StoryFlowComponent : MonoBehaviour
{
    // --- Configuration (Inspector) ---
    [Header("Project")]
    public StoryFlowProjectAsset Project;
    public string ScriptPath;              // Default script to run
    public string LanguageCode = "en";

    [Header("UI")]
    public StoryFlowDialogueUI DialogueUI; // Optional auto-bind

    [Header("Audio")]
    public bool StopAudioOnDialogueEnd = true;
    public AudioMixerGroup DialogueAudioMixerGroup;
    [Range(0f, 2f)] public float DialogueVolumeMultiplier = 1f;

    // --- Events (C# events + UnityEvents for Inspector) ---
    public event Action OnDialogueStarted;
    public event Action<StoryFlowDialogueState> OnDialogueUpdated;
    public event Action OnDialogueEnded;
    public event Action<string, StoryFlowVariant> OnVariableChanged;
    public event Action<string> OnScriptStarted;   // script path
    public event Action<string> OnScriptEnded;     // script path
    public event Action<string> OnError;
    public event Action<Sprite> OnBackgroundImageChanged;
    public event Action<AudioClip> OnAudioPlayRequested;

    // UnityEvent versions for Inspector wiring
    public UnityEvent OnDialogueStartedEvent;
    public UnityEvent<StoryFlowDialogueState> OnDialogueUpdatedEvent;
    public UnityEvent OnDialogueEndedEvent;

    // --- Control ---
    public void StartDialogue();
    public void StartDialogue(string scriptPath);
    public void SelectOption(string optionId);
    public void AdvanceDialogue();
    public void StopDialogue();
    public void PauseDialogue();
    public void ResumeDialogue();

    // --- State ---
    public StoryFlowDialogueState GetCurrentDialogue();
    public bool IsDialogueActive();
    public bool IsWaitingForInput();
    public bool IsPaused();

    // --- Variables (by display name) ---
    public bool GetBoolVariable(string name);
    public void SetBoolVariable(string name, bool value);
    public int GetIntVariable(string name);
    public void SetIntVariable(string name, int value);
    public float GetFloatVariable(string name);
    public void SetFloatVariable(string name, float value);
    public string GetStringVariable(string name);
    public void SetStringVariable(string name, string value);
    public string GetEnumVariable(string name);
    public void SetEnumVariable(string name, string value);
    public StoryFlowVariant GetCharacterVariable(string charPath, string varName);
    public void SetCharacterVariable(string charPath, string varName, StoryFlowVariant value);
    public void ResetVariables();

    // --- Localization ---
    public string GetLocalizedString(string key);
}
```

### 14.2 StoryFlowManager (Singleton)

```csharp
public class StoryFlowManager : MonoBehaviour
{
    public static StoryFlowManager Instance { get; private set; }

    // --- Project ---
    public void SetProject(StoryFlowProjectAsset project);
    public StoryFlowProjectAsset GetProject();
    public bool HasProject();

    // --- Scripts ---
    public StoryFlowScriptAsset GetScript(string path);
    public List<string> GetAllScriptPaths();

    // --- Shared State ---
    public Dictionary<string, StoryFlowVariable> GlobalVariables;
    public Dictionary<string, StoryFlowCharacterAsset> RuntimeCharacters;
    public HashSet<string> UsedOnceOnlyOptions;

    // --- Save/Load ---
    public void SaveToSlot(string slotName);
    public bool LoadFromSlot(string slotName);
    public bool DoesSaveExist(string slotName);
    public void DeleteSave(string slotName);

    // --- Reset ---
    public void ResetGlobalVariables();
    public void ResetRuntimeCharacters();
    public void ResetAllState();

    // --- Dialogue Tracking ---
    public bool IsDialogueActive();  // Any component has active dialogue
}
```

---

## 15. Unreal → Unity Mapping

| Unreal Engine | Unity | Notes |
|---------------|-------|-------|
| `UActorComponent` | `MonoBehaviour` | Attach to GameObject |
| `UGameInstanceSubsystem` | Singleton `MonoBehaviour` with `DontDestroyOnLoad` | `StoryFlowManager` |
| `UDataAsset` / `UPrimaryDataAsset` | `ScriptableObject` | Project, Script, Character assets |
| `UPROPERTY(BlueprintReadWrite)` | `[SerializeField] public` | Inspector-visible fields |
| `UFUNCTION(BlueprintCallable)` | `public` method | All public methods callable from scripts |
| `BlueprintAssignableEvent` | `UnityEvent` + C# `event` | Both Inspector and code binding |
| `UUserWidget` | `MonoBehaviour` on Canvas / UI Toolkit | Dialogue UI base |
| `USaveGame` + `SaveGameToSlot` | JSON to `Application.persistentDataPath` | Custom save system |
| `UAudioComponent` | `AudioSource` | Created on demand |
| `USoundWave` | `AudioClip` | Unity imports MP3 natively |
| `UTexture2D` | `Texture2D` / `Sprite` | Sprites for UI, Textures for 3D |
| `TSoftObjectPtr<>` | `AssetReference` (Addressables) or direct reference | Lazy loading |
| `TWeakObjectPtr<>` | `WeakReference<>` or null check | GC-friendly refs |
| `FString` | `string` | — |
| `TMap<K,V>` | `Dictionary<K,V>` | — |
| `TArray<T>` | `List<T>` | — |
| `TCHAR_TO_UTF8` | N/A | C# strings are Unicode |
| `FTimerManager` | `Coroutine` or `Invoke` | Delayed operations |
| `UE_LOG(LogStoryFlow, ...)` | `Debug.Log("[StoryFlow] ...")` | Conditional with `#if STORYFLOW_DEBUG` |
| `PostLoad()` index building | `OnEnable()` / `OnValidate()` | Rebuild indices when asset loads |
| `EditorSubsystem` | `[InitializeOnLoad]` / `EditorWindow` | Editor-only functionality |
| `UFactory` / `AssetImportTask` | `AssetDatabase.CreateAsset()` | Asset creation |
| `FWebSocketsModule` | `System.Net.WebSockets.ClientWebSocket` | .NET WebSocket API |
| minimp3 (C library) | Not needed | Unity handles MP3 natively |

---

## 16. Implementation Phases

### Phase 1: Foundation (Core Data + Import)

**Goal**: Import StoryFlow projects into Unity as ScriptableObjects.

- [ ] Package structure setup (`package.json`, assembly definitions)
- [ ] All data types (`StoryFlowTypes.cs`, `StoryFlowVariant.cs`, `StoryFlowNode.cs`, `StoryFlowConnection.cs`, `StoryFlowVariable.cs`)
- [ ] Asset ScriptableObjects (`StoryFlowProjectAsset`, `StoryFlowScriptAsset`, `StoryFlowCharacterAsset`)
- [ ] Handle system (`StoryFlowHandles.cs`)
- [ ] Path normalization utilities
- [ ] JSON importer (`StoryFlowImporter.cs`)
- [ ] Import editor window (`StoryFlowImporterWindow.cs`)
- [ ] Media import (images → Sprite/Texture2D, audio → AudioClip)
- [ ] Connection index building
- [ ] Basic tests for import pipeline

### Phase 2: Core Runtime (Execution Engine)

**Goal**: Execute node graphs from start to end.

- [ ] `StoryFlowExecutionContext` — state machine with all stacks and variables
- [ ] `StoryFlowNodeDispatcher` — dispatch table
- [ ] `StoryFlowEvaluator` — recursive expression evaluation with caching
- [ ] Control flow handlers: Start, End, Branch
- [ ] Boolean handlers: Get, Set, And, Or, Not, Equal
- [ ] Integer handlers: Get, Set, arithmetic, comparisons
- [ ] Float handlers: Get, Set, arithmetic, comparisons
- [ ] String handlers: Get, Set, Concatenate, Equal, Contains, case conversion
- [ ] Enum handlers: Get, Set, Equal, SwitchOnEnum, RandomBranch
- [ ] Conversion handlers: all type casting nodes
- [ ] Variable interpolation system
- [ ] `StoryFlowComponent` (basic — Start, Stop, ProcessNode)
- [ ] `StoryFlowManager` (basic — project loading, shared variables)
- [ ] Depth guards and cycle detection
- [ ] Runtime tests

### Phase 3: Dialogue System

**Goal**: Full dialogue display with options, characters, and media.

- [ ] `BuildDialogueState()` with correct order of operations
- [ ] Character resolution and `{Character.Name}` interpolation
- [ ] Image resolution with persistence logic
- [ ] Text block building
- [ ] Option building with visibility evaluation
- [ ] Once-only option tracking
- [ ] Set* node return-to-dialogue behavior
- [ ] Fresh entry detection for audio
- [ ] `StoryFlowDialogueUI` abstract base
- [ ] Events: OnDialogueStarted, OnDialogueUpdated, OnDialogueEnded
- [ ] AdvanceDialogue, SelectOption
- [ ] Dialogue tests

### Phase 4: Advanced Execution

**Goal**: Script nesting, flows, arrays, loops.

- [ ] RunScript handler with call stack, parameters, outputs, exit routes
- [ ] RunFlow + EntryFlow handlers with flow stack
- [ ] Cross-script flow stack save/restore
- [ ] Array handlers for all types (Get, Set, Element access, Add, Remove, etc.)
- [ ] ForEach loop handlers for all array types
- [ ] Loop stack integration with Set* no-edge behavior
- [ ] Media node handlers: SetImage, SetBackgroundImage, SetAudio, PlayAudio, SetCharacter
- [ ] Character variable handlers: GetCharacterVar, SetCharacterVar
- [ ] OnVariableChanged, OnScriptStarted, OnScriptEnded events
- [ ] OnBackgroundImageChanged, OnAudioPlayRequested events

### Phase 5: Audio + Save/Load

**Goal**: Complete audio management and persistence.

- [ ] AudioSource management on StoryFlowComponent
- [ ] Dialogue audio playback with loop/reset logic
- [ ] StopAudioOnDialogueEnd
- [ ] AudioMixerGroup support
- [ ] Volume multiplier
- [ ] PlayAudio node → event delegate
- [ ] `StoryFlowSaveData` structure
- [ ] JSON serialization/deserialization
- [ ] SaveToSlot, LoadFromSlot, DoesSaveExist, DeleteSave
- [ ] Active dialogue guard
- [ ] Reset functions
- [ ] Save/load tests

### Phase 6: Editor Tooling + Polish

**Goal**: Editor experience, live sync, samples, documentation.

- [ ] Custom inspector for `StoryFlowComponent`
- [ ] Custom inspector for `StoryFlowProjectAsset`
- [ ] Project Settings provider (`StoryFlowSettings`)
- [ ] Live Sync WebSocket client
- [ ] Live Sync editor window
- [ ] Default dialogue UI sample (uGUI)
- [ ] UI Toolkit dialogue sample
- [ ] Basic dialogue sample scene
- [ ] Branching story sample scene
- [ ] Custom UI sample scene
- [ ] Package documentation (README, API docs)
- [ ] Full test suite pass

### Phase 7: Input Options (Future)

**Goal**: Match Unreal plugin's planned typed input options.

- [ ] String input options
- [ ] Integer input options
- [ ] Float input options
- [ ] Boolean input options
- [ ] Enum input options
- [ ] On Change flow execution
- [ ] Value output handles
- [ ] UI components for each input type

---

## Dependencies

### Required
- **Unity 2022.3 LTS** (minimum supported version; also tested on Unity 6)
- **TextMeshPro** (for dialogue text rendering in samples)

### Optional
- **Addressables** (for lazy asset loading in large projects)
- **Newtonsoft.Json** (Unity package `com.unity.nuget.newtonsoft-json`) — for robust JSON parsing during import. Alternatively, use Unity's built-in `JsonUtility` where possible.

### No External Dependencies Required
- No C++ native plugins needed (unlike Unreal's minimp3)
- Unity's native MP3/OGG support eliminates the need for audio decoders
- .NET WebSocket API (`System.Net.WebSockets`) built into Unity's runtime

---

## Key Design Decisions

1. **MonoBehaviour singleton vs static class for Manager**: Use `MonoBehaviour` with `DontDestroyOnLoad` — enables Inspector configuration, coroutines, and lifecycle hooks. Auto-create if missing.

2. **ScriptableObject for all assets**: Follows Unity conventions. Enables Inspector viewing, direct references, and Addressables support.

3. **C# events + UnityEvents dual pattern**: C# events for code subscribers (performance), UnityEvents for Inspector wiring (designer-friendly).

4. **Newtonsoft.Json for import**: Unity's `JsonUtility` can't handle dictionaries or polymorphic types. The importer needs full JSON parsing. Runtime save/load can use simpler serialization.

5. **AudioSource on demand**: Don't require an AudioSource on the GameObject. Create one dynamically when first needed, similar to Unreal's `SpawnSound2D`.

6. **No async/await in execution**: Node processing is synchronous within a single frame, yielding only at dialogue nodes (waiting for input). This matches the Unreal approach and prevents complex async state management.

7. **Sprite vs Texture2D**: Use `Sprite` for UI-facing images (character portraits, dialogue images) since Unity UI components expect sprites. Store `Texture2D` as the base asset and create sprites from them.
