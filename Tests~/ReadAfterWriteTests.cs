using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;

namespace StoryFlow.Tests
{
    // A value read and then written in one processing pass, against the StoryFlow Editor runtime, which is the
    // reference: there a read after a write always sees the new value. Here a read is kept for the rest of the
    // pass, until a write drops what was kept, so a pass without writes still computes each value once.
    internal static partial class Program
    {
        private static void RunReadAfterWriteTests()
        {
            Run("A Switch on Enum Value reads the Set Enum written after another Switch read the same Get Enum", ReadAfterWriteSwitches);
            Run("Plus on a Get Int answers the Set Int written after its first read", ReadAfterWritePlus);
            Run("A character variable answers the Set Character Variable written after its first read", ReadAfterWriteCharacter);
            Run("Array Length answers the Add To Array written after its first read", ReadAfterWriteArray);
            Run("Plus on a Get Map Value answers the Set Map Value written after its first read", ReadAfterWriteMap);
            Run("A pass with no write computes a value it reads twice only once", ReadAfterWriteNoWrite);
            Run("A character variable answers the SetCharacterVariableById a host made during the pass", ReadAfterManagerCharacterWrite);
            Run("Get Int on a global answers the ResetGlobalVariables a host made during the pass",
                () => ReadAfterManagerGlobalReset(() => StoryFlowManager.Instance.ResetGlobalVariables()));
            Run("Get Int on a global answers the ResetAllState a host made during the pass",
                () => ReadAfterManagerGlobalReset(() => StoryFlowManager.Instance.ResetAllState()));
            Run("A character variable answers the ResetRuntimeCharacters a host made during the pass", ReadAfterManagerCharacterReset);
            Run("To Upper Case on a localized string answers the SetLanguage a host made during the pass", ReadAfterManagerLanguageSwitch);
        }

        private const string ReadAfterWriteLines = "{'id':'Fresh','type':'dialogue','choices':[]},{'id':'Stale','type':'dialogue','choices':[]},{'id':'Other','type':'dialogue','choices':[]}";

