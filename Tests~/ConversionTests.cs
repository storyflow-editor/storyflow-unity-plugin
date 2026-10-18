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

            // Int to Enum and String to Enum take their value list from the Set Enum they feed: Happy, Sad.
            foreach (var type in new[] { "intToEnum", "stringToEnum" })
                Run(type + " with nothing wired gives the empty value, not the list's first", () => AssertEqual("", ConversionUnwired(type, "enum", "Sad"), "Out"));
            foreach (var (index, expected) in new[] { (-1, "Happy"), (1, "Sad"), (99, "Sad") })
                Run("intToEnum clamps index " + index + " into the list", () => AssertEqual(expected, ConversionResult("intToEnum", "enum", "Sad", "integer", index), "Out"));
            Run("intToEnum feeding an enum with no values gives the empty value",
                () => AssertEqual("", ConversionResult("intToEnum", "enum", "Sad", "integer", 1, new string[0]), "Out"));
            foreach (var (text, expected) in new[] { ("Sad", "Sad"), ("sad", "Happy"), ("Angry", "Happy") })
                Run("stringToEnum turns " + text + " into " + expected, () => AssertEqual(expected, ConversionResult("stringToEnum", "enum", "Sad", "string", text), "Out"));
        }

        private static string ConversionUnwired(string type, string output, JToken initial) => ConversionResult(type, output, initial, null, null);

        // Start -> Set (Out) -> line Done, the Set's value pin wired from `type`, whose input is wired from Get (In)
        // unless `input` is null. An enum Out declares `enumValues`, Happy and Sad by default. Answers Out afterwards.
        private static string ConversionResult(string type, string output, JToken initial, string input, JToken inputValue, string[] enumValues = null)
        {
            var variables = new JArray(new JObject { ["id"] = "out", ["name"] = "Out", ["type"] = output, ["value"] = initial });
            if (output == "enum") variables[0]["enumValues"] = new JArray(enumValues ?? new[] { "Happy", "Sad" });
            var nodes = "{'id':'set','type':'set" + SingleInputToken(output) + "','variable':'out'},{'id':'node','type':'" + type + "'}," +
                "{'id':'Done','type':'dialogue','choices':[]}";
            var edges = new List<JObject> { RegressionEdge("0", "set"), RegressionEdge("set", "Done", "1", ""), RegressionEdge("node", "set", output + "-", output + "-2") };
            if (input != null)
            {
                variables.Add(new JObject { ["id"] = "in", ["name"] = "In", ["type"] = input, ["value"] = inputValue });
                nodes += ",{'id':'get','type':'get" + SingleInputToken(input) + "','variable':'in'}";
                edges.Add(RegressionEdge("get", "node", input + "-", input + "-"));
            }
            string result = null;
            WithLoopStory(false, new Dictionary<string, JObject> { ["main.json"] = LoopScript("[" + nodes + "]", edges, variables.ToString()) }, (c, errors) =>
            {
                AssertEqual("Done", c.GetCurrentDialogue()?.NodeId, "the walk reaches the line");
                AssertEqual(0, errors.Count, "no error");
                result = c.GetContext().LocalVariables["out"].Value.ToString();
            });
            return result;
        }
    }
}
