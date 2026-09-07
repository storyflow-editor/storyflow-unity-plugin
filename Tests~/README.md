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
authored/player/legacy character-name saves, array membership and search,
runtime literal strings that equal table keys, array save provenance, cached
array reads after switching language, and stopped-component project replacement.
Host and graph character-name reads also preserve player names that equal table
keys, and resolved host array copies retain their provenance when reused.
Authored string arrays written into `.sfd` state capture the active display text and
remain literal after a language switch, without modifying the source array.