        // Alice's Mood, Calm or Angry, and the Get Character Variable that reads it, as the exporter writes them.
        private static Dictionary<string, JObject> ReadAfterWriteAliceFiles() => new Dictionary<string, JObject> {
            ["characters.json"] = JObject.Parse(@"{'characters':{'characters\\alice.sfc':{'name':'Alice','image':'','variables':{
                'var-mood':{'id':'var-mood','name':'Mood','type':'enum','value':'Calm','enumValues':['Calm','Angry']}}}},'strings':{'en':{}},'assets':{}}"),
            ["character-index.json"] = JObject.Parse(@"{'schemaVersion':'1','characters':{'da_alice0001':'characters\\alice.sfc'}}") };

        private const string ReadAfterWriteMood = @"{'id':'mood','type':'getCharacterVar','characterPath':'characters\\alice.sfc','variable':'Mood',
            'variableType':'enum','characterId':'da_alice0001'}";

        // Line A, option Go -> `nodes`, in one pass, `prepare` run on the component first. Answers the line the pass
        // stopped on; `evaluated` collects the id of every node computed in it, a value served from what the pass kept
        // being left out.
        private static string ReadAfterWriteLine(string nodes, IEnumerable<JObject> edges, string variables = "[]",
            Dictionary<string, JObject> files = null, List<string> evaluated = null, System.Action<StoryFlowComponent> prepare = null)
        {
            var story = new Dictionary<string, JObject> { ["main.json"] = LoopScript(
                "[{'id':'A','type':'dialogue','choices':[{'id':'go','text':'go'}]}," + ReadAfterWriteLines + "," + nodes + "]",
                new[] { RegressionEdge("0", "A", "", "") }.Concat(edges), variables) };
            if (files != null) foreach (var file in files) story[file.Key] = file.Value;
            string reached = null;
            System.Action<object> onLog = message =>
            {
                var text = message?.ToString() ?? "";
                if (text.StartsWith("[SF-TRACE] EVAL ")) evaluated?.Add(text.Split(' ')[2]);
            };
            UnityEngine.Debug.OnLog += onLog;
            try
            {
                WithLoopStory(false, story, (c, errors) =>
                {
                    c.GetContext().TraceEnabled = evaluated != null;
                    prepare?.Invoke(c);
                    c.SelectOption("go");
                    reached = c.GetCurrentDialogue()?.NodeId;
                    AssertEqual(0, errors.Count, "no error");
                });
            }
            finally { UnityEngine.Debug.OnLog -= onLog; }
            return reached;
        }

        // Go -> Switch A on Get Enum (Mood): Happy -> Set Enum (Mood = Sad) -> Switch B on the same Get Enum.
        private static void ReadAfterWriteSwitches()
        {
            AssertEqual("Fresh", ReadAfterWriteLine(@"{'id':'mood','type':'getEnum','variable':'v_mood'},
                {'id':'switchA','type':'switchOnEnumValue','enumValues':['Happy','Sad']},{'id':'set','type':'setEnum','variable':'v_mood','value':'Sad'},
                {'id':'switchB','type':'switchOnEnumValue','enumValues':['Happy','Sad']}",
                new[] { RegressionEdge("A", "switchA", "go"), RegressionEdge("switchA", "set", "Happy"), RegressionEdge("switchA", "Other", "Sad", ""),
                    RegressionEdge("set", "switchB", "1"), RegressionEdge("switchB", "Fresh", "Sad", ""), RegressionEdge("switchB", "Stale", "Happy", ""),
                    RegressionEdge("mood", "switchA", "enum-", "enum-1"), RegressionEdge("mood", "switchB", "enum-", "enum-1") },
                "[{'id':'v_mood','name':'Mood','type':'enum','value':'Happy','enumValues':['Happy','Sad']}]"), "Switch B leaves through Sad");
        }

        // Go -> Branch (Plus (Get Int X, 1) == 6): true -> Set Int (X = 10) -> Branch (the same Plus == 11).
        private static void ReadAfterWritePlus()
        {
            AssertEqual("Fresh", ReadAfterWriteLine(@"{'id':'x','type':'getInt','variable':'v_x'},{'id':'plus','type':'plus','value2':1},
                {'id':'is6','type':'equalInt','value2':6},{'id':'gate1','type':'branch'},{'id':'set','type':'setInt','variable':'v_x','value':10},
                {'id':'is11','type':'equalInt','value2':11},{'id':'gate2','type':'branch'}",
                new[] { RegressionEdge("A", "gate1", "go"), RegressionEdge("gate1", "set", "true"), RegressionEdge("gate1", "Other", "false", ""),
                    RegressionEdge("set", "gate2", "1"), RegressionEdge("gate2", "Fresh", "true", ""), RegressionEdge("gate2", "Stale", "false", ""),
                    RegressionEdge("x", "plus", "integer-", "integer-1"), RegressionEdge("plus", "is6", "integer-", "integer-1"),
                    RegressionEdge("plus", "is11", "integer-", "integer-1"), RegressionEdge("is6", "gate1", "boolean-", "boolean-condition"),
                    RegressionEdge("is11", "gate2", "boolean-", "boolean-condition") },
                "[{'id':'v_x','name':'X','type':'integer','value':5}]"), "the second Branch sees X + 1 = 11");
        }

        // Go -> Switch A on Get Character Variable (Alice's Mood): Calm -> Set Character Variable (Alice's Mood = Angry)
        // -> Switch B on the same read. Both nodes as the exporter writes them.
        private static void ReadAfterWriteCharacter()
        {
            AssertEqual("Fresh", ReadAfterWriteLine(ReadAfterWriteMood + @",{'id':'switchA','type':'switchOnEnumValue','enumValues':['Calm','Angry']},
                {'id':'set','type':'setCharacterVar','characterPath':'characters\\alice.sfc','variable':'Mood','variableType':'enum','value':'Angry',
                'characterId':'da_alice0001'},{'id':'switchB','type':'switchOnEnumValue','enumValues':['Calm','Angry']}",
                new[] { RegressionEdge("A", "switchA", "go"), RegressionEdge("switchA", "set", "Calm"), RegressionEdge("switchA", "Other", "Angry", ""),
                    RegressionEdge("set", "switchB", "1"), RegressionEdge("switchB", "Fresh", "Angry", ""), RegressionEdge("switchB", "Stale", "Calm", ""),
                    RegressionEdge("mood", "switchA", "enum-output", "enum-1"), RegressionEdge("mood", "switchB", "enum-output", "enum-1") },
                files: ReadAfterWriteAliceFiles()), "Switch B leaves through Angry");
        }

        // Go -> Branch (Array Length (Numbers) == 2): true -> Add To Array (Numbers, 7) -> Branch (the same Length == 3).
        private static void ReadAfterWriteArray()
        {
            AssertEqual("Fresh", ReadAfterWriteLine(@"{'id':'numbers','type':'getIntArray','variable':'v_numbers'},{'id':'length','type':'arrayLengthInt'},
                {'id':'is2','type':'equalInt','value2':2},{'id':'gate1','type':'branch'},{'id':'add','type':'addToIntArray','value':7},
                {'id':'is3','type':'equalInt','value2':3},{'id':'gate2','type':'branch'}",
                new[] { RegressionEdge("A", "gate1", "go"), RegressionEdge("gate1", "add", "true"), RegressionEdge("gate1", "Other", "false", ""),
                    RegressionEdge("add", "gate2", "1"), RegressionEdge("gate2", "Fresh", "true", ""), RegressionEdge("gate2", "Stale", "false", ""),
                    RegressionEdge("numbers", "length", "integer-array-", "integer-array-1"), RegressionEdge("numbers", "add", "integer-array-", "integer-array-2"),
                    RegressionEdge("length", "is2", "integer-", "integer-1"), RegressionEdge("length", "is3", "integer-", "integer-1"),
                    RegressionEdge("is2", "gate1", "boolean-", "boolean-condition"), RegressionEdge("is3", "gate2", "boolean-", "boolean-condition") },
                "[{'id':'v_numbers','name':'Numbers','type':'integer','value':[1,2],'isArray':true}]"), "the second Branch sees three elements");
        }

        // Go -> Branch (Plus (Get Map Value (Pairs, k), 1) == 2): true -> Set Map Value (Pairs, k = 5) -> Branch (the same Plus == 6).
        private static void ReadAfterWriteMap()
        {
            AssertEqual("Fresh", ReadAfterWriteLine(@"{'id':'pairs','type':'getMap','variable':'v_pairs','keyType':'string','valueType':'integer'},
                {'id':'value','type':'getMapValue','keyType':'string','valueType':'integer','key':'k'},{'id':'plus','type':'plus','value2':1},
                {'id':'is2','type':'equalInt','value2':2},{'id':'gate1','type':'branch'},
                {'id':'write','type':'setMapValue','keyType':'string','valueType':'integer','key':'k','value':5},
                {'id':'is6','type':'equalInt','value2':6},{'id':'gate2','type':'branch'}",
                new[] { RegressionEdge("A", "gate1", "go"), RegressionEdge("gate1", "write", "true"), RegressionEdge("gate1", "Other", "false", ""),
                    RegressionEdge("write", "gate2", "1"), RegressionEdge("gate2", "Fresh", "true", ""), RegressionEdge("gate2", "Stale", "false", ""),
                    RegressionEdge("pairs", "value", "map-string-integer", "map-string-integer-1"), RegressionEdge("pairs", "write", "map-string-integer", "map-string-integer-2"),
                    RegressionEdge("value", "plus", "integer-value", "integer-1"), RegressionEdge("plus", "is2", "integer-", "integer-1"),
                    RegressionEdge("plus", "is6", "integer-", "integer-1"), RegressionEdge("is2", "gate1", "boolean-", "boolean-condition"),
                    RegressionEdge("is6", "gate2", "boolean-", "boolean-condition") },
                "[{'id':'v_pairs','name':'Pairs','type':'map','value':[{'key':'k','value':1}],'keyType':'string','valueType':'integer'}]"),
                "the second Branch sees the written value + 1 = 6");
        }

        // Go -> Branch (Plus (Get Int X, 1) == 6): true -> Branch (the same Plus == 6): true -> Fresh.
        private static void ReadAfterWriteNoWrite()
        {
            var evaluated = new List<string>();
            AssertEqual("Fresh", ReadAfterWriteLine(@"{'id':'x','type':'getInt','variable':'v_x'},{'id':'plus','type':'plus','value2':1},
                {'id':'is6','type':'equalInt','value2':6},{'id':'gate1','type':'branch'},{'id':'again','type':'equalInt','value2':6},{'id':'gate2','type':'branch'}",
                new[] { RegressionEdge("A", "gate1", "go"), RegressionEdge("gate1", "gate2", "true"), RegressionEdge("gate1", "Other", "false", ""),
                    RegressionEdge("gate2", "Fresh", "true", ""), RegressionEdge("gate2", "Stale", "false", ""),
                    RegressionEdge("x", "plus", "integer-", "integer-1"), RegressionEdge("plus", "is6", "integer-", "integer-1"),
                    RegressionEdge("plus", "again", "integer-", "integer-1"), RegressionEdge("is6", "gate1", "boolean-", "boolean-condition"),
                    RegressionEdge("again", "gate2", "boolean-", "boolean-condition") },
                "[{'id':'v_x','name':'X','type':'integer','value':5}]", evaluated: evaluated), "both Branches see X + 1 = 6");
            AssertEqual(1, evaluated.Count(id => id == "x"), "Get Int computed once");
            AssertEqual(1, evaluated.Count(id => id == "plus"), "Plus computed once");
        }

        // The host writes below run inside the pass, from the event Set Background Image raises.
        private static System.Action<StoryFlowComponent> OnBackgroundImage(System.Action write) =>
            c => c.OnBackgroundImageChanged += _ => write();

        // Go -> Switch A on Alice's Mood: Calm -> Set Background Image, whose event the host answers with
        // SetCharacterVariableById (Alice's Mood = Angry) -> Switch B on the same read.
        private static void ReadAfterManagerCharacterWrite()
        {
            AssertEqual("Fresh", ReadAfterWriteLine(ReadAfterWriteMood + @",{'id':'switchA','type':'switchOnEnumValue','enumValues':['Calm','Angry']},
                {'id':'bg','type':'setBackgroundImage'},{'id':'switchB','type':'switchOnEnumValue','enumValues':['Calm','Angry']}",
                new[] { RegressionEdge("A", "switchA", "go"), RegressionEdge("switchA", "bg", "Calm"), RegressionEdge("switchA", "Other", "Angry", ""),
                    RegressionEdge("bg", "switchB", "output"), RegressionEdge("switchB", "Fresh", "Angry", ""), RegressionEdge("switchB", "Stale", "Calm", ""),
                    RegressionEdge("mood", "switchA", "enum-output", "enum-1"), RegressionEdge("mood", "switchB", "enum-output", "enum-1") },
                files: ReadAfterWriteAliceFiles(), prepare: OnBackgroundImage(() =>
                    StoryFlowManager.Instance.SetCharacterVariableById("da_alice0001", "Mood", StoryFlowVariant.Enum("Angry")))),
                "Switch B leaves through Angry");
        }

        // Go -> Set Int (global Done = 5) -> Branch (Done == 5): true -> Set Background Image, whose event the host
        // answers with `reset` -> Branch (the same Done == 5), false once Done is back at 0.
        private static void ReadAfterManagerGlobalReset(System.Action reset)
        {
            AssertEqual("Fresh", ReadAfterWriteLine(@"{'id':'set','type':'setInt','variable':'done','value':5,'isGlobal':true},
                {'id':'done','type':'getInt','variable':'done','isGlobal':true},{'id':'is5','type':'equalInt','value2':5},{'id':'gate1','type':'branch'},
                {'id':'bg','type':'setBackgroundImage'},{'id':'gate2','type':'branch'}",
                new[] { RegressionEdge("A", "set", "go"), RegressionEdge("set", "gate1", "1"), RegressionEdge("gate1", "bg", "true"),
                    RegressionEdge("gate1", "Other", "false", ""), RegressionEdge("bg", "gate2", "output"), RegressionEdge("gate2", "Fresh", "false", ""),
                    RegressionEdge("gate2", "Stale", "true", ""), RegressionEdge("done", "is5", "integer-", "integer-1"),
                    RegressionEdge("is5", "gate1", "boolean-", "boolean-condition"), RegressionEdge("is5", "gate2", "boolean-", "boolean-condition") },
                prepare: OnBackgroundImage(reset)), "the second Branch sees Done back at 0");
        }

        // Go -> Set Character Variable (Alice's Mood = Angry) -> Switch A on Alice's Mood: Angry -> Set Background Image,
        // whose event the host answers with ResetRuntimeCharacters -> Switch B on the same read.
        private static void ReadAfterManagerCharacterReset()
        {
            AssertEqual("Fresh", ReadAfterWriteLine(ReadAfterWriteMood + @",{'id':'set','type':'setCharacterVar','characterPath':'characters\\alice.sfc',
                'variable':'Mood','variableType':'enum','value':'Angry','characterId':'da_alice0001'},
                {'id':'switchA','type':'switchOnEnumValue','enumValues':['Calm','Angry']},{'id':'bg','type':'setBackgroundImage'},
                {'id':'switchB','type':'switchOnEnumValue','enumValues':['Calm','Angry']}",
                new[] { RegressionEdge("A", "set", "go"), RegressionEdge("set", "switchA", "1"), RegressionEdge("switchA", "bg", "Angry"),
                    RegressionEdge("switchA", "Other", "Calm", ""), RegressionEdge("bg", "switchB", "output"), RegressionEdge("switchB", "Fresh", "Calm", ""),
                    RegressionEdge("switchB", "Stale", "Angry", ""), RegressionEdge("mood", "switchA", "enum-output", "enum-1"),
                    RegressionEdge("mood", "switchB", "enum-output", "enum-1") },
                files: ReadAfterWriteAliceFiles(), prepare: OnBackgroundImage(() => StoryFlowManager.Instance.ResetRuntimeCharacters())),
                "Switch B leaves through Calm");
        }

        // Go -> Branch (To Upper Case (Get String Word) == "HELLO"): true -> Set Background Image, whose event the host
        // answers with SetLanguage("fr") -> Branch (the same To Upper Case == "BONJOUR"). Word holds a string-table key.
        private static void ReadAfterManagerLanguageSwitch()
        {
            var files = new Dictionary<string, JObject> {
                ["global-variables.json"] = JObject.Parse("{'variables':{'word':{'id':'word','name':'Word','type':'string','value':'word'}},'strings':{'en':{'word':'Hello'}},'assets':{}}"),
                ["localization.json"] = JObject.Parse("{'schemaVersion':'1','sourceLanguage':'en','languages':[{'code':'fr','name':'French'}],'strings':{'fr':{'word':'Bonjour'}}}") };
            AssertEqual("Fresh", ReadAfterWriteLine(@"{'id':'word','type':'getString','variable':'word','isGlobal':true},{'id':'upper','type':'toUpperCase'},
                {'id':'isHello','type':'equalString','value2':'HELLO'},{'id':'gate1','type':'branch'},{'id':'bg','type':'setBackgroundImage'},
                {'id':'isBonjour','type':'equalString','value2':'BONJOUR'},{'id':'gate2','type':'branch'}",
                new[] { RegressionEdge("A", "gate1", "go"), RegressionEdge("gate1", "bg", "true"), RegressionEdge("gate1", "Other", "false", ""),
                    RegressionEdge("bg", "gate2", "output"), RegressionEdge("gate2", "Fresh", "true", ""), RegressionEdge("gate2", "Stale", "false", ""),
                    RegressionEdge("word", "upper", "string-", "string-"), RegressionEdge("upper", "isHello", "string-", "string-1"),
                    RegressionEdge("upper", "isBonjour", "string-", "string-1"), RegressionEdge("isHello", "gate1", "boolean-", "boolean-condition"),
                    RegressionEdge("isBonjour", "gate2", "boolean-", "boolean-condition") },
                files: files, prepare: OnBackgroundImage(() => StoryFlowManager.Instance.SetLanguage("fr"))), "the second Branch reads the French word");
        }
    }
}
