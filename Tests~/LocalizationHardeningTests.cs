using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using StoryFlow.Execution;
using UnityEngine;

namespace StoryFlow.Tests
{
    internal static partial class Program
    {
        private static void RunLocalizationHardeningTests()
        {
            Run(nameof(LocalizationReadsNewAndLegacySourceBuckets), LocalizationReadsNewAndLegacySourceBuckets);
            Run(nameof(FirstInstallUsesSourceThenCarriesPlayerChoice), FirstInstallUsesSourceThenCarriesPlayerChoice);
            Run(nameof(LanguageChangedEventUsesCommittedState), LanguageChangedEventUsesCommittedState);
            Run(nameof(AuthoredNameSaveResolvesInCurrentLanguage), AuthoredNameSaveResolvesInCurrentLanguage);
            Run(nameof(WrittenNameSaveSurvivesProjectResetAndLanguageSwitch), WrittenNameSaveSurvivesProjectResetAndLanguageSwitch);
            Run(nameof(LegacyNameSaveRemainsLiteral), LegacyNameSaveRemainsLiteral);
            Run(nameof(HostNameReadPreservesLiteralKeys), HostNameReadPreservesLiteralKeys);
            Run(nameof(GraphNameReadPreservesLiteralKeysAndFollowsLanguage), GraphNameReadPreservesLiteralKeysAndFollowsLanguage);
            Run(nameof(ResolvedHostArrayCanBeReusedWithoutRelocalizing), ResolvedHostArrayCanBeReusedWithoutRelocalizing);
            Run(nameof(AuthoredArrayContainsAndFindResolveBothSides), AuthoredArrayContainsAndFindResolveBothSides);
            Run(nameof(CachedAuthoredArrayElementFollowsLanguage), CachedAuthoredArrayElementFollowsLanguage);
            Run(nameof(WrittenArrayKeysRemainLiteralAcrossSave), WrittenArrayKeysRemainLiteralAcrossSave);
            Run(nameof(DataAssetArrayReadKeepsLiteralOverrides), DataAssetArrayReadKeepsLiteralOverrides);
            Run(nameof(DataAssetArrayWriteCapturesAuthoredText), DataAssetArrayWriteCapturesAuthoredText);
            Run(nameof(StoppedComponentUsesReplacementProject), StoppedComponentUsesReplacementProject);
            Run(nameof(MapKeysRemainLiteralInArrayConsumers), MapKeysRemainLiteralInArrayConsumers);
            RunDataAssetHardeningTests();
            RunLegacyCharacterImportTests();
        }

