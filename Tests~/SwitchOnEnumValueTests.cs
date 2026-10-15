using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;

namespace StoryFlow.Tests
{
    // Switch on Enum with a value pin against the StoryFlow Editor runtime, which is the reference
    // (switch-on-enum-value.test.ts). The enum arrives on a wire, so an enum variable, a character's enum
    // variable or a conversion can drive it, and an output is taken only when its handle names the value exactly.
    internal static partial class Program
    {
        private static void RunSwitchOnEnumValueTests()
        {
            Run("Switch on Enum Value leaves through the output named by the enum variable wired into it", SwitchValueFromVariable);
            Run("Switch on Enum Value leaves through the output named by a character's enum variable", SwitchValueFromCharacter);
            Run("Switch on Enum Value takes the output that matches the value exactly, not a longer value that starts with it", SwitchValueExactMatch);
            Run("Switch on Enum Value stops when nothing is wired into the value pin, instead of taking the first output", SwitchValueUnwired);
            Run("Switch on Enum Value stops when the output for the value is not connected", SwitchValueUnconnectedOutput);
            foreach (var conversion in new[] { "intToEnum", "stringToEnum" })
                Run(conversion + " feeding Switch on Enum Value maps into the switch's value list", () => SwitchValueFromConversion(conversion));
            Run("Import keeps the Switch on Enum Value type and its value list", SwitchValueImport);
        }

        private const string SwitchMood = "{'id':'mood','type':'getEnum','variable':'v_mood'}";

        private static string SwitchMoodVariable(string value, params string[] enumValues) => new JArray(new JObject {
            ["id"] = "v_mood", ["name"] = "Mood", ["type"] = "enum", ["value"] = value, ["enumValues"] = new JArray(enumValues) }).ToString();

        // Start -> Switch, its value pin fed by `source` (none when null), then one line per output named after its value.
        private static Dictionary<string, JObject> SwitchValueStory(string source, string[] outputs, string variables = "[]")
        {
            var nodes = new JArray { new JObject { ["id"] = "sw", ["type"] = "switchOnEnumValue", ["enumValues"] = new JArray(outputs) } };
            var edges = new List<JObject> { RegressionEdge("0", "sw") };
            if (source != null)
            {
                var sourceNode = JObject.Parse(source);
                nodes.Add(sourceNode);
                edges.Add(RegressionEdge((string)sourceNode["id"], "sw", "enum-", "enum-1"));
            }
            foreach (var value in outputs)
            {
                nodes.Add(new JObject { ["id"] = value, ["type"] = "dialogue", ["choices"] = new JArray() });
                edges.Add(RegressionEdge("sw", value, value));
            }
            return new Dictionary<string, JObject> { ["main.json"] = LoopScript(nodes.ToString(), edges, variables) };
        }

        // Starts the story and returns the line it stopped on, "" when it stopped before reaching one.
        private static string SwitchValueLine(Dictionary<string, JObject> story, List<string> warnings = null)
        {
            string reached = null;
            System.Action<object> onWarning = message => warnings?.Add(message?.ToString());
            UnityEngine.Debug.OnWarning += onWarning;
            try
            {
                WithLoopStory(false, story, (c, errors) =>
                {
                    reached = c.GetCurrentDialogue()?.NodeId ?? "";
                    AssertEqual(0, errors.Count, "no error");
                });
            }
            finally { UnityEngine.Debug.OnWarning -= onWarning; }
            return reached;
        }

        private static void SwitchValueFromVariable()
        {
            AssertEqual("Sad", SwitchValueLine(SwitchValueStory(SwitchMood, new[] { "Happy", "Sad" }, SwitchMoodVariable("Sad", "Happy", "Sad"))), "the line for Sad");
        }

