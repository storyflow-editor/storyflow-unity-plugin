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
        };

        // Supported image file extensions
        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".psd", ".gif" };

        // Supported audio file extensions
        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".wav", ".mp3", ".ogg", ".aif", ".aiff" };

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

            // --- Ensure output directories ---
            EnsureDirectory(outputPath);
            string scriptsDir = Path.Combine(outputPath, "Scripts");
            string charactersDir = Path.Combine(outputPath, "Characters");
            string mediaImagesDir = Path.Combine(outputPath, "Media", "Images");
            string mediaAudioDir = Path.Combine(outputPath, "Media", "Audio");
            EnsureDirectory(scriptsDir);
            EnsureDirectory(charactersDir);
            EnsureDirectory(mediaImagesDir);
            EnsureDirectory(mediaAudioDir);

            // --- Read global variables ---
            var globalVariableEntries = new List<StoryFlowProjectAsset.GlobalVariableEntry>();
            var globalStringEntries = new List<StoryFlowProjectAsset.GlobalStringEntry>();

            string globalVarsPath = Path.Combine(buildDirectory, "global-variables.json");
            if (File.Exists(globalVarsPath))
            {
                JObject globalVarsJson = JObject.Parse(File.ReadAllText(globalVarsPath));
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
            }

            // --- Read characters.json ---
            var characterReferences = new List<StoryFlowProjectAsset.CharacterReference>();
            string charactersJsonPath = Path.Combine(buildDirectory, "characters.json");
            if (File.Exists(charactersJsonPath))
            {
                JObject charactersJson = JObject.Parse(File.ReadAllText(charactersJsonPath));
                JObject charsObj = charactersJson.Value<JObject>("characters");
                JObject charStrings = charactersJson.Value<JObject>("strings");
                JObject charAssets = charactersJson.Value<JObject>("assets");

                // Build character string lookup
                var charStringLookup = new Dictionary<string, string>();
                if (charStrings != null)
                {
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

                // Build character asset lookup
                var charAssetLookup = new Dictionary<string, string>(); // assetKey → relative path
                if (charAssets != null)
                {
                    foreach (var assetProp in charAssets.Properties())
                    {
                        var assetObj = assetProp.Value as JObject;
                        if (assetObj == null) continue;
                        string assetPath = assetObj.Value<string>("path") ?? "";
                        if (!string.IsNullOrEmpty(assetPath))
                            charAssetLookup[assetProp.Name] = assetPath;
                    }
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
                            buildDirectory, charactersDir, mediaImagesDir);

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
                    scriptsDir, mediaImagesDir, mediaAudioDir);

                scriptReferences.Add(new StoryFlowProjectAsset.ScriptReference
                {
                    Path = scriptKey,
                    Asset = scriptAsset
                });
            }

            // --- Create / update project asset ---
            string projectAssetPath = Path.Combine(outputPath, "SF_Project.asset");
            var projectAsset = AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>(projectAssetPath);
            if (projectAsset == null)
            {
                projectAsset = ScriptableObject.CreateInstance<StoryFlowProjectAsset>();
                AssetDatabase.CreateAsset(projectAsset, projectAssetPath);
            }

            projectAsset.Version = version;
            projectAsset.ApiVersion = apiVersion;
            projectAsset.Title = title;
            projectAsset.Description = description;
            projectAsset.StartupScriptPath = startupScript;
            projectAsset.ScriptReferences = scriptReferences;
            projectAsset.CharacterReferences = characterReferences;
            projectAsset.GlobalVariableEntries = globalVariableEntries;
            projectAsset.GlobalStringEntries = globalStringEntries;

            EditorUtility.SetDirty(projectAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[StoryFlow] Imported project '{title}': {scriptReferences.Count} scripts, " +
                      $"{characterReferences.Count} characters, {globalVariableEntries.Count} global variables.");

            return projectAsset;
        }

        // ================================================================
        // Script Import
        // ================================================================

        private static StoryFlowScriptAsset ImportScript(
            string scriptPath, JObject scriptJson, string buildDirectory,
            string scriptsDir, string imagesDir, string audioDir)
        {
            // Create safe file name from script path
            string safeFileName = scriptPath.Replace("/", "_").Replace("\\", "_").Replace(".json", "");
            string assetPath = Path.Combine(scriptsDir, $"SF_{safeFileName}.asset");

            var scriptAsset = AssetDatabase.LoadAssetAtPath<StoryFlowScriptAsset>(assetPath);
            if (scriptAsset == null)
            {
                scriptAsset = ScriptableObject.CreateInstance<StoryFlowScriptAsset>();
                AssetDatabase.CreateAsset(scriptAsset, assetPath);
            }

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
                ImportMediaAssets(parsedAssets, buildDirectory, imagesDir, audioDir, scriptAsset.SetResolvedAsset);
            }

            // Parse flows
            JArray flowsArr = scriptJson.Value<JArray>("flows");
            if (flowsArr != null)
                scriptAsset.SetFlows(ParseFlows(flowsArr));

            EditorUtility.SetDirty(scriptAsset);
            return scriptAsset;
        }

        // ================================================================
        // Character Import
        // ================================================================

        private static StoryFlowCharacterAsset ImportCharacter(
            string normalizedPath, JObject charObj,
            Dictionary<string, string> stringLookup,
            Dictionary<string, string> assetLookup,
            string buildDirectory, string charactersDir, string imagesDir)
        {
            string safeFileName = normalizedPath.Replace("\\", "_").Replace("/", "_").Replace(".sfc", "");
            string assetPath = Path.Combine(charactersDir, $"SF_Char_{safeFileName}.asset");

            var charAsset = AssetDatabase.LoadAssetAtPath<StoryFlowCharacterAsset>(assetPath);
            if (charAsset == null)
            {
                charAsset = ScriptableObject.CreateInstance<StoryFlowCharacterAsset>();
                AssetDatabase.CreateAsset(charAsset, assetPath);
            }

            charAsset.CharacterPath = normalizedPath;

            // Resolve name from string table
            string nameKey = charObj.Value<string>("name") ?? "";
            charAsset.CharacterName = stringLookup.TryGetValue(nameKey, out var resolvedName) ? resolvedName : nameKey;

            // Resolve image
            string imageAssetKey = charObj.Value<string>("image") ?? "";
            charAsset.ImageAssetKey = imageAssetKey;
            if (!string.IsNullOrEmpty(imageAssetKey) && assetLookup.TryGetValue(imageAssetKey, out var imagePath))
            {
                var sprite = ImportImageAsset(buildDirectory, imagePath, imagesDir);
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
                    var defaultValue = ParseDefaultValue(varType, varObj["value"], isArray);

                    variables.Add(new StoryFlowVariable
                    {
                        Id = varProp.Name,
                        Name = varName,
                        Type = varType,
                        Value = defaultValue,
                        IsArray = isArray
                    });
                }
            }
            charAsset.Variables = variables;

            EditorUtility.SetDirty(charAsset);
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
                    Type = ParseNodeType(nodeTypeStr)
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

                    serializedNode.Data.Add(new StoryFlowScriptAsset.SerializedKV
                    {
                        Key = key,
                        Value = valueStr
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
                    IsOutput = isOutput
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
            Action<string, UnityEngine.Object> setResolvedAsset)
        {
            foreach (var asset in assets)
            {
                if (string.IsNullOrEmpty(asset.Path)) continue;

                string ext = Path.GetExtension(asset.Path).ToLowerInvariant();

                if (asset.Type == "image" || ImageExtensions.Contains(ext))
                {
                    var sprite = ImportImageAsset(buildDirectory, asset.Path, imagesDir);
                    if (sprite != null)
                        setResolvedAsset(asset.Id, sprite);
                }
                else if (asset.Type == "audio" || AudioExtensions.Contains(ext))
                {
                    var clip = ImportAudioAsset(buildDirectory, asset.Path, audioDir);
                    if (clip != null)
                        setResolvedAsset(asset.Id, clip);
                }
            }
        }

        /// <summary>
        /// Imports an image from the build directory into the Unity project as a Sprite.
        /// Returns the imported Sprite, or null on failure.
        /// </summary>
        private static Sprite ImportImageAsset(string buildDirectory, string relativePath, string imagesDir)
        {
            string sourcePath = Path.Combine(buildDirectory, relativePath);
            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning($"[StoryFlow] Image not found: {sourcePath}");
                return null;
            }

            string fileName = Path.GetFileName(relativePath);
            string destPath = Path.Combine(imagesDir, fileName);

            // Copy file to project
            File.Copy(sourcePath, destPath, overwrite: true);
            AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);

            // Configure as sprite
            var importer = AssetImporter.GetAtPath(destPath) as TextureImporter;
            if (importer != null)
            {
                bool needsReimport = false;
                if (importer.textureType != TextureImporterType.Sprite)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
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
        private static AudioClip ImportAudioAsset(string buildDirectory, string relativePath, string audioDir)
        {
            string sourcePath = Path.Combine(buildDirectory, relativePath);
            if (!File.Exists(sourcePath))
            {
                Debug.LogWarning($"[StoryFlow] Audio not found: {sourcePath}");
                return null;
            }

            string fileName = Path.GetFileName(relativePath);
            string ext = Path.GetExtension(fileName).ToLowerInvariant();

            // Unity handles MP3 natively (unlike Unreal), but warn about potential quality issues
            if (ext == ".mp3")
            {
                Debug.Log($"[StoryFlow] MP3 audio detected: {fileName}. Unity supports MP3 natively, " +
                          "but WAV is recommended for best quality and compatibility.");
            }

            string destPath = Path.Combine(audioDir, fileName);

            File.Copy(sourcePath, destPath, overwrite: true);
            AssetDatabase.ImportAsset(destPath, ImportAssetOptions.ForceUpdate);

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
                EnumValues = enumValues
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
        /// Serializes a variable's default value to a string for storage.
        /// </summary>
        private static string SerializeDefaultValue(StoryFlowVariableType type, JToken valueToken, bool isArray)
        {
            if (valueToken == null || valueToken.Type == JTokenType.Null)
                return "";

            if (isArray)
            {
                // Arrays are stored as their JSON representation
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

        /// <summary>
        /// Ensures a directory exists within the Unity Assets folder. Creates it if needed.
        /// </summary>
        private static void EnsureDirectory(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                // Walk up and create each missing segment
                string parent = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                {
                    EnsureDirectory(parent);
                }

                string folderName = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(folderName))
                {
                    AssetDatabase.CreateFolder(parent, folderName);
                }
            }
        }
    }
}
