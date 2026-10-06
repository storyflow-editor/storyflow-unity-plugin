using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StoryFlow.Tests
{
    // Array, loop, flow and Run Script handlers against the StoryFlow Editor runtime, which is the reference.
    internal static partial class Program
    {
        private static void RunHandlerParityTests()
        {
            Run("Walk that dead-ends on a Branch keeps the line usable", () => ParityDeadEnd("{'id':'tail','type':'branch','value':true}", ""));
            Run("For Each whose Completed is unconnected keeps the line usable", ParityDeadEndAfterLoop);
            Run("Option with nothing connected keeps the line usable", ParityUnconnectedOption);
            Run("Set Array with nothing wired to its array input keeps the variable", ParitySetArrayUnwired);
            Run("Wired Set Array copies the source array", ParitySetArrayCopies);
            Run("Set Array Element writes the element back to the variable", ParitySetArrayElement);
            Run("For Each iterates the array as it was at loop start", ParityLoopSnapshot);
            foreach (bool map in new[] { false, true })
                Run("For Each over 600 elements completes, map " + map, () => ParityLongLoop(map));
            Run("Exit flow with an unconnected route stays in the called script", () => ParityExitFlow(false));
            Run("Exit flow with a connected route takes it", () => ParityExitFlow(true));
            Run("End inside a flow of the startup script ends the dialogue", ParityRootEndInFlow);
            Run("Run Flow without its Entry Flow raises an error and leaves no flow frame", ParityMissingEntryFlow);
            Run("Run Script into a script whose Start is unconnected raises an error", ParityUnconnectedStart);
            Run("Unwired Run Script parameters pass type defaults and leave a map alone", ParityUnwiredParameters);
            Run("Diagnostics use the editor wording", ParityDiagnostics);
            Run("Global variable node without the scope flag does not reach the global", ParityStrictScope);
            Run("Run Script returning to an unconnected output keeps a usable line", ParityReturnToUnconnectedOutput);
            foreach (bool global in new[] { true, false })
                Run("Local and global sharing an id resolve by the node scope flag, global " + global, () => ParityScopeFlag(global));
        }

        private const string ParityLine = "{'id':'A','type':'dialogue','choices':[{'id':'go','text':'go'},{'id':'next','text':'next'},{'id':'idle','text':'idle'}]},{'id':'B','type':'dialogue','choices':[]}";
        private const string ParityNumbers = "{'id':'numbers','name':'Numbers','type':'integer','value':[1,2],'isArray':true},{'id':'other','name':'Other','type':'integer','value':[],'isArray':true}";

        // line A: Go -> `nodes` (the first is `tail`), Next -> line B, Idle -> nothing.
        private static Dictionary<string, JObject> ParityStory(string nodes, IEnumerable<JObject> edges, string variables = ParityNumbers, string flows = null) =>
            new Dictionary<string, JObject> { ["main.json"] = LoopScript("[" + ParityLine + (nodes == "" ? "" : "," + nodes) + "]",
                new[] { RegressionEdge("0", "A"), RegressionEdge("A", "B", "next") }.Concat(nodes == "" ? new JObject[0] : new[] { RegressionEdge("A", "tail", "go") }).Concat(edges),
                "[" + variables + "]", flows) };

        private static string ParityInts(StoryFlowComponent c, string name) =>
            string.Join(",", c.GetContext().FindVariableByName(name, true, false).Value.ArrayValue.Select(value => value.GetInt()));

        private static void ParityDeadEnd(string nodes, string unused)
        {
            WithLoopStory(false, ParityStory(nodes, new JObject[0]), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual("main.json:A waiting log=[] done=0 loops=0 errors=0", LoopState(c, errors), "the line still takes input");
                c.SelectOption("next");
                AssertEqual("main.json:B waiting log=[] done=0 loops=0 errors=0", LoopState(c, errors), "Next leaves the line");
            });
        }

        private static void ParityDeadEndAfterLoop()
        {
            var story = LoopLineStory(Tail("Set Int", "{'type':'setInt','variable':'mark','value':1}"), false);
            foreach (var edge in ((JArray)story["main.json"]["connections"]).ToList())
                if ((string)edge["sourceHandle"] == "source-loop-completed") edge.Remove();
            WithLoopStory(false, story, (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual("main.json:A waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "the line still takes input");
                c.SelectOption("next");
                AssertEqual("main.json:B waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "Next leaves the line");
            });
        }

        private static void ParityUnconnectedOption()
        {
            WithLoopStory(false, ParityStory("", new JObject[0]), (c, errors) =>
            {
                c.SelectOption("idle");
                AssertEqual("main.json:A waiting log=[] done=0 loops=0 errors=0", LoopState(c, errors), "the line still takes input");
            });
        }

        private static void ParitySetArrayUnwired()
        {
            WithLoopStory(false, ParityStory("{'id':'tail','type':'setIntArray','variable':'numbers'}", new JObject[0]), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual("1,2", ParityInts(c, "Numbers"), "the variable keeps its value");
            });
        }

        // Set Array(Other = Numbers) -> Add To Array(Other, 7): the source array must not grow.
        private static void ParitySetArrayCopies()
        {
            WithLoopStory(false, ParityStory(@"{'id':'tail','type':'setIntArray','variable':'other'},{'id':'source','type':'getIntArray','variable':'numbers'},
                {'id':'target','type':'getIntArray','variable':'other'},{'id':'add','type':'addToIntArray','value':7}",
                new[] { RegressionEdge("source", "tail", "integer-array-", "integer-array-2"), RegressionEdge("tail", "add", "1"),
                    RegressionEdge("target", "add", "integer-array-", "integer-array-2") }), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual("1,2,7", ParityInts(c, "Other"), "the copy grows");
                AssertEqual("1,2", ParityInts(c, "Numbers"), "the source array is untouched");
            });
        }

        private static void ParitySetArrayElement()
        {
            WithLoopStory(false, ParityStory(@"{'id':'tail','type':'setIntArrayElement','value1':1,'value2':9},{'id':'source','type':'getIntArray','variable':'numbers'}",
                new[] { RegressionEdge("source", "tail", "integer-array-", "integer-array-2") }), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual("1,9", ParityInts(c, "Numbers"), "the element is written back");
            });
        }

        // For Each(Numbers) -> Add To Array(Numbers, 5), Completed -> Set Int(Done = 1).
        private static void ParityLoopSnapshot()
        {
            WithLoopStory(false, ParityStory(@"{'id':'tail','type':'forEachIntLoop'},{'id':'source','type':'getIntArray','variable':'numbers'},
                {'id':'add','type':'addToIntArray','value':5},{'id':'done','type':'setInt','variable':'done','value':1,'isGlobal':true}",
                new[] { RegressionEdge("source", "tail", "integer-array-", "integer-array-array"), RegressionEdge("tail", "add", "loopBody"),
                    RegressionEdge("tail", "done", "completed"), RegressionEdge("source", "add", "integer-array-", "integer-array-2") }), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual("1,2,5,5", ParityInts(c, "Numbers"), "one append per element the loop started with");
                AssertEqual("main.json:A waiting log=[] done=1 loops=0 errors=0", LoopState(c, errors), "Completed runs");
            });
        }

        // For Each over 600 elements -> Add To Array(Log, element), Completed -> Set Int(Done = 1).
        private static void ParityLongLoop(bool map)
        {
            var values = Enumerable.Range(1, 600).ToList();
            string variable = map
                ? "{'id':'many','name':'Many','type':'map','keyType':'integer','valueType':'integer','value':[" + string.Join(",", values.Select(v => "{'key':" + v + ",'value':" + v + "}")) + "]}"
                : "{'id':'many','name':'Many','type':'integer','isArray':true,'value':[" + string.Join(",", values) + "]}";
            string nodes = (map ? "{'id':'tail','type':'forEachMap','keyType':'integer','valueType':'integer'},{'id':'source','type':'getMap','variable':'many','keyType':'integer','valueType':'integer'},"
                : "{'id':'tail','type':'forEachIntLoop'},{'id':'source','type':'getIntArray','variable':'many'},") + LoopLogNodes +
                ",{'id':'done','type':'setInt','variable':'done','value':1,'isGlobal':true}";
            var edges = new[] { map ? RegressionEdge("source", "tail", "map-integer-integer-", "map-integer-integer-map") : RegressionEdge("source", "tail", "integer-array-", "integer-array-array"),
                RegressionEdge("tail", "log", "loopBody"), RegressionEdge("tail", "done", "completed") }.Concat(LoopLogEdges("tail", map ? "integer-value" : "integer-element"));
            WithLoopStory(false, ParityStory(nodes, edges, variable), (c, errors) =>
            {
                c.SelectOption("go");
                var log = c.GetContext().FindVariableByName("Log", false, true).Value.ArrayValue;
                AssertEqual(600, log.Count, "every element ran"); AssertEqual(600, log[log.Count - 1].GetInt(), "in order");
                AssertEqual(1, c.GetIntVariable("Done", true), "Completed ran"); AssertEqual(0, errors.Count, "no error");
                AssertTrue(c.IsDialogueActive() && c.IsWaitingForInput(), "the line still takes input");
            });
        }

        // main: A -> Run Script(sub): output -> B, exit route X -> C when connected. sub: Visit -> Run Flow(exit X).
        private static void ParityExitFlow(bool connected)
        {
            var edges = new List<JObject> { RegressionEdge("0", "A"), RegressionEdge("A", "call"), RegressionEdge("call", "B", "output") };
            if (connected) edges.Add(RegressionEdge("call", "C", "exit-x"));
            var scripts = new Dictionary<string, JObject> {
                ["main.json"] = LoopScript(@"[{'id':'A','type':'dialogue','choices':[]},{'id':'call','type':'runScript','script':'sub.json'},
                    {'id':'B','type':'dialogue','choices':[]},{'id':'C','type':'dialogue','choices':[]}]", edges),
                ["sub.json"] = LoopScript("[{'id':'Visit','type':'dialogue','choices':[]},{'id':'leave','type':'runFlow','flowId':'x'}]",
                    new[] { RegressionEdge("0", "Visit"), RegressionEdge("Visit", "leave") }, "[]", "[{'id':'x','name':'X','isExit':true}]") };
            WithLoopStory(false, scripts, (c, errors) =>
            {
                c.AdvanceDialogue(); c.AdvanceDialogue();
                AssertEqual(connected ? "main.json:C" : "sub.json:Visit", LoopAt(c), "where the exit flow leads");
                AssertEqual(connected ? 0 : 1, c.GetContext().CallStackDepth, "call depth");
                AssertTrue(c.IsWaitingForInput(), "the line takes input"); AssertEqual(0, errors.Count, "no error");
            });
        }

        // A -> Run Flow(side) -> Entry Flow(side) -> Run Flow(inner) -> Entry Flow(inner) -> End.
        private static void ParityRootEndInFlow()
        {
            WithLoopStory(false, ParityStory(@"{'id':'tail','type':'runFlow','flowId':'side'},{'id':'entry','type':'entryFlow','flowId':'side'},
                {'id':'deeper','type':'runFlow','flowId':'inner'},{'id':'entry2','type':'entryFlow','flowId':'inner'},{'id':'end','type':'end'}",
                new[] { RegressionEdge("entry", "deeper"), RegressionEdge("entry2", "end") }, ParityNumbers,
                "[{'id':'side','name':'Side'},{'id':'inner','name':'Inner'}]"), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual(false, c.IsDialogueActive(), "the dialogue ended"); AssertEqual(0, errors.Count, "no error");
            });
        }

        private static void ParityMissingEntryFlow()
        {
            WithLoopStory(false, ParityStory("{'id':'tail','type':'runFlow','flowId':'gone'}", new JObject[0]), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual(1, errors.Count, "error raised"); AssertEqual(0, c.GetContext().FlowStackDepth, "no flow frame left");
            });
        }

        // The exporter derives a variable id from its name alone, so a local and a global with one name share an
        // id and the node's isGlobal flag is what tells them apart (absent means local).
        // Go -> Set Int(Done = 5) -> Add To Array(Log, 3) -> Set Int(global Seen = Done), every Done and Log in the scope under test.
        private static void ParityScopeFlag(bool global)
        {
            string flag = global ? ",'isGlobal':true" : "";
            var story = ParityStory("{'id':'tail','type':'setInt','variable':'done','value':5" + flag + "},{'id':'list','type':'getIntArray','variable':'log'" + flag + @"},
                {'id':'add','type':'addToIntArray','value':3},{'id':'read','type':'getInt','variable':'done'" + flag + @"},
                {'id':'copy','type':'setInt','variable':'seen','isGlobal':true}",
                new[] { RegressionEdge("tail", "add", "1"), RegressionEdge("list", "add", "integer-array-", "integer-array-2"), RegressionEdge("add", "copy", "1"),
                    RegressionEdge("read", "copy", "integer-", "integer-2") },
                "{'id':'done','name':'Done','type':'integer','value':100},{'id':'log','name':'Log','type':'integer','value':[9],'isArray':true}");
            WithLoopStory(false, story, (c, errors) =>
            {
                var flags = new List<bool>();
                c.OnVariableChanged += (variable, isGlobal) => { if (variable.Name == "Done") flags.Add(isGlobal); };
                c.SelectOption("go");
                var context = c.GetContext();
                Func<Dictionary<string, StoryFlow.Data.StoryFlowVariable>, string> state = variables =>
                    variables["done"].Value.GetInt() + " [" + string.Join(",", variables["log"].Value.ArrayValue.Select(value => value.GetInt())) + "]";
                AssertEqual(global ? "5 [3]" : "0 []", state(context.GlobalVariables), "global Done and Log");
                AssertEqual(global ? "100 [9]" : "5 [9,3]", state(context.LocalVariables), "local Done and Log");
                AssertEqual(5, context.GlobalVariables["seen"].Value.GetInt(), "Get Int reads the same scope");
                AssertEqual(global.ToString(), string.Join(",", flags), "the change is reported for that scope");
            });
        }

        // main: A -> Run Script(sub) with every parameter unwired. sub: Visit, with a non-default value on each input.
        private static void ParityUnwiredParameters()
        {
            var types = new[] { "boolean", "integer", "float", "string", "enum", "image", "audio", "character", "dataAsset" };
            var parameters = new JArray(); var variables = new JArray();
            foreach (var type in types)
                foreach (bool array in new[] { false, true })
                {
                    string name = type + (array ? "List" : "");
                    parameters.Add(new JObject { ["id"] = "p_" + name, ["name"] = name, ["type"] = type, ["isArray"] = array });
                    JToken value = type == "boolean" ? true : type == "integer" ? 5 : type == "float" ? 1.5 : (JToken)"set";
                    var variable = new JObject { ["id"] = "v_" + name, ["name"] = name, ["type"] = type, ["value"] = array ? new JArray(value) : value, ["isInput"] = true };
                    if (array) variable["isArray"] = true;
                    if (type == "enum") variable["enumValues"] = new JArray("set");
                    variables.Add(variable);
                }
            parameters.Add(JObject.Parse("{'id':'p_map','name':'pairs','type':'map','keyType':'string','valueType':'integer'}"));
            variables.Add(JObject.Parse("{'id':'v_map','name':'pairs','type':'map','keyType':'string','valueType':'integer','value':[{'key':'a','value':1}],'isInput':true}"));
            var call = new JObject { ["id"] = "call", ["type"] = "runScript", ["script"] = "sub.json", ["scriptInterface"] = new JObject { ["parameters"] = parameters, ["outputs"] = new JArray() } };
            var scripts = new Dictionary<string, JObject> {
                ["main.json"] = LoopScript("[{'id':'A','type':'dialogue','choices':[]}," + call + "]", new[] { RegressionEdge("0", "A"), RegressionEdge("A", "call") }),
                ["sub.json"] = LoopScript("[{'id':'Visit','type':'dialogue','choices':[]}]", new[] { RegressionEdge("0", "Visit") }, variables.ToString()) };
            WithLoopStory(false, scripts, (c, errors) =>
            {
                c.AdvanceDialogue();
                AssertEqual("sub.json:Visit", LoopAt(c), "the script was called");
                var locals = c.GetContext().LocalVariables;
                foreach (var type in types)
                {
                    var value = locals["v_" + type].Value;
                    string text = type == "boolean" ? value.GetBool().ToString() : type == "integer" ? value.GetInt().ToString() : type == "float" ? value.GetFloat().ToString()
                        : type == "enum" ? value.GetEnum() : value.GetString();
                    AssertEqual(type == "boolean" ? "False" : type == "integer" || type == "float" ? "0" : "", text, "unwired " + type + " parameter");
                    AssertEqual(0, locals["v_" + type + "List"].Value.ArrayValue?.Count ?? -1, "unwired " + type + " array parameter");
                }
                AssertEqual(1, locals["v_map"].Value.GetMap().Count, "unwired map parameter keeps the declared map"); AssertEqual(0, errors.Count, "no error");
            });
        }

        private static void ParityDiagnostics()
        {
            Action<string, string, string, string> check = (nodes, flows, expected, what) =>
            {
                var story = ParityStory(nodes, new JObject[0], ParityNumbers, flows);
                story["blank.json"] = LoopScript("[{'id':'end','type':'end'}]", new JObject[0]);
                WithLoopStory(false, story, (c, errors) => { c.SelectOption("go"); AssertEqual(expected, string.Join(" | ", errors), what); });
            };
            check("{'id':'tail','type':'runScript'}", null, "", "Run Script with no script only warns");
            check("{'id':'tail','type':'runFlow'}", null, "", "Run Flow with no flow only warns");
            check("{'id':'tail','type':'runScript','script':'missing.json'}", null, "Script not found: missing.json", "script not found");
            check("{'id':'tail','type':'runScript','script':'blank.json'}", null, "Script's Start node is not connected", "called script with an unconnected Start");
            check("{'id':'tail','type':'runFlow','flowId':'gone'}", "[{'id':'gone','name':'Side Quest'}]", "Flow \"Side Quest\" not found", "flow without its Entry Flow");
            var blank = new Dictionary<string, JObject> { ["main.json"] = LoopScript("[{'id':'A','type':'dialogue','choices':[]}]", new JObject[0]) };
            WithLoopStory(false, blank, (c, errors) => AssertEqual("Start node is not connected", string.Join(" | ", errors), "started script with an unconnected Start"));
        }

        // Every export writes isGlobal on a node bound to a global, so a node without it means the local scope only.
        private static void ParityStrictScope()
        {
            WithLoopStory(false, ParityStory("{'id':'tail','type':'setInt','variable':'done','value':5}", new JObject[0]), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual(0, c.GetContext().GlobalVariables["done"].Value.GetInt(), "the global is not written");
            });
        }

        // main: A -> Run Script(sub), output unconnected. sub: Visit -> End. The caller's last line comes back,
        // because the called script's line would no longer take input here.
        private static void ParityReturnToUnconnectedOutput()
        {
            var scripts = new Dictionary<string, JObject> {
                ["main.json"] = LoopScript("[{'id':'A','type':'dialogue','choices':[]},{'id':'call','type':'runScript','script':'sub.json'}]", new[] { RegressionEdge("0", "A"), RegressionEdge("A", "call") }),
                ["sub.json"] = LoopVisitScript() };
            WithLoopStory(false, scripts, (c, errors) =>
            {
                c.AdvanceDialogue(); c.AdvanceDialogue();
                AssertEqual("main.json:A", LoopAt(c), "the caller line is shown"); AssertTrue(c.IsWaitingForInput(), "and takes input");
            });
        }

        private static void ParityUnconnectedStart()
        {
            var story = ParityStory("{'id':'tail','type':'runScript','script':'blank.json'}", new JObject[0]);
            story["blank.json"] = LoopScript("[{'id':'end','type':'end'}]", new JObject[0]);
            WithLoopStory(false, story, (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual(1, errors.Count, "error raised");
            });
        }
    }
}
