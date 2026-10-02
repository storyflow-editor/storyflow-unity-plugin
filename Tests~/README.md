# Localization regression tests

From the package root, run:

```powershell
dotnet run --project 'Tests~/StoryFlow.LocalizationTests.csproj'
```

Requires .NET 9 and the existing local, gitignored build-verification stubs at
`_build_verify~/Stubs/UnityEngineStubs.cs` and `UnityEditorStubs.cs`. Those stubs
are internal tooling and are not included with this package; the project cannot
run from a clean checkout until that tooling is supplied. The tests compile the
current Runtime and Editor C# against those stubs. They do not validate Unity
player serialization, UI rendering, fonts, or build inclusion.

The existing full local harness can additionally include
`../../Tests~/LocalizationHardeningTests.cs` and call
`RunLocalizationHardeningTests()` from its partial `Program`. Run that suite with
`dotnet run --project '_build_verify~/Tests'`.

Cases cover new and legacy source buckets, first-install language selection,
language-change events after first install, explicit changes, no-ops, refusals
and project replacement,
authored/player/legacy character-name saves, array membership and search,
runtime literal strings that equal table keys, array save provenance, cached
array reads after switching language, and stopped-component project replacement.
Host and graph character-name reads also preserve player names that equal table
keys, and resolved host array copies retain their provenance when reused.
Authored string arrays written into `.sfd` state capture the active display text and
remain literal after a language switch, without modifying the source array.

Data Asset import cases cover version 2 authored scalar, array and map overrides,
inherited override ownership, declaration opt-out, literal map keys, language
switching, session save/reset, legacy missing/version 1 exports and version-only
reimports of an already cached project. The importer schema revision ensures the
new serialized metadata is refreshed when existing exports are reimported.
Map lookups, projected arrays and map loops also preserve resolved text when it
matches another translation key. These checks cover opt-outs, legacy overrides,
restored session writes and ordinary authored maps that still need localization.

`Fixtures/character-contract-v2` vendors the current editor's generated Data Asset
and localization outputs plus resolution expectations. The test imports these
outputs and consumes all 14 localized accessor cases and three literal cases
through both manager and component accessors, switching languages in one session.
It also compares raw seed values with the authored-owner expectations. Legacy
fixtures and their declaration-only override tests remain unchanged.

The synthetic `Fixtures/pre-character-index` export contains no character index,
private project data or media. The importer/runtime cases prove that old path-based
characters still import and play, then add a current index to the same fixture to
prove stable-id lookup without weakening the path fallback.

# Lipsync component regression tests

```powershell
dotnet run --project 'Tests~/StoryFlow.LipsyncTests.csproj'
```

This suite requires .NET 9 and includes its own Unity stubs, so it runs from a
clean checkout without `_build_verify~`. It compiles the real runtime component,
dialogue handler, and graph executor. Cases cover fresh entries versus redraws,
cross-script node ids, audio-source selection and acquisition, paused-line catch-up,
manual playback and audio tails, source rebinding, modular face changes, and
handoff to other animation at rest.

The harness explicitly drives lifecycle callbacks and substitutes scene
discovery, audio playback flags/spectra, and blendshape storage. It does not
validate Unity lifecycle ordering, real audio analysis, or rendered animation.
