using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Converts JSON exports from StoryFlow Editor into Unity ScriptableObject assets.
    ///
    /// The build directory is expected to contain:
    ///   - project.json           (version, apiVersion, metadata, startupScript)
    ///   - global-variables.json  (variables, strings)
    ///   - characters.json        (characters with names/images/variables, strings, assets)
    ///   - *.json script files    (startNode, nodes, connections, variables, strings, assets, flows)
    ///   - media files referenced by assets (images, audio)
    /// </summary>
    public static class StoryFlowImporter
    {
        // ================================================================
        // Node type string → enum mapping
        // ================================================================

        private static readonly Dictionary<string, StoryFlowNodeType> NodeTypeMap = new(StringComparer.Ordinal)
        {
            // Control Flow
            { "start", StoryFlowNodeType.Start },
            { "end", StoryFlowNodeType.End },
            { "branch", StoryFlowNodeType.Branch },
            { "runScript", StoryFlowNodeType.RunScript },
            { "runFlow", StoryFlowNodeType.RunFlow },
            { "entryFlow", StoryFlowNodeType.EntryFlow },

            // Dialogue
            { "dialogue", StoryFlowNodeType.Dialogue },

            // Boolean
            { "getBool", StoryFlowNodeType.GetBool },
            { "setBool", StoryFlowNodeType.SetBool },
            { "andBool", StoryFlowNodeType.AndBool },
            { "orBool", StoryFlowNodeType.OrBool },
            { "notBool", StoryFlowNodeType.NotBool },
            { "equalBool", StoryFlowNodeType.EqualBool },

            // Integer
            { "getInt", StoryFlowNodeType.GetInt },
            { "setInt", StoryFlowNodeType.SetInt },
            { "plus", StoryFlowNodeType.PlusInt },
            { "minus", StoryFlowNodeType.MinusInt },
            { "multiply", StoryFlowNodeType.MultiplyInt },
            { "divide", StoryFlowNodeType.DivideInt },
            { "modulo", StoryFlowNodeType.ModuloInt },
            { "random", StoryFlowNodeType.RandomInt },

            // Integer Comparison
            { "greaterThan", StoryFlowNodeType.GreaterInt },
            { "greaterThanOrEqual", StoryFlowNodeType.GreaterOrEqualInt },
            { "lessThan", StoryFlowNodeType.LessInt },
            { "lessThanOrEqual", StoryFlowNodeType.LessOrEqualInt },
            { "equalInt", StoryFlowNodeType.EqualInt },

            // Float
            { "getFloat", StoryFlowNodeType.GetFloat },
            { "setFloat", StoryFlowNodeType.SetFloat },
            { "plusFloat", StoryFlowNodeType.PlusFloat },
            { "minusFloat", StoryFlowNodeType.MinusFloat },
            { "multiplyFloat", StoryFlowNodeType.MultiplyFloat },
            { "divideFloat", StoryFlowNodeType.DivideFloat },
            { "moduloFloat", StoryFlowNodeType.ModuloFloat },
            { "randomFloat", StoryFlowNodeType.RandomFloat },

            // Float Comparison
            { "greaterThanFloat", StoryFlowNodeType.GreaterFloat },
            { "greaterThanOrEqualFloat", StoryFlowNodeType.GreaterOrEqualFloat },
            { "lessThanFloat", StoryFlowNodeType.LessFloat },
            { "lessThanOrEqualFloat", StoryFlowNodeType.LessOrEqualFloat },
            { "equalFloat", StoryFlowNodeType.EqualFloat },

            // String
            { "getString", StoryFlowNodeType.GetString },
            { "setString", StoryFlowNodeType.SetString },
            { "concatenateString", StoryFlowNodeType.ConcatenateString },
            { "equalString", StoryFlowNodeType.EqualString },
            { "containsString", StoryFlowNodeType.ContainsString },
            { "toUpperCase", StoryFlowNodeType.ToUpperCase },
            { "toLowerCase", StoryFlowNodeType.ToLowerCase },

            // Enum
            { "getEnum", StoryFlowNodeType.GetEnum },
            { "setEnum", StoryFlowNodeType.SetEnum },
            { "equalEnum", StoryFlowNodeType.EqualEnum },
            { "switchOnEnum", StoryFlowNodeType.SwitchOnEnum },
            { "randomBranch", StoryFlowNodeType.RandomBranch },

            // Type Conversion
            { "intToBoolean", StoryFlowNodeType.IntToBoolean },
            { "floatToBoolean", StoryFlowNodeType.FloatToBoolean },
            { "intToString", StoryFlowNodeType.IntToString },
            { "floatToString", StoryFlowNodeType.FloatToString },
            { "stringToInt", StoryFlowNodeType.StringToInt },
            { "stringToFloat", StoryFlowNodeType.StringToFloat },
            { "intToFloat", StoryFlowNodeType.IntToFloat },
            { "floatToInt", StoryFlowNodeType.FloatToInt },
            { "intToEnum", StoryFlowNodeType.IntToEnum },
            { "booleanToInt", StoryFlowNodeType.BooleanToInt },
            { "booleanToFloat", StoryFlowNodeType.BooleanToFloat },
            { "stringToEnum", StoryFlowNodeType.StringToEnum },
            { "enumToString", StoryFlowNodeType.EnumToString },
            { "lengthString", StoryFlowNodeType.LengthString },

            // Boolean Arrays
            { "getBoolArray", StoryFlowNodeType.GetBoolArray },
            { "setBoolArray", StoryFlowNodeType.SetBoolArray },
            { "getBoolArrayElement", StoryFlowNodeType.GetBoolArrayElement },
            { "setBoolArrayElement", StoryFlowNodeType.SetBoolArrayElement },
            { "getRandomBoolArrayElement", StoryFlowNodeType.GetRandomBoolArrayElement },
            { "addToBoolArray", StoryFlowNodeType.AddBoolArrayElement },
            { "removeFromBoolArray", StoryFlowNodeType.RemoveBoolArrayElement },
            { "clearBoolArray", StoryFlowNodeType.ClearBoolArray },
            { "arrayLengthBool", StoryFlowNodeType.BoolArrayLength },
            { "arrayContainsBool", StoryFlowNodeType.BoolArrayContains },
            { "findInBoolArray", StoryFlowNodeType.FindInBoolArray },

            // Integer Arrays
            { "getIntArray", StoryFlowNodeType.GetIntArray },
            { "setIntArray", StoryFlowNodeType.SetIntArray },
            { "getIntArrayElement", StoryFlowNodeType.GetIntArrayElement },
            { "setIntArrayElement", StoryFlowNodeType.SetIntArrayElement },
            { "getRandomIntArrayElement", StoryFlowNodeType.GetRandomIntArrayElement },
            { "addToIntArray", StoryFlowNodeType.AddIntArrayElement },
            { "removeFromIntArray", StoryFlowNodeType.RemoveIntArrayElement },
            { "clearIntArray", StoryFlowNodeType.ClearIntArray },
            { "arrayLengthInt", StoryFlowNodeType.IntArrayLength },
            { "arrayContainsInt", StoryFlowNodeType.IntArrayContains },
            { "findInIntArray", StoryFlowNodeType.FindInIntArray },

            // Float Arrays
            { "getFloatArray", StoryFlowNodeType.GetFloatArray },
            { "setFloatArray", StoryFlowNodeType.SetFloatArray },
            { "getFloatArrayElement", StoryFlowNodeType.GetFloatArrayElement },
            { "setFloatArrayElement", StoryFlowNodeType.SetFloatArrayElement },
            { "getRandomFloatArrayElement", StoryFlowNodeType.GetRandomFloatArrayElement },
            { "addToFloatArray", StoryFlowNodeType.AddFloatArrayElement },
            { "removeFromFloatArray", StoryFlowNodeType.RemoveFloatArrayElement },
            { "clearFloatArray", StoryFlowNodeType.ClearFloatArray },
            { "arrayLengthFloat", StoryFlowNodeType.FloatArrayLength },
            { "arrayContainsFloat", StoryFlowNodeType.FloatArrayContains },
            { "findInFloatArray", StoryFlowNodeType.FindInFloatArray },

            // String Arrays
            { "getStringArray", StoryFlowNodeType.GetStringArray },
            { "setStringArray", StoryFlowNodeType.SetStringArray },
            { "getStringArrayElement", StoryFlowNodeType.GetStringArrayElement },
            { "setStringArrayElement", StoryFlowNodeType.SetStringArrayElement },
            { "getRandomStringArrayElement", StoryFlowNodeType.GetRandomStringArrayElement },
            { "addToStringArray", StoryFlowNodeType.AddStringArrayElement },
            { "removeFromStringArray", StoryFlowNodeType.RemoveStringArrayElement },
            { "clearStringArray", StoryFlowNodeType.ClearStringArray },
            { "arrayLengthString", StoryFlowNodeType.StringArrayLength },
            { "arrayContainsString", StoryFlowNodeType.StringArrayContains },
            { "findInStringArray", StoryFlowNodeType.FindInStringArray },

            // Image Arrays
            { "getImageArray", StoryFlowNodeType.GetImageArray },
            { "setImageArray", StoryFlowNodeType.SetImageArray },
            { "getImageArrayElement", StoryFlowNodeType.GetImageArrayElement },
            { "setImageArrayElement", StoryFlowNodeType.SetImageArrayElement },
            { "getRandomImageArrayElement", StoryFlowNodeType.GetRandomImageArrayElement },
            { "addToImageArray", StoryFlowNodeType.AddImageArrayElement },
            { "removeFromImageArray", StoryFlowNodeType.RemoveImageArrayElement },
            { "clearImageArray", StoryFlowNodeType.ClearImageArray },
            { "arrayLengthImage", StoryFlowNodeType.ImageArrayLength },
            { "arrayContainsImage", StoryFlowNodeType.ImageArrayContains },
            { "findInImageArray", StoryFlowNodeType.FindInImageArray },

            // Character Arrays
            { "getCharacterArray", StoryFlowNodeType.GetCharacterArray },
            { "setCharacterArray", StoryFlowNodeType.SetCharacterArray },
            { "getCharacterArrayElement", StoryFlowNodeType.GetCharacterArrayElement },
            { "setCharacterArrayElement", StoryFlowNodeType.SetCharacterArrayElement },
            { "getRandomCharacterArrayElement", StoryFlowNodeType.GetRandomCharacterArrayElement },
            { "addToCharacterArray", StoryFlowNodeType.AddCharacterArrayElement },
            { "removeFromCharacterArray", StoryFlowNodeType.RemoveCharacterArrayElement },
            { "clearCharacterArray", StoryFlowNodeType.ClearCharacterArray },
            { "arrayLengthCharacter", StoryFlowNodeType.CharacterArrayLength },
            { "arrayContainsCharacter", StoryFlowNodeType.CharacterArrayContains },
            { "findInCharacterArray", StoryFlowNodeType.FindInCharacterArray },

            // Audio Arrays
            { "getAudioArray", StoryFlowNodeType.GetAudioArray },
            { "setAudioArray", StoryFlowNodeType.SetAudioArray },
            { "getAudioArrayElement", StoryFlowNodeType.GetAudioArrayElement },
            { "setAudioArrayElement", StoryFlowNodeType.SetAudioArrayElement },
            { "getRandomAudioArrayElement", StoryFlowNodeType.GetRandomAudioArrayElement },
            { "addToAudioArray", StoryFlowNodeType.AddAudioArrayElement },
            { "removeFromAudioArray", StoryFlowNodeType.RemoveAudioArrayElement },
            { "clearAudioArray", StoryFlowNodeType.ClearAudioArray },
            { "arrayLengthAudio", StoryFlowNodeType.AudioArrayLength },
            { "arrayContainsAudio", StoryFlowNodeType.AudioArrayContains },
            { "findInAudioArray", StoryFlowNodeType.FindInAudioArray },

            // Loops
            { "forEachBoolLoop", StoryFlowNodeType.ForEachBoolLoop },
            { "forEachIntLoop", StoryFlowNodeType.ForEachIntLoop },
            { "forEachFloatLoop", StoryFlowNodeType.ForEachFloatLoop },
            { "forEachStringLoop", StoryFlowNodeType.ForEachStringLoop },
            { "forEachImageLoop", StoryFlowNodeType.ForEachImageLoop },
            { "forEachCharacterLoop", StoryFlowNodeType.ForEachCharacterLoop },
            { "forEachAudioLoop", StoryFlowNodeType.ForEachAudioLoop },

            // Media
            { "getImage", StoryFlowNodeType.GetImage },
            { "setImage", StoryFlowNodeType.SetImage },
            { "setBackgroundImage", StoryFlowNodeType.SetBackgroundImage },
            { "getAudio", StoryFlowNodeType.GetAudio },
            { "setAudio", StoryFlowNodeType.SetAudio },
            { "playAudio", StoryFlowNodeType.PlayAudio },
            { "getCharacter", StoryFlowNodeType.GetCharacter },
            { "setCharacter", StoryFlowNodeType.SetCharacter },

            // Character Variables
            { "getCharacterVar", StoryFlowNodeType.GetCharacterVar },
            { "setCharacterVar", StoryFlowNodeType.SetCharacterVar },

            // Map Variables
            { "getMap", StoryFlowNodeType.GetMap },
            { "setMap", StoryFlowNodeType.SetMap },
            { "getMapValue", StoryFlowNodeType.GetMapValue },
            { "setMapValue", StoryFlowNodeType.SetMapValue },
            { "hasMapKey", StoryFlowNodeType.HasMapKey },
            { "mapSize", StoryFlowNodeType.MapSize },
            { "mapKeys", StoryFlowNodeType.MapKeys },
            { "mapValues", StoryFlowNodeType.MapValues },
            { "removeMapKey", StoryFlowNodeType.RemoveMapKey },
            { "clearMap", StoryFlowNodeType.ClearMap },
            { "forEachMap", StoryFlowNodeType.ForEachMap },
        };

        // ================================================================
        // Variable type string → enum mapping
        // ================================================================

        private static readonly Dictionary<string, StoryFlowVariableType> VariableTypeMap = new(StringComparer.Ordinal)
        {
            { "boolean", StoryFlowVariableType.Boolean },
            { "integer", StoryFlowVariableType.Integer },
            { "float", StoryFlowVariableType.Float },
            { "string", StoryFlowVariableType.String },
            { "enum", StoryFlowVariableType.Enum },
            { "image", StoryFlowVariableType.Image },
            { "audio", StoryFlowVariableType.Audio },
            { "character", StoryFlowVariableType.Character },
            { "map", StoryFlowVariableType.Map },
        };

        // Supported image file extensions
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".gif" };

        // Supported audio file extensions
        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".wav", ".mp3", ".ogg", ".aif", ".aiff" };

        // Per-import cache of media content hashes, keyed by path. One media file is
        // commonly referenced by several scripts and by the project pool, and hashing a
        // multi-megabyte audio clip once per reference would be wasteful. Cleared at the
        // start of every import so a file edited between syncs is never served stale.
        //
        // Ordinal, matching every other path set here: on a case-sensitive filesystem two
        // paths differing only in case are two different files, and answering with the wrong
        // file's hash would skip a copy that was needed. The dictionary holds OS-native
        // source paths and Assets-relative destination paths side by side, which cannot
        // collide — one is absolute, the other begins with the Assets folder.
        private static readonly Dictionary<string, string> MediaContentHashes =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // Destinations already known to hold their source's bytes this run, whether this
        // import wrote them or found them already matching. Cleared with the hash cache at
        // the start of every import.
        private static readonly HashSet<string> SettledMediaDestinations =
            new HashSet<string>(StringComparer.Ordinal);

        // Set from ImportProject's force parameter for the duration of one run. Kept here
        // rather than threaded through ImportScript, ImportCharacter, ImportMediaAssets and
        // both media importers, none of which would do anything with it but pass it on: it
        // is read only by CommitAsset and TryCopyMedia. Same lifetime and the same
        // single-run, non-reentrant assumption as the two caches above.
        private static bool ForceRewrite;

        // ================================================================
        // Main Entry Point
        // ================================================================

        /// <summary>
        /// Imports a StoryFlow project from a build directory into Unity ScriptableObject assets.
        /// </summary>
        /// <param name="buildDirectory">Absolute path to the StoryFlow Editor build output directory.</param>
        /// <param name="outputPath">Unity-relative output path (e.g. "Assets/StoryFlow/MyProject").</param>
        /// <returns>The created or updated StoryFlowProjectAsset.</returns>
        public static StoryFlowProjectAsset ImportProject(string buildDirectory, string outputPath)
        {
            return ImportProject(buildDirectory, outputPath, out _, force: false);
        }

        /// <summary>
        /// Imports a StoryFlow project and reports per-file outcomes.
        ///
        /// A file that cannot be written — read-only, checked in, locked by another
        /// application — fails on its own and the import carries on with the next one, so a
        /// single locked texture no longer costs the user the whole sync. Callers must check
        /// <see cref="StoryFlowImportReport.HasFailures"/> before telling the user the import
        /// succeeded.
        /// </summary>
        /// <param name="force">
        /// Rewrites every asset and media file even when nothing in the source changed.
        /// For recovering a project whose imported assets were hand-edited, corrupted or
        /// partially deleted: the skip check compares against what the last import recorded,
        /// so it cannot see damage done to the Unity side afterwards. The hashes are
        /// recomputed and re-recorded on the forced save, so the next ordinary sync goes
        /// back to skipping. Leave false for routine syncing — this rewrites everything.
        /// </param>
        public static StoryFlowProjectAsset ImportProject(
            string buildDirectory, string outputPath, out StoryFlowImportReport report,
            bool force = false)
        {
            report = new StoryFlowImportReport();
            MediaContentHashes.Clear();
            SettledMediaDestinations.Clear();
            // Saved and restored rather than simply assigned: force is a mode for one
            // run, not a cache. An import that throws before it finishes, or a nested
            // import triggered by the auto-reimport postprocessor partway through this
            // one, would otherwise leave the flag holding the wrong answer for whatever
            // runs next — silently making a forced run incremental, or the reverse.
            bool previousForce = ForceRewrite;
            ForceRewrite = force;

            try
            {
                // Normalized once, here: past this point every path derived from outputPath is
                // already in AssetDatabase form, so even plain string concatenation is safe.
                outputPath = ToAssetPath(outputPath);

                if (!Directory.Exists(buildDirectory))
                    throw new DirectoryNotFoundException($"Build directory not found: {buildDirectory}");

                // --- Read project.json ---
                string projectJsonPath = Path.Combine(buildDirectory, "project.json");
                if (!File.Exists(projectJsonPath))
                    throw new FileNotFoundException("project.json not found in build directory.", projectJsonPath);

                string projectJsonText = File.ReadAllText(projectJsonPath);
                JObject projectJson = JObject.Parse(projectJsonText);

                string version = projectJson.Value<string>("version") ?? "";
                string apiVersion = projectJson.Value<string>("apiVersion") ?? "";
                string startupScript = projectJson.Value<string>("startupScript") ?? "";

                JObject metadata = projectJson.Value<JObject>("metadata");
                string title = metadata?.Value<string>("title") ?? "Untitled";
                string description = metadata?.Value<string>("description") ?? "";

                // --- Ensure output directory ---
                EnsureDirectory(outputPath);
                // All assets go directly under outputPath, preserving build directory structure
                string scriptsDir = outputPath;
                string charactersDir = outputPath;
                string mediaImagesDir = outputPath;
                string mediaAudioDir = outputPath;

                // Condensed forms feed the project asset's certification: a change to either file
                // has to invalidate the project hash even when project.json itself is untouched.
                string globalVariablesCondensed = string.Empty;
                string charactersCondensed = string.Empty;

                // --- Read global variables ---
                var globalVariableEntries = new List<StoryFlowProjectAsset.GlobalVariableEntry>();
                var globalStringEntries = new List<StoryFlowProjectAsset.GlobalStringEntry>();
                // Media referenced by project-scoped (global) variables — e.g. a global Image
                // variable wired into a Set Character Variable node. These live in
                // global-variables.json's own "assets" section, scoped to neither a script nor a
                // character, so they are resolved into the project's resolved-asset pool below.
                var globalAssetEntries = new List<StoryFlowScriptAsset.SerializedAsset>();

                string globalVarsPath = Path.Combine(buildDirectory, "global-variables.json");
                if (File.Exists(globalVarsPath))
                {
                    JObject globalVarsJson = JObject.Parse(File.ReadAllText(globalVarsPath));
                    globalVariablesCondensed = globalVarsJson.ToString(Newtonsoft.Json.Formatting.None);
                    JObject gVars = globalVarsJson.Value<JObject>("variables");
                    if (gVars != null)
                    {
                        foreach (var prop in gVars.Properties())
                        {
                            var varObj = prop.Value as JObject;
                            if (varObj == null) continue;

                            globalVariableEntries.Add(ParseGlobalVariableEntry(prop.Name, varObj));
                        }
                    }

                    JObject gStrings = globalVarsJson.Value<JObject>("strings");
                    if (gStrings != null)
                    {
                        globalStringEntries.AddRange(FlattenStrings(gStrings));
                    }

                    JObject gAssets = globalVarsJson.Value<JObject>("assets");
                    if (gAssets != null)
                    {
                        globalAssetEntries = ParseAssets(gAssets);
                    }
                }

                // --- Read characters.json ---
                var characterReferences = new List<StoryFlowProjectAsset.CharacterReference>();
                // Character-scoped media beyond the single portrait: custom image/audio-typed
                // character variables and character map image/audio values. Like global-variable
                // assets, these live in characters.json's "assets" section but are otherwise
                // unresolved, so they are imported into the project pool below.
                var characterAssetEntries = new List<StoryFlowScriptAsset.SerializedAsset>();
                // Portrait key → already-resolved sprite (from ImportCharacter). Registered into the
                // project pool directly so the portrait's texture isn't re-imported a second time.
                var characterPortraitAssets = new Dictionary<string, UnityEngine.Object>();
                string charactersJsonPath = Path.Combine(buildDirectory, "characters.json");
                if (File.Exists(charactersJsonPath))
                {
                    JObject charactersJson = JObject.Parse(File.ReadAllText(charactersJsonPath));
                    charactersCondensed = charactersJson.ToString(Newtonsoft.Json.Formatting.None);
                    JObject charsObj = charactersJson.Value<JObject>("characters");
                    JObject charStrings = charactersJson.Value<JObject>("strings");
                    JObject charAssets = charactersJson.Value<JObject>("assets");

                    // Merge character strings into global strings and build local lookup
                    var charStringLookup = new Dictionary<string, string>();
                    if (charStrings != null)
                    {
                        globalStringEntries.AddRange(FlattenStrings(charStrings));
                        foreach (var langProp in charStrings.Properties())
                        {
                            var langObj = langProp.Value as JObject;
                            if (langObj == null) continue;
                            foreach (var strProp in langObj.Properties())
                            {
                                charStringLookup[strProp.Name] = strProp.Value.ToString();
                            }
                        }
                    }

                    // Parse character-scoped assets once. characterAssetEntries feeds project-pool
                    // resolution (every asset, not just the portrait); charAssetLookup (assetKey →
                    // path) is what ImportCharacter uses to resolve the portrait. a.Id equals the
                    // asset's object key in every export, so it matches the character's image ref.
                    var charAssetLookup = new Dictionary<string, string>();
                    if (charAssets != null)
                    {
                        characterAssetEntries = ParseAssets(charAssets);
                        foreach (var a in characterAssetEntries)
                            if (!string.IsNullOrEmpty(a.Path))
                                charAssetLookup[a.Id] = a.Path;
                    }

                    if (charsObj != null)
                    {
                        foreach (var charProp in charsObj.Properties())
                        {
                            string normalizedPath = StoryFlowPathNormalizer.NormalizeCharacterPath(charProp.Name);
                            var charObj = charProp.Value as JObject;
                            if (charObj == null) continue;

                            var charAsset = ImportCharacter(
                                normalizedPath, charObj, charStringLookup, charAssetLookup,
                                buildDirectory, charactersDir, mediaImagesDir, report);

                            // ImportCharacter already imported the portrait into ResolvedImage; keep
                            // that sprite to register in the project pool (and skip below) so the
                            // portrait texture isn't imported twice per import.
                            if (!string.IsNullOrEmpty(charAsset.ImageAssetKey) && charAsset.ResolvedImage != null)
                                characterPortraitAssets[charAsset.ImageAssetKey] = charAsset.ResolvedImage;

                            characterReferences.Add(new StoryFlowProjectAsset.CharacterReference
                            {
                                Path = normalizedPath,
                                Asset = charAsset
                            });
                        }
                    }
                }

                // --- Find and import script JSON files ---
                var scriptReferences = new List<StoryFlowProjectAsset.ScriptReference>();
                var scriptFiles = FindJsonScriptFiles(buildDirectory);

                foreach (string relativeScriptPath in scriptFiles)
                {
                    string fullScriptPath = Path.Combine(buildDirectory, relativeScriptPath);
                    string scriptJsonText = File.ReadAllText(fullScriptPath);
                    JObject scriptJson = JObject.Parse(scriptJsonText);

                    // Script path as used by runtime (forward slashes, lowercase)
                    string scriptKey = relativeScriptPath.Replace("\\", "/").ToLowerInvariant();

                    // Import the script
                    var scriptAsset = ImportScript(
                        scriptKey, scriptJson, buildDirectory,
                        scriptsDir, mediaImagesDir, mediaAudioDir, report);

                    scriptReferences.Add(new StoryFlowProjectAsset.ScriptReference
                    {
                        Path = scriptKey,
                        Asset = scriptAsset
                    });
                }

                // --- Create / update project asset ---
                string projectAssetPath = CombineAssetPath(outputPath, "Project.asset");
                var projectAsset = AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>(projectAssetPath);
                bool isNewProject = projectAsset == null;
                if (isNewProject)
                    projectAsset = ScriptableObject.CreateInstance<StoryFlowProjectAsset>();

                projectAsset.Version = version;
                projectAsset.ApiVersion = apiVersion;
                projectAsset.Title = title;
                projectAsset.Description = description;
                // Resolve startup script reference from imported scripts
                projectAsset.StartupScript = null;
                if (!string.IsNullOrEmpty(startupScript))
                {
                    string startupKey = startupScript.Replace("\\", "/").ToLowerInvariant();
                    foreach (var sr in scriptReferences)
                    {
                        if (sr.Path == startupKey)
                        {
                            projectAsset.StartupScript = sr.Asset;
                            break;
                        }
                    }
                }
                projectAsset.ScriptReferences = scriptReferences;
                projectAsset.CharacterReferences = characterReferences;
                projectAsset.GlobalVariableEntries = globalVariableEntries;
                projectAsset.GlobalStringEntries = globalStringEntries;

                // Import and resolve project-scoped media into the project's resolved-asset pool so
                // runtime ResolveAsset<T> can find keys that appear only in global-variables.json or
                // characters.json — e.g. a global Image variable driving a character portrait, or a
                // custom image-typed character variable holding an alternate pose. Rebuild the pool
                // each import so removed/renamed assets don't leave stale entries; ClearResolvedAssets
                // (not List.Clear) also invalidates the runtime cache.
                projectAsset.ClearResolvedAssets();
                ImportMediaAssets(globalAssetEntries, buildDirectory, mediaImagesDir, mediaAudioDir, projectAsset.SetResolvedAsset, report);
                // Portraits are already imported by ImportCharacter, so drop them from the copy pass
                // (avoids a redundant second texture import) and register their resolved sprites directly.
                // a.Id equals the portrait's ImageAssetKey because the export keys each asset by its id;
                // if they ever diverged this would simply fall back to re-importing the portrait (no break).
                characterAssetEntries.RemoveAll(a => characterPortraitAssets.ContainsKey(a.Id));
                ImportMediaAssets(characterAssetEntries, buildDirectory, mediaImagesDir, mediaAudioDir, projectAsset.SetResolvedAsset, report);
                foreach (var portrait in characterPortraitAssets)
                    projectAsset.SetResolvedAsset(portrait.Key, portrait.Value);

                // Create asset AFTER all data is set so the first disk write contains full state
                if (isNewProject)
                    AssetDatabase.CreateAsset(projectAsset, projectAssetPath);

                CommitAsset(
                    projectAsset, projectAssetPath,
                    projectAsset.ImportedSourceHash,
                    CertifyProject(
                        projectJson.ToString(Newtonsoft.Json.Formatting.None),
                        globalVariablesCondensed, charactersCondensed, projectAsset),
                    isNewProject,
                    hash => projectAsset.ImportedSourceHash = hash,
                    report);

                AssetDatabase.Refresh();

                AssignDefaultProject(projectAsset, report);

                if (report.HasFailures)
                {
                    Debug.LogError($"[StoryFlow] Imported project '{title}' with {report.FailedCount} " +
                                   $"failure(s) ({report.Summarize()}). These files are still stale on " +
                                   "disk and will be retried on the next sync:\n  " +
                                   string.Join("\n  ", report.Failures));
                }
                else
                {
                    Debug.Log($"[StoryFlow] Imported project '{title}': {scriptReferences.Count} scripts, " +
                              $"{characterReferences.Count} characters, {globalVariableEntries.Count} " +
                              $"global variables ({report.Summarize()}).");
                }

                return projectAsset;
            }
            finally
            {
                ForceRewrite = previousForce;
            }
        }

        /// <summary>
        /// Points StoryFlowSettings.DefaultProject at the imported project so runtime
        /// discovery works in player builds. Assets in plain folders are stripped from
        /// builds; the settings asset lives in a Resources folder and anchors the
        /// project (and everything it references) into the build.
        /// </summary>
        private static void AssignDefaultProject(
            StoryFlowProjectAsset projectAsset, StoryFlowImportReport report)
        {
            var settings = StoryFlowEditorHelpers.FindOrCreateSettings();
            if (settings == null)
            {
                Debug.LogWarning("[StoryFlow] Could not find or create StoryFlowSettings. " +
                                 "Player builds will not find the project automatically; " +
                                 "create the settings asset via Edit > Project Settings > StoryFlow.");
                return;
            }

            string settingsPath = ToAssetPath(AssetDatabase.GetAssetPath(settings));
            if (!settingsPath.Contains("/Resources/"))
            {
                Debug.LogWarning($"[StoryFlow] StoryFlowSettings at \"{settingsPath}\" is not inside a " +
                                 "Resources folder, so player builds cannot load it and will not find " +
                                 "the project. Move it to a Resources folder " +
                                 "(e.g. Assets/Resources/StoryFlowSettings.asset).");
            }

            if (settings.DefaultProject == projectAsset)
                return;

            if (settings.DefaultProject != null)
            {
                // Respect an explicit choice pointing at another still-existing project
                Debug.LogWarning("[StoryFlow] StoryFlowSettings.DefaultProject already points to " +
                                 $"\"{settings.DefaultProject.Title}\"; leaving it unchanged. Update it in " +
                                 "Edit > Project Settings > StoryFlow if the newly imported project " +
                                 "should be the default.");
                return;
            }

            // Same order as CommitAsset: settle writability first, so a settings asset that
            // cannot be written is not left dirty holding a change nobody agreed to.
            string settingsAssetPath = ToAssetPath(AssetDatabase.GetAssetPath(settings));
            if (!TryMakeAssetWritable(settingsAssetPath, report))
                return;

            settings.DefaultProject = projectAsset;
            EditorUtility.SetDirty(settings);
            if (TrySaveAsset(settings, settingsAssetPath, report))
            {
                Debug.Log($"[StoryFlow] StoryFlowSettings.DefaultProject set to \"{projectAsset.Title}\" " +
                          "so player builds can locate the project.");
            }
        }

        // ================================================================
        // Script Import
        // ================================================================

        private static StoryFlowScriptAsset ImportScript(
            string scriptPath, JObject scriptJson, string buildDirectory,
            string scriptsDir, string imagesDir, string audioDir, StoryFlowImportReport report)
        {
            // Preserve folder structure: "scripts/hello/main_menu.json" → "Scripts/scripts/hello/main_menu.asset"
            string relativePath = scriptPath.Replace("\\", "/").Replace(".json", "");
            string assetPath = CombineAssetPath(scriptsDir, relativePath + ".asset");
            EnsureDirectory(AssetParentFolder(assetPath));

            var scriptAsset = AssetDatabase.LoadAssetAtPath<StoryFlowScriptAsset>(assetPath);
            bool isNewScript = scriptAsset == null;
            if (isNewScript)
                scriptAsset = ScriptableObject.CreateInstance<StoryFlowScriptAsset>();

            scriptAsset.ScriptPath = scriptPath;
            scriptAsset.StartNodeId = scriptJson.Value<string>("startNode") ?? "0";

            // Parse nodes
            JObject nodesObj = scriptJson.Value<JObject>("nodes");
            if (nodesObj != null)
                scriptAsset.SetNodes(ParseNodes(nodesObj));

            // Parse connections
            JArray connectionsArr = scriptJson.Value<JArray>("connections");
            if (connectionsArr != null)
                scriptAsset.SetConnections(ParseConnections(connectionsArr));

            // Parse variables
            JObject varsObj = scriptJson.Value<JObject>("variables");
            if (varsObj != null)
                scriptAsset.SetVariables(ParseVariables(varsObj));

            // Parse strings
            JObject stringsObj = scriptJson.Value<JObject>("strings");
            if (stringsObj != null)
                scriptAsset.SetStrings(ParseStrings(stringsObj));

            // Parse assets (and import media files)
            JObject assetsObj = scriptJson.Value<JObject>("assets");
            if (assetsObj != null)
            {
                var parsedAssets = ParseAssets(assetsObj);
                scriptAsset.SetAssets(parsedAssets);

                // Import referenced media files and resolve them
                ImportMediaAssets(parsedAssets, buildDirectory, imagesDir, audioDir, scriptAsset.SetResolvedAsset, report);
            }

            // Parse flows
            JArray flowsArr = scriptJson.Value<JArray>("flows");
            if (flowsArr != null)
                scriptAsset.SetFlows(ParseFlows(flowsArr));

            // Create asset AFTER all data is set so the first disk write contains full state
            if (isNewScript)
                AssetDatabase.CreateAsset(scriptAsset, assetPath);

            CommitAsset(
                scriptAsset, assetPath,
                scriptAsset.ImportedSourceHash,
                CertifyScript(scriptJson.ToString(Newtonsoft.Json.Formatting.None), scriptAsset),
                isNewScript,
                hash => scriptAsset.ImportedSourceHash = hash,
                report);

            return scriptAsset;
        }

        // ================================================================
        // Character Import
        // ================================================================

        private static StoryFlowCharacterAsset ImportCharacter(
            string normalizedPath, JObject charObj,
            Dictionary<string, string> stringLookup,
            Dictionary<string, string> assetLookup,
            string buildDirectory, string charactersDir, string imagesDir,
            StoryFlowImportReport report)
        {
            // Preserve folder structure: "scripts\newfile.sfc" → "Characters/scripts/newfile.asset"
            string relativePath = normalizedPath.Replace("\\", "/").Replace(".sfc", "");
            string assetPath = CombineAssetPath(charactersDir, relativePath + ".asset");
            EnsureDirectory(AssetParentFolder(assetPath));

            var charAsset = AssetDatabase.LoadAssetAtPath<StoryFlowCharacterAsset>(assetPath);
            bool isNewChar = charAsset == null;
            if (isNewChar)
                charAsset = ScriptableObject.CreateInstance<StoryFlowCharacterAsset>();

            charAsset.CharacterPath = normalizedPath;

            // Resolve name from string table
            string nameKey = charObj.Value<string>("name") ?? "";
            charAsset.CharacterName = stringLookup.TryGetValue(nameKey, out var resolvedName) ? resolvedName : nameKey;

            // Resolve image
            string imageAssetKey = charObj.Value<string>("image") ?? "";
            charAsset.ImageAssetKey = imageAssetKey;
            if (!string.IsNullOrEmpty(imageAssetKey) && assetLookup.TryGetValue(imageAssetKey, out var imagePath))
            {
                var sprite = ImportImageAsset(buildDirectory, imagePath, imagesDir, report);
                charAsset.ResolvedImage = sprite;
            }

            // Parse character variables
            var variables = new List<StoryFlowVariable>();
            JObject varsObj = charObj.Value<JObject>("variables");
            if (varsObj != null)
            {
                foreach (var varProp in varsObj.Properties())
                {
                    var varObj = varProp.Value as JObject;
                    if (varObj == null) continue;

                    string varName = varObj.Value<string>("name") ?? varProp.Name;
                    string varTypeName = varObj.Value<string>("type") ?? "string";
                    bool isArray = varObj.Value<bool?>("isArray") ?? false;

                    var varType = ParseVariableType(varTypeName);

                    // Map variables carry declared key/value types and per-side enum values
                    var keyType = default(StoryFlowVariableType);
                    var valueType = default(StoryFlowVariableType);
                    var keyEnumValues = new List<string>();
                    var valueEnumValues = new List<string>();
                    if (varType == StoryFlowVariableType.Map)
                        ParseMapTypeInfo(varObj, out keyType, out valueType, out keyEnumValues, out valueEnumValues);

                    // Map default values are an ordered [{key,value},...] entry array,
                    // typed per the declared key/value types — not a scalar
                    var defaultValue = varType == StoryFlowVariableType.Map
                        ? StoryFlowVariant.DeserializeMapFromJson(keyType, valueType,
                            varObj["value"] is JArray mapEntries
                                ? mapEntries.ToString(Newtonsoft.Json.Formatting.None)
                                : null)
                        : ParseDefaultValue(varType, varObj["value"], isArray);

                    variables.Add(new StoryFlowVariable
                    {
                        Id = varProp.Name,
                        Name = varName,
                        Type = varType,
                        Value = defaultValue,
                        IsArray = isArray,
                        KeyType = keyType,
                        ValueType = valueType,
                        KeyEnumValues = keyEnumValues,
                        ValueEnumValues = valueEnumValues,
                        DefaultValueJson = (isArray || varType == StoryFlowVariableType.Map) && varObj["value"] != null
                            ? varObj["value"].ToString(Newtonsoft.Json.Formatting.None)
                            : null
                    });
                }
            }
            charAsset.Variables = variables;

            // Create asset AFTER all data is set so the first disk write contains full state
            if (isNewChar)
                AssetDatabase.CreateAsset(charAsset, assetPath);

            CommitAsset(
                charAsset, assetPath,
                charAsset.ImportedSourceHash,
                CertifyCharacter(charObj.ToString(Newtonsoft.Json.Formatting.None), charAsset),
                isNewChar,
                hash => charAsset.ImportedSourceHash = hash,
                report);

            return charAsset;
        }

        // ================================================================
        // JSON → Data Parsers
        // ================================================================

        /// <summary>
        /// Parses nodes from the JSON "nodes" object (keyed by node ID).
        /// Each node contains: type (string), id (string), and additional data fields.
        /// </summary>
        public static List<StoryFlowScriptAsset.SerializedNode> ParseNodes(JObject nodesJson)
        {
            var nodes = new List<StoryFlowScriptAsset.SerializedNode>();

            foreach (var prop in nodesJson.Properties())
            {
                var nodeObj = prop.Value as JObject;
                if (nodeObj == null) continue;

                string nodeId = nodeObj.Value<string>("id") ?? prop.Name;
                string nodeTypeStr = nodeObj.Value<string>("type") ?? "unknown";

                var serializedNode = new StoryFlowScriptAsset.SerializedNode
                {
                    Id = nodeId,
                    Type = ParseNodeType(nodeTypeStr),
                    RawType = nodeTypeStr
                };

                // Flatten all non-structural fields into the Data dictionary
                foreach (var nodeProp in nodeObj.Properties())
                {
                    string key = nodeProp.Name;
                    // Skip the id and type fields (already stored directly)
                    if (key == "id" || key == "type") continue;

                    // Store complex sub-objects as their JSON string for downstream parsing
                    string valueStr;
                    if (nodeProp.Value.Type == JTokenType.Object || nodeProp.Value.Type == JTokenType.Array)
                    {
                        valueStr = nodeProp.Value.ToString(Newtonsoft.Json.Formatting.None);
                    }
                    else if (nodeProp.Value.Type == JTokenType.Boolean)
                    {
                        valueStr = nodeProp.Value.Value<bool>() ? "true" : "false";
                    }
                    else if (nodeProp.Value.Type == JTokenType.Float)
                    {
                        valueStr = nodeProp.Value.Value<double>().ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        valueStr = nodeProp.Value.ToString();
                    }

                    // Only remap "choices" → "options" (editor export format variation)
                    string mappedKey = key == "choices" ? "options" : key;

                    serializedNode.Data.Add(new StoryFlowScriptAsset.SerializedKV
                    {
                        Key = mappedKey,
                        Value = valueStr
                    });
                }

                // Character variable nodes: the export uses "variable" for the variable name,
                // but runtime code reads "variableName". Add the mapping when characterPath is present.
                if (nodeObj["characterPath"] != null && nodeObj["variable"] != null)
                {
                    serializedNode.Data.Add(new StoryFlowScriptAsset.SerializedKV
                    {
                        Key = "variableName",
                        Value = nodeObj.Value<string>("variable") ?? ""
                    });
                }

                nodes.Add(serializedNode);
            }

            return nodes;
        }

        /// <summary>
        /// Parses connections from the JSON "connections" array.
        /// Each connection: id, source, target, sourceHandle, targetHandle.
        /// </summary>
        public static List<StoryFlowConnection> ParseConnections(JArray edgesJson)
        {
            var connections = new List<StoryFlowConnection>();

            foreach (var token in edgesJson)
            {
                var edgeObj = token as JObject;
                if (edgeObj == null) continue;

                connections.Add(new StoryFlowConnection
                {
                    Id = edgeObj.Value<string>("id") ?? "",
                    Source = edgeObj.Value<string>("source") ?? "",
                    Target = edgeObj.Value<string>("target") ?? "",
                    SourceHandle = edgeObj.Value<string>("sourceHandle") ?? "",
                    TargetHandle = edgeObj.Value<string>("targetHandle") ?? ""
                });
            }

            return connections;
        }

        /// <summary>
        /// Parses variables from the JSON "variables" object (keyed by variable ID).
        /// Each variable: id, name, type, value, isArray?, enumValues?, isInput?, isOutput?.
        /// </summary>
        public static List<StoryFlowScriptAsset.SerializedVariable> ParseVariables(JObject varsJson)
        {
            var variables = new List<StoryFlowScriptAsset.SerializedVariable>();

            foreach (var prop in varsJson.Properties())
            {
                var varObj = prop.Value as JObject;
                if (varObj == null) continue;

                string varId = varObj.Value<string>("id") ?? prop.Name;
                string varName = varObj.Value<string>("name") ?? "";
                string varTypeName = varObj.Value<string>("type") ?? "boolean";
                bool isArray = varObj.Value<bool?>("isArray") ?? false;
                bool isInput = varObj.Value<bool?>("isInput") ?? false;
                bool isOutput = varObj.Value<bool?>("isOutput") ?? false;

                var varType = ParseVariableType(varTypeName);

                // Map variables carry declared key/value types and per-side enum values
                var keyType = default(StoryFlowVariableType);
                var valueType = default(StoryFlowVariableType);
                var keyEnumValues = new List<string>();
                var valueEnumValues = new List<string>();
                if (varType == StoryFlowVariableType.Map)
                    ParseMapTypeInfo(varObj, out keyType, out valueType, out keyEnumValues, out valueEnumValues);

                // Serialize the default value to a string
                string defaultValueJson = SerializeDefaultValue(varType, varObj["value"], isArray);

                // Parse enum values if present
                var enumValues = new List<string>();
                JArray enumArr = varObj.Value<JArray>("enumValues");
                if (enumArr != null)
                {
                    foreach (var ev in enumArr)
                        enumValues.Add(ev.ToString());
                }

                variables.Add(new StoryFlowScriptAsset.SerializedVariable
                {
                    Id = varId,
                    Name = varName,
                    Type = varType,
                    DefaultValueJson = defaultValueJson,
                    IsArray = isArray,
                    EnumValues = enumValues,
                    IsInput = isInput,
                    IsOutput = isOutput,
                    KeyType = keyType,
                    ValueType = valueType,
                    KeyEnumValues = keyEnumValues,
                    ValueEnumValues = valueEnumValues
                });
            }

            return variables;
        }

        /// <summary>
        /// Parses strings from the JSON "strings" object.
        /// The format is nested by language: {"en": {"key1": "value1", ...}}.
        /// Flattened to a list of key-value pairs.
        /// </summary>
        public static List<StoryFlowScriptAsset.SerializedString> ParseStrings(JObject stringsJson)
        {
            var strings = new List<StoryFlowScriptAsset.SerializedString>();

            foreach (var langProp in stringsJson.Properties())
            {
                var langObj = langProp.Value as JObject;
                if (langObj == null) continue;

                string langPrefix = langProp.Name; // e.g. "en"

                foreach (var strProp in langObj.Properties())
                {
                    strings.Add(new StoryFlowScriptAsset.SerializedString
                    {
                        Key = $"{langPrefix}.{strProp.Name}",
                        Value = strProp.Value.ToString()
                    });
                }
            }

            return strings;
        }

        /// <summary>
        /// Parses assets from the JSON "assets" object (keyed by asset ID).
        /// Each asset: id, path, type.
        /// </summary>
        public static List<StoryFlowScriptAsset.SerializedAsset> ParseAssets(JObject assetsJson)
        {
            var assets = new List<StoryFlowScriptAsset.SerializedAsset>();

            foreach (var prop in assetsJson.Properties())
            {
                var assetObj = prop.Value as JObject;
                if (assetObj == null) continue;

                assets.Add(new StoryFlowScriptAsset.SerializedAsset
                {
                    Id = assetObj.Value<string>("id") ?? prop.Name,
                    Path = assetObj.Value<string>("path") ?? "",
                    Type = assetObj.Value<string>("type") ?? ""
                });
            }

            return assets;
        }

        /// <summary>
        /// Parses flow definitions from the JSON "flows" array.
        /// Each flow: id, name, entryNodeId, isExit.
        /// </summary>
        public static List<StoryFlowFlowDef> ParseFlows(JArray flowsJson)
        {
            var flows = new List<StoryFlowFlowDef>();

            foreach (var token in flowsJson)
            {
                var flowObj = token as JObject;
                if (flowObj == null) continue;

                flows.Add(new StoryFlowFlowDef
                {
                    Id = flowObj.Value<string>("id") ?? "",
                    Name = flowObj.Value<string>("name") ?? "",
                    EntryNodeId = flowObj.Value<string>("entryNodeId") ?? "",
                    IsExit = flowObj.Value<bool?>("isExit") ?? false
                });
            }

            return flows;
        }

        /// <summary>
        /// Maps a JSON node type string to the StoryFlowNodeType enum.
        /// Returns StoryFlowNodeType.Unknown for unrecognized types.
        /// </summary>
        public static StoryFlowNodeType ParseNodeType(string typeString)
        {
            if (string.IsNullOrEmpty(typeString))
                return StoryFlowNodeType.Unknown;

            if (NodeTypeMap.TryGetValue(typeString, out var nodeType))
                return nodeType;

            Debug.LogWarning($"[StoryFlow] Unknown node type: '{typeString}'");
            return StoryFlowNodeType.Unknown;
        }

        /// <summary>
        /// Maps a JSON variable type string to the StoryFlowVariableType enum.
        /// Defaults to Boolean for unrecognized types.
        /// </summary>
        public static StoryFlowVariableType ParseVariableType(string typeString)
        {
            if (string.IsNullOrEmpty(typeString))
                return StoryFlowVariableType.Boolean;

            if (VariableTypeMap.TryGetValue(typeString, out var varType))
                return varType;

            Debug.LogWarning($"[StoryFlow] Unknown variable type: '{typeString}', defaulting to Boolean.");
            return StoryFlowVariableType.Boolean;
        }

        // ================================================================
        // Media Asset Import
        // ================================================================

        /// <summary>
        /// Imports media files referenced by script assets and populates ResolvedAssets via the provided setter.
        /// </summary>
        private static void ImportMediaAssets(
            List<StoryFlowScriptAsset.SerializedAsset> assets,
            string buildDirectory,
            string imagesDir, string audioDir,
            Action<string, UnityEngine.Object> setResolvedAsset,
            StoryFlowImportReport report)
        {
            foreach (var asset in assets)
            {
                if (string.IsNullOrEmpty(asset.Path)) continue;

                string ext = Path.GetExtension(asset.Path).ToLowerInvariant();

                if (asset.Type == "image" || ImageExtensions.Contains(ext))
                {
                    var sprite = ImportImageAsset(buildDirectory, asset.Path, imagesDir, report);
                    if (sprite != null)
                        setResolvedAsset(asset.Id, sprite);
                }
                else if (asset.Type == "audio" || AudioExtensions.Contains(ext))
                {
                    var clip = ImportAudioAsset(buildDirectory, asset.Path, audioDir, report);
                    if (clip != null)
                        setResolvedAsset(asset.Id, clip);
                }
            }
        }

        /// <summary>
        /// Imports an image from the build directory into the Unity project as a Sprite.
        /// Returns the imported Sprite, or null on failure.
        /// </summary>
        private static Sprite ImportImageAsset(
            string buildDirectory, string relativePath, string imagesDir, StoryFlowImportReport report)
        {
            string sourcePath = Path.Combine(buildDirectory, relativePath);
            if (!File.Exists(sourcePath))
            {
                // Fallback: asset may already exist from a previous full sync (data-only sync)
                string normalizedRel = relativePath.Replace("\\", "/");
                string existingPath = CombineAssetPath(imagesDir, normalizedRel);
                var existing = AssetDatabase.LoadAssetAtPath<Sprite>(existingPath);
                if (existing != null)
                {
                    // The destination already holds this file, which is the whole point of a
                    // data-only sync. Counted, so the run reports work skipped rather than
                    // reporting nothing and reading like a run that did nothing.
                    report.RecordMediaUpToDate(existingPath);
                    return existing;
                }

                // Left uncounted deliberately: a missing source with nothing imported at the
                // destination is the pre-existing missing-media behaviour, a warning and a
                // null, not a write this run failed to perform.
                Debug.LogWarning($"[StoryFlow] Image not found: {sourcePath}");
                return null;
            }

            // Preserve folder structure from build directory
            string normalizedRelative = relativePath.Replace("\\", "/");
            // Nothing to fall back to: the refused path names no location inside the project,
            // so looking an asset up through it could only ever answer null.
            if (RefuseEscapingMediaPath(normalizedRelative, report))
                return null;

            string destPath = CombineAssetPath(imagesDir, normalizedRelative);
            EnsureDirectory(AssetParentFolder(destPath));

            // Copy file to project. A copy that fails is this file's problem alone — the
            // sprite is left unresolved, the failure is reported by name, and the rest of
            // the import carries on.
            if (!TryCopyMedia(sourcePath, destPath, report))
                return AssetDatabase.LoadAssetAtPath<Sprite>(destPath);

            // Configure as sprite: single mode, full rect mesh, bottom-center pivot
            var importer = AssetImporter.GetAtPath(destPath) as TextureImporter;
            if (importer != null)
            {
                bool needsReimport = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    needsReimport = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single)
                {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    needsReimport = true;
                }
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (settings.spriteMeshType != SpriteMeshType.FullRect ||
                    settings.spriteAlignment != (int)SpriteAlignment.BottomCenter)
                {
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
                    settings.spritePivot = new Vector2(0.5f, 0f);
                    importer.SetTextureSettings(settings);
                    needsReimport = true;
                }
                if (needsReimport)
                {
                    importer.SaveAndReimport();
                }
            }

            // Load the sprite
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(destPath);
            return sprite;
        }

        /// <summary>
        /// Imports an audio file from the build directory into the Unity project as an AudioClip.
        /// Returns the imported AudioClip, or null on failure.
        /// </summary>
        private static AudioClip ImportAudioAsset(
            string buildDirectory, string relativePath, string audioDir, StoryFlowImportReport report)
        {
            string sourcePath = Path.Combine(buildDirectory, relativePath);
            if (!File.Exists(sourcePath))
            {
                // Fallback: asset may already exist from a previous full sync (data-only sync)
                string normalizedRel = relativePath.Replace("\\", "/");
                string existingPath = CombineAssetPath(audioDir, normalizedRel);
                var existing = AssetDatabase.LoadAssetAtPath<AudioClip>(existingPath);
                if (existing != null)
                {
                    // See ImportImageAsset: a data-only sync resolving through the
                    // destination is work skipped, and it is counted as such.
                    report.RecordMediaUpToDate(existingPath);
                    return existing;
                }

                // Left uncounted deliberately: pre-existing missing-media behaviour.
                Debug.LogWarning($"[StoryFlow] Audio not found: {sourcePath}");
                return null;
            }

            string fileName = Path.GetFileName(relativePath);
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            if (ext == ".mp3")
            {
                Debug.Log($"[StoryFlow] MP3 audio detected: {fileName}. Unity supports MP3 natively, " +
                          "but WAV is recommended for best quality and compatibility.");
            }

            // Preserve folder structure from build directory
            string normalizedRelative = relativePath.Replace("\\", "/");
            // See ImportImageAsset: a refused path names nothing inside the project.
            if (RefuseEscapingMediaPath(normalizedRelative, report))
                return null;

            string destPath = CombineAssetPath(audioDir, normalizedRelative);
            EnsureDirectory(AssetParentFolder(destPath));

            if (!TryCopyMedia(sourcePath, destPath, report))
                return AssetDatabase.LoadAssetAtPath<AudioClip>(destPath);

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(destPath);
            return clip;
        }

        // ================================================================
        // Helper Methods
        // ================================================================

        /// <summary>
        /// Finds all JSON script files in the build directory (excluding project.json,
        /// global-variables.json, and characters.json).
        /// </summary>
        private static List<string> FindJsonScriptFiles(string buildDirectory)
        {
            var results = new List<string>();
            var excludedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "project.json",
                "global-variables.json",
                "characters.json"
            };

            FindJsonFilesRecursive(buildDirectory, buildDirectory, excludedFiles, results);
            return results;
        }

        private static void FindJsonFilesRecursive(
            string currentDir, string rootDir,
            HashSet<string> excludedFileNames,
            List<string> results)
        {
            foreach (var file in Directory.GetFiles(currentDir, "*.json"))
            {
                string fileName = Path.GetFileName(file);
                string relativePath = GetRelativePath(rootDir, file);

                // Skip excluded top-level files
                if (currentDir == rootDir && excludedFileNames.Contains(fileName))
                    continue;

                results.Add(relativePath);
            }

            foreach (var subDir in Directory.GetDirectories(currentDir))
            {
                FindJsonFilesRecursive(subDir, rootDir, excludedFileNames, results);
            }
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                !basePath.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                basePath += Path.DirectorySeparatorChar;
            }

            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Parses a global variable entry from project JSON into a ProjectAsset entry.
        /// </summary>
        private static StoryFlowProjectAsset.GlobalVariableEntry ParseGlobalVariableEntry(string id, JObject varObj)
        {
            string name = varObj.Value<string>("name") ?? "";
            string typeName = varObj.Value<string>("type") ?? "boolean";
            bool isArray = varObj.Value<bool?>("isArray") ?? false;

            var varType = ParseVariableType(typeName);

            // Map variables carry declared key/value types and per-side enum values
            var keyType = default(StoryFlowVariableType);
            var valueType = default(StoryFlowVariableType);
            var keyEnumValues = new List<string>();
            var valueEnumValues = new List<string>();
            if (varType == StoryFlowVariableType.Map)
                ParseMapTypeInfo(varObj, out keyType, out valueType, out keyEnumValues, out valueEnumValues);

            string defaultValueJson = SerializeDefaultValue(varType, varObj["value"], isArray);

            var enumValues = new List<string>();
            JArray enumArr = varObj.Value<JArray>("enumValues");
            if (enumArr != null)
            {
                foreach (var ev in enumArr)
                    enumValues.Add(ev.ToString());
            }

            return new StoryFlowProjectAsset.GlobalVariableEntry
            {
                Id = id,
                Name = name,
                Type = varType,
                DefaultValueJson = defaultValueJson,
                IsArray = isArray,
                EnumValues = enumValues,
                KeyType = keyType,
                ValueType = valueType,
                KeyEnumValues = keyEnumValues,
                ValueEnumValues = valueEnumValues
            };
        }

        /// <summary>
        /// Flattens a nested strings object into a list of GlobalStringEntry.
        /// </summary>
        private static List<StoryFlowProjectAsset.GlobalStringEntry> FlattenStrings(JObject stringsJson)
        {
            var entries = new List<StoryFlowProjectAsset.GlobalStringEntry>();

            foreach (var langProp in stringsJson.Properties())
            {
                var langObj = langProp.Value as JObject;
                if (langObj == null) continue;

                string langPrefix = langProp.Name;
                foreach (var strProp in langObj.Properties())
                {
                    entries.Add(new StoryFlowProjectAsset.GlobalStringEntry
                    {
                        Key = $"{langPrefix}.{strProp.Name}",
                        Value = strProp.Value.ToString()
                    });
                }
            }

            return entries;
        }

        /// <summary>
        /// Parses a map variable's declared keyType/valueType and per-side enum value lists.
        /// Wire shape: keyType 'string'|'integer'|'enum'; valueType any of the 8 scalar types.
        /// </summary>
        private static void ParseMapTypeInfo(JObject varObj,
            out StoryFlowVariableType keyType, out StoryFlowVariableType valueType,
            out List<string> keyEnumValues, out List<string> valueEnumValues)
        {
            keyType = ParseVariableType(varObj.Value<string>("keyType") ?? "string");
            valueType = ParseVariableType(varObj.Value<string>("valueType") ?? "string");

            keyEnumValues = new List<string>();
            JArray keyEnumArr = varObj.Value<JArray>("keyEnumValues");
            if (keyEnumArr != null)
            {
                foreach (var ev in keyEnumArr)
                    keyEnumValues.Add(ev.ToString());
            }

            valueEnumValues = new List<string>();
            JArray valueEnumArr = varObj.Value<JArray>("valueEnumValues");
            if (valueEnumArr != null)
            {
                foreach (var ev in valueEnumArr)
                    valueEnumValues.Add(ev.ToString());
            }
        }

        /// <summary>
        /// Serializes a variable's default value to a string for storage.
        /// </summary>
        private static string SerializeDefaultValue(StoryFlowVariableType type, JToken valueToken, bool isArray)
        {
            if (valueToken == null || valueToken.Type == JTokenType.Null)
                return "";

            if (isArray || type == StoryFlowVariableType.Map)
            {
                // Arrays and maps are stored as their raw JSON representation. Map entries
                // ([{key,value},...]) persist verbatim: keys are raw identifiers (never
                // localized) and string-typed values are strings-table keys, resolved at
                // runtime read time.
                return valueToken.ToString(Newtonsoft.Json.Formatting.None);
            }

            switch (type)
            {
                case StoryFlowVariableType.Boolean:
                    return valueToken.Type == JTokenType.Boolean
                        ? (valueToken.Value<bool>() ? "true" : "false")
                        : valueToken.ToString().ToLowerInvariant();

                case StoryFlowVariableType.Integer:
                    if (valueToken.Type == JTokenType.Integer)
                        return valueToken.Value<long>().ToString();
                    return valueToken.ToString();

                case StoryFlowVariableType.Float:
                    if (valueToken.Type == JTokenType.Float || valueToken.Type == JTokenType.Integer)
                        return valueToken.Value<double>().ToString(CultureInfo.InvariantCulture);
                    return valueToken.ToString();

                case StoryFlowVariableType.String:
                case StoryFlowVariableType.Enum:
                case StoryFlowVariableType.Image:
                case StoryFlowVariableType.Audio:
                case StoryFlowVariableType.Character:
                    return valueToken.ToString();

                default:
                    return valueToken.ToString();
            }
        }

        /// <summary>
        /// Parses a default value from JSON into a StoryFlowVariant.
        /// Used for character variables where we need the typed variant directly.
        /// </summary>
        private static StoryFlowVariant ParseDefaultValue(StoryFlowVariableType type, JToken valueToken, bool isArray)
        {
            var variant = new StoryFlowVariant { Type = type };

            if (valueToken == null || valueToken.Type == JTokenType.Null)
                return variant;

            if (isArray && valueToken.Type == JTokenType.Array)
            {
                variant.ArrayValue = new List<StoryFlowVariant>();
                foreach (var item in valueToken as JArray)
                {
                    variant.ArrayValue.Add(ParseDefaultValue(type, item, false));
                }
                return variant;
            }

            switch (type)
            {
                case StoryFlowVariableType.Boolean:
                    variant.BoolValue = valueToken.Type == JTokenType.Boolean
                        ? valueToken.Value<bool>()
                        : valueToken.ToString().ToLowerInvariant() == "true";
                    break;
                case StoryFlowVariableType.Integer:
                    if (int.TryParse(valueToken.ToString(), out int intVal))
                        variant.IntValue = intVal;
                    break;
                case StoryFlowVariableType.Float:
                    if (float.TryParse(valueToken.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float floatVal))
                        variant.FloatValue = floatVal;
                    break;
                case StoryFlowVariableType.String:
                    variant.StringValue = valueToken.ToString();
                    break;
                case StoryFlowVariableType.Enum:
                    variant.EnumValue = valueToken.ToString();
                    break;
                case StoryFlowVariableType.Image:
                case StoryFlowVariableType.Audio:
                case StoryFlowVariableType.Character:
                    variant.StringValue = valueToken.ToString();
                    break;
            }

            return variant;
        }

        // ================================================================
        // Per-file write failures
        // ================================================================

        /// <summary>
        /// Turns an exception from a raw file write into a message a user can act on.
        /// Unity's own error for these is "Access is denied", which names neither the cause
        /// nor the fix.
        /// </summary>
        private static string DescribeWriteFailure(Exception ex)
        {
            // The framework's own text is carried through in every branch: the guidance below
            // is the likely cause, not the certain one — UnauthorizedAccessException also
            // covers a directory sitting where the file should be, and a support thread needs
            // to be able to tell those apart.
            if (ex is UnauthorizedAccessException)
                return "the file is read-only or locked. Check it out in version control " +
                       "(Perforce and Plastic mark unopened files read-only) or clear its " +
                       $"read-only attribute, then sync again ({ex.Message})";

            // Before IOException, which it derives from. A build directory nested a few
            // folders deep plus a long exported asset path clears Windows' 260-character
            // limit without anything looking unusual.
            if (ex is PathTooLongException)
                return $"the full path is too long for the operating system ({ex.Message}). " +
                       "Import to a shorter output folder, or move the project closer to the " +
                       "drive root, then sync again";

            if (ex is IOException)
                return $"the file could not be written ({ex.Message}). It may be open in " +
                       "another application, or the destination drive may be out of space";

            return ex.Message;
        }

        /// <summary>
        /// Settles whether an asset may be written, before anything about it is modified.
        ///
        /// This has to run before the object is marked dirty, not after. Refusing the save
        /// alone is not enough: an object left dirty is written by the next flush that comes
        /// along — editor shutdown, a stray SaveAssets, the user pressing save — and Unity's
        /// write strips the read-only attribute on the way past. Measured against a real
        /// editor, the refusal held for the duration of the import and the file was rewritten
        /// twenty milliseconds later as the editor exited. So nothing that will be refused is
        /// ever dirtied, and the refusal is the only outcome.
        ///
        /// With a provider configured, MakeEditable is Unity's own checkout. With none, a
        /// read-only file is refused rather than force-cleared, for the same reasons the
        /// media path refuses one: it is nearly always owned by a version control system
        /// Unity is not driving — including a provider that is configured but offline — and
        /// overwriting it loses the change at the next sync or revert.
        /// </summary>
        private static bool TryMakeAssetWritable(string assetPath, StoryFlowImportReport report)
        {
            if (HasActiveVersionControl())
            {
                if (AssetDatabase.MakeEditable(assetPath))
                    return true;

                report.RecordAssetFailure(assetPath,
                    "version control refused to check the asset out. Check it out manually " +
                    "and sync again");
                return false;
            }

            if (!IsReadOnlyOnDisk(assetPath))
                return true;

            report.RecordAssetFailure(assetPath,
                "the asset is read-only. StoryFlow will not clear the attribute for you: " +
                "a read-only file is usually owned by a version control system such as " +
                "Perforce or Plastic, and overwriting it loses your change on the next " +
                "sync. Check the file out, or clear its read-only attribute, then sync again");
            return false;
        }

        /// <summary>
        /// Saves one asset and reports whether the bytes actually reached disk.
        ///
        /// AssetDatabase.SaveAssets() saves everything dirty in one call and swallows
        /// per-file failures, so a read-only .asset stayed stale on disk while the live-sync
        /// log still said "Import complete". SaveAssetIfDirty saves exactly one object, which
        /// makes the outcome attributable to a path; Unity leaves an object dirty when it
        /// could not write it (and logs rather than throws), so the dirty flag after the call
        /// is the signal this checks.
        /// </summary>
        private static bool TrySaveAsset(
            UnityEngine.Object asset, string assetPath, StoryFlowImportReport report)
        {
            // Nothing to write, and deliberately nothing to count: the up-to-date tally is
            // CommitAsset's, which is the only place that knows whether skipping was the
            // decision or merely the outcome. Two owners of one counter double-count.
            if (!EditorUtility.IsDirty(asset))
                return true;

            try
            {
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            catch (Exception ex)
            {
                report.RecordAssetFailure(assetPath, DescribeWriteFailure(ex));
                return false;
            }

            // Two independent signals, because a false "saved" is the expensive direction:
            // the skip-if-unchanged hash is written on the strength of this answer, so an
            // editor version that clears the dirty flag on a write it did not perform would
            // make every later sync skip the file and never repair it. A file that was
            // genuinely just written cannot be read-only.
            if (EditorUtility.IsDirty(asset) || IsReadOnlyOnDisk(assetPath))
            {
                report.RecordAssetFailure(assetPath,
                    "Unity could not write the asset. It is almost certainly read-only or " +
                    "checked in under version control — check it out (or clear its read-only " +
                    "attribute) and sync again");
                return false;
            }

            report.RecordAssetWritten();
            return true;
        }

        /// <summary>
        /// True when the path names a file that exists and carries the read-only attribute.
        /// An unreadable or missing path answers false: this is a corroborating check, and
        /// it must never be the thing that invents a failure.
        /// </summary>
        private static bool IsReadOnlyOnDisk(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && File.Exists(path) &&
                       (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ================================================================
        // Version control
        // ================================================================

        /// <summary>
        /// True when Unity has a version control provider (Perforce, Plastic, or a custom
        /// plugin) configured and connected for this project.
        /// </summary>
        private static bool HasActiveVersionControl()
        {
            return UnityEditor.VersionControl.Provider.enabled &&
                   UnityEditor.VersionControl.Provider.isActive;
        }

        /// <summary>
        /// Makes an existing media destination writable before a raw File.Copy.
        ///
        /// Unity auto-checks-out anything written through the AssetDatabase, but the media
        /// files this importer copies never go through it, so the checkout has to be asked
        /// for explicitly. With a provider configured, MakeEditable is Unity's own checkout
        /// path and honours whatever plugin the project uses.
        ///
        /// With no provider configured and a read-only destination, this deliberately does
        /// NOT clear the attribute. A read-only file in a Unity project is nearly always
        /// owned by a version control system Unity is not driving — Perforce and Plastic mark
        /// unopened files read-only — and force-clearing it writes over a file the VCS
        /// believes is untouched, so the user loses the change on their next sync or revert
        /// with nothing to show they ever had it. Refusing this one file with an actionable
        /// message keeps the user in control, lets the rest of the import finish, and retries
        /// automatically on the next sync.
        /// </summary>
        private static bool TryMakeMediaWritable(string destPath, StoryFlowImportReport report)
        {
            if (HasActiveVersionControl())
            {
                if (AssetDatabase.MakeEditable(destPath))
                    return true;

                report.RecordMediaFailure(destPath,
                    "version control refused to check the file out. Check it out manually " +
                    "(or resolve the lock held on it) and sync again");
                return false;
            }

            try
            {
                if ((File.GetAttributes(destPath) & FileAttributes.ReadOnly) == 0)
                    return true;
            }
            catch (Exception ex)
            {
                report.RecordMediaFailure(destPath, DescribeWriteFailure(ex));
                return false;
            }

            report.RecordMediaFailure(destPath,
                "the file is read-only. StoryFlow will not clear the attribute for you: a " +
                "read-only file is usually owned by a version control system such as Perforce " +
                "or Plastic, and overwriting it loses your change on the next sync. Check the " +
                "file out, or clear its read-only attribute, then sync again");
            return false;
        }

        /// <summary>
        /// Best-effort "mark for add" for media files this import created. Without it a new
        /// image or clip sits unversioned next to versioned siblings and never reaches the
        /// rest of the team. A failure here is a warning, never an import failure — the file
        /// is on disk and the project works either way.
        /// </summary>
        private static void AddToVersionControl(string assetPath)
        {
            if (!HasActiveVersionControl()) return;

            try
            {
                var task = UnityEditor.VersionControl.Provider.Add(
                    new UnityEditor.VersionControl.Asset(assetPath), false);
                task.Wait();
                if (!task.success)
                {
                    Debug.LogWarning($"[StoryFlow] Could not mark \"{assetPath}\" for add in version " +
                                     "control. Add it manually so it reaches the rest of the team.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StoryFlow] Could not mark \"{assetPath}\" for add in version " +
                                 $"control: {ex.Message}. Add it manually so it reaches the rest " +
                                 "of the team.");
            }
        }

        /// <summary>
        /// Copies one media file into the project. Returns true when the destination ends the
        /// call holding the source's bytes, false when it could not be written — in which
        /// case the failure is recorded by name and the caller carries on with the next file.
        /// </summary>
        private static bool TryCopyMedia(
            string sourcePath, string destPath, StoryFlowImportReport report)
        {
            // Already settled earlier in this same run. One media file is routinely pulled in
            // by several scripts and again by the project pool, and the destination has held
            // the right bytes since the first of them; copying it again would rewrite a file
            // this import just wrote and reimport it a second time. Checked before the content
            // comparison because it answers the same question without touching the disk, and
            // it holds under force too: this is per-run bookkeeping, not staleness.
            if (SettledMediaDestinations.Contains(destPath))
                return true;

            // Skipping is the common case on every sync after the first: unchanged bytes mean
            // no rewrite, no ForceUpdate reimport, no texture recompression, no version
            // control checkout and no diff on a file nobody touched.
            if (!ForceRewrite && MediaContentMatches(sourcePath, destPath))
            {
                report.RecordMediaUpToDate(destPath);
                SettledMediaDestinations.Add(destPath);
                // Returns true through the normal path, not the failure path's early return:
                // the caller's texture-importer settings pass has to keep running so a
                // hand-edited import setting is still corrected on a file that was skipped.
                return true;
            }

            bool isNewFile = !File.Exists(destPath);
            if (!isNewFile && !TryMakeMediaWritable(destPath, report))
                return false;

            try
            {
                File.Copy(sourcePath, destPath, overwrite: true);
                // The destination now holds the source's bytes; record that rather than
                // re-reading a file we just wrote.
                MediaContentHashes[destPath] = CachedFileHash(sourcePath);
                // Inside the try on purpose: this was the last raw call left in the media
                // path, and an importer that throws here would unwind the whole run for one
                // file, which is the failure mode the report exists to prevent.
                AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);
            }
            catch (Exception ex)
            {
                report.RecordMediaFailure(destPath, DescribeWriteFailure(ex));
                return false;
            }

            report.RecordMediaWritten(destPath);
            SettledMediaDestinations.Add(destPath);

            if (isNewFile)
                AddToVersionControl(destPath);

            return true;
        }

        /// <summary>
        /// Refuses an exported relative path that walks out of the folder it would be
        /// combined with, reporting it by name. Returns true when the caller must stop.
        ///
        /// Script and character JSON is written by another tool and can be hand-edited, so
        /// the paths inside it are untrusted input. A ".." segment would put a raw File.Copy
        /// outside the Unity project entirely, over files the user never asked the importer
        /// to touch and with nothing in the project pointing at the damage. The check lives
        /// here rather than in CombineAssetPath so the combiner stays a pure string helper
        /// and the refusal is reported against the file that caused it.
        /// </summary>
        private static bool RefuseEscapingMediaPath(
            string normalizedRelativePath, StoryFlowImportReport report)
        {
            if (string.IsNullOrEmpty(normalizedRelativePath)) return false;

            bool escapes = false;
            foreach (string segment in normalizedRelativePath.Split('/'))
            {
                if (string.Equals(segment, "..", StringComparison.Ordinal))
                {
                    escapes = true;
                    break;
                }
            }

            if (!escapes) return false;

            report.RecordMediaFailure(normalizedRelativePath,
                "the exported asset path escapes the output folder and was refused. " +
                "Re-export the project, or move the file inside the build directory, " +
                "then sync again",
                StoryFlowImportReport.FailureKind.InvalidPath);
            return true;
        }

        // ================================================================
        // Change detection
        // ================================================================

        private static string ComputeTextHash(string text)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }

        private static string ComputeFileHash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
            }
        }

        private static string CachedFileHash(string path)
        {
            if (MediaContentHashes.TryGetValue(path, out var cached))
                return cached;

            string hash = ComputeFileHash(path);
            MediaContentHashes[path] = hash;
            return hash;
        }

        /// <summary>
        /// True when the destination already holds exactly the source's bytes. Length is the
        /// cheap reject; the content hash is what makes skipping safe, because a same-length
        /// edit — a recoloured pixel, a retimed sample — must still be copied.
        /// </summary>
        private static bool MediaContentMatches(string sourcePath, string destPath)
        {
            if (!File.Exists(destPath)) return false;

            try
            {
                if (new FileInfo(sourcePath).Length != new FileInfo(destPath).Length)
                    return false;

                return string.Equals(
                    CachedFileHash(sourcePath), CachedFileHash(destPath), StringComparison.Ordinal);
            }
            catch (IOException)
            {
                // Unreadable destination: fall through to the copy, which reports properly.
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// The certified payload for a script asset: the condensed source JSON plus the media
        /// the asset actually ended up referencing. Hashing the JSON alone would certify the
        /// input rather than the output — an import whose media copy failed would record a
        /// hash saying "this asset is up to date with that JSON" while its resolved-asset
        /// pool was still missing the sprite, and every later sync would skip the repair.
        /// </summary>
        private static string CertifyScript(string condensedJson, StoryFlowScriptAsset asset)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(condensedJson);
            AppendResolvedAssets(sb, asset.ResolvedAssetEntries);
            return ComputeTextHash(sb.ToString());
        }

        /// <summary>
        /// One certified reference to another asset: its path, and then its GUID.
        ///
        /// The GUID is the part that matters. What gets serialized into the .asset file is a
        /// GUID, not a path, and the two can disagree — a user who deletes the imported media
        /// folder in the Project window and re-syncs gets the same files back at the same
        /// paths as brand new assets with brand new GUIDs. Certifying the path alone makes
        /// that byte-identical to "nothing changed", so the save is skipped and the asset on
        /// disk keeps referring to assets that no longer exist. The path stays in the payload
        /// because a hash nobody can explain is a hash nobody can debug.
        /// </summary>
        private static string CertifyReference(UnityEngine.Object asset, string absentToken)
        {
            if (asset == null) return absentToken;

            string path = ToAssetPath(AssetDatabase.GetAssetPath(asset));
            return path + "@" + AssetDatabase.AssetPathToGUID(path);
        }

        private static void AppendResolvedAssets(
            System.Text.StringBuilder sb, List<StoryFlowScriptAsset.ResolvedAssetEntry> entries)
        {
            sb.Append("\n#resolved");
            foreach (var entry in entries)
            {
                sb.Append('\n').Append(entry.Key).Append('=');
                sb.Append(CertifyReference(entry.Asset, "<missing>"));
            }
        }

        // StoryFlowProjectAsset declares its own ResolvedAssetEntry type, so the project pool
        // needs its own overload rather than sharing the script one.
        private static void AppendResolvedAssets(
            System.Text.StringBuilder sb, List<StoryFlowProjectAsset.ResolvedAssetEntry> entries)
        {
            sb.Append("\n#resolved");
            foreach (var entry in entries)
            {
                sb.Append('\n').Append(entry.Key).Append('=');
                sb.Append(CertifyReference(entry.Asset, "<missing>"));
            }
        }

        /// <summary>
        /// The certified payload for a character asset. The display name resolves through
        /// characters.json's string table and the portrait through its asset table, so a
        /// change in either has to invalidate the hash even though the character's own JSON
        /// object is byte-identical.
        ///
        /// A character carries a ResolvedAssetEntries pool of its own, and its absence here
        /// is deliberate rather than an oversight: the importer never puts anything in it.
        /// Character-scoped media beyond the portrait is resolved into the PROJECT pool, and
        /// is certified there. Fold this in if that ever changes.
        /// </summary>
        private static string CertifyCharacter(string condensedJson, StoryFlowCharacterAsset asset)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(condensedJson);
            sb.Append("\n#name=").Append(asset.CharacterName ?? string.Empty);
            sb.Append("\n#image=").Append(CertifyReference(asset.ResolvedImage, "<missing>"));
            return ComputeTextHash(sb.ToString());
        }

        /// <summary>
        /// The certified payload for the project asset: all three top-level JSON files plus
        /// the membership the asset actually ended up holding. A script that failed to import
        /// leaves a different membership, so the project asset is rewritten and the next sync
        /// still sees an honest picture.
        /// </summary>
        private static string CertifyProject(
            string projectJson, string globalVariablesJson, string charactersJson,
            StoryFlowProjectAsset asset)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(projectJson).Append('\n')
              .Append(globalVariablesJson).Append('\n')
              .Append(charactersJson);

            sb.Append("\n#startup=").Append(CertifyReference(asset.StartupScript, "<none>"));

            sb.Append("\n#scripts");
            foreach (var sr in asset.ScriptReferences)
            {
                sb.Append('\n').Append(sr.Path).Append('=');
                sb.Append(CertifyReference(sr.Asset, "<missing>"));
            }

            sb.Append("\n#characters");
            foreach (var cr in asset.CharacterReferences)
            {
                sb.Append('\n').Append(cr.Path).Append('=');
                sb.Append(CertifyReference(cr.Asset, "<missing>"));
            }

            AppendResolvedAssets(sb, asset.ResolvedAssetEntries);
            return ComputeTextHash(sb.ToString());
        }

        /// <summary>
        /// Writes an asset only when its certified payload changed since the last successful
        /// import, so an unchanged sync touches nothing on disk. On success the new hash sits
        /// on disk alongside the data it certifies; on failure the hash is cleared, because
        /// Unity keeps the failed object loaded and dirty with the new hash still in memory —
        /// leaving it there would make every later sync compare equal and skip the retry
        /// forever while disk stayed stale.
        /// </summary>
        private static void CommitAsset(
            UnityEngine.Object asset, string assetPath, string currentHash, string newHash,
            bool isNew, Action<string> applyHash, StoryFlowImportReport report)
        {
            // A forced run rewrites regardless: the hash certifies what the last import
            // recorded, so it cannot see an asset that was hand-edited or corrupted since.
            if (!ForceRewrite && !isNew &&
                string.Equals(currentHash, newHash, StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(currentHash))
            {
                report.RecordAssetUpToDate();
                return;
            }

            // Settled before the object is certified or marked dirty, so a refusal leaves the
            // asset clean and uncertified and no flush will write it. The in-memory object
            // does hold the new data by this point — everything above populated it — but it
            // is not dirty, so nothing carries it to disk behind the refusal. Its recorded
            // hash is left describing what is actually on disk, which is what makes the next
            // sync retry: that hash disagrees with the new source.
            if (!TryMakeAssetWritable(assetPath, report))
                return;

            applyHash(newHash);
            EditorUtility.SetDirty(asset);

            if (!TrySaveAsset(asset, assetPath, report))
                applyHash(string.Empty);
        }

        // ================================================================
        // Asset path normalization
        // ================================================================

        /// <summary>
        /// Unity's AssetDatabase understands forward-slash, Assets-relative paths only.
        /// Path.Combine inserts the platform separator between its arguments and leaves the
        /// separators already inside them alone, so on Windows it yields mixed paths like
        /// "Assets/StoryFlow\media/hero.png"; Path.GetDirectoryName then flips everything to
        /// backslash. LoadAssetAtPath returns null for both forms even when the asset is
        /// there, so the importer treats an existing asset as new and CreateAsset writes over
        /// the occupied path — churning its GUID and breaking every reference to it. Every
        /// path handed to an AssetDatabase API goes through here.
        /// </summary>
        private static string ToAssetPath(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        /// <summary>
        /// Path.Combine for AssetDatabase paths: normalizes both halves and joins them with
        /// a forward slash. Path.Combine is deliberately not used for the join — it throws
        /// the directory away entirely when the second argument is rooted, and the relative
        /// path here comes out of externally authored JSON, so a leading slash would silently
        /// turn "Assets/StoryFlow" + "/media/x.png" into a write outside the output folder.
        /// </summary>
        private static string CombineAssetPath(string directory, string relative)
        {
            string dir = ToAssetPath(directory) ?? string.Empty;
            string rel = ToAssetPath(relative) ?? string.Empty;

            rel = rel.TrimStart('/');
            if (dir.Length == 0) return rel;
            if (rel.Length == 0) return dir;

            return dir.EndsWith("/", StringComparison.Ordinal) ? dir + rel : dir + "/" + rel;
        }

        /// <summary>
        /// The parent folder of an AssetDatabase path, in AssetDatabase form.
        ///
        /// The input is converted to asset form BEFORE Path.GetDirectoryName splits it,
        /// because that method splits on the OS's separator, not on the AssetDatabase's. On
        /// macOS and Linux the OS separator is "/" and a backslash is an ordinary filename
        /// character, so "Assets/StoryFlow\media/hero.png" would split into a parent of
        /// "Assets/StoryFlow\media" — a folder name containing a backslash, which is not the
        /// folder anyone meant. Normalizing first makes the two notions of "separator" agree
        /// before the split; the result is normalized again because Windows hands back
        /// backslashes regardless of what went in.
        /// </summary>
        private static string AssetParentFolder(string assetPath)
        {
            return ToAssetPath(Path.GetDirectoryName(ToAssetPath(assetPath)));
        }

        /// <summary>
        /// Ensures a folder exists within the Unity Assets folder, creating each missing
        /// segment. Paths are normalized on the way in and on every recursion, because
        /// Path.GetDirectoryName hands back platform separators that IsValidFolder and
        /// CreateFolder do not accept.
        /// </summary>
        private static void EnsureDirectory(string path)
        {
            path = ToAssetPath(path);
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = AssetParentFolder(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureDirectory(parent);

            string folderName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
                AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
