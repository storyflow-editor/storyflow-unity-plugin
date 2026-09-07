using System;
using System.Collections.Generic;
using System.Reflection;
using StoryFlow.Data;
using StoryFlow.Execution;
using StoryFlow.Execution.NodeHandlers;
using UnityEngine;
namespace StoryFlow.Tests
{
    internal static partial class Program
    {
        private static void RunDataAssetHardeningTests()
        {
            Run(nameof(DataAssetMapWrite), DataAssetMapWrite);
            Run(nameof(DataAssetMediaMapHostValuesCopy), DataAssetMediaMapHostValuesCopy);
            Run(nameof(DataAssetDetachedMapStringIsLiteral), DataAssetDetachedMapStringIsLiteral);
            Run(nameof(DataAssetCharacterEnumSources), DataAssetCharacterEnumSources);
            Run(nameof(DataAssetArrayOutput), DataAssetArrayOutput);
            Run(nameof(DataAssetMissingScalar), DataAssetMissingScalar);
            Run(nameof(DataAssetMapShape), DataAssetMapShape);
            Run(nameof(DataAssetEmptyArrayShape), DataAssetEmptyArrayShape);
            Run(nameof(DataAssetDetachedMapResult), DataAssetDetachedMapResult);
            Run(nameof(DataAssetSharedCache), DataAssetSharedCache);
            Run(nameof(DataAssetCharacterBridgeCache), DataAssetCharacterBridgeCache);
            Run(nameof(DataAssetSavedShape), DataAssetSavedShape);
            Run(nameof(DataAssetRestoreIgnoresCulture), DataAssetRestoreIgnoresCulture);
            Run(nameof(DataAssetSavedDateShapedText), DataAssetSavedDateShapedText);
            Run(nameof(DataAssetChainedSources), DataAssetChainedSources);
            Run(nameof(DataAssetMigrationFixture), () => DataAssetMigrationFixture());
        }