        // The node as the exporter writes it: the character by id with its record key beside it, the variable by its name.
        private static void SwitchValueFromCharacter()
        {
            var story = SwitchValueStory(@"{'id':'rel','type':'getCharacterVar','characterPath':'characters\\alice.sfc','variable':'Relationship Type',
                'variableType':'enum','characterId':'da_alice0001'}", new[] { "Friend", "Rival", "Lover" });
            // WithLoopStory writes every entry into the build folder, and the importer reads these two by name.
            story["characters.json"] = JObject.Parse(@"{'characters':{'characters\\alice.sfc':{'name':'Alice','image':'','variables':{
                'var-rel':{'id':'var-rel','name':'Relationship Type','type':'enum','value':'Rival','enumValues':['Friend','Rival','Lover']}}}},
                'strings':{'en':{}},'assets':{}}");
            story["character-index.json"] = JObject.Parse(@"{'schemaVersion':'1','characters':{'da_alice0001':'characters\\alice.sfc'}}");
            AssertEqual("Rival", SwitchValueLine(story), "the line for Rival");
        }

        private static void SwitchValueExactMatch()
        {
            // Act10 is wired first, so a lookup by prefix or substring for source-sw-Act1 would find its wire.
            AssertEqual("Act1", SwitchValueLine(SwitchValueStory(SwitchMood, new[] { "Act10", "Act1" }, SwitchMoodVariable("Act1", "Act1", "Act10"))), "the line for Act1");
        }

        private static void SwitchValueUnwired()
        {
            var warnings = new List<string>();
            AssertEqual("", SwitchValueLine(SwitchValueStory(null, new[] { "Happy", "Sad" }), warnings), "no line is reached");
            AssertTrue(warnings.Contains("[StoryFlow] SwitchOnEnumValue: no output for enum value '' (node sw)."), "the empty value is reported");
        }

        private static void SwitchValueUnconnectedOutput()
        {
            var warnings = new List<string>();
            AssertEqual("", SwitchValueLine(SwitchValueStory(SwitchMood, new[] { "Happy" }, SwitchMoodVariable("Sad", "Happy", "Sad")), warnings), "no line is reached");
            AssertTrue(warnings.Contains("[StoryFlow] SwitchOnEnumValue: no output for enum value 'Sad' (node sw)."), "the value is reported");
        }

        // Level 2 -> Int to Enum, or "Lover" -> String to Enum, -> Switch. The conversion has no list of its own and
        // reads the list of the node it feeds, so it can only land on Lover through the switch's list.
        private static void SwitchValueFromConversion(string conversion)
        {
            bool fromInt = conversion == "intToEnum";
            var story = SwitchValueStory("{'id':'convert','type':'" + conversion + "'}", new[] { "Friend", "Rival", "Lover" }, fromInt
                ? "[{'id':'v_level','name':'Level','type':'integer','value':2}]" : "[{'id':'v_label','name':'Label','type':'string','value':'Lover'}]");
            var main = story["main.json"];
            main["nodes"]["input"] = JObject.Parse(fromInt ? "{'id':'input','type':'getInt','variable':'v_level'}" : "{'id':'input','type':'getString','variable':'v_label'}");
            // Wired as integer-1 / string-1, which both runtimes read. FindInputEdge does not match the bare
            // integer- / string- handle the editor writes on a conversion's input, a gap outside this node.
            ((JArray)main["connections"]).Add(fromInt ? RegressionEdge("input", "convert", "integer-", "integer-1") : RegressionEdge("input", "convert", "string-", "string-1"));
            AssertEqual("Lover", SwitchValueLine(story), "the line for Lover");
        }

        private static void SwitchValueImport()
        {
            WithLoopStory(false, SwitchValueStory(SwitchMood, new[] { "Act10", "Act1" }, SwitchMoodVariable("Act1", "Act1", "Act10")), (c, errors) =>
            {
                var node = c.GetContext().CurrentScript.GetNode("sw");
                AssertEqual(StoryFlowNodeType.SwitchOnEnumValue, node.Type, "the node type");
                AssertEqual("switchOnEnumValue", node.RawType, "the exported type name");
                AssertEqual("[\"Act10\",\"Act1\"]", node.GetData("enumValues"), "the value list, in order");
            });
        }
    }
}