        private static void MapKeysRemainLiteralInArrayConsumers()
        {
            var project = LocalizationProject();
            project.GlobalStringEntries.Add(new() { Key = "en.needle", Value = "item.value.0" });
            SetManagerProject(project);
            try
            {
                var manager = StoryFlowManager.Instance;
                manager.GlobalVariables["map"] = new StoryFlowVariable
                {
                    Id = "map", Type = StoryFlowVariableType.Map,
                    Value = new StoryFlowVariant { Type = StoryFlowVariableType.Map, MapValue = new()
                    {
                        new() { Key = StoryFlowVariant.String("item.value.0"), Value = StoryFlowVariant.String("value") }
                    } }
                };
                var ctx = ArraySearchContext(manager);
                ctx.CurrentScript.SetNodes(new()
                {
                    new() { Id = "map", Type = StoryFlowNodeType.GetMap,
                        Data = new() { new() { Key = "variable", Value = "map" }, new() { Key = "isGlobal", Value = "true" } } },
                    new() { Id = "keys", Type = StoryFlowNodeType.MapKeys, Data = new()
                    {
                        new() { Key = "keyType", Value = "string" }, new() { Key = "valueType", Value = "string" }
                    } }
                });
                ctx.CurrentScript.SetConnections(new()
                {
                    new() { Source = "map", Target = "keys", SourceHandle = "source-map-map-string-string-",
                        TargetHandle = "target-keys-map-string-string-1" },
                    new() { Source = "keys", Target = "search", SourceHandle = "source-keys-string-array-",
                        TargetHandle = "target-search-string-array-1" }
                });
                var node = new StoryFlowNode { Id = "search", Type = StoryFlowNodeType.StringArrayContains,
                    Data = new() { ["value"] = "needle" } };
                AssertTrue(StoryFlowEvaluator.EvaluateBooleanFromNode(ctx, node), "Contains keeps map identifiers literal");
                ctx.ClearNodeRuntimeStates();
                node.Type = StoryFlowNodeType.FindInStringArray;
                AssertEqual(0, StoryFlowEvaluator.EvaluateIntegerFromNode(ctx, node), "Find keeps map identifiers literal");
                ctx.ClearNodeRuntimeStates();
                node.Type = StoryFlowNodeType.GetStringArrayElement;
                node.Data["value"] = "0";
                AssertEqual("item.value.0", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "element keeps map identifier literal");
                manager.SetLanguage("fr");
                AssertEqual("item.value.0", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "cached identifier stays literal after language switch");
                AssertTrue(!manager.GlobalVariables["map"].Value.MapValue[0].Key.IsLiteralString, "projection does not mutate source map");
                manager.SetLanguage("en");
                ctx.ClearNodeRuntimeStates();
                var store = new StoryFlowDataAssetStoreRef
                {
                    Seed = new() { ["asset"] = new() { Id = "asset", Variables = new()
                    {
                        new() { Id = "entry", Name = "item.value.0", Type = StoryFlowVariableType.String }
                    } } },
                    Overlay = new()
                };
                ctx.Initialize(ctx.CurrentScript, manager.GlobalVariables, manager.RuntimeCharacters, manager.UsedOnceOnlyOptions, store);
                ctx.CurrentScript.SetNodes(new()
                {
                    new() { Id = "asset", Type = StoryFlowNodeType.GetDataAsset,
                        Data = new() { new() { Key = "assetId", Value = "asset" } } },
                    new() { Id = "names", Type = StoryFlowNodeType.GetDataAssetVariableNames }
                });
                ctx.CurrentScript.SetConnections(new()
                {
                    new() { Source = "asset", Target = "names", SourceHandle = "source-asset-dataAsset-asset",
                        TargetHandle = "target-names-dataAsset-asset" },
                    new() { Source = "names", Target = "search", SourceHandle = "source-names-string-array-",
                        TargetHandle = "target-search-string-array-1" }
                });
                AssertEqual("item.value.0", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "variable names are literal identifiers");
                ctx.ClearNodeRuntimeStates();
                node.Type = StoryFlowNodeType.StringArrayContains;
                node.Data["value"] = "needle";
                AssertTrue(StoryFlowEvaluator.EvaluateBooleanFromNode(ctx, node), "Contains keeps variable names literal");
            }
            finally { ClearManager(); }
        }

        private static StoryFlowProjectAsset LocalizationProject(string source = "en")
        {
            var p = ScriptableObject.CreateInstance<StoryFlowProjectAsset>();
            p.SetLocalization(true, source, new List<StoryFlowProjectAsset.LanguageEntry>
            {
                new() { Code = "fr", Name = "French" }, new() { Code = "es", Name = "Spanish" }
            }, new List<StoryFlowProjectAsset.LanguageStringEntry>
            {
                new() { Language = "fr", Key = "hero.name", Value = "Chevalier" },
                new() { Language = "es", Key = "hero.name", Value = "Caballero" },
                new() { Language = "fr", Key = "item.value.0", Value = "Épée" },
                new() { Language = "fr", Key = "contains.value", Value = "Épée" }
            });
            p.GlobalStringEntries.Add(new() { Key = "en.hero.name", Value = "Knight" });
            p.GlobalStringEntries.Add(new() { Key = "en.item.value.0", Value = "Sword" });
            p.GlobalStringEntries.Add(new() { Key = "en.contains.value", Value = "Sword" });
            p.GlobalVariableEntries.Add(new()
            {
                Id = "items", Name = "Items", Type = StoryFlowVariableType.String,
                IsArray = true, DefaultValueJson = "[\"item.value.0\"]"
            });
            var c = ScriptableObject.CreateInstance<StoryFlowCharacterAsset>();
            c.CharacterName = "hero.name";
            p.CharacterReferences.Add(new() { Path = "hero", Asset = c });
            p.SetCharacterIdEntries(new() { new() { Id = "hero-id", Path = "hero" } });
            return p;
        }

