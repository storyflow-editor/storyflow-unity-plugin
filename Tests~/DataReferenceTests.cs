using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StoryFlow.Execution.NodeHandlers;
using UnityEngine;
using System.Collections.Generic;
using StoryFlow.Data;
using StoryFlow.Execution;
using StoryFlow.Utilities;
namespace StoryFlow.Tests
{
    internal static partial class Program
    {
        private static void RunDataReferenceTests()
        {
            Run(nameof(DataReferencesAndNestedText), DataReferencesAndNestedText);
            Run(nameof(SharedNestedReferenceFixture), SharedNestedReferenceFixture);
            Run(nameof(DataReferenceGraphPlumbing), DataReferenceGraphPlumbing);
            Run(nameof(DataReferenceLocalizedSaveReset), DataReferenceLocalizedSaveReset);
            Run(nameof(DataReferenceFamilyImportAndArrayOperations), DataReferenceFamilyImportAndArrayOperations);
            Run(nameof(DataReferenceVariableScopes), DataReferenceVariableScopes);
            Run(nameof(DataReferenceArraySourceShape), DataReferenceArraySourceShape);
            Run(nameof(DataReferenceArrayMutationBoundaries), DataReferenceArrayMutationBoundaries);
            Run(nameof(DataReferenceRunScriptDeclaration), DataReferenceRunScriptDeclaration);
        }
        private static void DataReferencesAndNestedText()
        {
            AssertTrue(StoryFlowWireTypes.TryParseWireType("dataAsset", out var dataType), "Data wire type imports");
            var seed = new Dictionary<string, StoryFlowDataAssetDef>
            {
                ["base"] = new StoryFlowDataAssetDef { Id = "base", Variables = new List<StoryFlowVariable>
                {
                    RefField("hp", "HP", StoryFlowVariableType.Integer, "10"),
                    RefField("next", "Next", dataType, "other"),
                    RefField("owner", "Owner", StoryFlowVariableType.Character, "hero.sfc"),
                    RefField("title", "Title", StoryFlowVariableType.String, "{count}"),
                } },
                ["child"] = new StoryFlowDataAssetDef { Id = "child", ParentId = "base", Overrides = new Dictionary<string, StoryFlowVariant> { ["hp"] = StoryFlowVariant.Int(20) } },
                ["other"] = new StoryFlowDataAssetDef { Id = "other", Variables = new List<StoryFlowVariable> { RefField("hp2", "HP", StoryFlowVariableType.Integer, "30"), RefField("next2", "Next", dataType, "child") } }
            };
            var hero = new StoryFlowCharacterData { Name = "Hero" };
            foreach (var f in new[] { RefField("stats", "Stats", dataType, "child"), RefField("dot", "Skill.Level", StoryFlowVariableType.Integer, "17") }) { hero.VariablesList.Add(f); hero.Variables[f.Name] = f.Value; }
            var chars = new Dictionary<string, StoryFlowCharacterData> { ["hero.sfc"] = hero };
            var store = new StoryFlowDataAssetStoreRef { Seed = seed, Overlay = new Dictionary<string, Dictionary<string, StoryFlowVariant>>() };
            var (_, ctx, unused) = DaSetup(new(), new(), new());
            ctx.Initialize(ctx.CurrentScript, new Dictionary<string, StoryFlowVariable>(), chars, new HashSet<string>(), store);
            ctx.LocalVariables["db"] = RefField("db", "DB", dataType, "child");
            ctx.LocalVariables["player"] = RefField("player", "Player", StoryFlowVariableType.Character, "hero.sfc");
            AssertEqual("20 17", StoryFlowInterpolation.Interpolate("{DB.Owner.Stats.Next.Next.HP} {Player.Skill.Level}", ctx), "mixed finite hops and dotted names");
            AssertEqual("{DB} {DB.Owner} {DB.HP.More}", StoryFlowInterpolation.Interpolate("{DB} {DB.Owner} {DB.HP.More}", ctx), "references and invalid leaves stay intact");
            AssertEqual("{count}", StoryFlowInterpolation.Interpolate("{DB.Title}", ctx), "inserted text is not recursive");
            StoryFlowDataAssetStore.TrySet(seed, store.Overlay, "child", "hp", StoryFlowVariant.Int(45));
            AssertEqual("45", StoryFlowInterpolation.Interpolate("{Player.Stats.HP}", ctx), "live overlay");
            hero.Variables["Stats"].StringValue = "other";
            AssertEqual("30", StoryFlowInterpolation.Interpolate("{DB.Owner.Stats.HP}", ctx), "live reassignment");
            chars.Clear();
            AssertEqual("{Player.Name}", StoryFlowInterpolation.Interpolate("{Player.Name}", ctx), "missing character stays missing");
        }
        private static StoryFlowVariable FixtureField(JToken token)
        {
            if (!StoryFlowWireTypes.TryParseWireType((string)token["type"], out var type)) return null;
            var field = RefField((string)token["id"], (string)token["name"], type, token["value"]?.ToString() ?? "");
            field.IsArray = token.Value<bool?>("isArray") == true;
            if (field.IsArray) field.Value = StoryFlowVariant.DeserializeArrayFromJson(type, token["value"].ToString());
            return field;
        }

