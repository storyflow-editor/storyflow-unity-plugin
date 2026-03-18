# Changelog

All notable changes to the StoryFlow Unity plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-03-18

### Added

- Full runtime execution engine with 160+ node types
- Zero-setup workflow: auto-creating StoryFlowManager, auto-project discovery, auto-fallback UI
- StoryFlowComponent with Script asset reference and auto-startup script fallback
- Variable system: boolean, integer, float, string, enum, image, audio, character types
- Array operations with ForEach loop support across all 7 array types
- Live text interpolation with `{varname}` and `{Character.Name}` syntax
- RunScript (call/return with parameters and outputs) and RunFlow (jump with exit flows)
- Branch nodes with boolean expression evaluation chains
- Character system with GetCharacterVar/SetCharacterVar including built-in Name/Image fields
- Audio playback with loop, reset, advance-on-end, and allow-skip support
- Background image support with persistence and reset
- Save/Load system with slot-based persistence for global variables, characters, and once-only options
- Once-only option tracking shared across components
- Built-in fallback dialogue UI (auto-creates when no custom UI assigned)
- StoryFlowDefaultDialogueUI for explicit Inspector-wired UI setup
- Custom inspector for StoryFlowComponent with runtime debugging
- JSON importer preserving original folder structure
- Live Sync WebSocket client with auto-reconnect across play mode transitions
- Toolbar overlay button with StoryFlow logo (Connect/Sync toggle)
- StoryFlow logo in Import and Live Sync editor windows
