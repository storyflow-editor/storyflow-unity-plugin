# StoryFlow Unity Plugin — Status

## What's Done

- Full architecture matching Unreal plugin v1.0.3 (44 C# files, ~10,500 lines)
- All 160+ node types: enum, dispatcher, evaluator, handlers
- All critical behavioral patterns ported:
  - Set* return-to-dialogue re-render (no outgoing edge fallback)
  - Fresh entry audio detection (IsEnteringDialogueViaEdge)
  - BuildDialogueState order (character FIRST, then text interpolation)
  - Character path normalization (ToLower + backslash)
  - RunScript call/return with parameter passing and output collection
  - RunFlow jump with exit flow support
  - ForEach loop mechanics across all 7 array types
  - Once-only option tracking (shared across components)
  - Variable interpolation ({varname}, {Character.Name}, {Character.VarName})
- API parity with Unreal: component, manager, save/load, events, variable access
- Handle format constants verified against Unreal source
- JSON importer with EditorWindow UI
- Default uGUI dialogue UI
- Live sync WebSocket client with message processing (connects, receives project-updated, re-imports)
- Custom inspector for StoryFlowComponent
- Custom inspector for StoryFlowProjectAsset
- Asset postprocessor for auto-detecting StoryFlow JSON imports
- Editor utility helpers (StoryFlowEditorHelpers)
- Project Settings integration
- Compilation verified via dotnet build with stub Unity types (0 errors, 0 plugin warnings)
- Package files: LICENSE (MIT), README.md, CHANGELOG.md

## What Needs To Be Done

### Must Do (before first real use)

1. **~~Compile in Unity~~** ✅ — Verified via dotnet build with stub types. Still need final verification in actual Unity 2022.3+ project.

2. **Test against real JSON export** — Export a project from StoryFlow Editor and run through the importer. Validate that nodes, edges, variables, strings, assets, and characters all parse correctly.

3. **Runtime testing** — Run a dialogue and verify node traversal, variable evaluation, branching, script nesting, and dialogue display all work correctly.

4. **~~Multi-agent file conflict check~~** ✅ — All conflicts resolved: duplicate dispatcher removed, BroadcastVariableChanged signature unified, handle constants rewritten from Unreal source, 30+ issues fixed across cross-check review.

### Should Do

5. **Write tests** — `Tests/Runtime/` and `Tests/Editor/` are empty. Priority tests:
   - Evaluator: boolean chains, integer arithmetic, string operations, array ops
   - Execution: node traversal, branch logic, RunScript call/return, ForEach loops
   - Import: JSON parsing against real exports
   - Save/Load: round-trip serialization

6. **Write samples** — `Samples~/` directories are empty. Need:
   - BasicDialogue: minimal setup scene
   - BranchingStory: variables + branching
   - CustomUI: custom dialogue UI implementation

### Nice To Have

7. **Typed input options** — Phase 7 / not yet implemented (string, int, float, bool, enum inputs on dialogue options). Same status as Unreal plugin where this is planned for future release.

8. **UI Toolkit sample** — Plan calls for a USS/UXML-based dialogue UI sample in addition to the uGUI default.
