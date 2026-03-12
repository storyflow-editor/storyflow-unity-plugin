# Changelog

All notable changes to the StoryFlow Unity plugin will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2025-03-12

### Added

- Initial release matching Unreal plugin v1.0.3 feature set
- Full runtime execution engine with 160+ node types
- JSON importer with Editor window UI for importing StoryFlow Editor projects
- StoryFlowComponent and StoryFlowManager API
- Variable system with boolean, integer, float, string, enum, image, audio, and character types
- Live text interpolation with `{varname}` and `{Character.Name}` syntax
- Array operations with ForEach loop support across all 7 array types
- RunScript (call/return with parameters) and RunFlow (jump with exit flow) execution
- Branch nodes with boolean expression evaluation
- Character system with path normalization and asset resolution
- Audio playback with loop and reset support
- Save/Load system for full execution state serialization
- Once-only option tracking shared across components
- Default uGUI dialogue UI component
- Custom inspector for StoryFlowComponent
- Project Settings integration for global configuration
- Live Sync WebSocket client for real-time editor connection
- Three importable samples: Basic Dialogue, Branching Story, Custom UI