        private static void LocalizationReadsNewAndLegacySourceBuckets()
        {
            var p = LocalizationProject("ja");
            p.GlobalStringEntries.Add(new() { Key = "en.legacy.text", Value = "こんにちは" });
            p.GlobalStringEntries.Add(new() { Key = "ja.new.text", Value = "新しい" });
            p.GlobalStringEntries.Add(new() { Key = "en.new.text", Value = "wrong legacy" });
            SetManagerProject(p);
            try
            {
                var context = new StoryFlowExecutionContext { Project = p };
                AssertEqual("こんにちは", context.LookUpLocalized("legacy.text"), "legacy en bucket is a final fallback");
                AssertEqual("新しい", context.LookUpLocalized("new.text"), "configured source precedes legacy en");
                StoryFlowManager.Instance.SetLanguage("fr");
                AssertEqual("こんにちは", context.LookUpLocalized("legacy.text"), "target miss also falls back to legacy en");
            }
            finally { ClearManager(); }
        }

        private static void FirstInstallUsesSourceThenCarriesPlayerChoice()
        {
            var p = LocalizationProject("ja");
            p.LanguageEntries.Add(new() { Code = "en", Name = "English" });
            SetManagerProject(p);
            try
            {
                var manager = StoryFlowManager.Instance;
                AssertEqual("ja", manager.GetLanguage(), "first install is the source, not the default en field");
                AssertTrue(manager.SetLanguage("en"), "host can choose English");
                manager.SetProject(p);
                AssertEqual("en", manager.GetLanguage(), "reinstall carries the established choice");
            }
            finally { ClearManager(); }
        }

        private static void LanguageChangedEventUsesCommittedState()
        {
            var manager = CreateManager();
            var observed = new List<string>();
            manager.OnLanguageChanged += language =>
            {
                var hero = manager.RuntimeCharacters.TryGetValue("hero", out var value)
                    ? value.Name
                    : "<missing>";
                observed.Add(language + ":" + manager.GetLanguage() + ":" + hero);
            };
            try
            {
                var japanese = LocalizationProject("ja");
                japanese.LanguageEntries.Add(new() { Code = "en", Name = "English" });
                japanese.GlobalStringEntries.Add(new() { Key = "ja.hero.name", Value = "騎士" });
                manager.SetProject(japanese);
                AssertEqual("ja:ja:騎士", observed[0], "first install event sees installed source state");

                AssertTrue(manager.SetLanguage("FR"), "registered codes are case-insensitive");
                AssertEqual("fr:fr:Chevalier", observed[1], "explicit event sees canonical code and refreshed name");
                manager.SetLanguage("fr");
                AssertEqual(2, observed.Count, "setting the active language is silent");
                AssertTrue(!manager.SetLanguage("unknown"), "unknown language is refused");
                AssertEqual(2, observed.Count, "refused language is silent");

                manager.SetProject(LocalizationProject("en"));
                AssertEqual("fr", manager.GetLanguage(), "replacement carries a valid explicit choice");
                AssertEqual(2, observed.Count, "carried replacement is silent");

                var japaneseOnly = LocalizationProject("ja");
                japaneseOnly.LanguageEntries.Clear();
                japaneseOnly.GlobalStringEntries.Add(new() { Key = "ja.hero.name", Value = "騎士" });
                manager.SetProject(japaneseOnly);
                AssertEqual("ja:ja:騎士", observed[2], "snap event sees the replacement project after seeding");
            }
            finally { ClearManager(); }
        }