        private static void SharedNestedReferenceFixture()
        {
            var fixture = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "engine-contract", "nested-reference-interpolation.json")));
            var seed = new Dictionary<string, StoryFlowDataAssetDef>();
            var project = ScriptableObject.CreateInstance<StoryFlowProjectAsset>();
            var imported = new List<StoryFlowDataAssetAsset>();
            foreach (var property in ((JObject)fixture["dataAssets"]).Properties())
            {
                var asset = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>();
                StoryFlow.Editor.StoryFlowImporter.PopulateDataAsset(asset, property.Name, (JObject)property.Value);
                imported.Add(asset);
            }
            project.SetDataAssetReferences(imported);
            StoryFlowDataAssetStore.BuildSeed(project, seed);
            var characters = new Dictionary<string, StoryFlowCharacterData>();
            var bridge = new Dictionary<string, string>();
            foreach (var item in fixture["characters"])
            {
                var character = new StoryFlowCharacterData { Name = (string)item["name"] };
                foreach (var token in item["variables"])
                {
                    var field = FixtureField(token); if (field == null) continue;
                    character.VariablesList.Add(field); character.Variables[field.Name] = field.Value;
                }
                string path = (string)item["path"];
                bridge[(string)item["id"]] = path; characters[path] = character;
            }
            foreach (var test in fixture["cases"])
            {
                var (_, context, _) = DaSetup(new(), new(), new());
                var script = context.CurrentScript;
                context.Initialize(script, new(), characters, new(), new StoryFlowDataAssetStoreRef { Seed = seed, Overlay = new() }, bridge);
                foreach (var token in test["roots"] ?? fixture["roots"])
                {
                    var field = FixtureField(token); if (field != null) context.LocalVariables[field.Id] = field;
                }
                context.CurrentDialogueState.CharacterReference = (string)fixture["assignedCharacterId"];
                AssertEqual((string)test["expected"], StoryFlowInterpolation.Interpolate((string)test["text"], context), "HTML shared fixture: " + (string)test["name"]);
            }
        }

        private static void DataReferenceGraphPlumbing()
        {
            var type = StoryFlowVariableType.DataAsset;
            var nodes = new List<StoryFlowScriptAsset.SerializedNode> {
                DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")),
                DaN("R", StoryFlowNodeType.GetDataAssetRef, ("variable", "ref")),
                DaN("S", StoryFlowNodeType.SetDataAssetRef, ("variable", "ref")),
                DaN("G", StoryFlowNodeType.GetDataAssetVariable, ("variableId", "next"), ("variableType", "dataAsset")),
                DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "next"), ("variableType", "dataAsset")),
                DaN("CG", StoryFlowNodeType.GetCharacterVar, ("characterPath", "hero.sfc"), ("variableName", "Stats"), ("variableType", "dataAsset")),
                DaN("CS", StoryFlowNodeType.SetCharacterVar, ("characterPath", "hero.sfc"), ("variableName", "Stats"), ("variableType", "dataAsset")),
                DaN("A", StoryFlowNodeType.GetDataAssetArray, ("variable", "array")),
                DaN("ADD", StoryFlowNodeType.AddDataAssetArrayElement),
                DaN("EL", StoryFlowNodeType.GetDataAssetArrayElement, ("value", "0")),
                DaN("LEN", StoryFlowNodeType.DataAssetArrayLength),
                DaN("HAS", StoryFlowNodeType.DataAssetArrayContains, ("value", "asset")),
                DaN("FIND", StoryFlowNodeType.FindInDataAssetArray, ("value", "asset")),
                DaN("M", StoryFlowNodeType.GetMap, ("variable", "map")),
                DaN("MW", StoryFlowNodeType.SetMapValue, ("keyType", "string"), ("valueType", "dataAsset"), ("key", "k")),
                DaN("MG", StoryFlowNodeType.GetMapValue, ("keyType", "string"), ("valueType", "dataAsset"), ("key", "k")),
                DaN("RUN", StoryFlowNodeType.RunScript), DaN("OUT", StoryFlowNodeType.SetDataAssetRef, ("variable", "out"))
            };
            var edges = new List<StoryFlowConnection> {
                DaE("P", "S", "dataAsset-1"), DaE("R", "G", "dataAsset-asset"), DaE("R", "W", "dataAsset-asset"), DaE("P", "W", "dataAsset-2"),
                DaE("P", "CS", "dataAsset-1"), DaE("A", "ADD", "dataAsset-array-2"), DaE("P", "ADD", "dataAsset-3"),
                DaE("A", "EL", "dataAsset-array-1"), DaE("A", "LEN", "dataAsset-array-1"), DaE("A", "HAS", "dataAsset-array-1"), DaE("A", "FIND", "dataAsset-array-1"),
                DaE("M", "MW", "map-string-dataAsset-2"), DaE("P", "MW", "dataAsset-4"), DaE("M", "MG", "map-string-dataAsset-1"),
                new StoryFlowConnection { Source = "RUN", Target = "OUT", SourceHandle = "source-RUN-dataAsset-out-result", TargetHandle = "target-OUT-dataAsset-1" }
            };
            var (component, ctx, store) = DaSetup(nodes, edges, new() { RefField("next", "Next", type, "old") });
            ctx.LocalVariables["ref"] = RefField("ref", "DB", type, "old");
            ctx.LocalVariables["out"] = RefField("out", "Out", type, "old");
            ctx.LocalVariables["array"] = new() { Id = "array", Type = type, IsArray = true, Value = new() { Type = type, ArrayValue = new() } };
            ctx.LocalVariables["map"] = new() { Id = "map", Type = StoryFlowVariableType.Map, KeyType = StoryFlowVariableType.String, ValueType = type, Value = new() { Type = StoryFlowVariableType.Map, MapValue = new() } };
            var stats = RefField("stats", "Stats", type, "old");
            ctx.Characters["hero.sfc"] = new() { VariablesList = new() { stats }, Variables = new() { ["Stats"] = stats.Value } };
            DataAssetNodeHandler.HandleSetReference(component, ctx.CurrentScript.GetNode("S"));
            AssertEqual("asset", ctx.LocalVariables["ref"].Value.GetString(), "Data Set keeps stable ID and type");
            AssertEqual(type, ctx.LocalVariables["ref"].Value.Type, "Data Set retains reference type");
            DataAssetNodeHandler.HandleSetDataAssetVariable(component, ctx.CurrentScript.GetNode("W"));
            AssertEqual("asset", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("G")), "accessor accepts dynamic Data root and writes Data leaf");
            CharacterVarNodeHandler.HandleSetCharacterVar(component, ctx.CurrentScript.GetNode("CS"));
            AssertEqual("asset", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("CG")), "Character Data reference get/set");
            ArrayNodeHandler.HandleAddArrayElement(component, ctx.CurrentScript.GetNode("ADD"), type);
            AssertEqual("asset", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("EL")), "Data array add/get");
            AssertEqual(1, StoryFlowEvaluator.EvaluateIntegerFromNode(ctx, ctx.CurrentScript.GetNode("LEN")), "Data array length");
            AssertEqual(true, StoryFlowEvaluator.EvaluateBooleanFromNode(ctx, ctx.CurrentScript.GetNode("HAS")), "Data array contains");
            AssertEqual(0, StoryFlowEvaluator.EvaluateIntegerFromNode(ctx, ctx.CurrentScript.GetNode("FIND")), "Data array find");
            MapNodeHandler.HandleMapModify(component, ctx.CurrentScript.GetNode("MW"));
            AssertEqual("asset", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("MG")), "Data map write/read");
            ctx.GetNodeRuntimeState("RUN").OutputValues["result"] = RefField("", "", type, "asset").Value;
            DataAssetNodeHandler.HandleSetReference(component, ctx.CurrentScript.GetNode("OUT"));
            AssertEqual("asset", ctx.LocalVariables["out"].Value.GetString(), "RunScript Data output flows through typed setter");
            AssertEqual(type, StoryFlowEvaluator.EvaluateTyped(ctx, "S", "dataAsset-1", "dataAsset").Type, "RunScript typed parameter retains Data type");
            var saved = StoryFlowStateSerializer.Serialize(ctx.LocalVariables, ctx.Characters, new(), store.Seed, store.Overlay);
            AssertEqual("asset", (string)JObject.Parse(saved)["dataAssets"]["asset"]["next"], "Data overlay saves raw reference");
            AssertEqual("asset", StoryFlowStateSerializer.BareValueFromJson(new JValue("asset"), store.Seed["asset"].Variables[0]).GetString(), "Data overlay restores typed reference");
            StoryFlowDataAssetStore.ResetOverlay(store.Overlay);
            AssertEqual("old", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("G")), "Data overlay reset restores seed");
            ctx.LocalVariables["ref"].Value.StringValue = "missing";
            AssertEqual("", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("G")), "deleted asset reference never uses cached result");
        }

        private static void DataReferenceLocalizedSaveReset()
        {
            var project = LocalizationProject();
            project.GlobalStringEntries.Add(new() { Key = "en.title.value", Value = "Knight" });
            project.GlobalStringEntries.Add(new() { Key = "en.literal.source", Value = "title.value" });
            project.LanguageStringEntries.Add(new() { Language = "fr", Key = "title.value", Value = "Chevalier" });
            var baseAsset = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>();
            StoryFlow.Editor.StoryFlowImporter.PopulateDataAsset(baseAsset, "base", JObject.Parse("{\"variables\":[{\"id\":\"title\",\"name\":\"Title\",\"type\":\"string\",\"value\":\"title.value\"},{\"id\":\"hp\",\"name\":\"HP\",\"type\":\"integer\",\"value\":10}]}"));
            var child = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>(); child.Id = "child"; child.ParentId = "base";
            var other = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>(); other.Id = "other"; other.ParentId = "base";
            other.Overrides.Add(new() { VariableId = "hp", ValueJson = "30" });
            project.SetDataAssetReferences(new() { baseAsset, child, other });
            var hero = ScriptableObject.CreateInstance<StoryFlowCharacterAsset>(); hero.CharacterName = "hero.name"; hero.CharacterPath = "hero.sfc";
            hero.Variables.Add(RefField("stats", "Stats", StoryFlowVariableType.DataAsset, "child"));
            hero.Variables.Add(RefField("text", "Text", StoryFlowVariableType.String, "title.value"));
            project.SetCharacterReferences(new() { new() { Path = "hero.sfc", Asset = hero } });
            SetManagerProject(project);
            try
            {
                var manager = StoryFlowManager.Instance;
                var (component, ctx, _) = DaSetup(new() {
                    DaN("WRITE", StoryFlowNodeType.SetCharacterVar, ("characterPath", "hero.sfc"), ("variableName", "Text"), ("variableType", "string"), ("value", "literal.source")),
                    DaN("REF", StoryFlowNodeType.SetCharacterVar, ("characterPath", "hero.sfc"), ("variableName", "Stats"), ("variableType", "dataAsset"), ("value", "other"))
                }, new(), new());
                ctx.Initialize(ctx.CurrentScript, manager.GlobalVariables, manager.RuntimeCharacters, manager.UsedOnceOnlyOptions, manager.GetDataAssetStore()); ctx.Project = project;
                ctx.LocalVariables["p"] = RefField("p", "Player", StoryFlowVariableType.Character, "hero.sfc");
                ctx.CurrentDialogueState.CharacterReference = "hero.sfc";
                ctx.CurrentDialogueState.Character = manager.RuntimeCharacters["hero.sfc"];
                manager.SetLanguage("fr");
                AssertEqual("Chevalier Chevalier", StoryFlowInterpolation.Interpolate("{Player.Stats.Title} {Character.Text}", ctx), "current localized inherited Data and Character fields");
                manager.SetLanguage("en");
                CharacterVarNodeHandler.HandleSetCharacterVar(component, ctx.CurrentScript.GetNode("WRITE"));
                CharacterVarNodeHandler.HandleSetCharacterVar(component, ctx.CurrentScript.GetNode("REF"));
                var store = manager.GetDataAssetStore(); StoryFlowDataAssetStore.TrySet(store.Seed, store.Overlay, "other", "title", StoryFlowVariant.String("title.value"));
                var saved = manager.ExportState();
                manager.SetLanguage("fr");
                AssertEqual("title.value title.value 30", StoryFlowInterpolation.Interpolate("{Player.Stats.Title} {Character.Text} {Character.Stats.HP}", ctx), "literal writes and current references survive language change");
                manager.ResetAllState();
                AssertEqual("Chevalier Chevalier 10", StoryFlowInterpolation.Interpolate("{Player.Stats.Title} {Character.Text} {Character.Stats.HP}", ctx), "reset reads new live character and pristine Data seed");
                AssertTrue(manager.ImportState(saved), "saved Data and Character refs restore");
                AssertEqual("title.value title.value 30", StoryFlowInterpolation.Interpolate("{Player.Stats.Title} {Character.Text} {Character.Stats.HP}", ctx), "restored literal provenance and mixed paths");
                manager.RuntimeCharacters.Clear();
                AssertEqual("{Character.Name} {Player.Name}", StoryFlowInterpolation.Interpolate("{Character.Name} {Player.Name}", ctx), "assigned character does not revive stale presentation object");
            }
            finally { ClearManager(); }
        }

        private static void DataReferenceFamilyImportAndArrayOperations()
        {
            var wireNames = new[] { "getDataAssetRef", "setDataAssetRef", "getDataAssetRefArray", "setDataAssetRefArray", "getDataAssetArrayElement", "setDataAssetArrayElement", "getRandomDataAssetArrayElement", "addToDataAssetArray", "removeFromDataAssetArray", "clearDataAssetArray", "arrayLengthDataAsset", "arrayContainsDataAsset", "findInDataAssetArray", "forEachDataAssetLoop" };
            var json = new JObject();
            foreach (var name in wireNames) json[name] = new JObject { ["id"] = name, ["type"] = name, ["data"] = new JObject() };
            var imported = StoryFlow.Editor.StoryFlowImporter.ParseNodes(json);
            AssertEqual(wireNames.Length, imported.Count, "every Data node imports");
            foreach (var node in imported)
            {
                AssertTrue(node.Type != StoryFlowNodeType.Unknown, node.Id + " has a native enum");
                AssertTrue(StoryFlowNodeDispatcher.HasHandler(node.Type), node.Id + " has runtime dispatch");
            }
            var type = StoryFlowVariableType.DataAsset;
            var (component, ctx, _) = DaSetup(new() {
                DaN("A", StoryFlowNodeType.GetDataAssetArray, ("variable", "a")),
                DaN("S", StoryFlowNodeType.SetDataAssetArray, ("variable", "b")),
                DaN("SET", StoryFlowNodeType.SetDataAssetArrayElement, ("value1", "0"), ("value2", "second")),
                DaN("RANDOM", StoryFlowNodeType.GetRandomDataAssetArrayElement),
                DaN("REMOVE", StoryFlowNodeType.RemoveDataAssetArrayElement, ("value", "0")),
                DaN("CLEAR", StoryFlowNodeType.ClearDataAssetArray),
                DaN("LOOP", StoryFlowNodeType.ForEachDataAssetLoop)
            }, new() { DaE("A", "S", "dataAsset-array-1"), DaE("A", "SET", "dataAsset-array-2"), DaE("A", "RANDOM", "dataAsset-array-1"), DaE("A", "REMOVE", "dataAsset-array-2"), DaE("A", "CLEAR", "dataAsset-array-2"), DaE("A", "LOOP", "dataAsset-array-1") }, new());
            ctx.LocalVariables["a"] = new() { Id = "a", Type = type, IsArray = true, Value = StoryFlowVariant.DeserializeArrayFromJson(type, "[\"first\"]") };
            ctx.LocalVariables["b"] = new() { Id = "b", Type = type, IsArray = true, Value = StoryFlowVariant.DeserializeArrayFromJson(type, "[]") };
            StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode("S"));
            AssertEqual("first", ctx.LocalVariables["b"].Value.ArrayValue[0].GetString(), "Set Data array dispatch");
            StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode("SET"));
            AssertEqual("second", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("RANDOM")), "Set element and random Data element use raw IDs");
            ArrayNodeHandler.HandleForEachLoop(component, ctx.CurrentScript.GetNode("LOOP"), type);
            AssertEqual("second", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("LOOP")), "Data ForEach exposes typed current element");
            ctx.PopLoop();
            StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode("REMOVE"));
            AssertEqual(0, ctx.LocalVariables["a"].Value.ArrayValue.Count, "Remove Data element dispatch");
            ctx.LocalVariables["a"].Value.ArrayValue.Add(RefField("", "", type, "third").Value);
            StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode("CLEAR"));
            AssertEqual(0, ctx.LocalVariables["a"].Value.ArrayValue.Count, "Clear Data array dispatch");
        }

        private static void DataReferenceVariableScopes()
        {
            foreach (bool global in new[] { false, true })
            foreach (bool missing in new[] { false, true })
            foreach (var operation in new[] { "get", "set", "arrayGet", "arraySet", "add", "remove", "clear", "element" })
            {
                bool array = operation != "get" && operation != "set";
                var scope = global ? "true" : "false";
                var (component, ctx, _) = DaSetup(new() {
                    DaN("G", StoryFlowNodeType.GetDataAssetRef, ("variable", "same"), ("isGlobal", scope)),
                    DaN("S", StoryFlowNodeType.SetDataAssetRef, ("variable", "same"), ("isGlobal", scope), ("value", "written")),
                    DaN("A", StoryFlowNodeType.GetDataAssetArray, ("variable", "same"), ("isGlobal", scope)),
                    DaN("SA", StoryFlowNodeType.SetDataAssetArray, ("variable", "same"), ("isGlobal", scope)),
                    DaN("INPUT", StoryFlowNodeType.GetDataAssetArray, ("variable", "input")),
                    DaN("ADD", StoryFlowNodeType.AddDataAssetArrayElement, ("value", "written")),
                    DaN("REMOVE", StoryFlowNodeType.RemoveDataAssetArrayElement, ("value", "0")),
                    DaN("CLEAR", StoryFlowNodeType.ClearDataAssetArray),
                    DaN("ELEMENT", StoryFlowNodeType.SetDataAssetArrayElement, ("value1", "0"), ("value2", "written"))
                }, new() { DaE("INPUT", "SA", "dataAsset-array-1"), DaE("A", "ADD", "dataAsset-array-2"), DaE("A", "REMOVE", "dataAsset-array-2"), DaE("A", "CLEAR", "dataAsset-array-2"), DaE("A", "ELEMENT", "dataAsset-array-2") }, new());
                StoryFlowVariable Value(string value)
                {
                    var field = RefField("same", "Same", StoryFlowVariableType.DataAsset, value);
                    field.IsArray = array;
                    if (array) field.Value = StoryFlowVariant.DeserializeArrayFromJson(StoryFlowVariableType.DataAsset, new JArray(value).ToString());
                    return field;
                }
                var localValue = Value("local"); var globalValue = Value("global");
                if (!missing || global) ctx.LocalVariables["same"] = localValue;
                if (!missing || !global) ctx.GlobalVariables["same"] = globalValue;
                ctx.LocalVariables["input"] = new() { Id = "input", Type = StoryFlowVariableType.DataAsset, IsArray = true, Value = StoryFlowVariant.DeserializeArrayFromJson(StoryFlowVariableType.DataAsset, "[\"written\"]") };
                string label = operation + " global=" + global + " missing=" + missing;
                var target = global ? globalValue : localValue; var decoy = global ? localValue : globalValue;
                if (operation == "get") AssertEqual(missing ? "" : global ? "global" : "local", DataReferenceEvaluator.EvaluateFromNode(ctx, ctx.CurrentScript.GetNode("G")), label);
                else if (operation == "arrayGet")
                {
                    var result = ArrayEvaluator.EvaluateArrayFromNode(ctx, ctx.CurrentScript.GetNode("A"), StoryFlowVariableType.DataAsset);
                    AssertEqual(missing ? 0 : 1, result.Count, label + " typed count");
                    if (result.Count > 0) AssertEqual(global ? "global" : "local", result[0].GetString(), label + " typed value");
                    var untyped = ArrayEvaluator.EvaluateArray(ctx, "ADD", "dataAsset-array-2");
                    AssertEqual(missing ? 0 : 1, untyped.Count, label + " untyped count");
                    if (untyped.Count > 0) AssertEqual(global ? "global" : "local", untyped[0].GetString(), label + " untyped value");
                }
                else
                {
                    var nodeId = operation == "set" ? "S" : operation == "arraySet" ? "SA" : operation.ToUpperInvariant();
                    StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode(nodeId));
                    if (!missing)
                    {
                        if (operation == "set") AssertEqual("written", target.Value.GetString(), label + " target");
                        else if (operation == "remove" || operation == "clear") AssertEqual(0, target.Value.ArrayValue.Count, label + " target empty");
                        else if (operation == "add") AssertEqual(2, target.Value.ArrayValue.Count, label + " target appended");
                        else AssertEqual("written", target.Value.ArrayValue[0].GetString(), label + " target assigned");
                    }
                }
                AssertEqual(global ? "local" : "global", array ? decoy.Value.ArrayValue.Count == 1 ? decoy.Value.ArrayValue[0].GetString() : "changed" : decoy.Value.GetString(), label + " opposite scope untouched");
            }
        }

        private static void DataReferenceArraySourceShape()
        {
            foreach (var sourceKind in new[] { "names", "mapValues", "mapKeys", "modifier", "badDataModifier", "validMapValues", "validDataModifier" })
            foreach (bool empty in new[] { false, true })
            {
                bool valid = sourceKind.StartsWith("valid", StringComparison.Ordinal);
                var sourceType = sourceKind == "names" ? StoryFlowNodeType.GetDataAssetVariableNames : (sourceKind == "mapValues" || sourceKind == "validMapValues") ? StoryFlowNodeType.MapValues : sourceKind == "mapKeys" ? StoryFlowNodeType.MapKeys : sourceKind == "modifier" ? StoryFlowNodeType.AddStringArrayElement : StoryFlowNodeType.AddDataAssetArrayElement;
                // A correctly declared empty Data modifier is valid; its nonempty corrupt members are not.
                if (sourceKind == "badDataModifier" && empty) continue;
                var (component, ctx, store) = DaSetup(new() {
                    DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")),
                    DaN("PN", StoryFlowNodeType.GetDataAsset, ("assetId", "names")),
                    DaN("M", StoryFlowNodeType.GetMap, ("variable", "map")),
                    DaN("SOURCE", sourceType, ("keyType", "string"), ("valueType", valid ? "dataAsset" : "string")),
                    DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "dest"), ("variableType", "dataAsset"), ("isArray", "true")),
                    DaN("S", StoryFlowNodeType.SetDataAssetArray, ("variable", "dest"))
                }, new() { DaE("PN", "SOURCE", "dataAsset-asset"), DaE("M", "SOURCE", valid ? "map-string-dataAsset-1" : "map-string-string-1"), DaE("P", "W", "dataAsset-asset"), DaE("SOURCE", "W", "dataAsset-array-2"), DaE("SOURCE", "S", "dataAsset-array-1") }, new() {
                    new() { Id = "dest", Name = "Dest", Type = StoryFlowVariableType.DataAsset, IsArray = true, Value = StoryFlowVariant.DeserializeArrayFromJson(StoryFlowVariableType.DataAsset, "[\"keep\"]") }
                });
                store.Seed["names"] = new() { Id = "names", Variables = empty ? new() : new(store.Seed["asset"].Variables) };
                ctx.LocalVariables["dest"] = new(store.Seed["asset"].Variables[0]);
                var entries = new List<StoryFlowMapEntry>();
                if (!empty) entries.Add(new() { Key = StoryFlowVariant.String("key"), Value = valid ? RefField("", "", StoryFlowVariableType.DataAsset, "good").Value : StoryFlowVariant.String("wrong") });
                ctx.LocalVariables["map"] = new() { Id = "map", Type = StoryFlowVariableType.Map, KeyType = StoryFlowVariableType.String, ValueType = valid ? StoryFlowVariableType.DataAsset : StoryFlowVariableType.String, Value = new() { Type = StoryFlowVariableType.Map, MapValue = entries } };
                ctx.GetNodeRuntimeState("SOURCE").CachedOutput = new() { Type = (valid || sourceKind == "badDataModifier") ? StoryFlowVariableType.DataAsset : StoryFlowVariableType.String, ArrayValue = empty ? new() : new() { valid ? RefField("", "", StoryFlowVariableType.DataAsset, "good").Value : StoryFlowVariant.String("wrong") } };
                ctx.GetNodeRuntimeState("SOURCE").HasExecutionOutput = true;
                DataAssetNodeHandler.HandleSetDataAssetVariable(component, ctx.CurrentScript.GetNode("W"));
                StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode("S"));
                AssertEqual(valid ? 1 : 0, store.Overlay.Count, sourceKind + " empty=" + empty + " refuses asset array write");
                AssertEqual(valid ? empty ? "changed" : "good" : "keep", ctx.LocalVariables["dest"].Value.ArrayValue.Count == 1 ? ctx.LocalVariables["dest"].Value.ArrayValue[0].GetString() : "changed", sourceKind + " empty=" + empty + " refuses ordinary Data array write");
            }
        }

        private static void DataReferenceArrayMutationBoundaries()
        {
            foreach (var sourceKind in new[] { "script", "character", "asset" })
            foreach (var operation in new[] { "directClear", "chainClear", "chainAdd", "chainRemove", "chainSet" })
            {
                var type = StoryFlowVariableType.DataAsset;
                var sourceType = sourceKind == "script" ? StoryFlowNodeType.GetDataAssetArray : sourceKind == "character" ? StoryFlowNodeType.GetCharacterVar : StoryFlowNodeType.GetDataAssetVariable;
                var secondType = operation == "chainAdd" ? StoryFlowNodeType.AddDataAssetArrayElement : operation == "chainRemove" ? StoryFlowNodeType.RemoveDataAssetArrayElement : operation == "chainSet" ? StoryFlowNodeType.SetDataAssetArrayElement : StoryFlowNodeType.ClearDataAssetArray;
                var field = new StoryFlowVariable { Id = "array", Name = "Refs", Type = type, IsArray = true, Value = StoryFlowVariant.DeserializeArrayFromJson(type, "[\"first\"]") };
                var (component, ctx, store) = DaSetup(new() {
                    DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")),
                    DaN("SOURCE", sourceType, ("variable", "array"), ("variableId", "array"), ("variableType", "dataAsset"), ("isArray", "true"), ("characterPath", "hero.sfc"), ("variableName", "Refs")),
                    DaN("ADD", StoryFlowNodeType.AddDataAssetArrayElement, ("value", "appended")),
                    DaN("SECOND", secondType, ("value", "0"), ("value1", "0"), ("value2", "replacement"))
                }, new() { DaE("P", "SOURCE", "dataAsset-asset"), DaE("SOURCE", "ADD", "dataAsset-array-2"), DaE(operation == "directClear" ? "SOURCE" : "ADD", "SECOND", "dataAsset-array-2") }, new() { new(field) });
                ctx.LocalVariables["array"] = new(field);
                ctx.Characters["hero.sfc"] = new() { VariablesList = new() { new(field) } };
                ctx.Characters["hero.sfc"].Variables["Refs"] = ctx.Characters["hero.sfc"].VariablesList[0].Value;
                if (operation != "directClear") StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode("ADD"));
                StoryFlowNodeDispatcher.ProcessNode(component, ctx.CurrentScript.GetNode("SECOND"));
                List<StoryFlowVariant> actual;
                if (sourceKind == "character") actual = ctx.Characters["hero.sfc"].Variables["Refs"].ArrayValue;
                else if (sourceKind == "asset") { StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, null, "en", "asset", "array", out var read); actual = read.ArrayValue; }
                else actual = ctx.LocalVariables["array"].Value.ArrayValue;
                AssertEqual(operation == "directClear" ? 0 : 2, actual.Count, sourceKind + " " + operation + " affects only immediate source");
                if (operation != "directClear" && actual.Count == 2)
                    AssertEqual("first,appended", actual[0].GetString() + "," + actual[1].GetString(), sourceKind + " " + operation + " retains original source values");
            }
        }

        private static void DataReferenceRunScriptDeclaration()
        {
            foreach (bool array in new[] { false, true })
            foreach (bool stale in new[] { false, true })
            {
                var callee = ScriptableObject.CreateInstance<StoryFlowScriptAsset>(); callee.ScriptPath = "callee.sfe";
                callee.SetNodes(new() { DaN("0", StoryFlowNodeType.End) });
                callee.SetVariables(new() { new() { Id = "result", Name = "Ref", Type = stale ? StoryFlowVariableType.String : StoryFlowVariableType.DataAsset, IsInput = true, IsOutput = true, IsArray = array, DefaultValueJson = array ? "[]" : "default" } });
                var project = ScriptableObject.CreateInstance<StoryFlowProjectAsset>(); project.SetScriptReferences(new() { new() { Path = "callee.sfe", Asset = callee } });
                SetManagerProject(project);
                try
                {
                    var iface = new JObject { ["parameters"] = new JArray(new JObject { ["id"] = "p", ["name"] = "Ref", ["type"] = "dataAsset", ["isArray"] = array }), ["outputs"] = new JArray(new JObject { ["id"] = "o", ["name"] = "Ref", ["type"] = "dataAsset", ["isArray"] = array }) };
                    var prefix = array ? "dataAsset-array" : "dataAsset";
                    var (component, ctx, store) = DaSetup(new() {
                        DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")),
                        DaN("INPUT", array ? StoryFlowNodeType.GetDataAssetArray : StoryFlowNodeType.GetDataAsset, ("assetId", "incoming"), ("variable", "input")),
                        DaN("CALL", StoryFlowNodeType.RunScript, ("script", "callee.sfe"), ("scriptInterface", iface.ToString())),
                        DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "dest"), ("variableType", "dataAsset"), ("isArray", array ? "true" : "false"))
                    }, new() { DaE("INPUT", "CALL", prefix + "-param-p"), DaE("P", "W", "dataAsset-asset"), new() { Source = "CALL", Target = "W", SourceHandle = "source-CALL-" + prefix + "-out-o", TargetHandle = "target-W-" + prefix + "-2" } }, new() { new() { Id = "dest", Name = "Dest", Type = StoryFlowVariableType.DataAsset, IsArray = array, Value = array ? StoryFlowVariant.DeserializeArrayFromJson(StoryFlowVariableType.DataAsset, "[\"keep\"]") : RefField("", "", StoryFlowVariableType.DataAsset, "keep").Value } });
                    ctx.LocalVariables["input"] = new() { Id = "input", Type = StoryFlowVariableType.DataAsset, IsArray = true, Value = StoryFlowVariant.DeserializeArrayFromJson(StoryFlowVariableType.DataAsset, "[\"incoming\"]") };
                    var caller = ctx.CurrentScript;
                    ControlFlowNodeHandler.HandleRunScript(component, caller.GetNode("CALL"));
                    AssertEqual(stale ? StoryFlowVariableType.String : StoryFlowVariableType.DataAsset, ctx.LocalVariables["result"].Value.Type, "callee declaration guards Data input array=" + array + " stale=" + stale);
                    ControlFlowNodeHandler.HandleEnd(component, callee.GetNode("0"));
                    DataAssetNodeHandler.HandleSetDataAssetVariable(component, caller.GetNode("W"));
                    AssertEqual(stale ? 0 : 1, store.Overlay.Count, "actual RunScript output declaration guards Data setter array=" + array + " stale=" + stale);
                }
                finally { ClearManager(); }
            }
        }

        private static StoryFlowVariable RefField(string id, string name, StoryFlowVariableType type, string value) => new StoryFlowVariable { Id = id, Name = name, Type = type, Value = StoryFlowVariant.DeserializeFromJson(type, value) };
    }
}
