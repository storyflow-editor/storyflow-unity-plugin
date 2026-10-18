using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using StoryFlow.Execution;

namespace StoryFlow.Tests
{
    // Every Run Flow pushes a flow frame and only an End pops one. Run Flow is a jump, so a story that loops
    // back through it (a menu's Back to Start, a shop that reopens) never reaches an End. A line ends the walk
    // that led to it, so showing one, or showing it again, starts the count over; a loop with no line still
    // stops at the limit, and exit flows still route. The StoryFlow Editor runtime has the same rule.
    internal static partial class Program
    {
        private static void RunRunFlowDepthTests()
        {
            Run("A Run Flow loop back to Start through a line runs 60 laps with no error", RunFlowLoopThroughLine);
            Run("A Run Flow loop that shows its line again through a Set node runs 60 laps with no error", RunFlowLoopThroughReshownLine);
            Run("A Run Flow loop with no line in it still stops at the limit", RunFlowLoopWithoutLine);
            Run("An exit flow reached after a line in a called script still leaves through the caller's exit output", RunFlowExitAfterLine);
        }

        // Start -> Menu (Again) -> Run Flow (Start) -> Menu.
        private static void RunFlowLoopThroughLine()
        {
            WithLoopStory(false, new Dictionary<string, JObject> { ["main.json"] = LoopScript(
                "[{'id':'Menu','type':'dialogue','choices':[{'id':'again','text':'again'}]},{'id':'back','type':'runFlow','flowId':'start'}]",
                new[] { RegressionEdge("0", "Menu"), RegressionEdge("Menu", "back", "again") }) }, (c, errors) =>
            {
                for (int lap = 0; lap < 60; lap++) c.SelectOption("again");
                AssertEqual(0, errors.Count, "no error");
                AssertEqual("main.json:Menu", LoopAt(c), "back at the menu");
                AssertTrue(c.IsWaitingForInput(), "the menu takes input");
                AssertEqual(0, c.GetContext().FlowStackDepth, "the line starts the count over");
            });
        }

        // Start -> A (Loop) -> Run Flow (Side) -> Entry Flow (Side) -> Set Int (global Done = Done + 1) with nothing
        // after it, which shows A again.
        private static void RunFlowLoopThroughReshownLine()
        {
            WithLoopStory(false, new Dictionary<string, JObject> { ["main.json"] = LoopScript(
                @"[{'id':'A','type':'dialogue','choices':[{'id':'loop','text':'loop'}]},{'id':'side','type':'runFlow','flowId':'side'},
                {'id':'entry','type':'entryFlow','flowId':'side'},{'id':'get','type':'getInt','variable':'done','isGlobal':true},
                {'id':'plus','type':'plus','value2':1},{'id':'count','type':'setInt','variable':'done','isGlobal':true}]",
                new[] { RegressionEdge("0", "A"), RegressionEdge("A", "side", "loop"), RegressionEdge("entry", "count"),
                    RegressionEdge("get", "plus", "integer-", "integer-1"), RegressionEdge("plus", "count", "integer-", "integer-2") },
                "[]", "[{'id':'side','name':'Side'}]") }, (c, errors) =>
            {
                for (int lap = 0; lap < 60; lap++) c.SelectOption("loop");
                AssertEqual(0, errors.Count, "no error");
                AssertEqual(60, c.GetIntVariable("Done", true), "every lap ran");
                AssertEqual("main.json:A", LoopAt(c), "the line is shown again");
                AssertEqual(0, c.GetContext().FlowStackDepth, "showing the line again starts the count over");
            });
        }

        // Start -> Run Flow (Spin) -> Entry Flow (Spin) -> Run Flow (Spin) -> ..., no line anywhere.
        private static void RunFlowLoopWithoutLine()
        {
            WithLoopStory(false, new Dictionary<string, JObject> { ["main.json"] = LoopScript(
                "[{'id':'go','type':'runFlow','flowId':'spin'},{'id':'entry','type':'entryFlow','flowId':'spin'},{'id':'again','type':'runFlow','flowId':'spin'}]",
                new[] { RegressionEdge("0", "go"), RegressionEdge("entry", "again") }, "[]", "[{'id':'spin','name':'Spin'}]") }, (c, errors) =>
            {
                AssertEqual(1, errors.Count, "one error");
                AssertEqual("Flow stack overflow: too many nested flow calls.", errors.Count > 0 ? errors[0] : null, "the depth limit");
                AssertEqual(StoryFlowExecutionContext.MaxFlowDepth, c.GetContext().FlowStackDepth, "stopped at the limit");
            });
        }

        // main: Run Script (sub): exit Quit -> line Quit, output -> line Normal. sub: Inside (Leave) -> Run Flow (exit Quit).
        private static void RunFlowExitAfterLine()
        {
            WithLoopStory(false, new Dictionary<string, JObject> {
                ["main.json"] = LoopScript(
                    "[{'id':'call','type':'runScript','script':'sub.json'},{'id':'Quit','type':'dialogue','choices':[]},{'id':'Normal','type':'dialogue','choices':[]}]",
                    new[] { RegressionEdge("0", "call"), RegressionEdge("call", "Quit", "exit-quit"), RegressionEdge("call", "Normal", "output") }),
                ["sub.json"] = LoopScript(
                    "[{'id':'Inside','type':'dialogue','choices':[{'id':'leave','text':'leave'}]},{'id':'quit','type':'runFlow','flowId':'quit'}]",
                    new[] { RegressionEdge("0", "Inside"), RegressionEdge("Inside", "quit", "leave") }, "[]", "[{'id':'quit','name':'Quit','isExit':true}]") },
                (c, errors) =>
                {
                    AssertEqual("sub.json:Inside", LoopAt(c), "the called script's line");
                    c.SelectOption("leave");
                    AssertEqual("main.json:Quit", LoopAt(c), "left through the exit output");
                    AssertEqual(0, c.GetContext().CallStackDepth, "back in the caller");
                    AssertEqual(0, errors.Count, "no error");
                });
        }
    }
}