        private static void AuthoredNameSaveResolvesInCurrentLanguage()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var manager = StoryFlowManager.Instance;
                manager.SetLanguage("fr");
                var save = manager.ExportState();
                manager.SetLanguage("es");
                AssertTrue(manager.ImportState(save), "authored name load accepted");
                AssertEqual("Caballero", manager.RuntimeCharacters["hero"].Name, "saved authored name follows current language");
            }
            finally { ClearManager(); }
        }

        private static void WrittenNameSaveSurvivesProjectResetAndLanguageSwitch()
        {
            var p = LocalizationProject();
            SetManagerProject(p);
            try
            {
                var manager = StoryFlowManager.Instance;
                var hero = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>();
                hero.Id = "hero-id";
                AssertTrue(manager.SetDataAssetString(hero, "cf_name", "Player Bob"), "real name write accepted");
                var save = manager.ExportState();
                manager.SetProject(p);
                manager.ImportState(save);
                manager.SetLanguage("fr");
                AssertEqual("Player Bob", manager.RuntimeCharacters["hero"].Name, "saved player name remains literal");
            }
            finally { ClearManager(); }
        }

        private static void LegacyNameSaveRemainsLiteral()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var manager = StoryFlowManager.Instance;
                var save = JObject.Parse(manager.ExportState());
                var hero = (JObject)save["characters"]["hero"];
                hero.Remove("nameKey");
                hero["name"] = "Legacy Player";
                AssertTrue(manager.ImportState(save.ToString()), "legacy save accepted");
                manager.SetLanguage("fr");
                AssertEqual("Legacy Player", manager.RuntimeCharacters["hero"].Name, "ambiguous legacy names remain literal");
            }
            finally { ClearManager(); }
        }

        private static StoryFlowExecutionContext ArraySearchContext(StoryFlowManager manager)
        {
            var script = ScriptableObject.CreateInstance<StoryFlowScriptAsset>();
            script.SetNodes(new() { new() { Id = "get", Type = StoryFlowNodeType.GetStringArray,
                Data = new() { new() { Key = "variable", Value = "items" }, new() { Key = "isGlobal", Value = "true" } } } });
            script.SetConnections(new() { new() { Source = "get", Target = "search",
                SourceHandle = StoryFlowHandles.Source("get", "string-array"),
                TargetHandle = StoryFlowHandles.Target("search", StoryFlowHandles.In_StringArray + "-1") } });
            var ctx = new StoryFlowExecutionContext { Project = manager.Project };
            ctx.Initialize(script, manager.GlobalVariables, manager.RuntimeCharacters, manager.UsedOnceOnlyOptions);
            return ctx;
        }

        private static void HostNameReadPreservesLiteralKeys()
        {
            var project = LocalizationProject();
            SetManagerProject(project);
            try
            {
                var manager = StoryFlowManager.Instance;
                var component = new StoryFlowComponent();
                var hero = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>();
                hero.Id = "hero-id";
                AssertTrue(manager.SetDataAssetString(hero, "cf_name", "hero.name"), "literal key rename accepted");
                AssertEqual("hero.name", component.GetCharacterVariable("hero", "cf_name").GetString(), "host preserves key-shaped player name");
                var save = manager.ExportState();
                manager.SetProject(project);
                manager.ImportState(save);
                manager.SetLanguage("fr");
                AssertEqual("hero.name", component.GetCharacterVariable("hero", "Name").GetString(), "host preserves literal after save and language switch");
            }
            finally { ClearManager(); }
        }

        private static void GraphNameReadPreservesLiteralKeysAndFollowsLanguage()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var manager = StoryFlowManager.Instance;
                var ctx = ArraySearchContext(manager);
                var hero = ScriptableObject.CreateInstance<StoryFlowDataAssetAsset>();
                hero.Id = "hero-id";
                foreach (var type in new[] { StoryFlowNodeType.GetCharacterVar, StoryFlowNodeType.SetCharacterVar })
                {
                    var node = new StoryFlowNode { Id = type.ToString(), Type = type,
                        Data = new() { ["characterPath"] = "hero", ["variableName"] = "cf_name" } };
                    manager.RuntimeCharacters["hero"].NameKey = "hero.name";
                    manager.SetLanguage("en");
                    AssertEqual("Knight", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "authored name starts in source");
                    manager.SetLanguage("fr");
                    AssertEqual("Chevalier", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "repeated authored reader follows language");
                    manager.SetDataAssetString(hero, "cf_name", "hero.name");
                    AssertEqual("hero.name", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "graph preserves key-shaped player name");
                    AssertEqual("hero.name", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "cached graph preserves key-shaped player name");
                    manager.ImportState(manager.ExportState());
                    manager.SetLanguage("fr");
                    AssertEqual("hero.name", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "graph preserves literal after save and language switch");
                    manager.RuntimeCharacters["hero"].NameKey = "hero.name";
                    manager.SetLanguage("es");
                    AssertEqual("Caballero", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "authored name refresh remains live through same graph reader");
                }
            }
            finally { ClearManager(); }
        }

        private static void ResolvedHostArrayCanBeReusedWithoutRelocalizing()
        {
            var project = LocalizationProject();
            project.GlobalStringEntries.Add(new() { Key = "en.Sword", Value = "Wrong second lookup" });
            SetManagerProject(project);
            try
            {
                var manager = StoryFlowManager.Instance;
                var component = new StoryFlowComponent();
                var resolved = component.GetArrayVariable("Items", true);
                AssertEqual("Sword", resolved[0].GetString(), "host resolves authored entry once");
                manager.GlobalVariables["items"].Value.ArrayValue = resolved;
                var ctx = ArraySearchContext(manager);
                var node = new StoryFlowNode { Id = "search", Type = StoryFlowNodeType.GetStringArrayElement,
                    Data = new() { ["value"] = "0" } };
                AssertEqual("Sword", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "reused host result remains finished text");
            }
            finally { ClearManager(); }
        }

        private static void AuthoredArrayContainsAndFindResolveBothSides()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var manager = StoryFlowManager.Instance;
                var ctx = ArraySearchContext(manager);
                foreach (var language in new[] { "en", "fr" })
                {
                    manager.SetLanguage(language);
                    ctx.ClearNodeRuntimeStates();
                    var node = new StoryFlowNode { Id = "search", Type = StoryFlowNodeType.StringArrayContains,
                        Data = new() { ["value"] = "contains.value" } };
                    AssertTrue(StoryFlowEvaluator.EvaluateBooleanFromNode(ctx, node), language + " contains authored text");
                    ctx.ClearNodeRuntimeStates();
                    node.Type = StoryFlowNodeType.FindInStringArray;
                    AssertEqual(0, StoryFlowEvaluator.EvaluateIntegerFromNode(ctx, node), language + " finds authored text");
                }
            }
            finally { ClearManager(); }
        }

        private static void WrittenArrayKeysRemainLiteralAcrossSave()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var manager = StoryFlowManager.Instance;
                var component = new StoryFlowComponent();
                component.SetStringArrayVariable("Items", new() { "item.value.0" }, true);
                var save = manager.ExportState();
                manager.SetProject(manager.Project);
                manager.ImportState(save);
                manager.SetLanguage("fr");
                AssertEqual("item.value.0", component.GetArrayVariable("Items", true)[0].GetString(), "host getter preserves literal key across load");
                var ctx = ArraySearchContext(manager);
                var node = new StoryFlowNode { Id = "search", Type = StoryFlowNodeType.StringArrayContains,
                    Data = new() { ["value"] = "contains.value" } };
                AssertTrue(!StoryFlowEvaluator.EvaluateBooleanFromNode(ctx, node), "literal key does not become translated prose");
                ctx.ClearNodeRuntimeStates();
                node.Type = StoryFlowNodeType.GetStringArrayElement;
                node.Data["value"] = "0";
                AssertEqual("item.value.0", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "graph element preserves literal key");
                AssertEqual("item.value.0", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "cached graph element preserves literal key");
            }
            finally { ClearManager(); }
        }

        private static void CachedAuthoredArrayElementFollowsLanguage()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var manager = StoryFlowManager.Instance;
                var ctx = ArraySearchContext(manager);
                var node = new StoryFlowNode { Id = "search", Type = StoryFlowNodeType.GetStringArrayElement,
                    Data = new() { ["value"] = "0" } };
                AssertEqual("Sword", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "authored element starts in source");
                manager.SetLanguage("fr");
                AssertEqual("Épée", StoryFlowEvaluator.EvaluateStringFromNode(ctx, node), "cached authored element follows language");
            }
            finally { ClearManager(); }
        }

        private static void StoppedComponentUsesReplacementProject()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var component = new StoryFlowComponent { UIStyle = BuiltInUIStyle.None };
                var script = ScriptableObject.CreateInstance<StoryFlowScriptAsset>();
                script.SetNodes(new() { new() { Id = "0", Type = StoryFlowNodeType.End } });
                component.StartDialogue(script);
                StoryFlowManager.Instance.SetProject(LocalizationProject());
                StoryFlowManager.Instance.SetLanguage("fr");
                AssertEqual("Chevalier", component.GetLocalizedString("hero.name"), "idle lookup reads the current project");
            }
            finally { ClearManager(); }
        }

        private static void DataAssetArrayReadKeepsLiteralOverrides()
        {
            var project = LocalizationProject();
            var declaration = new StoryFlowVariable
            {
                Id = "items", Name = "Items", Type = StoryFlowVariableType.String, IsArray = true,
                Value = StoryFlowVariant.DeserializeArrayFromJson(StoryFlowVariableType.String, "[\"item.value.0\"]")
            };
            var seed = new Dictionary<string, StoryFlowDataAssetDef>
            {
                ["asset"] = new() { Id = "asset", Variables = new() { declaration } }
            };
            var overlay = new Dictionary<string, Dictionary<string, StoryFlowVariant>>
            {
                ["asset"] = new() { ["items"] = new StoryFlowVariant(declaration.Value) }
            };
            AssertTrue(StoryFlowDataAssetStore.TryRead(seed, overlay, project, "fr", "asset", "items", out var result), "data asset array read succeeds");
            var ctx = new StoryFlowExecutionContext { Project = project };
            AssertEqual("item.value.0", ctx.ResolveArrayString(result.ArrayValue[0]), "consumer preserves literal session value");
        }

        private static void DataAssetArrayWriteCapturesAuthoredText()
        {
            SetManagerProject(LocalizationProject());
            try
            {
                var manager = StoryFlowManager.Instance;
                manager.SetLanguage("fr");
                var ctx = ArraySearchContext(manager);
                ctx.CurrentScript.SetConnections(new() { new() { Source = "get", Target = "setter",
                    SourceHandle = StoryFlowHandles.Source("get", "string-array"),
                    TargetHandle = StoryFlowHandles.Target("setter", StoryFlowHandles.InDataAssetArrayValue("string")) } });
                var node = new StoryFlowNode { Id = "setter", Type = StoryFlowNodeType.SetDataAssetVariable,
                    Data = new() { ["variableType"] = "string", ["isArray"] = "true" } };
                var reader = typeof(Execution.NodeHandlers.DataAssetNodeHandler).GetMethod("TryReadSetInput",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                object[] args = { ctx, node, null };
                AssertTrue((bool)reader.Invoke(null, args), "real setter value pin resolves");
                var written = (StoryFlowVariant)args[2];
                AssertEqual("Épée", written.ArrayValue[0].GetString(), "write captures the authored element's active text");
                AssertEqual("item.value.0", manager.GlobalVariables["items"].Value.ArrayValue[0].GetString(), "write leaves source identity intact");
                var seed = new Dictionary<string, StoryFlowDataAssetDef>
                {
                    ["asset"] = new() { Id = "asset", Variables = new() { new() {
                        Id = "items", Name = "Items", Type = StoryFlowVariableType.String, IsArray = true,
                        Value = manager.GlobalVariables["items"].Value } } }
                };
                var overlay = new Dictionary<string, Dictionary<string, StoryFlowVariant>>();
                AssertTrue(StoryFlowDataAssetStore.TrySet(seed, overlay, "asset", "items", written), "array snapshot stores");
                manager.SetLanguage("en");
                AssertTrue(StoryFlowDataAssetStore.TryRead(seed, overlay, manager.Project, "en", "asset", "items", out var read), "array snapshot reads");
                AssertEqual("Épée", ctx.ResolveArrayString(read.ArrayValue[0]), "stored snapshot remains literal after language change");
            }
            finally { ClearManager(); }
        }
    }
}
