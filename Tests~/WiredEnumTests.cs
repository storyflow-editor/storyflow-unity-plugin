using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StoryFlow.Tests
{
    // Set Enum and Set Character Variable with an enum wired into their value pin, against the StoryFlow Editor
    // runtime, which is the reference: updateEnumVariable and updateCharacterVariable read the wire as an enum,
    // so every enum producer can feed them. With nothing wired they store their own value.
    internal static partial class Program
    {
        private static void RunWiredEnumTests()
        {
            foreach (var source in WiredEnumSources())
                Run("Set Enum stores the value " + source.Key + " wires into it", () => WiredEnumSet(source.Value));
            Run("Set Enum with nothing wired stores its own value", WiredEnumSetInline);
            Run("Set Character Variable stores the enum a Get Enum wires into it", () => WiredEnumSetCharacter(true));
            Run("Set Character Variable on an enum with nothing wired stores its own value", () => WiredEnumSetCharacter(false));
        }

        private const string WiredEnumAlice = @"{'characters':{'characters\\alice.sfc':{'name':'Alice','image':'','variables':{
            'var-mood':{'id':'var-mood','name':'Mood','type':'enum','value':'Calm','enumValues':['Calm','Sad']}}}},'strings':{'en':{}},'assets':{}}";

        // Each source answers Sad out of Happy and Sad: its nodes, its wires, and the files it needs beside the script.
        private sealed class WiredEnumSource
        {
            public string Nodes;
            public JObject[] Edges;
            public Dictionary<string, JObject> Files = new Dictionary<string, JObject>();
        }

        private static IEnumerable<KeyValuePair<string, WiredEnumSource>> WiredEnumSources()
        {
            yield return new KeyValuePair<string, WiredEnumSource>("a Get Enum", new WiredEnumSource {
                Nodes = "{'id':'src','type':'getEnum','variable':'v_source'}", Edges = new[] { RegressionEdge("src", "set", "enum-", "enum-2") } });
            // The conversions take their value list from the Set Enum they feed.
            yield return new KeyValuePair<string, WiredEnumSource>("Int to Enum", new WiredEnumSource {
                Nodes = "{'id':'src','type':'intToEnum'},{'id':'one','type':'getInt','variable':'v_one'}",
                Edges = new[] { RegressionEdge("one", "src", "integer-", "integer-"), RegressionEdge("src", "set", "enum-", "enum-2") } });
            yield return new KeyValuePair<string, WiredEnumSource>("String to Enum", new WiredEnumSource {
                Nodes = "{'id':'src','type':'stringToEnum'},{'id':'text','type':'getString','variable':'v_text'}",
                Edges = new[] { RegressionEdge("text", "src", "string-", "string-"), RegressionEdge("src", "set", "enum-", "enum-2") } });
            yield return new KeyValuePair<string, WiredEnumSource>("a character's enum variable", new WiredEnumSource {
                Nodes = @"{'id':'src','type':'getCharacterVar','characterPath':'characters\\alice.sfc','variable':'Mood','variableType':'enum','characterId':'da_alice0001'}",
                Edges = new[] { RegressionEdge("src", "set", "enum-output", "enum-2") },
                Files = { ["characters.json"] = JObject.Parse(WiredEnumAlice.Replace("'value':'Calm'", "'value':'Sad'")),
                    ["character-index.json"] = JObject.Parse(@"{'schemaVersion':'1','characters':{'da_alice0001':'characters\\alice.sfc'}}") } });
            yield return new KeyValuePair<string, WiredEnumSource>("a Data Asset's enum variable", new WiredEnumSource {
                Nodes = "{'id':'pill','type':'getDataAsset','assetId':'asset'},{'id':'src','type':'getDataAssetVariable','variableId':'mood','variable':'Mood','variableType':'enum'}",
                Edges = new[] { RegressionEdge("pill", "src", "dataAsset-", "dataAsset-asset"), RegressionEdge("src", "set", "enum-", "enum-2") },
                Files = { ["data-assets.json"] = JObject.Parse(@"{'localizationVersion':2,'dataAssets':{'asset':{'name':'Asset','parent':null,
                    'variables':[{'id':'mood','name':'Mood','type':'enum','value':'Sad','enumValues':['Happy','Sad']}],'overrides':{}}},'strings':{'en':{}},'assets':{}}") } });
        }

        private const string WiredEnumVariables = @"[{'id':'v_target','name':'Target','type':'enum','value':'Happy','enumValues':['Happy','Sad']},
            {'id':'v_source','name':'Source','type':'enum','value':'Sad','enumValues':['Happy','Sad']},
            {'id':'v_one','name':'One','type':'integer','value':1},{'id':'v_text','name':'Text','type':'string','value':'Sad'}]";

        // Start -> `set` -> line Done. Answers the Set's variable afterwards.
        private static string WiredEnumTarget(string nodes, IEnumerable<JObject> edges, Dictionary<string, JObject> files, System.Func<StoryFlowComponent, string> read)
        {
            var story = new Dictionary<string, JObject> { ["main.json"] = LoopScript("[{'id':'Done','type':'dialogue','choices':[]}," + nodes + "]",
                new[] { RegressionEdge("0", "set"), RegressionEdge("set", "Done", "1", "") }.Concat(edges), WiredEnumVariables) };
            foreach (var file in files) story[file.Key] = file.Value;
            string value = null;
            WithLoopStory(false, story, (c, errors) =>
            {
                AssertEqual("Done", c.GetCurrentDialogue()?.NodeId, "the walk reaches the line");
                AssertEqual(0, errors.Count, "no error");
                value = read(c);
            });
            return value;
        }

        private static string WiredEnumTargetValue(StoryFlowComponent c) => c.GetContext().LocalVariables["v_target"].Value.GetEnum();

        private static void WiredEnumSet(WiredEnumSource source)
        {
            AssertEqual("Sad", WiredEnumTarget("{'id':'set','type':'setEnum','variable':'v_target'}," + source.Nodes, source.Edges, source.Files,
                WiredEnumTargetValue), "Target");
        }

        private static void WiredEnumSetInline()
        {
            AssertEqual("Sad", WiredEnumTarget("{'id':'set','type':'setEnum','variable':'v_target','value':'Sad'}", new JObject[0],
                new Dictionary<string, JObject>(), WiredEnumTargetValue), "Target");
        }

        // Alice's Mood is Calm; the Set writes Sad, wired from Get Enum (Source) or as its own value.
        private static void WiredEnumSetCharacter(bool wired)
        {
            var files = new Dictionary<string, JObject> { ["characters.json"] = JObject.Parse(WiredEnumAlice),
                ["character-index.json"] = JObject.Parse(@"{'schemaVersion':'1','characters':{'da_alice0001':'characters\\alice.sfc'}}") };
            var set = @"{'id':'set','type':'setCharacterVar','characterPath':'characters\\alice.sfc','variable':'Mood','variableType':'enum',
                'characterId':'da_alice0001'" + (wired ? "},{'id':'src','type':'getEnum','variable':'v_source'}" : ",'value':'Sad'}");
            AssertEqual("Sad", WiredEnumTarget(set, wired ? new[] { RegressionEdge("src", "set", "enum-", "enum-input") } : new JObject[0], files,
                c => c.GetCharacterVariable("characters/alice.sfc", "Mood")?.GetEnum()), "Alice's Mood");
        }
    }
}
