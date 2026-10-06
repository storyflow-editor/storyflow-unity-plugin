using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StoryFlow.Editor;
using UnityEditor;

namespace StoryFlow.Tests
{
    // For Each parity with the StoryFlow Editor runtime. A loop body runs until a node has nothing
    // connected after it, then the loop moves on to its next element and finally takes Completed. A
    // called script that returns from inside its own loop abandons that loop, a loop parked in a
    // caller keeps its position, and no loop position survives a stop, load, reset or restart.
    internal static partial class Program
    {
        private static void RunLoopParityTests()
        {
            foreach (var tail in LoopTails())
                foreach (bool rollback in new[] { false, true })
                    Run("For Each body ends on " + tail.Name + ", rollback " + rollback, () => LoopBodyEndsOn(tail, rollback));
            foreach (var tail in LoopTails())
                Run("For Each body in a called script ends on " + tail.Name, () => LoopBodyInCalledScriptEndsOn(tail));
            foreach (bool rollback in new[] { false, true })
            {
                Run("For Each with nothing connected to Loop Body reaches Completed, rollback " + rollback, () => LoopEmptyBody(rollback));
                Run("Dead end in a called script leaves the caller loop parked, rollback " + rollback, () => LoopDeadEndInCalledScript(rollback));
                Run("Called script that ends inside its loop starts it afresh, rollback " + rollback, () => LoopLeftByEnd(rollback));
                Run("Caller loop keeps its position while the called loop restarts, rollback " + rollback, () => LoopParkedInCaller(rollback));
                Run("Script calling itself from its loop keeps the caller position, rollback " + rollback, () => LoopSelfCall(rollback));
                Run("Stop, Load and Start while a loop is parked plays every element, rollback " + rollback, () => LoopAfterLoad(rollback));
                Run("Stop, Load and Start after a walk stopped in a loop body starts at the first element, rollback " + rollback, () => LoopAfterStoppedWalk(rollback));
                foreach (string reset in new[] { "ResetAllState", "ResetVariables" })
                    Run(reset + " from a called script keeps the caller loop running, rollback " + rollback, () => LoopAfterReset(reset, rollback));
                Run("Restart from a called script plays every element, rollback " + rollback, () => LoopAfterRestart(rollback));
                Run("Save inside a called script holds no script position, rollback " + rollback, () => LoopSaveInsideCalledScript(rollback));
            }
            foreach (var tail in LoopTails())
                Run("Back across a For Each whose body ends on " + tail.Name, () => LoopBackAcross(tail));
        }

        // The node a loop body ends on, with nothing connected to the output it takes.
        private sealed class LoopTail
        {
            public string Name, Node, Nodes, Flows;
            public JObject[] Edges;
        }

        private static LoopTail Tail(string name, string node, string nodes = null, string flows = null, params JObject[] edges) =>
            new LoopTail { Name = name, Node = node, Nodes = nodes, Flows = flows, Edges = edges };

        // Node type token, element type and label of each Set Array node.
        private static readonly string[][] LoopArrayTypes =
        {
            new[] { "Bool", "boolean", "Bool" }, new[] { "Int", "integer", "Int" }, new[] { "Float", "float", "Float" }, new[] { "String", "string", "String" },
            new[] { "Image", "image", "Image" }, new[] { "Character", "character", "Character" }, new[] { "DataAssetRef", "dataAsset", "Data Asset" },
            new[] { "Audio", "audio", "Audio" },
        };

