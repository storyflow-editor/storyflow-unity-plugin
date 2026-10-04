using System;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StoryFlow.Editor;
using UnityEditor;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using StoryFlow.Execution;

namespace StoryFlow.Tests
{
    internal static partial class Program
    {
        private static void RunRollbackTests()
        {
            foreach (string action in new[] { "back", "block", "stop", "restart", "disabled" })
                Run("Availability finishes current " + action, () => RollbackAvailabilityReentry(action));
            Run("Rollback settings contract", RollbackSettingsContract);
            foreach(bool enabled in new[]{false,true}) Run("Caller memo after callee write " + enabled, ()=>RollbackCallerMemo(enabled));
            foreach(string action in new[]{"normal","stop","restart","replace"}) Run("Graph barrier callback " + action, ()=>RollbackGraphBarrier(action));
            foreach(string phase in new[]{"availability","restored"}) foreach(string action in new[]{"advance","stop","restart","replace","multi"})
                Run("Restored public event " + phase + " " + action, ()=>RollbackPublicRestored(phase,action));
            Run("Rollback public surface", RollbackPublicSurface);
            Run("Rollback activation isolation", RollbackActivationIsolation);
            foreach (var name in new[] { "purchase", "nested-loops", "same-node", "random", "barrier" })
                Run("Rollback " + name, () => RunRollbackCase(name));
            Run("Rollback host scalar invalidation", RollbackHostScalarInvalidation);
            Run("Rollback settings vectors reimport", RollbackSettingsVectors);
            Run("Rollback lifecycle and ownership", RollbackLifecycleOwnership);
            Run("Rollback detached alias and budget", RollbackDetachedBudget);
            Run("Rollback private RNG vectors", RollbackRandomVectors);
            Run("Rollback wide integer RNG", RollbackWideIntegerRandom);
            foreach (bool advance in new[] { false, true })
                foreach (bool restart in new[] { false, true })
                    Run("Rollback BeforeLeave " + advance + " restart " + restart, () => RollbackBeforeLeave(advance, restart));
            Run("Rollback host callback invalidation", RollbackHostCallbacks);
            Run("Rollback reentrant invalidation", RollbackReentrantInvalidation);
            Run("Rollback restarted host mutation", RollbackRestartedMutation);
            Run("Rollback restarted content replacement", RollbackRestartedContent);
            Run("Rollback restarted stop owner", RollbackRestartedStop);
            Run("Rollback start replacement retains counted owner", RollbackStartReplacement);
            Run("Rollback registration callback retains counted owner", RollbackRegistrationReplacement);
            Run("Rollback detached presentation dependencies", RollbackPresentationDependencies);
            Run("Rollback authored names retain project scope", RollbackCharacterNameScope);
            Run("Rollback failed Back control and voice recovery", RollbackControlRecovery);
            Run("Rollback restore observer reentrancy", RollbackRestoreObserver);
            Run("Rollback preparation preserves live state", RollbackPrepareFailure);
            Run("Rollback retained character reference", RollbackCharacterIdentity);
            Run("Rollback current language presentation", RollbackCurrentLanguage);
            Run("Rollback looping audio descriptor", RollbackLoopingAudio);
            Run("Rollback commit failure recovery", RollbackCommitFailure);
            Run("Rollback root End clears history", RollbackRootEnd);
            Run("Rollback executed character and Data Asset mutations", RollbackCharacterDataExecution);
            Run("Rollback recursive script outputs and locals", RollbackRecursiveExecution);
        }
        private static void RollbackAvailabilityReentry(string action)
        {
            WithRollbackFixture("same-node", (c, p, r) => {
                bool changed = false; var observed = new List<StoryFlowRollbackAvailability>();
                c.OnRollbackAvailabilityChanged += a => {
                    if (changed || !a.CanGoBack) return;
                    changed = true;
                    if (action == "back") AssertTrue(c.GoBack().Ok, "reentrant Back succeeds");
                    else if (action == "block") c.BlockRollback();
                    else { c.StopDialogue(); if (action != "stop") { p.DialogueRollback.Enabled = action != "disabled"; c.StartDialogue(); } }
                };
                c.OnRollbackAvailabilityChanged += a => observed.Add(a);
                c.SelectOption("again");
                AssertTrue(changed, "first observer mutates available session");
                AssertTrue(observed.Count >= 2, "later observer receives correcting transition");
                var last = observed[observed.Count - 1]; var live = c.GetRollbackAvailability();
                AssertEqual(false, last.CanGoBack, "final payload disables Back");
                AssertEqual(live.CanGoBack, last.CanGoBack, "final availability matches getter");
                AssertEqual(live.Steps, last.Steps, "final steps match getter");
                AssertEqual(live.Reason, last.Reason, "final reason matches getter");
            });
        }
        private static void RollbackCallerMemo(bool enabled)
        {
            WithRollbackFixture("same-node", (c,p,r)=> {
                c.StopDialogue(); p.DialogueRollback.Enabled=enabled; c.StartDialogue(); var ctx=c.GetContext(); var getter=p.StartupScript.GetNode("get");
                AssertEqual(0,StoryFlowEvaluator.EvaluateIntegerFromNode(ctx,getter),"caller getter seeded before call");
                ctx.PushCallFrame("call"); ctx.FindVariableByName("Visits",false,true).Value.SetInt(5);
                AssertEqual(5,StoryFlowEvaluator.EvaluateIntegerFromNode(ctx,getter),"callee evaluator consumes current global");
                ctx.PopCallFrame(); AssertEqual(5,StoryFlowEvaluator.EvaluateIntegerFromNode(ctx,getter),"returned caller getter sees callee write");
            });
        }
        private static void RollbackGraphBarrier(string action)
        {
            WithRollbackFixture("barrier",(c,p,r)=> {
                c.AdvanceDialogue(); bool once=false;
                c.OnRollbackAvailabilityChanged+=a=> { if(once || a.CanGoBack || action=="normal")return; once=true;
                    if(action=="stop")c.StopDialogue();
                    else if(action=="restart")c.StartDialogue();
                    else { var replacement=p.StartupScript; c.StartDialogue(replacement); }
                };
                c.AdvanceDialogue();
                if(action=="normal")AssertEqual("C",c.GetCurrentDialogue().NodeId,"normal graph barrier follows continuation");
                else { AssertTrue(once,"barrier callback fired"); AssertEqual(action!="stop",c.IsDialogueActive(),"callback lifecycle wins");
                    AssertTrue(c.GetContext().NextNode==null,"obsolete barrier must not enqueue continuation"); c.ResumeExecution();
                    if(action!="stop")AssertEqual("A",c.GetCurrentDialogue().NodeId,"public resume cannot follow old edge"); }
            });
        }
        private static void RollbackPublicRestored(string phase,string action)
        {
            WithRollbackFixture("barrier",(c,p,r)=> {
                c.AdvanceDialogue(); bool once=false; int delivered=0;
                StoryFlowComponent second=null;
                Action change=()=> { if(once)return; once=true; if(action=="multi") { second=new StoryFlowComponent {UIStyle=BuiltInUIStyle.None,TraceEnabled=false}; second.StartDialogue(); } else if(action=="advance")c.AdvanceDialogue(); else if(action=="stop")c.StopDialogue(); else c.StartDialogue(); };
                if(phase=="availability")c.OnRollbackAvailabilityChanged+=a=> { if(!a.CanGoBack)change(); };
                else c.OnDialogueRestored+=state=> { AssertEqual("A",state.NodeId,"first observer gets actual restoration"); change(); };
                c.OnDialogueRestored+=state=>delivered++;
                AssertTrue(c.GoBack().Ok,"restore committed"); AssertTrue(once,"callback changed entry"); AssertEqual(0,delivered,"later observer never gets obsolete or fresh restoration"); second?.StopDialogue();
            });
        }
        private static void WithRollbackFixture(string name, Action<StoryFlowComponent, StoryFlowProjectAsset, string> test)
        {
            var cwd = Directory.GetCurrentDirectory();
            var root = Path.Combine(Path.GetTempPath(), "storyflow-rollback-tests-" + Guid.NewGuid().ToString("N"));
            var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "dialogue-rollback-v1", name);
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(root, "build", Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(file, destination);
            }
            StoryFlowComponent component = null;
            try
            {
                Directory.SetCurrentDirectory(root); EditorStubs.Reset();
                var project = StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Rollback", out var report);
                AssertTrue(project != null && !report.HasFailures, "shared export imports");
                SetManagerProject(project);
                component = new StoryFlowComponent { UIStyle = BuiltInUIStyle.None, TraceEnabled = false };
                component.StartDialogue(); test(component, project, root);
            }
            finally { component?.StopDialogue(); ClearManager(); Directory.SetCurrentDirectory(cwd); TryDeleteDirectory(root); }
        }
        private static JToken RollbackValue(StoryFlowVariable variable, StoryFlowExecutionContext context)
        {
            JToken Scalar(StoryFlowVariant value)
            {
                if (value == null) return JValue.CreateNull();
                if (value.MapValue != null) return new JArray(value.MapValue.Select(entry => new JObject { ["key"] = Scalar(entry.Key), ["value"] = Scalar(entry.Value) }));
                if (value.ArrayValue != null) return new JArray(value.ArrayValue.Select(Scalar));
                return value.Type switch { StoryFlowVariableType.Integer => new JValue(value.IntValue), StoryFlowVariableType.Float => new JValue(value.FloatValue),
                    StoryFlowVariableType.Boolean => new JValue(value.BoolValue), StoryFlowVariableType.String => new JValue(value.IsLiteralString ? value.StringValue : context.ResolveStringKey(value.StringValue)), _ => new JValue(value.ToString()) };
            }
            return Scalar(variable.Value);
        }
        private static void RunRollbackCase(string name)
        {
            var trace = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dialogue-rollback-v1", "expected-traces.json")))[name];
            WithRollbackFixture(name, (component, project, root) =>
            {
                var remembered = new Dictionary<string, JToken>();
                foreach (var action in trace)
                {
                    switch ((string)action["op"])
                    {
                        case "choose": component.SelectOption((string)action["id"]); break;
                        case "advance": component.AdvanceDialogue(); break;
                        case "back": AssertTrue(component.GoBack().Ok, "Back succeeds"); break;
                        case "block": component.BlockRollback("test"); break;
                        case "assert":
                            var state = action["state"]; var context = component.GetContext();
                            if (state["dialogue"] != null) AssertEqual((string)state["dialogue"], component.GetCurrentDialogue().Text, "live dialogue");
                            if (state["canGoBack"] != null) AssertEqual((bool)state["canGoBack"], component.CanGoBack(), "availability");
                            if (state["callDepth"] != null) AssertEqual((int)state["callDepth"], context.CallStackDepth, "caller depth");
                            if (state["globals"] is JObject globals)
                                foreach (var pair in globals)
                                    AssertTrue(JToken.DeepEquals(pair.Value, RollbackValue(context.FindVariableByName(pair.Key, false, true), context)), "live global " + pair.Key);
                            if (state["options"] != null) AssertEqual(state["options"].ToString(Newtonsoft.Json.Formatting.None), new JArray(component.GetCurrentDialogue().Options.Select(option => option.Id)).ToString(Newtonsoft.Json.Formatting.None), "restored options");
                            if (state["loopCursors"] != null)
                            {
                                var calls = (List<CallFrame>)typeof(StoryFlowExecutionContext).GetField("callStack", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context);
                                var cursors = calls.SelectMany(frame => frame.SavedLoopStack).Select(loop => loop.CurrentIndex);
                                AssertEqual(state["loopCursors"].ToString(Newtonsoft.Json.Formatting.None), new JArray(cursors).ToString(Newtonsoft.Json.Formatting.None), "parked live loop cursors");
                            }
                            if (state["rememberGlobals"] is JArray remember)
                                foreach (string key in remember) remembered[key] = RollbackValue(context.FindVariableByName(key, false, true), context);
                            if (state["sameGlobals"] is JArray same)
                                foreach (string key in same) AssertTrue(JToken.DeepEquals(remembered[key], RollbackValue(context.FindVariableByName(key, false, true), context)), "random repeats " + key);
                            break;
                    }
                }
            });
        }
        private static void RollbackHostScalarInvalidation()
        {
            WithRollbackFixture("same-node", (component, _, __) =>
            {
                component.SelectOption("again"); AssertTrue(component.CanGoBack(), "history before host setter");
                component.SetIntVariable("Visits", 42, true);
                AssertEqual(false, component.CanGoBack(), "public scalar setter invalidates before mutation");
            });
        }
        private static StoryFlowRollbackController Controller(StoryFlowComponent component) =>
            (StoryFlowRollbackController)typeof(StoryFlowComponent).GetField("rollback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component);
        private static void RollbackSettingsVectors()
        {
            var vectors = JArray.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "dialogue-rollback-v1", "settings.json")));
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.StopDialogue();
                foreach (var vector in vectors)
                {
                    var file = Path.Combine(root, "build", "project.json"); var json = JObject.Parse(File.ReadAllText(file));
                    json["metadata"]["dialogueRollback"] = vector["input"]; File.WriteAllText(file, json.ToString());
                    var imported = StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Rollback");
                    AssertEqual((bool)vector["expected"]["enabled"], imported.DialogueRollback.Enabled, (string)vector["name"]);
                    AssertEqual((int)vector["expected"]["historyLimit"], imported.DialogueRollback.HistoryLimit, (string)vector["name"]);
                    component.StartDialogue();
                    if (!imported.DialogueRollback.Enabled)
                    {
                        component.SelectOption("again"); component.SetIntVariable("Visits", 12, true);
                        AssertTrue(Controller(component) == null, "disabled has no controller or history allocation");
                        AssertTrue(!component.GetContext().RollbackRandomState.HasValue, "disabled has no private RNG");
                    }
                    component.StopDialogue();
                }
            });
        }
        private static void RollbackLifecycleOwnership()
        {
            WithRollbackFixture("same-node", (first, project, root) =>
            {
                first.SelectOption("again"); var manager = StoryFlowManager.Instance;
                var save = manager.ExportState(); AssertTrue(first.CanGoBack(), "Save preserves history");
                AssertEqual(false, manager.ImportState("bad"), "active load guard retained"); AssertTrue(first.CanGoBack(), "failed load preserves history");
                var second = new StoryFlowComponent { UIStyle = BuiltInUIStyle.None, TraceEnabled = false };
                second.StartDialogue(); AssertEqual("multipleSessions", first.GetRollbackAvailability().Reason, "first loses ownership");
                AssertEqual(false, second.CanGoBack(), "second cannot rewind shared world"); second.StopDialogue();
                first.SelectOption("again"); AssertEqual(false, first.CanGoBack(), "next entry is fresh baseline");
                first.SelectOption("again"); AssertTrue(first.CanGoBack(), "exclusive session resumes history");
                StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Rollback");
                AssertEqual("contentChanged", first.GetRollbackAvailability().Reason, "reimport invalidates active session");
                first.SelectOption("again"); AssertEqual(false, first.CanGoBack(), "content replacement disables rest of session");
                first.StopDialogue(); AssertTrue(manager.ImportState(save), "stopped load retains established behavior");
                first.StartDialogue(); first.SelectOption("again"); AssertTrue(first.CanGoBack(), "next session relatches settings");
                manager.ResetAllState(); AssertEqual(false, first.CanGoBack(), "reset invalidates history");
            });
        }
        private static void RollbackRandomVectors()
        {
            var context = new StoryFlowExecutionContext { RollbackRandomState = 1 };
            foreach (uint expected in new uint[] {270369, 67634689, 2647435461}) AssertEqual(expected, context.NextRollbackRandom(), "xorshift vector");
            context.RollbackRandomState = 0; AssertEqual(270369u, context.NextRollbackRandom(), "zero seed normalizes");
        }
        private static void RollbackWideIntegerRandom()
        {
            var context = new StoryFlowExecutionContext { RollbackRandomState = 1 };
            foreach (int expected in new[] { -2147213280, -2079848960, 499951812 })
                AssertEqual(expected, context.RandomRange(int.MinValue, int.MaxValue), "wide signed range");
            context.RollbackRandomState = 1; AssertEqual(-1073539048, context.RandomRange(-1073741824, int.MaxValue), "wide mixed range");
            context.RollbackRandomState = 1; AssertEqual(3, context.RandomRange(3, 7), "small range");
            context.RollbackRandomState = 1; AssertEqual(-10, context.RandomRange(-10, -2), "negative range");
            AssertEqual(5, context.RandomRange(5, 5), "equal bounds");
            context.RollbackRandomState = 1; AssertEqual(9, context.RandomRange(10, 0), "existing reversed bounds semantics");
            context.RollbackRandomState = null; AssertEqual(5, context.RandomRange(5, 5), "disabled delegates engine RNG");
            AssertTrue(!context.RollbackRandomState.HasValue, "disabled leaves private RNG unset");
        }
        private static void RollbackBeforeLeave(bool advance, bool restart)
        {
            WithRollbackFixture("same-node", (c, p, root) => {
                if (advance) {
                    c.StopDialogue(); p.StartupScript.GetNode("A").Data["options"] = "[]";
                    p.StartupScript.Connections.Add(new StoryFlowConnection { Id="continue", Source="A", Target="set", SourceHandle=StoryFlowHandles.Source("A"), TargetHandle="target-set-0" });
                    p.StartupScript.SetConnections(new List<StoryFlowConnection>(p.StartupScript.Connections));
                    c.StartDialogue();
                }
                Action input = () => { if (advance) c.AdvanceDialogue(); else c.SelectOption("again"); };
                bool selected=false, replaced=false;
                c.OnDialogueUpdated += _ => { if (!selected && c.GetIntVariable("Visits", true)==1) { selected=true; input(); } };
                c.OnRollbackAvailabilityChanged += a => { if (!replaced && a.Reason=="busy") { replaced=true; c.StopDialogue(); if(restart) c.StartDialogue(); } };
                input(); AssertTrue(replaced, "BeforeLeave observer ran");
                AssertEqual(1, c.GetIntVariable("Visits",true), "obsolete input never executes ordinary set control");
                AssertEqual(restart, c.IsDialogueActive(), "observer lifecycle wins");
                var count=typeof(StoryFlowManager).GetField("_activeDialogueCount",BindingFlags.Instance|BindingFlags.NonPublic);
                AssertEqual(restart?1:0, (int)count.GetValue(StoryFlowManager.Instance), "current owner count");
                c.StopDialogue(); AssertEqual(0,(int)count.GetValue(StoryFlowManager.Instance),"final count");
            });
        }
        private static void RollbackDetachedBudget()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.StopDialogue(); project.DialogueRollback.HistoryLimit = 1; component.StartDialogue();
                component.SelectOption("again"); component.SelectOption("again");
                AssertEqual(1, component.GetRollbackAvailability().Steps, "count budget evicts oldest");
                var context = component.GetContext(); var live = new List<StoryFlowMapEntry> { new StoryFlowMapEntry { Key = StoryFlowVariant.String("a"), Value = StoryFlowVariant.Int(2) } };
                context.GlobalVariables["m1"] = new StoryFlowVariable { Type = StoryFlowVariableType.Map, Value = new StoryFlowVariant { MapValue = live } };
                context.GlobalVariables["m2"] = new StoryFlowVariable { Type = StoryFlowVariableType.Map, Value = new StoryFlowVariant { MapValue = live } };
                var snapshot = context.CaptureRollback(1, StoryFlowManager.Instance, StoryFlowRollbackController.PayloadLimit);
                live[0].Value.IntValue = 88;
                AssertEqual(2, snapshot.Globals["m1"].Value.MapValue[0].Value.IntValue, "nested payload detached");
                AssertTrue(ReferenceEquals(snapshot.Globals["m1"].Value.MapValue, snapshot.Globals["m2"].Value.MapValue), "semantic map alias preserved in snapshot");
                context.GlobalVariables["huge"] = new StoryFlowVariable { Value = StoryFlowVariant.String(new string('x', 17 * 1024 * 1024)) };
                component.SelectOption("again"); AssertEqual("budget", component.GetRollbackAvailability().Reason, "oversized entry rejected without serialization");
                AssertEqual(0L, Controller(component).EstimatedBytes, "failed capture releases history bytes");
                context.GlobalVariables.Remove("huge"); component.SelectOption("again"); component.SelectOption("again");
                AssertTrue(component.CanGoBack(), "eligible entry recovers after oversize");
            });
        }
        private static void RollbackHostCallbacks()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.StopDialogue();
                project.StartupScript.GetNode("A").Data["tags"] = "[\"host\"]";
                component.StartDialogue(); component.SelectOption("again");
                AssertTrue(component.CanGoBack(), "history before host callback");
                int tags = 0;
                component.OnDialogueTagReached += _ => { tags++; StoryFlowManager.Instance.SetCharacterVariableById("missing", "Name", StoryFlowVariant.String("host")); };
                component.SelectOption("again");
                AssertEqual(1, tags, "actual fresh dialogue executes tag listener");
                AssertEqual(false, component.CanGoBack(), "manager public setter from tag remains external");
            });
        }
        private static void RollbackReentrantInvalidation()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); bool once = false;
                component.OnRollbackAvailabilityChanged += value =>
                {
                    if (!once && !value.CanGoBack) { once = true; component.SelectOption("again"); component.SelectOption("again"); }
                };
                component.SetIntVariable("Visits", 42, true);
                AssertEqual(false, component.CanGoBack(), "availability observer cannot retain pre-write checkpoint");
            });
        }
        private static void RollbackRestartedMutation()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); bool once = false;
                StoryFlowRollbackAvailability lastAvailability = default;
                component.OnRollbackAvailabilityChanged += value =>
                {
                    lastAvailability = value;
                    if (!once && !value.CanGoBack) { once = true; component.StopDialogue(); component.StartDialogue(); }
                };
                component.SetIntVariable("Visits", 42, true);
                AssertEqual(component.GetRollbackAvailability().Reason, lastAvailability.Reason, "scope publishes its completed unavailable state");
                component.SelectOption("again");
                AssertEqual(false, component.CanGoBack(), "replacement session cannot retain a pre-write baseline");
                component.SelectOption("again"); AssertTrue(component.GoBack().Ok, "new post-write history works");
                AssertEqual(43, component.GetIntVariable("Visits", true), "Back preserves external write");
            });
        }
        private static void RollbackRestartedContent()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); bool once = false;
                component.OnRollbackAvailabilityChanged += value =>
                {
                    if (!once && value.Reason == "contentChanged") { once = true; component.StopDialogue(); component.StartDialogue(); }
                };
                StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Rollback", out _, force: true);
                component.SelectOption("again"); AssertEqual("contentChanged", component.GetRollbackAvailability().Reason, "session registered during replacement remains invalid");
                AssertEqual(false, component.GoBack().Ok, "pre-import history unavailable");
                component.StopDialogue(); component.StartDialogue(); component.SelectOption("again");
                AssertTrue(component.CanGoBack(), "session after completed replacement collects normally");
                once = false; StoryFlowManager.Instance.SetProject(project); component.SelectOption("again");
                AssertEqual("contentChanged", component.GetRollbackAvailability().Reason, "runtime project replacement covers observer-started session");
                AssertEqual(false, component.GoBack().Ok, "runtime replacement cannot revive old shared-state checkpoint");
            });
        }
        private static void RollbackRestartedStop()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); bool once = false;
                component.OnRollbackAvailabilityChanged += value => { if (!once && !value.CanGoBack) { once = true; component.StartDialogue(); } };
                component.StopDialogue(); AssertTrue(component.IsDialogueActive(), "observer restarts dialogue");
                component.SelectOption("again"); AssertTrue(component.GoBack().Ok, "outgoing cleanup preserves new owner and baseline");
                AssertEqual(1, component.GetIntVariable("Visits", true), "new session baseline restored");
            });
        }
        private static void RollbackStartReplacement()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); var manager = StoryFlowManager.Instance; bool once = false;
                var replacement = new StoryFlowScriptAsset { ScriptPath = "replacement.sfe" };
                replacement.SetNodes(new List<StoryFlowScriptAsset.SerializedNode> { new StoryFlowScriptAsset.SerializedNode { Id="0",Type=StoryFlowNodeType.Start }, new StoryFlowScriptAsset.SerializedNode { Id="REPLACEMENT",Type=StoryFlowNodeType.Dialogue } });
                replacement.SetConnections(new List<StoryFlowConnection> { new StoryFlowConnection { Id="start",Source="0",Target="REPLACEMENT",SourceHandle=StoryFlowHandles.Source("0"),TargetHandle="target-REPLACEMENT" } });
                project.Scripts[replacement.ScriptPath] = replacement;
                component.OnRollbackAvailabilityChanged += a => { if (!once && !a.CanGoBack) { once=true; component.StartDialogue(replacement); } };
                component.StartDialogue(); AssertEqual("REPLACEMENT", component.GetCurrentDialogue().NodeId, "callback replacement wins start boundary");
                var count = typeof(StoryFlowManager).GetField("_activeDialogueCount",BindingFlags.Instance|BindingFlags.NonPublic);
                AssertEqual(1, (int)count.GetValue(manager), "one component counted once"); component.StopDialogue();
                AssertEqual(0, (int)count.GetValue(manager), "final stop clears count"); AssertTrue(manager.ImportState(manager.ExportState()), "later Load succeeds");
                component.StartDialogue(); component.SelectOption("again"); AssertTrue(component.CanGoBack(), "later session owns rollback normally");
            });
        }
        private static void RollbackRegistrationReplacement()
        {
            WithRollbackFixture("same-node", (first, project, root) =>
            {
                first.SelectOption("again"); var second = new StoryFlowComponent { UIStyle=BuiltInUIStyle.None,TraceEnabled=false }; bool once=false;
                var replacement = new StoryFlowScriptAsset { ScriptPath="replacement.sfe" };
                replacement.SetNodes(new List<StoryFlowScriptAsset.SerializedNode> { new StoryFlowScriptAsset.SerializedNode { Id="0",Type=StoryFlowNodeType.Start }, new StoryFlowScriptAsset.SerializedNode { Id="REPLACEMENT",Type=StoryFlowNodeType.Dialogue } });
                replacement.SetConnections(new List<StoryFlowConnection> { new StoryFlowConnection { Id="start",Source="0",Target="REPLACEMENT",SourceHandle=StoryFlowHandles.Source("0"),TargetHandle="target-REPLACEMENT" } }); project.Scripts[replacement.ScriptPath]=replacement;
                first.OnRollbackAvailabilityChanged += a => { if (!once && !a.CanGoBack) { once=true; second.StartDialogue(replacement); } };
                second.StartDialogue(); AssertEqual("REPLACEMENT",second.GetCurrentDialogue().NodeId,"registration notification replacement wins");
                var manager=StoryFlowManager.Instance; var count=typeof(StoryFlowManager).GetField("_activeDialogueCount",BindingFlags.Instance|BindingFlags.NonPublic);
                AssertEqual(2,(int)count.GetValue(manager),"two real active components"); second.StopDialogue(); first.StopDialogue();
                AssertEqual(0,(int)count.GetValue(manager),"both final stops detach counted owners");
            });
        }
        private static void RollbackPresentationDependencies()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.StopDialogue(); var file = Path.Combine(root, "build", "main.json");
                var json = JObject.Parse(File.ReadAllText(file));
                json["nodes"]["A"]["character"] = "characters/legacy_hero.sfc";
                json["variables"]["asset"] = JObject.Parse("{'id':'asset','name':'Asset','type':'dataAsset','value':'child'}");
                json["variables"]["other"] = JObject.Parse("{'id':'other','name':'Other','type':'character','value':'da_friend'}");
                json["nodes"]["A"]["title"] = "A.title";
                json["nodes"]["A"]["textBlocks"] = JArray.Parse("[{'id':'block','text':'A.block'}]");
                json["strings"]["en"]["A.text"] = "Hello {Character.Name}: {Asset.Text} / {Other.Name}";
                foreach (var key in new[] { "A.title", "A.block", "again" }) json["strings"]["en"][key] = "{Asset.Text} / {Other.Name}";
                File.WriteAllText(file, json.ToString());
                var characters = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pre-character-index", "characters.json")));
                characters["characters"]["characters/friend.sfc"] = JObject.Parse("{'name':'friend.name','image':'','variables':{}}");
                characters["strings"]["en"]["friend.name"] = "Friend";
                File.WriteAllText(Path.Combine(root, "build", "characters.json"), characters.ToString());
                File.WriteAllText(Path.Combine(root, "build", "data-assets.json"), DataAssetLocalizationExport().ToString());
                File.WriteAllText(Path.Combine(root, "build", "character-index.json"), "{'schemaVersion':'1','characters':{'da_friend':'characters/friend.sfc'}}");
                File.WriteAllText(Path.Combine(root, "build", "localization.json"), "{'schemaVersion':'1','sourceLanguage':'en','languages':[{'code':'fr','name':'French'}],'strings':{'fr':{'char_hero_name':'Heros','friend.name':'Ami','data.child.text.value':'Texte enfant','A.text':'Bonjour {Character.Name}: {Asset.Text} / {Other.Name}','A.title':'{Asset.Text} / {Other.Name}','A.block':'{Asset.Text} / {Other.Name}','again':'{Asset.Text} / {Other.Name}'}}}");
                StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Rollback", out _, force: true);
                StoryFlowManager.Instance.SetProject(project); component.StartDialogue();
                // Target overlay differs from the live overlay at preparation time.
                StoryFlowManager.Instance.DataAssetOverlay["child"] = new Dictionary<string, StoryFlowVariant> { ["text"] = new StoryFlowVariant { Type = StoryFlowVariableType.String, StringValue = "Target overlay", IsLiteralString = true } };
                component.SelectOption("again"); StoryFlowManager.Instance.DataAssetOverlay["child"]["text"].StringValue = "Live overlay";
                component.SelectOption("again"); StoryFlowManager.Instance.SetLanguage("fr");
                AssertTrue(component.GoBack().Ok, "staged Back succeeds");
                var state = component.GetCurrentDialogue();
                AssertEqual("Bonjour Heros: Target overlay / Ami", state.Text, "body resolves localized characters and detached overlay");
                AssertEqual("Target overlay / Ami", state.Title, "title uses staged dependencies");
                AssertEqual("Target overlay / Ami", state.Options[0].Text, "option uses staged dependencies");
                AssertEqual("Target overlay / Ami", state.TextBlocks[0].Text, "text block uses staged dependencies");
                AssertEqual("Ami", component.GetContext().FindCharacter("da_friend").Name, "non-speaker authored name remains current");
                AssertTrue(component.GoBack().Ok, "Back to authored seed");
                AssertEqual("Bonjour Heros: Texte enfant / Ami", component.GetCurrentDialogue().Text, "empty detached overlay resolves current seed language");
            });
        }
        private static void RollbackCharacterNameScope()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.StopDialogue(); var file = Path.Combine(root, "build", "main.json");
                var json = JObject.Parse(File.ReadAllText(file));
                json["nodes"]["A"]["character"] = "characters/legacy_hero.sfc";
                json["variables"]["other"] = JObject.Parse("{'id':'other','name':'Other','type':'character','value':'da_friend'}");
                json["variables"]["missing"] = JObject.Parse("{'id':'missing','name':'Missing','type':'character','value':'characters/missing.sfc'}");
                json["nodes"]["A"]["title"] = "A.title";
                json["nodes"]["A"]["textBlocks"] = JArray.Parse("[{'id':'block','text':'A.block'}]");
                foreach (var key in new[] { "A.text", "A.title", "A.block", "again" })
                    json["strings"]["en"][key] = "{Character.Name} / {Other.Name} / {Missing.Name}";
                json["strings"]["en"]["char_hero_name"] = "SCRIPT HERO";
                json["strings"]["en"]["friend.name"] = "SCRIPT SHADOW";
                File.WriteAllText(file, json.ToString());
                var characters = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pre-character-index", "characters.json")));
                characters["characters"]["characters/friend.sfc"] = JObject.Parse("{'name':'friend.name','image':'','variables':{}}");
                characters["characters"]["characters/missing.sfc"] = JObject.Parse("{'name':'missing.name','image':'','variables':{}}");
                characters["strings"]["en"]["friend.name"] = "PROJECT FRIEND";
                File.WriteAllText(Path.Combine(root, "build", "characters.json"), characters.ToString());
                File.WriteAllText(Path.Combine(root, "build", "character-index.json"), "{'schemaVersion':'1','characters':{'da_friend':'characters/friend.sfc'}}");
                StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Rollback", out var report, force: true);
                AssertEqual(false, report.HasFailures, "name-scope graph imports");
                StoryFlowManager.Instance.SetProject(project); component.StartDialogue();
                const string expected = "Legacy Hero / PROJECT FRIEND / missing.name";
                AssertEqual(expected, component.GetCurrentDialogue().Text, "normal presentation uses project character names");
                component.GetContext().FindCharacter("characters/missing.sfc").Name = "STALE CACHED NAME";
                component.SelectOption("again"); component.SelectOption("again");
                AssertTrue(component.GoBack().Ok, "Back with shadowed script keys succeeds");
                var state = component.GetCurrentDialogue();
                AssertEqual(expected, state.Text, "restored body ignores script shadow and uses key fallback");
                AssertEqual(expected, state.Title, "restored title retains project scope");
                AssertEqual(expected, state.Options[0].Text, "restored option retains project scope");
                AssertEqual(expected, state.TextBlocks[0].Text, "restored text block retains project scope");
                AssertEqual("PROJECT FRIEND", component.GetContext().FindCharacter("da_friend").Name, "committed non-speaker name ignores script shadow");
                AssertEqual("missing.name", component.GetContext().FindCharacter("characters/missing.sfc").Name, "missing project name falls back to authored key");
            });
        }
        private static void RollbackControlRecovery()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); component.PauseDialogue();
                var context = component.GetContext(); context.ShouldPause = false;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(StoryFlowComponent).GetField("_dialogueAudioSource", flags).SetValue(component, new UnityEngine.AudioSource());
                var clip = new UnityEngine.AudioClip(); component.PlayDialogueAudio(clip, false); component.SetAudioAdvanceState(true, true);
                var generation = typeof(StoryFlowComponent).GetField("mediaGeneration", flags).GetValue(component);
                StoryFlowManager.CommitFault = () => { StoryFlowManager.CommitFault = null; throw new InvalidOperationException("recovery probe"); };
                try
                {
                    AssertEqual(false, component.GoBack().Ok, "failed application refuses Back");
                    AssertEqual(true, component.IsPaused(), "failure preserves immediate pause");
                    AssertEqual(false, context.ShouldPause, "failure preserves execution control");
                    AssertTrue(ReferenceEquals(clip, component.CurrentDialogueAudioClip), "failure preserves nonloop voice");
                    AssertEqual(true, (bool)typeof(StoryFlowComponent).GetField("_waitingForAudioAdvance", flags).GetValue(component), "failure preserves waiting");
                    AssertEqual(true, (bool)typeof(StoryFlowComponent).GetField("_audioAdvanceAllowSkip", flags).GetValue(component), "failure preserves skip");
                    AssertEqual(generation, typeof(StoryFlowComponent).GetField("mediaGeneration", flags).GetValue(component), "failure preserves pending delay generation");
                }
                finally { StoryFlowManager.CommitFault = null; }
            });
        }
        private static void RollbackRestoreObserver()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); component.SelectOption("again");
                int restored = 0, ordinary = 0; StoryFlowRollbackResult nested = default;
                component.OnDialogueUpdated += _ => ordinary++;
                component.OnDialogueRestored += _ => { restored++; nested = component.GoBack(); };
                AssertTrue(component.GoBack().Ok, "outer restore succeeds");
                AssertEqual(1, restored, "exactly one restoration event"); AssertEqual(0, ordinary, "ordinary events not replayed");
                AssertEqual("busy", nested.Reason, "recursive Back from restored callback gated");
            });
        }
        private static void RollbackPrepareFailure()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again");
                var controller = Controller(component);
                var history = (List<StoryFlowExecutionSnapshot>)typeof(StoryFlowRollbackController).GetField("history", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(controller);
                history[0].Node = "missing"; var count = history.Count;
                AssertEqual(false, component.GoBack().Ok, "invalid target refused");
                AssertEqual(1, component.GetIntVariable("Visits", true), "preparation failure leaves live world unchanged");
                AssertEqual(count, history.Count, "preparation failure retains history unchanged");
            });
        }
        private static void RollbackCharacterIdentity()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                var context = component.GetContext(); var character = new StoryFlowCharacterData { Name = "Before", NameKey = "", ImageAssetKey = "old" };
                context.Characters["hero"] = character;
                component.SelectOption("again"); character.Name = "After";
                component.SelectOption("again"); AssertTrue(component.GoBack().Ok, "Back succeeds");
                AssertTrue(ReferenceEquals(character, context.Characters["hero"]), "host-retained character object remains current");
                AssertEqual("Before", character.Name, "host-retained character gets restored values");
            });
        }
        private static void RollbackCurrentLanguage()
        {
            WithRollbackFixture("purchase", (component, project, root) =>
            {
                var node = component.GetContext().CurrentScript.GetNode("A");
                var optionKey = (string)JArray.Parse(node.GetData("options"))[0]["text"];
                project.SetLocalization(true, "en", new List<StoryFlowProjectAsset.LanguageEntry> { new StoryFlowProjectAsset.LanguageEntry { Code = "fr", Name = "French" } },
                    new List<StoryFlowProjectAsset.LanguageStringEntry> { new StoryFlowProjectAsset.LanguageStringEntry { Language = "fr", Key = node.GetData("text"), Value = "Bonjour" },
                    new StoryFlowProjectAsset.LanguageStringEntry { Language = "fr", Key = optionKey, Value = "Acheter" } });
                component.SelectOption("buy"); StoryFlowManager.Instance.SetLanguage("fr");
                var before = component.GetContext().RollbackRandomState; AssertTrue(component.GoBack().Ok, "Back succeeds in current language");
                AssertEqual("Bonjour", component.GetCurrentDialogue().Text, "text resolves in current language");
                AssertEqual("Acheter", component.GetCurrentDialogue().Options[0].Text, "retained options resolve in current language");
                AssertEqual(before, component.GetContext().RollbackRandomState, "presentation does not draw random");
            });
        }
        private static void RollbackLoopingAudio()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                typeof(StoryFlowComponent).GetField("_dialogueAudioSource", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(component, new UnityEngine.AudioSource());
                var clip = new UnityEngine.AudioClip(); component.PlayDialogueAudio(clip, true);
                component.SelectOption("again"); component.PlayDialogueAudio(new UnityEngine.AudioClip(), false);
                component.SelectOption("again"); AssertTrue(component.GoBack().Ok, "loop presentation restore succeeds");
                AssertTrue(ReferenceEquals(clip, component.CurrentDialogueAudioClip), "persistent looping channel restored without replaying voice");
            });
        }
        private static void RollbackCommitFailure()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again");
                component.GetContext().FindVariableByName("Visits", false, true).Value.IntValue = 77;
                StoryFlowManager.CommitFault = () => { StoryFlowManager.CommitFault = null; throw new InvalidOperationException("injected commit failure"); };
                try
                {
                    AssertEqual(false, component.GoBack().Ok, "injected commit failure refused");
                    AssertEqual(77, component.GetIntVariable("Visits", true), "recovery uses immediate live state, not old current checkpoint");
                    AssertTrue(component.IsDialogueActive(), "recovered session remains coherent");
                    AssertEqual("restoreFailed", component.GetRollbackAvailability().Reason, "failed target disabled");
                }
                finally { StoryFlowManager.CommitFault = null; }
            });
        }
        private static void RollbackRootEnd()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.SelectOption("again"); AssertTrue(component.CanGoBack(), "history exists before root End");
                component.ProcessNode(new StoryFlowNode { Id = "end", Type = StoryFlowNodeType.End });
                AssertEqual(false, component.IsDialogueActive(), "root End stops session");
                AssertTrue(Controller(component) == null, "root End releases history and owner");
                AssertEqual(false, StoryFlowManager.Instance.IsDialogueActive(), "manager owner count released");
            });
        }
        private static JObject RegressionEdge(string source, string target, string output = "", string input = "0") => new JObject {
            ["id"] = source + output + target + input, ["source"] = source, ["target"] = target,
            ["sourceHandle"] = "source-" + source + "-" + output, ["targetHandle"] = "target-" + target + "-" + input };
        private static void RollbackCharacterDataExecution()
        {
            WithRollbackFixture("purchase", (component, project, root) =>
            {
                component.StopDialogue(); var file = Path.Combine(root, "build", "main.json"); var json = JObject.Parse(File.ReadAllText(file));
                var nodes = (JObject)json["nodes"]; var edges = (JArray)json["connections"];
                nodes["A"]["character"] = "characters/legacy_hero.sfc"; nodes["B"]["character"] = "characters/legacy_hero.sfc";
                nodes["charwrite"] = JObject.Parse("{'id':'charwrite','type':'setCharacterVar','characterPath':'characters/legacy_hero.sfc','variableName':'Name','variableType':'string'}");
                nodes["textvalue"] = JObject.Parse("{'id':'textvalue','type':'getString','variable':'literal'}");
                nodes["asset"] = JObject.Parse("{'id':'asset','type':'getDataAsset','assetId':'child'}");
                nodes["datawrite"] = JObject.Parse("{'id':'datawrite','type':'setDataAssetVariable','variableId':'text','variableType':'string'}");
                foreach (var edge in edges) if ((string)edge["source"] == "count" && (string)edge["target"] == "B") { edge["target"] = "charwrite"; edge["targetHandle"] = "target-charwrite-0"; }
                edges.Add(RegressionEdge("charwrite", "datawrite", "1")); edges.Add(RegressionEdge("datawrite", "B", "1"));
                edges.Add(RegressionEdge("textvalue", "charwrite", "string-", "string"));
                edges.Add(RegressionEdge("asset", "datawrite", "dataAsset-", "dataAsset-asset"));
                edges.Add(RegressionEdge("textvalue", "datawrite", "string-", "string-2"));
                json["variables"]["literal"] = JObject.Parse("{'id':'literal','name':'Literal','type':'string','value':'new.literal'}");
                json["strings"]["en"]["new.literal"] = "text.value"; File.WriteAllText(file, json.ToString());
                File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pre-character-index", "characters.json"), Path.Combine(root, "build", "characters.json"), true);
                File.WriteAllText(Path.Combine(root, "build", "data-assets.json"), DataAssetLocalizationExport().ToString());
                File.WriteAllText(Path.Combine(root, "build", "localization.json"), @"{'schemaVersion':'1','sourceLanguage':'en','languages':[{'code':'fr','name':'French'}],
                    'strings':{'fr':{'char_hero_name':'Heros','data.child.text.value':'Texte enfant','text.value':'Texte de base'}}}");
                StoryFlowImporter.ImportProject(Path.Combine(root, "build"), "Assets/Rollback", out _, force:true);
                StoryFlowManager.Instance.SetProject(project); component.StartDialogue();
                var character = component.GetCurrentDialogue().Character;
                AssertEqual("Legacy Hero", character.Name, "A has authored character name");
                AssertEqual("Child text", ReadLocalizedDataAsset("child", "text").GetString(), "A has authored inherited data value");
                component.SelectOption("buy");
                AssertEqual("B", component.GetCurrentDialogue().Text, "real mutation chain reaches B");
                AssertEqual("text.value", character.Name, "character write becomes literal");
                AssertEqual("text.value", ReadLocalizedDataAsset("child", "text").GetString(), "Data Asset write becomes literal");
                AssertTrue(string.IsNullOrEmpty(character.NameKey), "written character loses authored ownership");
                StoryFlowManager.Instance.SetLanguage("fr");
                AssertEqual("text.value", character.Name, "literal character survives language change");
                AssertEqual("text.value", ReadLocalizedDataAsset("child", "text").GetString(), "literal overlay survives language change");
                AssertTrue(component.GoBack().Ok, "Back restores full A-owned state");
                AssertEqual("Heros", character.Name, "authored character restores in current language");
                AssertEqual("char_hero_name", character.NameKey, "character ownership restored");
                AssertEqual("Texte enfant", ReadLocalizedDataAsset("child", "text").GetString(), "overlay removal reveals current localized authored value");
                AssertEqual(0, StoryFlowManager.Instance.DataAssetOverlay.Count, "story overlay reverted");
                component.SelectOption("buy"); AssertEqual("text.value", ReadLocalizedDataAsset("child", "text").GetString(), "fresh traversal repeats data write");
            });
        }
        private static void RollbackRecursiveExecution()
        {
            WithRollbackFixture("same-node", (component, project, root) =>
            {
                component.StopDialogue();
                var json = JObject.Parse(@"{'startNode':'0','nodes':{
                  '0':{'id':'0','type':'start'}, 'A':{'id':'A','type':'dialogue','text':'A.text'},
                  'branch':{'id':'branch','type':'branch'}, 'depth':{'id':'depth','type':'getInt','variable':'depth'},
                  'gt':{'id':'gt','type':'greaterThan','value2':0}, 'dec':{'id':'dec','type':'minus','value2':1},
                  'call':{'id':'call','type':'runScript','script':'main.json','scriptInterface':{'parameters':[{'id':'in','name':'Depth','type':'integer'}],'outputs':[{'id':'out','name':'Result','type':'integer'}]}},
                  'plus':{'id':'plus','type':'plus','value2':1},'set':{'id':'set','type':'setInt','variable':'result'},
                  'base':{'id':'base','type':'setInt','variable':'result','value':10},
                  'Returned':{'id':'Returned','type':'dialogue','text':'R.text'},'end':{'id':'end','type':'end'}},
                  'connections':[], 'variables':{'depth':{'id':'depth','name':'Depth','type':'integer','value':2,'isInput':true},'result':{'id':'result','name':'Result','type':'integer','value':0,'isOutput':true}},
                  'strings':{'en':{'A.text':'Depth {Depth}','R.text':'Result {Result}'}},'assets':{}}");
                var edges = (JArray)json["connections"];
                foreach (var edge in new[] { RegressionEdge("0","A"), RegressionEdge("A","branch"), RegressionEdge("depth","gt","integer-","integer-1"),
                    RegressionEdge("gt","branch","boolean-","boolean-condition"), RegressionEdge("branch","call","true"), RegressionEdge("branch","base","false"),
                    RegressionEdge("depth","dec","integer-","integer-1"), RegressionEdge("dec","call","integer-","integer-param-in"),
                    RegressionEdge("call","set","output"), RegressionEdge("call","plus","integer-out-out","integer-1"), RegressionEdge("plus","set","integer-","integer-2"),
                    RegressionEdge("base","Returned","1"), RegressionEdge("set","Returned","1"), RegressionEdge("Returned","end") }) edges.Add(edge);
                File.WriteAllText(Path.Combine(root,"build","main.json"), json.ToString());
                StoryFlowImporter.ImportProject(Path.Combine(root,"build"),"Assets/Rollback",out _, force:true);
                StoryFlowManager.Instance.SetProject(project); component.StartDialogue();
                AssertEqual("Depth 2", component.GetCurrentDialogue().Text, "recursive root locals");
                component.AdvanceDialogue(); AssertEqual("Depth 1", component.GetCurrentDialogue().Text, "first recursive activation input");
                component.AdvanceDialogue(); AssertEqual("Depth 0", component.GetCurrentDialogue().Text, "second recursive activation input");
                AssertTrue(component.GoBack().Ok, "Back across recursive entry"); AssertEqual(1, component.GetContext().CallStackDepth, "recursive frame count restored");
                AssertEqual(1, component.GetIntVariable("Depth"), "recursive locals restored");
                component.AdvanceDialogue(); component.AdvanceDialogue(); AssertEqual("Result 10", component.GetCurrentDialogue().Text, "base activation output");
                component.AdvanceDialogue(); AssertEqual("Result 11", component.GetCurrentDialogue().Text, "caller receives typed return output");
                AssertTrue(component.GoBack().Ok, "Back restores callee before return"); AssertEqual(2, component.GetContext().CallStackDepth, "both caller frames restored");
                component.AdvanceDialogue(); AssertEqual("Result 11", component.GetCurrentDialogue().Text, "return output reproduced");
                component.AdvanceDialogue(); AssertEqual("Result 12", component.GetCurrentDialogue().Text, "root output preserves recursive cache identities");
                AssertEqual(2, component.GetIntVariable("Depth"), "root input local preserved across recursive returns");
            });
        }
        private static void RollbackSettingsContract()
        {
            var type = typeof(StoryFlowProjectAsset).Assembly.GetType("StoryFlow.Data.StoryFlowRollbackSettings");
            AssertTrue(type != null, "rollback settings are imported and serialized");
            if (type == null) return;
            var parse = type.GetMethod("Normalize", new[] { typeof(JToken) });
            foreach (var json in new[] { "null", "true", "{}", "{ 'version':2, 'enabled':true }", "{ 'version':1, 'enabled':'true', 'historyLimit':0 }" })
            {
                var settings = parse.Invoke(null, new object[] { JToken.Parse(json) });
                AssertEqual(false, type.GetField("Enabled").GetValue(settings), "invalid settings disabled");
                AssertEqual(100, type.GetField("HistoryLimit").GetValue(settings), "invalid limit defaults");
            }
            var valid = parse.Invoke(null, new object[] { JToken.Parse("{'version':1,'enabled':true,'historyLimit':3}") });
            AssertEqual(true, type.GetField("Enabled").GetValue(valid), "enabled preserved");
            AssertEqual(3, type.GetField("HistoryLimit").GetValue(valid), "limit preserved");
        }
        private static void RollbackPublicSurface()
        {
            foreach (var name in new[] { "CanGoBack", "GetRollbackAvailability", "GoBack", "BlockRollback" })
                AssertTrue(typeof(StoryFlowComponent).GetMethod(name) != null, "component supports " + name);
            AssertTrue(typeof(StoryFlowComponent).GetEvent("OnDialogueRestored") != null, "dedicated restoration event");
        }
        private static void RollbackActivationIsolation()
        {
            var ctx = new StoryFlowExecutionContext();
            ctx.PushLoop(new LoopContext("loop", 2, "integer"));
            ctx.GetNodeRuntimeState("same").CachedOutput = StoryFlowVariant.Int(42);
            ctx.GetNodeRuntimeState("same").HasExecutionOutput = true;
            ctx.PushCallFrame("return");
            AssertEqual(0, ctx.LoopStackDepth, "callee has its own loop stack");
            AssertTrue(ctx.GetNodeRuntimeState("same").CachedOutput == null, "callee cache does not alias caller");
            ctx.GetNodeRuntimeState("same").CachedOutput = StoryFlowVariant.Int(99);
            ctx.PopCallFrame();
            AssertEqual(1, ctx.LoopStackDepth, "caller loop restored");
            AssertEqual(42, ctx.GetNodeRuntimeState("same").CachedOutput.GetInt(), "caller output restored");
        }
    }
}
