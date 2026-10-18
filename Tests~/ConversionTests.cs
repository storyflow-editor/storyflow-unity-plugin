using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace StoryFlow.Tests
{
    // Conversion results against the StoryFlow Editor runtime, which is the reference.
    internal static partial class Program
    {
        private static void RunConversionTests()
        {
            // Math.round: the nearest integer, and a value exactly halfway rounds toward +infinity.
            foreach (var (value, expected) in new[] { (2.5, 3), (-2.5, -2), (-2.6, -3), (2.4999, 2), (2.6, 3), (0.5, 1), (-0.5, 0), (7.0, 7) })
                Run("Float to Int rounds " + value.ToString(CultureInfo.InvariantCulture) + " to " + expected,
                    () => SingleInputReadsItsPin(Single("floatToInt", "float", value, "integer", expected.ToString(CultureInfo.InvariantCulture))));
            Run("Float to Int with nothing wired gives 0", () => AssertEqual("0", ConversionUnwired("floatToInt", "integer", 7), "Out"));
        }

        // Start -> Set (Out) -> line Done, the Set's value pin wired from `type` with nothing wired into the conversion.
        // Answers Out afterwards.
        private static string ConversionUnwired(string type, string output, JToken initial)
        {
            var variable = new JObject { ["id"] = "out", ["name"] = "Out", ["type"] = output, ["value"] = initial };
            if (output == "enum") variable["enumValues"] = new JArray("Happy", "Sad");
            var nodes = "[{'id':'set','type':'set" + SingleInputToken(output) + "','variable':'out'},{'id':'node','type':'" + type + "'}," +
                "{'id':'Done','type':'dialogue','choices':[]}]";
            var edges = new[] { RegressionEdge("0", "set"), RegressionEdge("set", "Done", "1", ""), RegressionEdge("node", "set", output + "-", output + "-2") };
            string result = null;
            WithLoopStory(false, new Dictionary<string, JObject> { ["main.json"] = LoopScript(nodes, edges, new JArray(variable).ToString()) }, (c, errors) =>
            {
                AssertEqual("Done", c.GetCurrentDialogue()?.NodeId, "the walk reaches the line");
                AssertEqual(0, errors.Count, "no error");
                result = c.GetContext().LocalVariables["out"].Value.ToString();
            });
            return result;
        }
    }
}