        private static IEnumerable<LoopTail> LoopTails()
        {
            // The first group is the list the StoryFlow Editor runtime tests pin (loop-body-unconnected-tail.test.ts).
            yield return Tail("Set Int", "{'type':'setInt','variable':'mark','value':1}");
            yield return Tail("Set Background Image", "{'type':'setBackgroundImage'}");
            yield return Tail("Play Audio", "{'type':'playAudio'}");
            // Each Set Array writes its own scratch array, not the array the loop reads: with nothing wired
            // to its array input this runtime empties the variable, which would shorten the loop's next run.
            foreach (var type in LoopArrayTypes)
                yield return Tail("Set " + type[2] + " Array", "{'type':'set" + type[0] + "Array','variable':'scratch" + type[0] + "'}");
            yield return Tail("Random Branch whose selected output is unconnected", "{'type':'randomBranch','options':[{'id':'only','weight':1}]}");
            yield return Tail("Switch On Enum with no output for the value", "{'type':'switchOnEnum','variable':'mode','enumValues':['x']}");
            yield return Tail("Run Script whose output is unconnected", "{'type':'runScript','script':'empty.json'}");

            // Every other handler that can end a body.
            yield return Tail("Branch whose taken output is unconnected", "{'type':'branch','value':true}");
            yield return Tail("Block Rollback", "{'type':'blockRollback'}");
            yield return Tail("Run Flow into an Entry Flow with nothing after it", "{'type':'runFlow','flowId':'side'}",
                "[{'id':'entry','type':'entryFlow','flowId':'side'}]", "[{'id':'side','name':'Side'}]");
            yield return Tail("Set Bool", "{'type':'setBool','variable':'flag','value':true}");
            yield return Tail("Set Float", "{'type':'setFloat','variable':'ratio','value':1.5}");
            yield return Tail("Set String", "{'type':'setString','variable':'label','value':'x'}");
            yield return Tail("Set Enum", "{'type':'setEnum','variable':'mode','value':'x'}");
            yield return Tail("Set Image", "{'type':'setImage','variable':'picture'}");
            yield return Tail("Set Audio", "{'type':'setAudio','variable':'sound'}");
            yield return Tail("Set Character", "{'type':'setCharacter','variable':'who'}");
            yield return Tail("Set Data Asset reference", "{'type':'setDataAssetRef','variable':'asset'}");
            yield return Tail("Set Character Variable", "{'type':'setCharacterVar'}");
            yield return Tail("Set Data Asset Variable", "{'type':'setDataAssetVariable'}");
            yield return Tail("Set Int Array Element", "{'type':'setIntArrayElement'}");
            yield return Tail("Add To Int Array", "{'type':'addToIntArray'}");
            yield return Tail("Remove From Int Array", "{'type':'removeFromIntArray'}");
            yield return Tail("Clear Int Array", "{'type':'clearIntArray'}");
            yield return Tail("Set Map", "{'type':'setMap','variable':'scratchMap','keyType':'string','valueType':'integer'}");
            foreach (var type in new[] { new[] { "Set Map Value", "setMapValue" }, new[] { "Remove Map Key", "removeMapKey" }, new[] { "Clear Map", "clearMap" } })
                yield return Tail(type[0], "{'type':'" + type[1] + "','keyType':'string','valueType':'integer','key':'k','value':1}");
            yield return Tail("inner For Each whose Completed is unconnected", "{'type':'forEachIntLoop'}",
                "[{'id':'innerArray','type':'getIntArray','variable':'numbers'},{'id':'innerBody','type':'setInt','variable':'mark','value':1}]", null,
                RegressionEdge("innerArray", "tail", "integer-array-", "integer-array-array"), RegressionEdge("tail", "innerBody", "loopBody"));
            yield return Tail("inner For Each over a map whose Completed is unconnected", "{'type':'forEachMap','keyType':'string','valueType':'integer'}",
                "[{'id':'innerMap','type':'getMap','variable':'pairs','keyType':'string','valueType':'integer'},{'id':'innerBody','type':'setInt','variable':'mark','value':1}]", null,
                RegressionEdge("innerMap", "tail", "map-string-integer-", "map-string-integer-map"), RegressionEdge("tail", "innerBody", "loopBody"));
            yield return Tail("For Each over a map without key and value types", "{'type':'forEachMap'}");
        }

        private const string LoopLocals = @"[
            {'id':'numbers','name':'Numbers','type':'integer','value':[1,2],'isArray':true},
            {'id':'pairs','name':'Pairs','type':'map','value':[{'key':'a','value':1},{'key':'b','value':2}],'keyType':'string','valueType':'integer'},
            {'id':'mark','name':'Mark','type':'integer','value':0}, {'id':'flag','name':'Flag','type':'boolean','value':false},
            {'id':'ratio','name':'Ratio','type':'float','value':0}, {'id':'label','name':'Label','type':'string','value':''},
            {'id':'mode','name':'Mode','type':'enum','value':'x','enumValues':['x']}, {'id':'picture','name':'Picture','type':'image','value':''},
            {'id':'sound','name':'Sound','type':'audio','value':''}, {'id':'who','name':'Who','type':'character','value':''},
            {'id':'asset','name':'Asset','type':'dataAsset','value':''},
            {'id':'scratchMap','name':'ScratchMap','type':'map','value':[],'keyType':'string','valueType':'integer'}]";

