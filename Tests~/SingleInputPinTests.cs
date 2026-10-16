using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Tests
{
    // Single-input nodes against the StoryFlow Editor runtime, which is the reference. The editor writes a pin
    // without an option id as target-<id>-<type>- (buildHandleId in node-handle-registry.ts): the input of every
    // conversion, of Not and of the single-input string ops. The editor runtime reads it as wired, and so must this one.
    internal static partial class Program
    {
        private static void RunSingleInputPinTests()
        {
            foreach (var single in SingleInputCases())
                Run(single.Type + " reads the value wired into its input pin", () => SingleInputReadsItsPin(single));
            foreach (bool visited in new[] { false, true })
                Run("Not on a character variable drives a Branch and an option condition as in the sample game, visited " + visited,
                    () => SingleInputNotOnCharacter(visited));
            Run("An input reads the exact handle before the one with a trailing hyphen", SingleInputExactFirst);
            Run("A numbered input reads only its own pin", SingleInputNumberedPin);
            Run("A typed input never reads an array, map or Run Script parameter pin", SingleInputTypedPinOnly);
        }

        private sealed class SingleInput
        {
            public string Type, In, Out, Expected;
            public JToken Value;
        }

        private static SingleInput Single(string type, string input, JToken value, string output, string expected) =>
            new SingleInput { Type = type, In = input, Value = value, Out = output, Expected = expected };

        // The registry entries whose input pin has no option id. Every value read differs from what the node answers
        // with nothing wired. Int to Enum and String to Enum take their value list from the node they feed, so
        // SwitchOnEnumValueTests covers them feeding a Switch on Enum Value.
        private static IEnumerable<SingleInput> SingleInputCases()
        {
            yield return Single("notBool", "boolean", true, "boolean", "False");
            yield return Single("booleanToInt", "boolean", true, "integer", "1");
            yield return Single("booleanToFloat", "boolean", true, "float", "1");
            yield return Single("intToBoolean", "integer", 7, "boolean", "True");
            yield return Single("intToFloat", "integer", 7, "float", "7");
            yield return Single("intToString", "integer", 7, "string", "7");
            yield return Single("floatToBoolean", "float", 2.5, "boolean", "True");
            yield return Single("floatToInt", "float", 3.25, "integer", "3");
            yield return Single("floatToString", "float", 2.5, "string", "2.5");
            yield return Single("stringToInt", "string", "42", "integer", "42");
            yield return Single("stringToFloat", "string", "2.5", "float", "2.5");
            yield return Single("lengthString", "string", "Hello", "integer", "5");
            yield return Single("toUpperCase", "string", "Hello", "string", "HELLO");
            yield return Single("toLowerCase", "string", "Hello", "string", "hello");
            yield return Single("enumToString", "enum", "Rival", "string", "Rival");
        }

        private static string SingleInputToken(string type) =>
            type == "boolean" ? "Bool" : type == "integer" ? "Int" : type == "float" ? "Float" : type == "string" ? "String" : "Enum";

        // Start -> Set (Out) -> line Done. The Set's value pin is wired from the node under test, and that node's
        // input from Get (In), each wire on the handles the editor writes.
        private static void SingleInputReadsItsPin(SingleInput single)
        {
            var variables = new JArray(
                new JObject { ["id"] = "in", ["name"] = "In", ["type"] = single.In, ["value"] = single.Value },
                new JObject { ["id"] = "out", ["name"] = "Out", ["type"] = single.Out,
                    ["value"] = single.Out == "boolean" ? false : single.Out == "string" ? (JToken)"" : 0 });
            if (single.In == "enum") variables[0]["enumValues"] = new JArray("Friend", "Rival", "Lover");
            var nodes = "[{'id':'set','type':'set" + SingleInputToken(single.Out) + "','variable':'out'},{'id':'node','type':'" + single.Type + "'}," +
                "{'id':'get','type':'get" + SingleInputToken(single.In) + "','variable':'in'},{'id':'Done','type':'dialogue','choices':[]}]";
            var edges = new[] { RegressionEdge("0", "set"), RegressionEdge("set", "Done", "1", ""),
                RegressionEdge("get", "node", single.In + "-", single.In + "-"), RegressionEdge("node", "set", single.Out + "-", single.Out + "-2") };
            WithLoopStory(false, new Dictionary<string, JObject> { ["main.json"] = LoopScript(nodes, edges, variables.ToString()) }, (c, errors) =>
            {
                AssertEqual("Done", c.GetCurrentDialogue()?.NodeId, "the walk reaches the line");
                AssertEqual(single.Expected, c.GetContext().LocalVariables["out"].Value.ToString(), single.Type + " result");
                AssertEqual(0, errors.Count, "no error");
            });
        }

        // The merchant and inn scripts of the sample game: Not reads the merchant's Visited Before variable and feeds
        // a Branch (First or Return) and the condition of the Ask option on line A. The script runs Start -> A -> Branch.
        private static void SingleInputNotOnCharacter(bool visited)
        {
            var main = LoopScript(@"[{'id':'visited','type':'getCharacterVar','characterPath':'characters\\npc_merchant.sfc','variable':'visitedBefore','variableType':'boolean'},
                {'id':'not','type':'notBool'},{'id':'A','type':'dialogue','choices':[{'id':'ask','text':'ask'},{'id':'leave','text':'leave'}]},
                {'id':'gate','type':'branch'},{'id':'First','type':'dialogue','choices':[]},{'id':'Return','type':'dialogue','choices':[]}]",
                new[] { RegressionEdge("0", "A", "", ""), RegressionEdge("A", "gate", "leave"), RegressionEdge("gate", "First", "true", ""), RegressionEdge("gate", "Return", "false", ""),
                    RegressionEdge("visited", "not", "boolean-output", "boolean-"), RegressionEdge("not", "gate", "boolean-", "boolean-condition"),
                    RegressionEdge("not", "A", "boolean-", "boolean-ask") });
            var story = new Dictionary<string, JObject> { ["main.json"] = main, ["characters.json"] = JObject.Parse(@"{'characters':{'characters\\npc_merchant.sfc':{
                'name':'Merchant','image':'','variables':{'var_3bba7e84':{'id':'var_3bba7e84','name':'visitedBefore','type':'boolean','value':" +
                (visited ? "true" : "false") + "}}}},'strings':{'en':{}},'assets':{}}") };
            WithLoopStory(false, story, (c, errors) =>
            {
                AssertEqual(visited ? "leave" : "ask,leave", string.Join(",", c.GetCurrentDialogue().Options.Select(option => option.Id)), "options shown on A");
                c.SelectOption("leave");
                AssertEqual(visited ? "Return" : "First", c.GetCurrentDialogue().NodeId, "the line the Branch leads to");
                AssertEqual(0, errors.Count, "no error");
            });
        }

        // FindInputEdge over one node's wires, each on the handle target-n-<pin>. Answers the pin of the wire found.
        private static string SingleInputFound(string request, params string[] pins)
        {
            var script = ScriptableObject.CreateInstance<StoryFlowScriptAsset>();
            script.SetConnections(pins.Select(pin => new StoryFlowConnection {
                Id = pin, Source = "from-" + pin, Target = "n", SourceHandle = "source-from-" + pin + "-", TargetHandle = "target-n-" + pin }).ToList());
            return script.FindInputEdge("n", request)?.TargetHandle.Substring("target-n-".Length);
        }

        private static void SingleInputExactFirst()
        {
            AssertEqual("integer-", SingleInputFound("integer", "integer-"), "the pin without an option id");
            AssertEqual("integer", SingleInputFound("integer", "integer-", "integer"), "the exact handle, wired second");
        }

        private static void SingleInputNumberedPin()
        {
            AssertEqual(null, SingleInputFound("integer-1", "integer-", "integer-2", "integer-10"), "integer-1 with only other pins wired");
            AssertEqual("integer-1", SingleInputFound("integer-1", "integer-", "integer-10", "integer-1"), "integer-1 among other pins");
        }

        // The array, map and Run Script parameter pins the editor writes beside a typed one.
        private static void SingleInputTypedPinOnly()
        {
            var others = new[] { "string-array-1", "string-array-", "string-array-array", "string-param-d27117fd7b9540efa6cbf9239e75549a",
                "string-array-param-d27117fd7b9540efa6cbf9239e75549a", "map-string-integer-1", "map-string-string-map" };
            AssertEqual(null, SingleInputFound("string", others), "string with no string pin wired");
            AssertEqual(null, SingleInputFound("map", others), "map with only map pins wired");
            AssertEqual("string-", SingleInputFound("string", others.Concat(new[] { "string-" }).ToArray()), "string beside them");
        }
    }
}
