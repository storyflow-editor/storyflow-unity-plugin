using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace StoryFlow.Tests
{
    // A line inside a For Each body plays once per element, as in the StoryFlow Editor runtime, Godot and Unreal. The
    // iteration ends where a node after the line has nothing connected, while a choice or an advance with nothing
    // connected keeps the line, as it does outside a loop. Back on such a line returns to the earlier element.
    internal static partial class Program
    {
        private static void RunLineInLoopBodyTests()
        {
            foreach (bool rollback in new[] { false, true })
            {
                Run("A line in a For Each body shows once per element, with options gated on it, rollback " + rollback, () => LineInLoopBodyGatedChoice(rollback));
                Run("A line without options in a For Each body advances into the rest of the body, rollback " + rollback, () => LineInLoopBodyNarrative(rollback));
                Run("A choice with nothing connected keeps its line in a For Each body, rollback " + rollback, () => LineInLoopBodyTakeOrStay(rollback));
            }
            Run("Back to a line in a For Each body returns to the earlier element", LineInLoopBodyBack);
        }

        private const string LineInLoopNumbers = "[{'id':'numbers','name':'Numbers','type':'integer','value':[1,2],'isArray':true}]";

        private static string LineInLoopOptions(StoryFlowComponent c) => string.Join(",", c.GetCurrentDialogue().Options.Select(option => option.Id));

        // For Each [1, 2] -> line D, whose option One shows only for 1 and Two only for 2. Either option ->
        // Set Int(Last = element), with nothing after it. Completed -> line Done.
        private static void LineInLoopBodyGatedChoice(bool rollback)
        {
            var main = LoopScript(@"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'},
                {'id':'isOne','type':'equalInt','value2':1},{'id':'isTwo','type':'equalInt','value2':2},
                {'id':'D','type':'dialogue','choices':[{'id':'one','text':'One'},{'id':'two','text':'Two'}]},
                {'id':'last','type':'setInt','variable':'last','isGlobal':true},{'id':'Done','type':'dialogue','choices':[]}]",
                new[] { RegressionEdge("0", "loop"), RegressionEdge("array", "loop", "integer-array-", "integer-array-array"), RegressionEdge("loop", "D", "loopBody"),
                    RegressionEdge("loop", "isOne", "integer-element", "integer-1"), RegressionEdge("loop", "isTwo", "integer-element", "integer-1"),
                    RegressionEdge("isOne", "D", "boolean-", "boolean-one"), RegressionEdge("isTwo", "D", "boolean-", "boolean-two"),
                    RegressionEdge("D", "last", "one"), RegressionEdge("D", "last", "two"), RegressionEdge("loop", "last", "integer-element", "integer-2"),
                    RegressionEdge("loop", "Done", "completed") },
                LineInLoopNumbers);
            WithLoopStory(rollback, new Dictionary<string, JObject> { ["main.json"] = main }, (c, errors) =>
            {
                AssertEqual("main.json:D one loops=1 errors=0", LoopAt(c) + " " + LineInLoopOptions(c) + " loops=" + c.GetContext().LoopStackDepth + " errors=" + errors.Count,
                    "the line shows for the first element with the option gated on it");
                c.SelectOption("one");
                AssertEqual(1, c.GetIntVariable("Last", true), "a node after the pick reads the first element");
                AssertEqual("main.json:D two loops=1 errors=0", LoopAt(c) + " " + LineInLoopOptions(c) + " loops=" + c.GetContext().LoopStackDepth + " errors=" + errors.Count,
                    "the line shows again for the second element with its own option");
                c.SelectOption("two");
                AssertEqual(2, c.GetIntVariable("Last", true), "a node after the pick reads the second element");
                AssertEqual("main.json:Done loops=0 errors=0", LoopAt(c) + " loops=" + c.GetContext().LoopStackDepth + " errors=" + errors.Count, "the loop reaches Completed");
            });
        }

        // For Each [1, 2] -> line Beat -> Set Int(Last = element), with nothing after it. Completed -> line Done.
        private static void LineInLoopBodyNarrative(bool rollback)
        {
            var main = LoopScript(@"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'},
                {'id':'Beat','type':'dialogue','choices':[]},{'id':'last','type':'setInt','variable':'last','isGlobal':true},{'id':'Done','type':'dialogue','choices':[]}]",
                new[] { RegressionEdge("0", "loop"), RegressionEdge("array", "loop", "integer-array-", "integer-array-array"), RegressionEdge("loop", "Beat", "loopBody"),
                    RegressionEdge("Beat", "last"), RegressionEdge("loop", "last", "integer-element", "integer-2"), RegressionEdge("loop", "Done", "completed") },
                LineInLoopNumbers);
            WithLoopStory(rollback, new Dictionary<string, JObject> { ["main.json"] = main }, (c, errors) =>
            {
                AssertEqual("main.json:Beat waiting loops=1 errors=0", LoopAt(c) + (c.IsWaitingForInput() ? " waiting" : " stalled") + " loops=" + c.GetContext().LoopStackDepth +
                    " errors=" + errors.Count, "the line shows for the first element");
                c.AdvanceDialogue();
                AssertEqual(1, c.GetIntVariable("Last", true), "the rest of the body reads the first element");
                AssertEqual("main.json:Beat", LoopAt(c), "the line shows again for the second element");
                c.AdvanceDialogue();
                AssertEqual(2, c.GetIntVariable("Last", true), "the rest of the body reads the second element");
                AssertEqual("main.json:Done loops=0 errors=0", LoopAt(c) + " loops=" + c.GetContext().LoopStackDepth + " errors=" + errors.Count, "the loop reaches Completed");
            });
        }

        // For Each [1, 2] -> line Pick, options Take and Stay. Take -> Add To Array(Log, element), with nothing after it.
        // Stay has nothing connected. Completed -> line Done.
        private static Dictionary<string, JObject> LineInLoopTakeOrStay() => new Dictionary<string, JObject> { ["main.json"] = LoopScript(
            @"[{'id':'array','type':'getIntArray','variable':'numbers'},{'id':'loop','type':'forEachIntLoop'},
                {'id':'Pick','type':'dialogue','choices':[{'id':'take','text':'Take'},{'id':'stay','text':'Stay'}]}," + LoopLogNodes + @",
                {'id':'Done','type':'dialogue','choices':[]}]",
            new[] { RegressionEdge("0", "loop"), RegressionEdge("array", "loop", "integer-array-", "integer-array-array"), RegressionEdge("loop", "Pick", "loopBody"),
                RegressionEdge("Pick", "log", "take"), RegressionEdge("loop", "Done", "completed") }.Concat(LoopLogEdges("loop")),
            LineInLoopNumbers) };

        private static void LineInLoopBodyTakeOrStay(bool rollback)
        {
            WithLoopStory(rollback, LineInLoopTakeOrStay(), (c, errors) =>
            {
                c.SelectOption("stay");
                AssertEqual("main.json:Pick waiting log=[] done=0 loops=1 errors=0", LoopState(c, errors), "Stay keeps the line on the first element");
                c.SelectOption("take");
                AssertEqual("main.json:Pick waiting log=[1] done=0 loops=1 errors=0", LoopState(c, errors), "Take ends the iteration and the line shows for the second element");
                c.SelectOption("stay");
                AssertEqual("main.json:Pick waiting log=[1] done=0 loops=1 errors=0", LoopState(c, errors), "Stay keeps the line on the second element");
                c.SelectOption("take");
                AssertEqual("main.json:Done waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "the loop reaches Completed after its last element");
            });
        }

        private static void LineInLoopBodyBack()
        {
            WithLoopStory(true, LineInLoopTakeOrStay(), (c, errors) =>
            {
                c.SelectOption("take");
                AssertEqual("main.json:Pick waiting log=[1] done=0 loops=1 errors=0", LoopState(c, errors), "the line for the second element");
                AssertTrue(c.GoBack().Ok, "Back to the line for the first element");
                AssertEqual("main.json:Pick waiting log=[] done=0 loops=1 errors=0", LoopState(c, errors), "Back restores the first element");
                c.SelectOption("take");
                AssertEqual("main.json:Pick waiting log=[1] done=0 loops=1 errors=0", LoopState(c, errors), "the loop goes on from the first element");
                c.SelectOption("take");
                AssertEqual("main.json:Done waiting log=[1,2] done=0 loops=0 errors=0", LoopState(c, errors), "and reaches Completed once");
            });
        }
    }
}