        private const string LoopGlobals = @"{'variables':{
            'log':{'id':'log','name':'Log','type':'integer','value':[],'isArray':true},
            'done':{'id':'done','name':'Done','type':'integer','value':0},
            'last':{'id':'last','name':'Last','type':'integer','value':0},
            'seen':{'id':'seen','name':'Seen','type':'integer','value':0},
            'inner':{'id':'inner','name':'Inner','type':'boolean','value':false}},'strings':{'en':{}},'assets':{}}";

        // A script in the JSON export's shape: the Start node plus `nodes`, each carrying its own id.
        private static JObject LoopScript(string nodes, IEnumerable<JObject> edges, string variables = "[]", string flows = null)
        {
            var json = new JObject { ["startNode"] = "0", ["nodes"] = new JObject { ["0"] = JObject.Parse("{'id':'0','type':'start'}") },
                ["connections"] = new JArray(edges), ["variables"] = new JObject(), ["strings"] = new JObject { ["en"] = new JObject() }, ["assets"] = new JObject() };
            foreach (var node in JArray.Parse(nodes)) json["nodes"][(string)node["id"]] = node;
            foreach (var variable in JArray.Parse(variables)) json["variables"][(string)variable["id"]] = variable;
            if (flows != null) json["flows"] = JArray.Parse(flows);
            return json;
        }

        private static JObject LoopEndScript() => LoopScript("[{'id':'end','type':'end'}]", new[] { RegressionEdge("0", "end") });

        // Visit -> End.
        private static JObject LoopVisitScript() => LoopScript("[{'id':'Visit','type':'dialogue','choices':[]},{'id':'end','type':'end'}]",
            new[] { RegressionEdge("0", "Visit"), RegressionEdge("Visit", "end") });

        // Add To Array(Log, element of `loop`): the global Log records every element a loop body ran for.
        private const string LoopLogNodes = "{'id':'logArray','type':'getIntArray','variable':'log','isGlobal':true},{'id':'log','type':'addToIntArray'}";
        private static JObject[] LoopLogEdges(string loop, string output = "integer-element") => new[] {
            RegressionEdge("logArray", "log", "integer-array-", "integer-array-2"), RegressionEdge(loop, "log", output, "integer-3") };

        // line A, options Go and Next.
        // Go -> For Each [1, 2] -> Loop Body -> Add To Array(Log, element) -> tail, Completed -> Set Int(Done = 1) or line B.
        // Next -> line B -> line C, or End when the line sits in a called script.
        private static JObject LoopLineScript(LoopTail tail, bool mapLoop, bool completedToLine = false, bool emptyBody = false, bool nextToEnd = false)
        {
            var nodes = JArray.Parse(@"[{'id':'A','type':'dialogue','choices':[{'id':'go','text':'go'},{'id':'next','text':'next'}]},
                {'id':'B','type':'dialogue','choices':[]}, {'id':'C','type':'dialogue','choices':[]}, {'id':'end','type':'end'}, " + LoopLogNodes + @",
                {'id':'done','type':'setInt','variable':'done','value':1,'isGlobal':true}]");
            nodes.Add(JObject.Parse(mapLoop ? "{'id':'source','type':'getMap','variable':'pairs','keyType':'string','valueType':'integer'}"
                : "{'id':'source','type':'getIntArray','variable':'numbers'}"));
            nodes.Add(JObject.Parse(mapLoop ? "{'id':'loop','type':'forEachMap','keyType':'string','valueType':'integer'}" : "{'id':'loop','type':'forEachIntLoop'}"));
            var edges = new List<JObject> { RegressionEdge("0", "A"), RegressionEdge("A", "loop", "go"), RegressionEdge("A", nextToEnd ? "end" : "B", "next"), RegressionEdge("B", "C"),
                RegressionEdge("loop", completedToLine ? "B" : "done", "completed"),
                mapLoop ? RegressionEdge("source", "loop", "map-string-integer-", "map-string-integer-map") : RegressionEdge("source", "loop", "integer-array-", "integer-array-array") };
            var locals = JArray.Parse(LoopLocals);
            foreach (var type in LoopArrayTypes)
                locals.Add(new JObject { ["id"] = "scratch" + type[0], ["name"] = "Scratch" + type[0], ["type"] = type[1], ["value"] = new JArray(), ["isArray"] = true });
            if (!emptyBody)
            {
                var tailNode = JObject.Parse(tail.Node); tailNode["id"] = "tail"; nodes.Add(tailNode);
                if (tail.Nodes != null) foreach (var node in JArray.Parse(tail.Nodes)) nodes.Add(node);
                edges.Add(RegressionEdge("loop", "log", "loopBody")); edges.Add(RegressionEdge("log", "tail", "1"));
                edges.AddRange(LoopLogEdges("loop", mapLoop ? "integer-value" : "integer-element")); edges.AddRange(tail.Edges);
            }
            return LoopScript(nodes.ToString(), edges, locals.ToString(), tail?.Flows);
        }

        // The line in the startup script.
        private static Dictionary<string, JObject> LoopLineStory(LoopTail tail, bool mapLoop, bool completedToLine = false, bool emptyBody = false) =>
            new Dictionary<string, JObject> { ["main.json"] = LoopLineScript(tail, mapLoop, completedToLine, emptyBody), ["empty.json"] = LoopEndScript() };

        // The line in a script called from a For Each body: main: For Each [1, 2, 3] -> Run Script(sub) -> Set Int.
        private static Dictionary<string, JObject> LoopLineInCalledScript(LoopTail tail) => new Dictionary<string, JObject> {
            ["main.json"] = LoopScript(@"[{'id':'array','type':'getIntArray','variable':'outerNumbers'},{'id':'outer','type':'forEachIntLoop'},
                {'id':'call','type':'runScript','script':'sub.json'},{'id':'after','type':'setInt','variable':'step','value':1}]",
                new[] { RegressionEdge("0", "outer"), RegressionEdge("outer", "call", "loopBody"), RegressionEdge("call", "after", "output"),
                    RegressionEdge("array", "outer", "integer-array-", "integer-array-array") },
                "[{'id':'outerNumbers','name':'Outer','type':'integer','value':[1,2,3],'isArray':true},{'id':'step','name':'Step','type':'integer','value':0}]"),
            ["sub.json"] = LoopLineScript(tail, false, nextToEnd: true), ["empty.json"] = LoopEndScript() };

        // main: A -> Run Script(chapter) -> B -> Run Script(chapter) -> C. `chapter` is the script under test.
        private static Dictionary<string, JObject> LoopStoryAround(JObject chapter) => new Dictionary<string, JObject> {
            ["main.json"] = LoopScript(@"[{'id':'A','type':'dialogue','choices':[]},{'id':'first','type':'runScript','script':'chapter.json'},
                {'id':'B','type':'dialogue','choices':[]},{'id':'second','type':'runScript','script':'chapter.json'},{'id':'C','type':'dialogue','choices':[]}]",
                new[] { RegressionEdge("0", "A"), RegressionEdge("A", "first"), RegressionEdge("first", "B", "output"), RegressionEdge("B", "second"), RegressionEdge("second", "C", "output") }),
            ["chapter.json"] = chapter, ["sub.json"] = LoopVisitScript() };

        // chapter: For Each [1, 2, 3] -> Run Script(sub) -> Set Int, Completed -> End. sub: Visit -> End.
        // One Visit line per iteration, so the lines shown count the iterations that ran.
        private static Dictionary<string, JObject> LoopVisitingChapter() => LoopStoryAround(LoopScript(
            @"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'},{'id':'call','type':'runScript','script':'sub.json'},
                {'id':'after','type':'setInt','variable':'visited','value':1},{'id':'end','type':'end'}]",
            new[] { RegressionEdge("0", "loop"), RegressionEdge("loop", "call", "loopBody"), RegressionEdge("loop", "end", "completed"), RegressionEdge("call", "after", "output"),
                RegressionEdge("array", "loop", "integer-array-", "integer-array-array") },
            "[{'id':'numbers','name':'Numbers','type':'integer','value':[1,2,3],'isArray':true},{'id':'visited','name':'Visited','type':'integer','value':0}]"));

        // scan: For Each [1, 2, 3] -> log the element -> Branch(Stop): false -> Set Bool(Stop = true), true -> End.
        // The first element arms Stop, the second returns from inside the loop body.
        private static JObject LoopScanScript() => LoopScript(
            @"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'}," + LoopLogNodes + @",
                {'id':'stop','type':'getBool','variable':'stop'},{'id':'gate','type':'branch'},{'id':'arm','type':'setBool','variable':'stop','value':true},{'id':'end','type':'end'}]",
            new[] { RegressionEdge("0", "loop"), RegressionEdge("loop", "log", "loopBody"), RegressionEdge("log", "gate", "1"), RegressionEdge("gate", "arm", "false"),
                RegressionEdge("gate", "end", "true"), RegressionEdge("array", "loop", "integer-array-", "integer-array-array"),
                RegressionEdge("stop", "gate", "boolean-", "boolean-condition") }.Concat(LoopLogEdges("loop")),
            "[{'id':'numbers','name':'Numbers','type':'integer','value':[1,2,3],'isArray':true},{'id':'stop','name':'Stop','type':'boolean','value':false}]");

        private static void WithLoopStory(bool rollback, Dictionary<string, JObject> scripts, Action<StoryFlowComponent, List<string>> test)
        {
            var cwd = Directory.GetCurrentDirectory();
            var root = Path.Combine(Path.GetTempPath(), "storyflow-unity-loop-parity-" + Guid.NewGuid().ToString("N"));
            var errors = new List<string>();
            StoryFlowComponent component = null;
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "Assets"));
                Directory.CreateDirectory(Path.Combine(root, "build"));
                File.WriteAllText(Path.Combine(root, "build", "project.json"), "{\"version\":\"1\",\"metadata\":{\"title\":\"Loops\",\"dialogueRollback\":" +
                    "{\"version\":1,\"enabled\":" + (rollback ? "true" : "false") + ",\"historyLimit\":100}},\"startupScript\":\"main.json\"}");
                File.WriteAllText(Path.Combine(root, "build", "global-variables.json"), JObject.Parse(LoopGlobals).ToString());
                foreach (var script in scripts) File.WriteAllText(Path.Combine(root, "build", script.Key), script.Value.ToString());
                Directory.SetCurrentDirectory(root); EditorStubs.Reset();
                var project = StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Loops", out var report);
                AssertTrue(project != null && !report.HasFailures, "loop story imports");
                SetManagerProject(project);
                component = new StoryFlowComponent { UIStyle = BuiltInUIStyle.None, TraceEnabled = false };
                component.OnError += errors.Add;
                component.StartDialogue(); test(component, errors);
            }
            finally { component?.StopDialogue(); ClearManager(); Directory.SetCurrentDirectory(cwd); TryDeleteDirectory(root); }
        }

        private static string LoopAt(StoryFlowComponent c) => c.GetContext().CurrentScript.ScriptPath + ":" + c.GetCurrentDialogue().NodeId;

        // Where the story stands: the script and line, whether the line takes input, what the loop bodies logged,
        // the Completed marker, the loops still open and the errors raised.
        private static string LoopState(StoryFlowComponent c, List<string> errors)
        {
            var log = c.GetContext().FindVariableByName("Log", false, true).Value.ArrayValue;
            return LoopAt(c) + (c.IsWaitingForInput() ? " waiting" : " stalled") + " log=[" + string.Join(",", log.Select(value => value.GetInt())) + "]" +
                " done=" + c.GetIntVariable("Done", true) + " loops=" + c.GetContext().LoopStackDepth + " errors=" + errors.Count;
        }

        // Advances through the Visit lines and returns how many were shown before the story left `sub`.
        private static int LoopVisits(StoryFlowComponent c)
        {
            int shown = 0;
            while (c.IsWaitingForInput() && LoopAt(c) == "sub.json:Visit" && shown < 10) { shown++; c.AdvanceDialogue(); }
            return shown;
        }

        private static void LoopBodyEndsOn(LoopTail tail, bool rollback)
        {
            foreach (bool mapLoop in new[] { false, true })
                WithLoopStory(rollback, LoopLineStory(tail, mapLoop), (c, errors) =>
                {
                    string kind = mapLoop ? "map loop, " : "array loop, ";
                    c.SelectOption("go");
                    AssertEqual("main.json:A waiting log=[1,2] done=1 loops=0 errors=0", LoopState(c, errors), kind + "Go runs every element and Completed");
                    c.SelectOption("go");
                    AssertEqual("main.json:A waiting log=[1,2,1,2] done=1 loops=0 errors=0", LoopState(c, errors), kind + "Go again runs every element again");
                    c.SelectOption("next");
                    AssertEqual("main.json:B waiting log=[1,2,1,2] done=1 loops=0 errors=0", LoopState(c, errors), kind + "Next leaves the line");
                });
        }

        // The caller's loop stays parked while the line in the called script runs its own.
        private static void LoopBodyInCalledScriptEndsOn(LoopTail tail)
        {
            foreach (bool rollback in new[] { false, true })
                WithLoopStory(rollback, LoopLineInCalledScript(tail), (c, errors) =>
                {
                    string mode = "rollback " + rollback + ", ";
                    var started = new List<string>();
                    c.OnScriptStarted += started.Add;
                    c.SelectOption("go");
                    AssertEqual("sub.json:A waiting log=[1,2] done=1 loops=0 errors=0", LoopState(c, errors), mode + "Go runs every element and Completed");
                    c.SelectOption("go");
                    AssertEqual("sub.json:A waiting log=[1,2,1,2] done=1 loops=0 errors=0", LoopState(c, errors), mode + "Go again runs every element again");
                    c.SelectOption("next");
                    // Next ends the called script, and the caller's loop calls it again for its second element.
                    AssertEqual("sub.json:A waiting log=[1,2,1,2] done=1 loops=0 errors=0", LoopState(c, errors), mode + "Next shows the line of the next call");
                    AssertEqual(1, started.Count(path => path == "sub.json"), mode + "the caller loop calls the script again");
                    AssertEqual(1, c.GetContext().CallStackDepth, mode + "one call deep");
                });
        }

        private static void LoopEmptyBody(bool rollback)
        {
            foreach (bool mapLoop in new[] { false, true })
                WithLoopStory(rollback, LoopLineStory(null, mapLoop, emptyBody: true), (c, errors) =>
                {
                    c.SelectOption("go");
                    AssertEqual("main.json:A waiting log=[] done=1 loops=0 errors=0", LoopState(c, errors), (mapLoop ? "map loop, " : "array loop, ") + "Go reaches Completed");
                    c.SelectOption("next");
                    AssertEqual("main.json:B waiting log=[] done=1 loops=0 errors=0", LoopState(c, errors), (mapLoop ? "map loop, " : "array loop, ") + "Next leaves the line");
                });
        }

        // main: For Each [1, 2, 3] -> Run Script(sub) -> Set Int. sub: Add To Array(Log, 7) -> Branch with nothing connected.
        // The walk ends in the called script, which has no loop of its own: only its return continues the caller's loop.
        private static void LoopDeadEndInCalledScript(bool rollback)
        {
            var scripts = LoopLineInCalledScript(Tail("Set Int", "{'type':'setInt','variable':'mark','value':1}"));
            scripts["sub.json"] = LoopScript(@"[{'id':'logArray','type':'getIntArray','variable':'log','isGlobal':true},
                {'id':'log','type':'addToIntArray','value':7},{'id':'gate','type':'branch','value':true}]",
                new[] { RegressionEdge("0", "log"), RegressionEdge("log", "gate", "1"), RegressionEdge("logArray", "log", "integer-array-", "integer-array-2") });
            WithLoopStory(rollback, scripts, (c, errors) =>
            {
                var log = c.GetContext().FindVariableByName("Log", false, true).Value.ArrayValue;
                AssertEqual("sub.json", c.GetContext().CurrentScript.ScriptPath, "the walk ended in the called script");
                AssertEqual("7", string.Join(",", log.Select(value => value.GetInt())), "the caller loop called it once");
                AssertEqual(1, c.GetContext().CallStackDepth, "the call has not returned"); AssertEqual(0, errors.Count, "no error");
            });
        }

        // main: A -> Run Script(scan) -> B -> Run Script(scan) -> C.
        private static void LoopLeftByEnd(bool rollback)
        {
            var scripts = LoopStoryAround(LoopScanScript());
            WithLoopStory(rollback, scripts, (c, errors) =>
            {
                c.AdvanceDialogue();
                AssertEqual("main.json:B waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "first call returns from inside its loop");
                c.AdvanceDialogue();
                AssertEqual("main.json:C waiting log=[1,2,1,2] done=0 loops=0 errors=0", LoopState(c, errors), "second call starts the loop at its first element");
            });
        }

        // main: A -> Run Script(chapter) -> B.
        // chapter: For Each [10, 20, 30] -> log the element -> Run Script(scan) -> Set Int, Completed -> End.
        private static void LoopParkedInCaller(bool rollback)
        {
            var scripts = LoopStoryAround(LoopScript(
                @"[{'id':'array','type':'getIntArray','variable':'tens'},{'id':'outer','type':'forEachIntLoop'}," + LoopLogNodes + @",
                    {'id':'call','type':'runScript','script':'scan.json'},{'id':'after','type':'setInt','variable':'visited','value':1},{'id':'end','type':'end'}]",
                new[] { RegressionEdge("0", "outer"), RegressionEdge("outer", "log", "loopBody"), RegressionEdge("log", "call", "1"), RegressionEdge("call", "after", "output"),
                    RegressionEdge("outer", "end", "completed"), RegressionEdge("array", "outer", "integer-array-", "integer-array-array") }.Concat(LoopLogEdges("outer")),
                "[{'id':'tens','name':'Tens','type':'integer','value':[10,20,30],'isArray':true},{'id':'visited','name':'Visited','type':'integer','value':0}]"));
            scripts["scan.json"] = LoopScanScript();
            WithLoopStory(rollback, scripts, (c, errors) =>
            {
                c.AdvanceDialogue();
                AssertEqual("main.json:B waiting log=[10,1,2,20,1,2,30,1,2] done=0 loops=0 errors=0", LoopState(c, errors), "every caller element calls a loop that starts afresh");
            });
        }

        // main: A -> Run Script(walk) -> B.
        // walk: For Each [1, 2] -> log the element -> Branch(Inner): false -> Set Bool(Inner = true) -> Run Script(walk) -> Set Int,
        // true -> End. The inner call returns on its first element, then the outer call on its second.
        private static void LoopSelfCall(bool rollback)
        {
            var scripts = LoopStoryAround(LoopScript(
                @"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'}," + LoopLogNodes + @",
                    {'id':'inner','type':'getBool','variable':'inner','isGlobal':true},{'id':'gate','type':'branch'},
                    {'id':'mark','type':'setBool','variable':'inner','value':true,'isGlobal':true},{'id':'again','type':'runScript','script':'chapter.json'},
                    {'id':'after','type':'setInt','variable':'visited','value':1},{'id':'end','type':'end'}]",
                new[] { RegressionEdge("0", "loop"), RegressionEdge("loop", "log", "loopBody"), RegressionEdge("log", "gate", "1"), RegressionEdge("gate", "mark", "false"),
                    RegressionEdge("mark", "again", "1"), RegressionEdge("again", "after", "output"), RegressionEdge("gate", "end", "true"),
                    RegressionEdge("array", "loop", "integer-array-", "integer-array-array"), RegressionEdge("inner", "gate", "boolean-", "boolean-condition") }.Concat(LoopLogEdges("loop")),
                "[{'id':'numbers','name':'Numbers','type':'integer','value':[1,2],'isArray':true},{'id':'visited','name':'Visited','type':'integer','value':0}]"));
            WithLoopStory(rollback, scripts, (c, errors) =>
            {
                c.AdvanceDialogue();
                AssertEqual("main.json:B waiting log=[1,1,2] done=0 loops=0 errors=0", LoopState(c, errors), "the inner End leaves the caller cursor alone");
            });
        }

        // Saves hold story state, not the script position, and Load is refused while a dialogue runs: the
        // host stops the dialogue, loads and starts it again.
        private static void LoopAfterLoad(bool rollback)
        {
            WithLoopStory(rollback, LoopVisitingChapter(), (c, errors) =>
            {
                var save = StoryFlowManager.Instance.ExportState();
                c.AdvanceDialogue(); c.AdvanceDialogue();
                AssertEqual("sub.json:Visit", LoopAt(c), "second iteration of the first run");
                c.StopDialogue(); AssertTrue(StoryFlowManager.Instance.ImportState(save), "stopped Load"); c.StartDialogue();
                AssertEqual("main.json:A", LoopAt(c), "story starts again");
                c.AdvanceDialogue();
                AssertEqual(3, LoopVisits(c), "the loop plays every element");
                AssertEqual("main.json:B", LoopAt(c), "first run completes");
                c.AdvanceDialogue();
                AssertEqual(3, LoopVisits(c), "the next run plays every element");
                AssertEqual("main.json:C", LoopAt(c), "second run completes"); AssertEqual(0, errors.Count, "no error");
            });
        }

        // chapter: For Each [1, 2, 3] -> Branch(Stop): false -> Set Bool(Stop = true),
        // true -> Set Int(Seen = element) -> Run Script with no script selected, where the walk stops.
        private static void LoopAfterStoppedWalk(bool rollback)
        {
            var scripts = LoopStoryAround(LoopScript(
                @"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'},{'id':'stop','type':'getBool','variable':'stop'},
                    {'id':'gate','type':'branch'},{'id':'arm','type':'setBool','variable':'stop','value':true},
                    {'id':'seen','type':'setInt','variable':'seen','isGlobal':true},{'id':'halt','type':'runScript'}]",
                new[] { RegressionEdge("0", "loop"), RegressionEdge("loop", "gate", "loopBody"), RegressionEdge("gate", "arm", "false"), RegressionEdge("gate", "seen", "true"),
                    RegressionEdge("seen", "halt", "1"), RegressionEdge("array", "loop", "integer-array-", "integer-array-array"),
                    RegressionEdge("stop", "gate", "boolean-", "boolean-condition"), RegressionEdge("loop", "seen", "integer-element", "integer-2") },
                "[{'id':'numbers','name':'Numbers','type':'integer','value':[1,2,3],'isArray':true},{'id':'stop','name':'Stop','type':'boolean','value':false}]"));
            WithLoopStory(rollback, scripts, (c, errors) =>
            {
                var save = StoryFlowManager.Instance.ExportState();
                c.AdvanceDialogue();
                AssertEqual("chapter.json", c.GetContext().CurrentScript.ScriptPath, "the walk stopped in the called script");
                AssertEqual(2, c.GetIntVariable("Seen", true), "on the second element");
                c.StopDialogue(); AssertTrue(StoryFlowManager.Instance.ImportState(save), "stopped Load"); c.StartDialogue();
                AssertEqual("main.json:A", LoopAt(c), "story starts again"); AssertEqual(0, c.GetIntVariable("Seen", true), "loaded state");
                c.AdvanceDialogue();
                AssertEqual(2, c.GetIntVariable("Seen", true), "the loop starts at its first element"); AssertEqual(0, errors.Count, "no error");
            });
        }

        private static void LoopAfterReset(string reset, bool rollback)
        {
            WithLoopStory(rollback, LoopVisitingChapter(), (c, errors) =>
            {
                c.AdvanceDialogue(); c.AdvanceDialogue();
                if (reset == "ResetAllState") StoryFlowManager.Instance.ResetAllState(); else c.ResetVariables();
                AssertEqual("sub.json:Visit", LoopAt(c), "the reset does not move the player");
                AssertEqual(2, LoopVisits(c), "the caller loop runs on to its end");
                AssertEqual("main.json:B", LoopAt(c), "first run completes");
                c.AdvanceDialogue();
                AssertEqual(3, LoopVisits(c), "the next run plays every element");
                AssertEqual("main.json:C", LoopAt(c), "second run completes"); AssertEqual(0, errors.Count, "no error");
            });
        }

        private static void LoopAfterRestart(bool rollback)
        {
            WithLoopStory(rollback, LoopVisitingChapter(), (c, errors) =>
            {
                c.AdvanceDialogue(); c.AdvanceDialogue();
                AssertEqual("sub.json:Visit", LoopAt(c), "second iteration");
                c.StartDialogue();
                AssertEqual("main.json:A", LoopAt(c), "story starts again");
                c.AdvanceDialogue();
                AssertEqual(3, LoopVisits(c), "the restarted story plays every element");
                AssertEqual("main.json:B", LoopAt(c), "first run completes"); AssertEqual(0, errors.Count, "no error");
            });
        }

        // main: For Each [1, 2, 3] -> Run Script(sub) -> Set Int(Last = element), Completed -> Done. sub: Visit -> End.
        // A save holds story state and no script position: Load is refused while the dialogue runs, and
        // after Stop, Load and Start the story begins again with the loaded state.
        private static void LoopSaveInsideCalledScript(bool rollback)
        {
            var scripts = new Dictionary<string, JObject> { ["sub.json"] = LoopVisitScript(), ["main.json"] = LoopScript(
                @"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'},{'id':'call','type':'runScript','script':'sub.json'},
                    {'id':'after','type':'setInt','variable':'last','isGlobal':true},{'id':'Done','type':'dialogue','choices':[]}]",
                new[] { RegressionEdge("0", "loop"), RegressionEdge("loop", "call", "loopBody"), RegressionEdge("loop", "Done", "completed"), RegressionEdge("call", "after", "output"),
                    RegressionEdge("array", "loop", "integer-array-", "integer-array-array"), RegressionEdge("loop", "after", "integer-element", "integer-2") },
                "[{'id':'numbers','name':'Numbers','type':'integer','value':[1,2,3],'isArray':true}]") };
            WithLoopStory(rollback, scripts, (c, errors) =>
            {
                Func<string> position = () => LoopAt(c) + " last=" + c.GetIntVariable("Last", true);
                AssertEqual("sub.json:Visit last=0", position(), "first iteration");
                c.AdvanceDialogue();
                AssertEqual("sub.json:Visit last=1", position(), "second iteration");
                var save = StoryFlowManager.Instance.ExportState();
                c.AdvanceDialogue();
                AssertEqual("sub.json:Visit last=2", position(), "third iteration");
                AssertEqual(false, StoryFlowManager.Instance.ImportState(save), "Load is refused while the dialogue runs");
                AssertEqual("sub.json:Visit last=2", position(), "the refused Load changes nothing");
                c.AdvanceDialogue();
                AssertEqual("main.json:Done last=3", position(), "the remaining iteration and Completed run");
                c.StopDialogue(); AssertTrue(StoryFlowManager.Instance.ImportState(save), "stopped Load"); c.StartDialogue();
                AssertEqual("sub.json:Visit last=1", position(), "the loaded state on the first line");
                AssertEqual(3, LoopVisits(c), "the story starts over and every element runs");
                AssertEqual("main.json:Done last=3", position(), "Completed runs"); AssertEqual(0, errors.Count, "no error");
            });
        }

        // The checkpoint taken on line B right after the option ran the loop is what Back restores.
        private static void LoopBackAcross(LoopTail tail)
        {
            WithLoopStory(true, LoopLineStory(tail, false, completedToLine: true), (c, errors) =>
            {
                c.SelectOption("go");
                AssertEqual("main.json:B waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "Go runs the loop and Completed shows the next line");
                c.AdvanceDialogue();
                AssertEqual("main.json:C waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "the story carries on");
                AssertTrue(c.GoBack().Ok, "Back to the line the loop led to");
                AssertEqual("main.json:B waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "Back restores the line");
                c.AdvanceDialogue();
                AssertEqual("main.json:C waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "the restored line carries on");
            });
        }
    }
}