        static StoryFlowScriptAsset.SerializedNode DaN(string id, StoryFlowNodeType type, params (string, string)[] data)
        {
            var n = new StoryFlowScriptAsset.SerializedNode { Id = id, Type = type }; foreach (var (k, v) in data) n.Data.Add(new() { Key = k, Value = v }); return n;
        }
        static StoryFlowConnection DaE(string src, string dst, string pin) => new() { Source = src, Target = dst, SourceHandle = "source-" + src + "-", TargetHandle = "target-" + dst + "-" + pin };
        static (StoryFlowComponent, StoryFlowExecutionContext, StoryFlowDataAssetStoreRef) DaSetup(List<StoryFlowScriptAsset.SerializedNode> nodes, List<StoryFlowConnection> edges, List<StoryFlowVariable> decls)
        {
            var script = ScriptableObject.CreateInstance<StoryFlowScriptAsset>(); script.SetNodes(nodes); script.SetConnections(edges);
            var store = new StoryFlowDataAssetStoreRef { Seed = new() { ["asset"] = new() { Id = "asset", Variables = decls } }, Overlay = new() };
            var ctx = new StoryFlowExecutionContext(); ctx.Initialize(script, new(), new(), new(), store);
            var component = new StoryFlowComponent(); typeof(StoryFlowComponent).GetField("_context", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(component, ctx);
            return (component, ctx, store);
        }
        static void DataAssetMapWrite()
        {
            var (c, x, s) = DaSetup(new() { DaN("M", StoryFlowNodeType.GetMap, ("variable", "map")), DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")), DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "dest"), ("variableType", "map"), ("keyType", "string"), ("valueType", "string")) }, new() { DaE("M", "W", "map-string-string-2"), DaE("P", "W", "dataAsset-asset") }, new() { new() { Id = "dest", Name = "Dest", Type = StoryFlowVariableType.Map, KeyType = StoryFlowVariableType.String, ValueType = StoryFlowVariableType.String, Value = new() { Type = StoryFlowVariableType.Map, MapValue = new() } } });
            x.LocalVariables["map"] = new() { Id = "map", Type = StoryFlowVariableType.Map, KeyType = StoryFlowVariableType.String, ValueType = StoryFlowVariableType.String, Value = new() { Type = StoryFlowVariableType.Map, MapValue = new() { new() { Key = StoryFlowVariant.String("id"), Value = StoryFlowVariant.String("item.value.0") } } } };
            var p = LocalizationProject(); x.Project = p; SetManagerProject(p); StoryFlowManager.Instance.SetLanguage("fr");
            DataAssetNodeHandler.HandleSetDataAssetVariable(c, x.CurrentScript.GetNode("W"));
            StoryFlowDataAssetStore.TryRead(s.Seed, s.Overlay, p, "fr", "asset", "dest", out var v);
            AssertEqual("Épée", v.MapValue[0].Value.GetString(), "authored map -> DA must write displayed text"); ClearManager();
        }
        static void DataAssetArrayOutput()
        {
            var (c, x, s) = DaSetup(new() { DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")), DaN("G", StoryFlowNodeType.GetDataAssetVariable, ("variableId", "arr"), ("variableType", "string"), ("isArray", "true")), DaN("A", StoryFlowNodeType.AddStringArrayElement, ("value", "second")), DaN("I", StoryFlowNodeType.GetInt, ("variable", "num")), DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "count"), ("variableType", "integer")), DaN("OUT", StoryFlowNodeType.GetStringArrayElement, ("value", "1")) }, new() { DaE("P", "G", "dataAsset-asset"), DaE("G", "A", "string-array-2"), DaE("P", "W", "dataAsset-asset"), DaE("I", "W", "integer-2"), DaE("A", "OUT", "string-array-1") }, new() { new() { Id = "arr", Name = "Arr", Type = StoryFlowVariableType.String, IsArray = true, Value = new() { Type = StoryFlowVariableType.String, ArrayValue = new() { StoryFlowVariant.String("first") } } }, new() { Id = "count", Name = "Count", Type = StoryFlowVariableType.Integer, Value = StoryFlowVariant.Int(7) } });
            x.LocalVariables["num"] = new() { Id = "num", Type = StoryFlowVariableType.Integer, Value = StoryFlowVariant.Int(3) };
            ArrayNodeHandler.HandleAddArrayElement(c, x.CurrentScript.GetNode("A"), StoryFlowVariableType.String);
            AssertEqual(2, x.GetNodeRuntimeState("A").CachedOutput.ArrayValue.Count, "array result is available before unrelated write");
            DataAssetNodeHandler.HandleSetDataAssetVariable(c, x.CurrentScript.GetNode("W"));
            AssertEqual("second", StoryFlowEvaluator.EvaluateStringFromNode(x, x.CurrentScript.GetNode("OUT")), "array result survives unrelated DA write");
        }
        static void DataAssetSharedCache()
        {
            var (c, x, s) = DaSetup(new() { DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")), DaN("G", StoryFlowNodeType.GetDataAssetVariable, ("variableId", "count"), ("variableType", "integer")), DaN("PLUS", StoryFlowNodeType.PlusInt, ("value2", "1")) }, new() { DaE("P", "G", "dataAsset-asset"), DaE("G", "PLUS", "integer-1") }, new() { new() { Id = "count", Name = "Count", Type = StoryFlowVariableType.Integer, Value = StoryFlowVariant.Int(7) } });
            var other = new StoryFlowExecutionContext(); other.Initialize(x.CurrentScript, new(), new(), new(), s);
            AssertEqual(8, StoryFlowEvaluator.EvaluateIntegerFromNode(x, x.CurrentScript.GetNode("PLUS")), "first context caches derived value");
            AssertEqual(8, StoryFlowEvaluator.EvaluateIntegerFromNode(other, other.CurrentScript.GetNode("PLUS")), "second context caches derived value");
            var asset = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>(); asset.Id = "asset";
            AssertTrue(StoryFlowDataAssetAccess.SetInt(s, null, null, asset, "Count", 20), "host writes shared store");
            AssertEqual(21, StoryFlowEvaluator.EvaluateIntegerFromNode(x, x.CurrentScript.GetNode("PLUS")), "first context sees shared write");
            AssertEqual(21, StoryFlowEvaluator.EvaluateIntegerFromNode(other, other.CurrentScript.GetNode("PLUS")), "second context sees shared write");
            StoryFlowDataAssetStore.ResetOverlay(s.Overlay);
            AssertEqual(8, StoryFlowEvaluator.EvaluateIntegerFromNode(other, other.CurrentScript.GetNode("PLUS")), "reset invalidates shared derived value");
        }
        static void DataAssetSavedShape()
        {
            var declaration = new StoryFlowVariable { Type = StoryFlowVariableType.Integer, IsArray = true };
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(Newtonsoft.Json.Linq.JArray.Parse("[1,\"old\"]"), declaration) == null, "incompatible saved array is dropped whole");
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(Newtonsoft.Json.Linq.JArray.Parse("[1,2]"), declaration) != null, "compatible saved array retained");
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(new Newtonsoft.Json.Linq.JArray(), declaration) != null, "empty saved array retained");
            declaration.IsArray = false;
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(new Newtonsoft.Json.Linq.JValue(1.5), declaration) == null, "fractional integer save dropped");
            declaration.Type = StoryFlowVariableType.Enum; declaration.EnumValues = new() { "Ready" };
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(new Newtonsoft.Json.Linq.JValue("Old"), declaration) == null, "removed enum member dropped");
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(new Newtonsoft.Json.Linq.JValue((long)int.MaxValue + 1), new StoryFlowVariable { Type = StoryFlowVariableType.Integer }) == null, "out-of-range saved integer dropped");
            declaration.Type = StoryFlowVariableType.Float;
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(new Newtonsoft.Json.Linq.JValue(double.MaxValue), declaration) == null, "out-of-range saved float dropped");
            AssertTrue(StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(new Newtonsoft.Json.Linq.JValue(double.PositiveInfinity), declaration) == null, "nonfinite saved float dropped");
        }
        static void DataAssetMissingScalar()
        {
            var (c, x, s) = DaSetup(new() { DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")), DaN("G", StoryFlowNodeType.GetInt, ("variable", "removed")), DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "count"), ("variableType", "integer")) }, new() { DaE("P", "W", "dataAsset-asset"), DaE("G", "W", "integer-2") }, new() { new() { Id = "count", Name = "Count", Type = StoryFlowVariableType.Integer, Value = StoryFlowVariant.Int(7) } });
            DataAssetNodeHandler.HandleSetDataAssetVariable(c, x.CurrentScript.GetNode("W"));
            StoryFlowDataAssetStore.TryRead(s.Seed, s.Overlay, null, "en", "asset", "count", out var v);
            AssertEqual(7, v.GetInt(), "unresolvable scalar must refuse instead of overwriting default");
        }
        static void DataAssetChainedSources()
        {
            foreach (var scenario in new[] { "removed", "dead", "wrongtype", "missingAssetVariable", "missingCharacter", "fallback", "zero" })
            {
                var nodes = new List<StoryFlowScriptAsset.SerializedNode> { DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")), DaN("PLUS", StoryFlowNodeType.PlusInt, ("value1", "0"), ("value2", "0")), DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "count"), ("variableType", "integer")) };
                var edges = new List<StoryFlowConnection> { DaE("P", "W", "dataAsset-asset"), DaE("PLUS", "W", "integer-2") };
                if (scenario != "fallback")
                {
                    if (scenario == "missingAssetVariable") { nodes.Add(DaN("G", StoryFlowNodeType.GetDataAssetVariable, ("variableId", "removed"), ("variableType", "integer"))); edges.Add(DaE("P", "G", "dataAsset-asset")); }
                    else if (scenario == "missingCharacter") nodes.Add(DaN("G", StoryFlowNodeType.GetCharacterVar, ("characterPath", "missing.sfc"), ("variableName", "Count"), ("variableType", "integer")));
                    else if (scenario != "dead") nodes.Add(DaN("G", StoryFlowNodeType.GetInt, ("variable", "source")));
                    edges.Add(DaE("G", "PLUS", "integer-1"));
                }
                var (c, x, store) = DaSetup(nodes, edges, new() { new() { Id = "count", Name = "Count", Type = StoryFlowVariableType.Integer, Value = StoryFlowVariant.Int(7) } });
                if (scenario == "wrongtype") x.LocalVariables["source"] = new() { Id = "source", Type = StoryFlowVariableType.String, Value = StoryFlowVariant.String("bad") };
                if (scenario == "zero") x.LocalVariables["source"] = new() { Id = "source", Type = StoryFlowVariableType.Integer, Value = StoryFlowVariant.Int(0) };
                // Previously memoized failed expressions must not conceal failure on a later Set.
                StoryFlowEvaluator.EvaluateIntegerFromNode(x, x.CurrentScript.GetNode("PLUS"));
                DataAssetNodeHandler.HandleSetDataAssetVariable(c, x.CurrentScript.GetNode("W"));
                StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, null, "en", "asset", "count", out var value);
                AssertEqual(scenario == "fallback" || scenario == "zero" ? 0 : 7, value.GetInt(), scenario + " chain preserves failed sources and accepts valid zero/fallback");
            }
        }

        static void DataAssetMigrationFixture([System.Runtime.CompilerServices.CallerFilePath] string source = "")
        {
            var fixture = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source), "Fixtures", "engine-contract", "data-assets-migration.json")));
            var project = ScriptableObject.CreateInstance<StoryFlowProjectAsset>();
            var assets = new List<StoryFlowDataAssetAsset>();
            foreach (var row in ((Newtonsoft.Json.Linq.JObject)fixture["dataAssets"]).Properties())
            {
                var asset = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>();
                StoryFlow.Editor.StoryFlowImporter.PopulateDataAsset(asset, row.Name, (Newtonsoft.Json.Linq.JObject)row.Value);
                assets.Add(asset);
            }
            project.SetDataAssetReferences(assets); SetManagerProject(project);
            try
            {
                var mgr = StoryFlowManager.Instance;
                var snapshot = StoryFlow.Utilities.StoryFlowStateSerializer.Deserialize(new Newtonsoft.Json.Linq.JObject { ["version"] = "1.3.0", ["dataAssets"] = fixture["saved"].DeepClone() }.ToString());
                typeof(StoryFlowManager).GetMethod("ApplySnapshot", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(mgr, new object[] { snapshot });
                var store = mgr.GetDataAssetStore();
                var actual = Newtonsoft.Json.Linq.JObject.Parse(StoryFlow.Utilities.StoryFlowStateSerializer.Serialize(new(), new(), new(), store.Seed, store.Overlay));
                AssertTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(fixture["expectedOverlay"], actual["dataAssets"]), "shared migration fixture restored overlay");
                var bare = typeof(StoryFlow.Utilities.StoryFlowStateSerializer).GetMethod("BareValueToJson", BindingFlags.NonPublic | BindingFlags.Static);
                foreach (var read in fixture["expectedReads"])
                {
                    var asset = (string)read["assetId"]; var id = (string)read["variableId"];
                    StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, project, "en", asset, id, out var value);
                    var declaration = StoryFlowDataAssetStore.FindDeclaration(store.Seed, asset, id);
                    var json = (Newtonsoft.Json.Linq.JToken)bare.Invoke(null, new object[] { value, declaration });
                    AssertTrue(Newtonsoft.Json.Linq.JToken.DeepEquals(read["value"], json), "shared migration read " + asset + "." + id);
                }
            }
            finally { ClearManager(); }
        }

        static void DataAssetMapShape()
        {
            foreach (bool empty in new[] { false, true })
            {
                var (c, x, store) = DaSetup(new() { DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")), DaN("G", StoryFlowNodeType.GetDataAssetVariable, ("variableId", "source"), ("variableType", "map"), ("keyType", "string"), ("valueType", "integer")), DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "dest"), ("variableType", "map"), ("keyType", "string"), ("valueType", "string")) }, new() { DaE("P", "G", "dataAsset-asset"), DaE("P", "W", "dataAsset-asset"), DaE("G", "W", "map-string-string-2") }, new() { new() { Id = "source", Name = "Source", Type = StoryFlowVariableType.Map, KeyType = StoryFlowVariableType.String, ValueType = StoryFlowVariableType.Integer, Value = new() { Type = StoryFlowVariableType.Map, MapValue = empty ? new() : new() { new() { Key = StoryFlowVariant.String("a"), Value = StoryFlowVariant.Int(1) } } } }, new() { Id = "dest", Name = "Dest", Type = StoryFlowVariableType.Map, KeyType = StoryFlowVariableType.String, ValueType = StoryFlowVariableType.String, Value = new() { Type = StoryFlowVariableType.Map, MapValue = new() } } });
                DataAssetNodeHandler.HandleSetDataAssetVariable(c, x.CurrentScript.GetNode("W"));
                AssertTrue(!store.Overlay.ContainsKey("asset"), "mismatched map source refuses whole write; empty=" + empty);
            }
        }

        static void DataAssetCharacterBridgeCache()
        {
            var (_, first, store) = DaSetup(new() { DaN("G", StoryFlowNodeType.GetCharacterVar, ("characterPath", "hero.sfc"), ("variableName", "Count")), DaN("PLUS", StoryFlowNodeType.PlusInt, ("value2", "1")) }, new() { DaE("G", "PLUS", "integer-1") }, new());
            var value = StoryFlowVariant.Int(7);
            first.Characters["hero.sfc"] = new() { Variables = new() { ["Count"] = value }, VariablesList = new() { new() { Id = "count", Name = "Count", Type = StoryFlowVariableType.Integer, Value = value } } };
            var second = new StoryFlowExecutionContext();
            second.Initialize(first.CurrentScript, new(), first.Characters, new(), store);
            AssertEqual(8, StoryFlowEvaluator.EvaluateIntegerFromNode(first, first.CurrentScript.GetNode("PLUS")), "first bridge condition cached");
            AssertEqual(8, StoryFlowEvaluator.EvaluateIntegerFromNode(second, second.CurrentScript.GetNode("PLUS")), "second bridge condition cached");
            var characters = new StoryFlowCharacterStoreRef { Bridge = new() { ["hero"] = "hero.sfc" }, Characters = first.Characters };
            var asset = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>(); asset.Id = "hero";
            AssertTrue(StoryFlowDataAssetAccess.SetInt(store, null, characters, asset, "Count", 20), "character bridge host write succeeds");
            AssertEqual(21, StoryFlowEvaluator.EvaluateIntegerFromNode(first, first.CurrentScript.GetNode("PLUS")), "first bridge condition refreshed");
            AssertEqual(21, StoryFlowEvaluator.EvaluateIntegerFromNode(second, second.CurrentScript.GetNode("PLUS")), "second bridge condition refreshed");
        }

        static void DataAssetDetachedMapResult()
        {
            var (component, context, store) = DaSetup(new() {
                DaN("P",StoryFlowNodeType.GetDataAsset,("assetId","asset")),
                DaN("G",StoryFlowNodeType.GetDataAssetVariable,("variableId","source"),("variableType","map"),("keyType","string"),("valueType","integer")),
                DaN("M",StoryFlowNodeType.SetMapValue,("keyType","string"),("valueType","integer"),("key","new"),("value","9")),
                DaN("W",StoryFlowNodeType.SetDataAssetVariable,("variableId","dest"),("variableType","map"),("keyType","string"),("valueType","integer"))
            },new(){DaE("P","G","dataAsset-asset"),DaE("P","W","dataAsset-asset"),DaE("G","M","map-string-integer-2"),DaE("M","W","map-string-integer-2")},new(){
                new(){Id="source",Name="Source",Type=StoryFlowVariableType.Map,KeyType=StoryFlowVariableType.String,ValueType=StoryFlowVariableType.Integer,Value=new(){Type=StoryFlowVariableType.Map,MapValue=new(){new(){Key=StoryFlowVariant.String("old"),Value=StoryFlowVariant.Int(1)}}}},
                new(){Id="dest",Name="Dest",Type=StoryFlowVariableType.Map,KeyType=StoryFlowVariableType.String,ValueType=StoryFlowVariableType.Integer,Value=new(){Type=StoryFlowVariableType.Map,MapValue=new()}}
            });
            MapNodeHandler.HandleMapModify(component,context.CurrentScript.GetNode("M"));
            StoryFlowDataAssetStore.TryRead(store.Seed,store.Overlay,null,"en","asset","source",out var source);
            AssertEqual(1,source.MapValue.Count,"detached map mutation does not write source");
            StoryFlowDataAssetStore.ResetOverlay(store.Overlay);
            DataAssetNodeHandler.HandleSetDataAssetVariable(component,context.CurrentScript.GetNode("W"));
            StoryFlowDataAssetStore.TryRead(store.Seed,store.Overlay,null,"en","asset","dest",out var dest);
            AssertEqual(2,dest.MapValue.Count,"explicit map Set consumes completed detached mutation across invalidation");
        }

        static void DataAssetRestoreIgnoresCulture()
        {
            var before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                var value = StoryFlow.Utilities.StoryFlowStateSerializer.BareValueFromJson(Newtonsoft.Json.Linq.JToken.Parse("1.5"), new StoryFlowVariable { Type = StoryFlowVariableType.Float });
                AssertTrue(value != null && value.GetFloat() == 1.5f, "restore accepts JSON numeric tokens independently of host culture");
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = before; }
        }

        static void DataAssetSavedDateShapedText()
        {
            var asset = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>();
            asset.Id = "dates";
            asset.Variables.Add(new() { Id = "text", Name = "Text", Type = StoryFlowVariableType.String, Value = StoryFlowVariant.String("default") });
            asset.Variables.Add(new() {
                Id = "array", Name = "Array", Type = StoryFlowVariableType.String, IsArray = true,
                Value = new() { Type = StoryFlowVariableType.String, ArrayValue = new() { StoryFlowVariant.String("default") } }
            });
            asset.Variables.Add(new() {
                Id = "map", Name = "Map", Type = StoryFlowVariableType.Map,
                KeyType = StoryFlowVariableType.String, ValueType = StoryFlowVariableType.String,
                Value = new() { Type = StoryFlowVariableType.Map, MapValue = new() }
            });
            var project = ScriptableObject.CreateInstance<StoryFlowProjectAsset>();
            project.SetDataAssetReferences(new() { asset });
            SetManagerProject(project);
            try
            {
                var manager = StoryFlowManager.Instance;
                foreach (var text in new[] { "2026-09-08T12:34:56Z", "2026-09-08T12:34:56.1234567+02:30", "plain text", "" })
                {
                    AssertTrue(manager.SetDataAssetString(asset, "Text", text), "host writes date-shaped text");
                    AssertTrue(manager.SetDataAssetArrayVariable(asset, "Array", new() { StoryFlowVariant.String(text) }), "host writes date-shaped array text");
                    AssertTrue(manager.SetDataAssetMapVariable(asset, "Map", new() { StoryFlowVariant.String(text) }, new() { StoryFlowVariant.String(text) }), "host writes date-shaped map keys and values");
                    var saved = manager.ExportState();
                    manager.SetDataAssetString(asset, "Text", "later");
                    AssertTrue(manager.ImportState(saved), "load accepts the saved state");
                    AssertEqual(text, manager.GetDataAssetString(asset, "Text", out _), "save/load preserves scalar text exactly");
                    var store = manager.GetDataAssetStore();
                    StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, project, "en", asset.Id, "array", out var array);
                    StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, project, "en", asset.Id, "map", out var map);
                    AssertTrue(array.GetArray().Count == 1 && array.GetArray()[0].GetString() == text, "save/load preserves array text exactly: " + text);
                    AssertTrue(map.GetMap().Count == 1 && map.GetMap()[0].Key.GetString() == text && map.GetMap()[0].Value.GetString() == text,
                        "save/load preserves map text exactly: " + text);
                    manager.SetDataAssetString(asset, "Text", "keep current state");
                    AssertTrue(!manager.ImportState(saved + "{}"), "load still rejects a trailing second JSON document");
                    AssertEqual("keep current state", manager.GetDataAssetString(asset, "Text", out _), "rejected load leaves text unchanged");
                }
            }
            finally { ClearManager(); }
        }

        static void DataAssetMediaMapHostValuesCopy()
        {
            foreach (var (type, wire) in new[] {
                (StoryFlowVariableType.Image, "image"),
                (StoryFlowVariableType.Audio, "audio"),
                (StoryFlowVariableType.Character, "character") })
            {
                var declarations = new List<StoryFlowVariable>();
                foreach (var id in new[] { "source", "dest" })
                    declarations.Add(new() {
                        Id = id, Name = id, Type = StoryFlowVariableType.Map,
                        KeyType = StoryFlowVariableType.String, ValueType = type,
                        Value = new() { Type = StoryFlowVariableType.Map, MapValue = new() }
                    });
                var (component, context, store) = DaSetup(new() {
                    DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")),
                    DaN("G", StoryFlowNodeType.GetDataAssetVariable, ("variableId", "source"), ("variableType", "map"), ("keyType", "string"), ("valueType", wire)),
                    DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "dest"), ("variableType", "map"), ("keyType", "string"), ("valueType", wire))
                }, new() { DaE("P", "G", "dataAsset-asset"), DaE("P", "W", "dataAsset-asset"), DaE("G", "W", "map-string-" + wire + "-2") }, declarations);
                var asset = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>(); asset.Id = "asset";
                AssertTrue(component.SetDataAssetMapVariable(asset, "source",
                    new() { StoryFlowVariant.String("portrait") }, new() { StoryFlowVariant.String("asset_key") }), wire + " host map write accepts string storage");

                DataAssetNodeHandler.HandleSetDataAssetVariable(component, context.CurrentScript.GetNode("W"));
                StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, null, "en", "asset", "dest", out var copied);
                AssertTrue(copied.MapValue.Count == 1 && copied.MapValue[0].Key.GetString() == "portrait" &&
                    copied.MapValue[0].Value.GetString() == "asset_key", wire + " graph Set copies a valid host-written media map");
            }
        }

        static void DataAssetDetachedMapStringIsLiteral()
        {
            var declarations = new List<StoryFlowVariable>();
            foreach (var id in new[] { "source", "dest" })
                declarations.Add(new() {
                    Id = id, Name = id, Type = StoryFlowVariableType.Map,
                    KeyType = StoryFlowVariableType.String, ValueType = StoryFlowVariableType.String,
                    Value = new() { Type = StoryFlowVariableType.Map, MapValue = new() }
                });
            var (component, context, store) = DaSetup(new() {
                DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")),
                DaN("G", StoryFlowNodeType.GetDataAssetVariable, ("variableId", "source"), ("variableType", "map"), ("keyType", "string"), ("valueType", "string")),
                DaN("M", StoryFlowNodeType.SetMapValue, ("keyType", "string"), ("valueType", "string"), ("key", "secondKey"), ("value", "firstKey")),
                DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "dest"), ("variableType", "map"), ("keyType", "string"), ("valueType", "string"))
            }, new() { DaE("P", "G", "dataAsset-asset"), DaE("P", "W", "dataAsset-asset"), DaE("G", "M", "map-string-string-2"), DaE("M", "W", "map-string-string-2") }, declarations);
            var project = ScriptableObject.CreateInstance<StoryFlowProjectAsset>();
            project.SetLocalization(true, "en", new() { new() { Code = "fr", Name = "French" } },
                new() { new() { Language = "fr", Key = "secondKey", Value = "Wrong second translation" } });
            project.GlobalStringEntries.Add(new() { Key = "en.firstKey", Value = "secondKey" });
            context.Project = project; SetManagerProject(project);
            try
            {
                MapNodeHandler.HandleMapModify(component, context.CurrentScript.GetNode("M"));
                AssertEqual("secondKey", context.GetNodeRuntimeState("M").DetachedMapOutput.Value.MapValue[0].Value.GetString(), "mutation captures displayed text");
                StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, project, "en", "asset", "source", out var source);
                AssertEqual(0, source.MapValue.Count, "detached string mutation leaves the source asset untouched");
                StoryFlowManager.Instance.SetLanguage("fr");
                StoryFlowDataAssetStore.ResetOverlay(store.Overlay);
                DataAssetNodeHandler.HandleSetDataAssetVariable(component, context.CurrentScript.GetNode("W"));
                StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, project, "fr", "asset", "dest", out var copied);
                AssertTrue(copied.MapValue.Count == 1 && copied.MapValue[0].Key.GetString() == "secondKey" &&
                    copied.MapValue[0].Value.GetString() == "secondKey", "explicit Set preserves completed display text and literal keys across a language change");
            }
            finally { ClearManager(); }
        }

        static void DataAssetCharacterEnumSources()
        {
            foreach (var scenario in new[] { "missingCharacter", "missingVariable", "wrongType", "empty", "valid" })
            foreach (bool convertToString in new[] { false, true })
            {
                var nodes = new List<StoryFlowScriptAsset.SerializedNode> {
                    DaN("P", StoryFlowNodeType.GetDataAsset, ("assetId", "asset")),
                    DaN("G", StoryFlowNodeType.GetCharacterVar, ("characterPath", "hero.sfc"), ("variableName", "State"), ("variableType", "enum")),
                    DaN("W", StoryFlowNodeType.SetDataAssetVariable, ("variableId", "state"), ("variableType", convertToString ? "string" : "enum"))
                };
                var edges = new List<StoryFlowConnection> { DaE("P", "W", "dataAsset-asset") };
                if (convertToString)
                {
                    nodes.Add(DaN("C", StoryFlowNodeType.EnumToString));
                    edges.Add(DaE("G", "C", "enum-1"));
                    edges.Add(DaE("C", "W", "string-2"));
                }
                else edges.Add(DaE("G", "W", "enum-2"));
                var (component, context, store) = DaSetup(nodes, edges, new() { new() {
                    Id = "state", Name = "State", Type = convertToString ? StoryFlowVariableType.String : StoryFlowVariableType.Enum,
                    Value = convertToString ? StoryFlowVariant.String("Ready") : StoryFlowVariant.Enum("Ready")
                } });
                if (scenario != "missingCharacter") context.Characters["hero.sfc"] = new() { Variables = new() };
                if (scenario != "missingCharacter" && scenario != "missingVariable")
                    context.Characters["hero.sfc"].Variables["State"] = scenario == "wrongType"
                        ? StoryFlowVariant.String("Done") : StoryFlowVariant.Enum(scenario == "empty" ? "" : "Done");
                bool resolved = scenario == "empty" || scenario == "valid";
                // Memoizing the normal display fallback must not hide failure from a subsequent Set.
                AssertEqual(scenario == "valid" ? "Done" : "", StoryFlowEvaluator.EvaluateEnumFromNode(context, context.CurrentScript.GetNode("G")), scenario + " normal enum read keeps its display default");
                DataAssetNodeHandler.HandleSetDataAssetVariable(component, context.CurrentScript.GetNode("W"));
                StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, null, "en", "asset", "state", out var value);
                AssertEqual(resolved ? (scenario == "empty" ? "" : "Done") : "Ready", convertToString ? value.GetString() : value.GetEnum(), scenario + " enum source through conversion=" + convertToString);
                AssertEqual(resolved, store.Overlay.ContainsKey("asset"), scenario + " only resolved enum sources create a session write");
            }
        }

        static void DataAssetEmptyArrayShape()
        {
            foreach(var kind in new[]{"ordinary","character","runScript"})
            {
                var source = kind == "character" ? DaN("G",StoryFlowNodeType.GetCharacterVar,("characterPath","hero.sfc"),("variableName","List")) : kind == "runScript" ? DaN("G",StoryFlowNodeType.RunScript) : DaN("G",StoryFlowNodeType.GetStringArray,("variable","source"));
                var(c,x,store)=DaSetup(new(){DaN("P",StoryFlowNodeType.GetDataAsset,("assetId","asset")),source,DaN("W",StoryFlowNodeType.SetDataAssetVariable,("variableId","dest"),("variableType","integer"),("isArray","true"))},new(){DaE("P","W","dataAsset-asset"),DaE("G","W","integer-array-2")},new(){new(){Id="dest",Name="Dest",Type=StoryFlowVariableType.Integer,IsArray=true,Value=new(){Type=StoryFlowVariableType.Integer,ArrayValue=new(){StoryFlowVariant.Int(7)}}}});
                var value = new StoryFlowVariant {Type=StoryFlowVariableType.String,ArrayValue=new()};
                x.LocalVariables["source"]=new(){Id="source",Type=StoryFlowVariableType.String,IsArray=true,Value=value};
                x.Characters["hero.sfc"]=new(){Variables=new(){["List"]=value}};
                x.GetNodeRuntimeState("G").OutputValues["result"]=value;
                DataAssetNodeHandler.HandleSetDataAssetVariable(c,x.CurrentScript.GetNode("W"));
                AssertTrue(store.Overlay.Count==0,kind+" empty wrong-type array refuses whole Set");
            }
        }

    }
}
