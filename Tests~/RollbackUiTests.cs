#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using StoryFlow.Data;
using StoryFlow.Editor;
using StoryFlow.UI;
using StoryFlow.Lipsync;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace StoryFlow.Tests
{
    public class RollbackUiTests
    {
        private static StoryFlow.Execution.StoryFlowExecutionContext Context(StoryFlowComponent component) => (StoryFlow.Execution.StoryFlowExecutionContext)typeof(StoryFlowComponent).GetMethod("GetContext", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
        private readonly List<GameObject> objects = new List<GameObject>();
        private StoryFlowProjectAsset Import(string name) => StoryFlowImporter.ImportProject(
            Path.Combine(Directory.GetCurrentDirectory(), "RollbackFixtures", name), "Assets/RollbackImported/" + name);
        private GameObject NewObject(string name, params Type[] types)
        { var go = new GameObject(name, types); objects.Add(go); return go; }
        private StoryFlowComponent Start(StoryFlowProjectAsset project)
        {
            var manager = StoryFlowManager.Instance != null ? StoryFlowManager.Instance : NewObject("Manager").AddComponent<StoryFlowManager>();
            typeof(StoryFlowManager).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { manager });
            manager.SetProject(project);
            var component = NewObject("Dialogue").AddComponent<StoryFlowComponent>();
            component.UIStyle = BuiltInUIStyle.None; component.TraceEnabled = false; component.StartDialogue(); return component;
        }
        [TearDown] public void TearDown()
        { for (int i = objects.Count - 1; i >= 0; --i) if (objects[i]) Object.DestroyImmediate(objects[i]); objects.Clear(); typeof(StoryFlowManager).GetProperty("Instance").GetSetMethod(true).Invoke(null, new object[] { null }); }

        [TestCase("back")][TestCase("block")][TestCase("stop")][TestCase("restart")][TestCase("disabled")]
        public void AvailabilityFinishesWithLiveStateAfterObserverMutation(string action)
        {
            var p = Import("same-node"); var c = Start(p); bool changed = false;
            var observed = new List<StoryFlowRollbackAvailability>();
            c.OnRollbackAvailabilityChanged += a => {
                if (changed || !a.CanGoBack) return;
                changed = true;
                if (action == "back") Assert.That(c.GoBack().Ok, Is.True);
                else if (action == "block") c.BlockRollback();
                else { c.StopDialogue(); if (action != "stop") { p.DialogueRollback.Enabled = action != "disabled"; c.StartDialogue(); } }
            };
            c.OnRollbackAvailabilityChanged += a => observed.Add(a);
            c.SelectOption("again");
            Assert.That(changed, Is.True); Assert.That(observed.Count, Is.GreaterThanOrEqualTo(2));
            var last = observed[observed.Count - 1]; var live = c.GetRollbackAvailability();
            Assert.That(last.CanGoBack, Is.False); Assert.That(last.CanGoBack, Is.EqualTo(live.CanGoBack));
            Assert.That(last.Steps, Is.EqualTo(live.Steps)); Assert.That(last.Reason, Is.EqualTo(live.Reason));
        }
        [TestCase(false)][TestCase(true)]
        public void ReturnedCallerGetterSeesCalleeGlobalWrite(bool enabled)
        {
            var p=Import("same-node"); p.DialogueRollback.Enabled=enabled; var c=Start(p); var ctx=Context(c); var getter=p.StartupScript.GetNode("get");
            Assert.That(StoryFlow.Execution.StoryFlowEvaluator.EvaluateIntegerFromNode(ctx,getter),Is.Zero);
            ctx.PushCallFrame("call"); ctx.FindVariableByName("Visits",false,true).Value.SetInt(5);
            Assert.That(StoryFlow.Execution.StoryFlowEvaluator.EvaluateIntegerFromNode(ctx,getter),Is.EqualTo(5)); ctx.PopCallFrame();
            Assert.That(StoryFlow.Execution.StoryFlowEvaluator.EvaluateIntegerFromNode(ctx,getter),Is.EqualTo(5),"restored caller must discard derived memo");
        }
        [TestCase("normal",true)][TestCase("normal",false)][TestCase("stop",true)][TestCase("restart",true)][TestCase("replace",true)]
        public void GraphBarrierRejectsObsoleteContinuation(string action,bool enabled)
        {
            var p=Import("barrier"); p.DialogueRollback.Enabled=enabled; var c=Start(p); c.AdvanceDialogue(); bool once=false; StoryFlowScriptAsset replacement=null;
            if(action=="replace") { replacement=Object.Instantiate(p.StartupScript); replacement.ScriptPath="replacement.sfe"; p.Scripts[replacement.ScriptPath]=replacement; }
            c.OnRollbackAvailabilityChanged+=a=> { if(once || a.CanGoBack || action=="normal")return; once=true;
                if(action=="stop")c.StopDialogue(); else c.StartDialogue(replacement!=null?replacement:p.StartupScript); };
            c.AdvanceDialogue();
            if(action=="normal")Assert.That(c.GetCurrentDialogue().NodeId,Is.EqualTo("C"));
            else { Assert.That(once,Is.True); Assert.That(c.IsDialogueActive(),Is.EqualTo(action!="stop")); Assert.That(Context(c).NextNode,Is.Null,"old continuation never queued"); c.ResumeExecution();
                if(action!="stop")Assert.That(c.GetCurrentDialogue().NodeId,Is.EqualTo("A")); }
            if(replacement)Object.DestroyImmediate(replacement);
        }
        [TestCase("availability","advance")][TestCase("availability","stop")][TestCase("availability","restart")][TestCase("availability","replace")]
        [TestCase("restored","advance")][TestCase("restored","stop")][TestCase("restored","restart")][TestCase("restored","replace")]
        [TestCase("availability","multi")][TestCase("restored","multi")]
        [TestCase("restored","nested")][TestCase("none","none")]
        public void PublicRestoredEventOnlyReachesCurrentEntry(string phase,string action)
        {
            var p=Import("barrier"); var c=Start(p); c.AdvanceDialogue(); bool once=false; int later=0; StoryFlowDialogueState actual=null; StoryFlowScriptAsset replacement=null;
            if(action=="replace") { replacement=Object.Instantiate(p.StartupScript); replacement.ScriptPath="replacement.sfe"; p.Scripts[replacement.ScriptPath]=replacement; }
            StoryFlowComponent second=null;
            Action change=()=> { if(once)return; once=true; if(action=="multi") { second=NewObject("Second").AddComponent<StoryFlowComponent>(); second.UIStyle=BuiltInUIStyle.None; second.TraceEnabled=false; second.StartDialogue(); } else if(action=="advance")c.AdvanceDialogue(); else if(action=="stop")c.StopDialogue();
                else if(action=="nested")Assert.That(c.GoBack().Reason,Is.EqualTo("busy")); else c.StartDialogue(replacement!=null?replacement:p.StartupScript); };
            if(phase=="availability")c.OnRollbackAvailabilityChanged+=a=> { if(!a.CanGoBack)change(); };
            else if(phase=="restored")c.OnDialogueRestored+=state=> { actual=state; Assert.That(state.NodeId,Is.EqualTo("A")); change(); };
            c.OnDialogueRestored+=state=> { later++; Assert.That(state.NodeId,Is.EqualTo("A")); if(actual!=null)Assert.That(state,Is.SameAs(actual)); };
            Assert.That(c.GoBack().Ok,Is.True); Assert.That(later,Is.EqualTo(action=="nested" || phase=="none" ? 1:0)); second?.StopDialogue();
            if(replacement)Object.DestroyImmediate(replacement);
        }
        [TestCase(false, false)][TestCase(false, true)][TestCase(true, false)][TestCase(true, true)]
        public void BeforeLeaveNotificationCannotExecuteObsoleteInput(bool advance, bool restart)
        {
            var c=Start(Import("same-node")); var p=StoryFlowManager.Instance.Project;
            if(advance) { c.StopDialogue(); p.StartupScript.GetNode("A").Data["options"]="[]";
                p.StartupScript.Connections.Add(new StoryFlowConnection { Id="continue",Source="A",Target="set",SourceHandle=StoryFlowHandles.Source("A"),TargetHandle="target-set-0" }); p.StartupScript.SetConnections(new List<StoryFlowConnection>(p.StartupScript.Connections)); c.StartDialogue(); }
            Action input=()=> { if(advance)c.AdvanceDialogue(); else c.SelectOption("again"); }; bool selected=false,replaced=false;
            c.OnDialogueUpdated+=_=> { if(!selected && c.GetIntVariable("Visits",true)==1) { selected=true; input(); } };
            c.OnRollbackAvailabilityChanged+=a=> { if(!replaced && a.Reason=="busy") { replaced=true; c.StopDialogue(); if(restart)c.StartDialogue(); } };
            input(); Assert.That(replaced,Is.True); Assert.That(c.GetIntVariable("Visits",true),Is.EqualTo(1)); Assert.That(c.IsDialogueActive(),Is.EqualTo(restart));
            var count=typeof(StoryFlowManager).GetField("_activeDialogueCount",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(count.GetValue(StoryFlowManager.Instance),Is.EqualTo(restart?1:0)); c.StopDialogue(); Assert.That(count.GetValue(StoryFlowManager.Instance),Is.Zero);
        }
        [TestCase(false)][TestCase(true)]
        public void RestoredAudioRedrawKeepsContinueVisible(bool portrait)
        {
            var c=Start(Import("barrier")); c.StopDialogue(); var p=StoryFlowManager.Instance.Project;
            var node=p.StartupScript.GetNode("A"); node.Data["audioAdvanceOnEnd"]="true"; node.Data["audioAllowSkip"]="false"; node.Data["audio"]="voice";
            var clip=AudioClip.Create("Voice",44100,1,44100,false); p.ResolvedAssets["voice"]=clip;
            var ui=portrait ? (StoryFlowDialogueUI)NewObject("Portrait UI").AddComponent<StoryFlowRuntimeUIPortrait>() : NewObject("Runtime UI").AddComponent<StoryFlowRuntimeUI>();
            var settingsField=typeof(TMPro.TMP_Settings).GetField("s_Instance",BindingFlags.Static|BindingFlags.NonPublic);
            var oldSettings=settingsField.GetValue(null); var settings=ScriptableObject.CreateInstance<TMPro.TMP_Settings>(); settingsField.SetValue(null,settings);
            ui.InitializeWithComponent(c);
            try {
                c.StartDialogue(); c.SetAudioAdvanceState(false,false); c.AdvanceDialogue(); ClearEditModeUiButtons(ui); Assert.That(c.GoBack().Ok,Is.True);
                Context(c).GlobalVariables["numbers"]=new StoryFlowVariable {Id="numbers",Name="Numbers",Type=StoryFlowVariableType.Integer,IsArray=true,Value=new StoryFlowVariant {Type=StoryFlowVariableType.Integer,ArrayValue=new List<StoryFlowVariant>()}};
                ClearEditModeUiButtons(ui); c.SetIntArrayVariable("Numbers",new List<int>{42},true);
                var state=c.GetCurrentDialogue(); Assert.That(state.AudioAdvanceOnEnd,Is.False); Assert.That(state.AudioAllowSkip,Is.False); Assert.That(c.CurrentDialogueAudioClip,Is.Null);
                var buttons=(System.Collections.ICollection)ui.GetType().GetField("_optionButtons",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
                Assert.That(buttons.Count,Is.EqualTo(1),"restored redraw exposes Continue");
                ClearEditModeUiButtons(ui); c.AdvanceDialogue(); Assert.That(c.GetCurrentDialogue().NodeId,Is.EqualTo("B"));
                ClearEditModeUiButtons(ui); c.StartDialogue(); Assert.That(c.GetCurrentDialogue().AudioAdvanceOnEnd,Is.True,"fresh entry recovers authored audio gating");
            } finally {
                ClearEditModeUiButtons(ui); c.StopDialogue();
                foreach(var fieldName in new[]{"_canvasObj","_spriteRoot"}) {
                    var field=ui.GetType().GetField(fieldName,BindingFlags.Instance|BindingFlags.NonPublic);
                    if(field!=null) { var owned=field.GetValue(ui) as GameObject; if(owned)Object.DestroyImmediate(owned); field.SetValue(ui,null); }
                }
                Object.DestroyImmediate(ui.gameObject); settingsField.SetValue(null,oldSettings); Object.DestroyImmediate(settings); Object.DestroyImmediate(clip);
            }
        }
        private static void ClearEditModeUiButtons(StoryFlowDialogueUI ui)
        {
            // Runtime UIs use deferred Destroy, which is unavailable in EditMode.
            var buttons=(List<GameObject>)ui.GetType().GetField("_optionButtons",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(ui);
            foreach(var button in buttons) if(button)Object.DestroyImmediate(button);
            buttons.Clear();
        }
        [Test]
        [TestCase("restored")][TestCase("availability")][TestCase("unchanged")]
        public void PersistentUiRejectsRestoredCallbackFromPreviousBinding(string trigger)
        {
            var component = Start(Import("same-node")); var project = StoryFlowManager.Instance.Project;
            var original = TestScript(project, "original.sfe", "ORIGINAL");
            var replacement = TestScript(project, "replacement.sfe", "REPLACEMENT");
            var second = NewObject("Replacement").AddComponent<StoryFlowComponent>(); second.UIStyle = BuiltInUIStyle.None; second.TraceEnabled = false;
            var ui = NewObject("Persistent UI").AddComponent<RollbackPersistentUiSpy>(); bool rebound = false;
            component.StartDialogue(original); component.AdvanceDialogue();
            Action rebind = () => { if (rebound) return; rebound = true; ui.InitializeWithComponent(second); second.StartDialogue(replacement); };
            if (trigger == "restored") component.OnDialogueRestored += _ => rebind();
            if (trigger == "availability") component.OnRollbackAvailabilityChanged += a => { if (!a.CanGoBack && component.GetCurrentDialogue().NodeId == "A") rebind(); };
            ui.InitializeWithComponent(component);
            Assert.That(component.GoBack().Ok, Is.True);
            if (trigger == "unchanged") { Assert.That(ui.IsBoundTo(component), Is.True); Assert.That(ui.Showing, Is.EqualTo("ORIGINAL")); }
            else { Assert.That(ui.IsBoundTo(second), Is.True); Assert.That(ui.Showing, Is.EqualTo(second.GetCurrentDialogue().Text)); Assert.That(ui.Showing, Is.EqualTo("REPLACEMENT")); }
            Object.DestroyImmediate(original); Object.DestroyImmediate(replacement);
        }
        private static StoryFlowScriptAsset TestScript(StoryFlowProjectAsset project, string path, string text)
        {
            var script = ScriptableObject.CreateInstance<StoryFlowScriptAsset>(); script.ScriptPath = path;
            script.SetNodes(new List<StoryFlowScriptAsset.SerializedNode> {
                new StoryFlowScriptAsset.SerializedNode { Id = "0", Type = StoryFlowNodeType.Start },
                new StoryFlowScriptAsset.SerializedNode { Id = "A", Type = StoryFlowNodeType.Dialogue, Data = new List<StoryFlowScriptAsset.SerializedKV> { new StoryFlowScriptAsset.SerializedKV { Key = "text", Value = text } } } });
            script.SetConnections(new List<StoryFlowConnection> {
                new StoryFlowConnection { Id = "start", Source = "0", Target = "A", SourceHandle = StoryFlowHandles.Source("0"), TargetHandle = "target-A" },
                new StoryFlowConnection { Id = "again", Source = "A", Target = "A", SourceHandle = StoryFlowHandles.Source("A"), TargetHandle = "target-A" } });
            script.SetStrings(new List<StoryFlowScriptAsset.SerializedString> { new StoryFlowScriptAsset.SerializedString { Key = "en." + text, Value = text } });
            project.Scripts[path] = script; return script;
        }
        [TestCase(true)][TestCase(false)]
        public void ReentrantStartKeepsOneCountedSession(bool enabled)
        {
            var project = Import("same-node"); project.DialogueRollback.Enabled = enabled;
            var component = Start(project); component.SelectOption("again"); var manager = StoryFlowManager.Instance;
            var replacement = TestScript(project, "replacement.sfe", "REPLACEMENT"); bool restarted = false;
            Action restart = () => { if (restarted) return; restarted = true; component.StartDialogue(replacement); };
            if (enabled) component.OnRollbackAvailabilityChanged += a => { if (!a.CanGoBack) restart(); };
            else component.OnDialogueEnded += restart;
            component.StartDialogue();
            Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo("REPLACEMENT"));
            var count = typeof(StoryFlowManager).GetField("_activeDialogueCount", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(count.GetValue(manager), Is.EqualTo(1));
            component.StopDialogue(); Assert.That(count.GetValue(manager), Is.Zero); Assert.That(manager.IsDialogueActive(), Is.False);
            Assert.That(manager.ImportState(manager.ExportState()), Is.True); component.StartDialogue(); Assert.That(count.GetValue(manager), Is.EqualTo(1)); component.StopDialogue();
            Assert.That(count.GetValue(manager), Is.Zero); Object.DestroyImmediate(replacement);
        }
        [Test]
        public void RegistrationCallbackReplacementKeepsExactNativeCount()
        {
            var first = Start(Import("same-node")); first.SelectOption("again"); var manager = StoryFlowManager.Instance;
            var replacement = TestScript(manager.Project, "replacement.sfe", "REPLACEMENT");
            var second = NewObject("Second").AddComponent<StoryFlowComponent>(); second.UIStyle = BuiltInUIStyle.None; second.TraceEnabled = false;
            bool once = false;
            first.OnRollbackAvailabilityChanged += a => { if (!once && !a.CanGoBack) { once = true; second.StartDialogue(replacement); } };
            second.StartDialogue(); Assert.That(second.GetCurrentDialogue().Text, Is.EqualTo("REPLACEMENT"));
            var count = typeof(StoryFlowManager).GetField("_activeDialogueCount", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(count.GetValue(manager), Is.EqualTo(2)); second.StopDialogue(); first.StopDialogue();
            Assert.That(count.GetValue(manager), Is.Zero); Object.DestroyImmediate(replacement);
        }
        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void StaleRestoredNotificationPreservesFreshNativeLipsync(int callback)
        {
            var component = Start(Import("same-node")); component.SelectOption("again");
            bool entered = false;
            if (callback == 1) component.OnDialogueRestored += _ => { entered = true; component.SelectOption("again"); };
            else component.OnRollbackAvailabilityChanged += availability => {
                if (!availability.CanGoBack && !entered)
                {
                    entered = true;
                    if (callback == 2) { component.StopDialogue(); component.StartDialogue(); }
                    else component.SelectOption("again");
                }
            };
            var lip = NewObject("Fresh Face").AddComponent<StoryFlowLipsync>(); lip.Source = component;
            typeof(StoryFlowLipsync).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(lip, null);
            var before = component.DialogueEntrySerial;
            Assert.That(component.GoBack().Ok, Is.True);
            Assert.That(entered && component.IsDialogueActive() && component.DialogueEntrySerial > before + 1, Is.True);
            Assert.That(lip.IsLipsyncActive, Is.True, "stale restored callback must not release a new entry or session");
        }
        [Test]
        public void BackReleasesNativeLipsyncAndRestoredEntrySurvivesBinding()
        {
            var component = Start(Import("same-node")); component.SelectOption("again");
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            mesh.AddBlendShapeFrame("jawOpen", 100f, new Vector3[3], new Vector3[3], new Vector3[3]);
            try
            {
                var face = NewObject("Face", typeof(SkinnedMeshRenderer));
                var renderer = face.GetComponent<SkinnedMeshRenderer>(); renderer.sharedMesh = mesh;
                var lip = face.AddComponent<StoryFlowLipsync>(); lip.Source = component; lip.FaceRoot = face.transform;
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(StoryFlowLipsync).GetMethod("OnEnable", flags).Invoke(lip, null);
                Assert.That(lip.IsLipsyncActive, Is.True); renderer.SetBlendShapeWeight(0, 60f);
                int normal = 0, restored = 0;
                component.OnDialogueUpdated += _ => normal++; component.OnDialogueRestored += _ => restored++;
                Assert.That(component.GoBack().Ok, Is.True);
                Assert.That(lip.IsLipsyncActive, Is.False); Assert.That(renderer.GetBlendShapeWeight(0), Is.Zero);
                Assert.That(normal, Is.Zero); Assert.That(restored, Is.EqualTo(1));
                renderer.SetBlendShapeWeight(0, 42f);
                typeof(StoryFlowComponent).GetMethod("BroadcastDialogueUpdate", flags).Invoke(component, null);
                typeof(StoryFlowLipsync).GetMethod("LateUpdate", flags).Invoke(lip, null);
                Assert.That(lip.IsLipsyncActive, Is.False); Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(42f));
                lip.enabled = false; typeof(StoryFlowLipsync).GetMethod("OnDisable", flags).Invoke(lip, null);
                lip.enabled = true; typeof(StoryFlowLipsync).GetMethod("OnEnable", flags).Invoke(lip, null);
                Assert.That(lip.IsLipsyncActive, Is.False);
                var late = NewObject("Late Face").AddComponent<StoryFlowLipsync>(); late.Source = component; late.FaceRoot = face.transform;
                typeof(StoryFlowLipsync).GetMethod("OnEnable", flags).Invoke(late, null);
                Assert.That(late.IsLipsyncActive, Is.False);
                var handlers = (Delegate)typeof(StoryFlowComponent).GetField("OnDialogueRestored", flags).GetValue(component);
                Assert.That(handlers.GetInvocationList().Count(h => h.Target == late), Is.EqualTo(1));
                late.enabled = false;
                typeof(StoryFlowLipsync).GetMethod("OnDisable", flags).Invoke(late, null);
                handlers = (Delegate)typeof(StoryFlowComponent).GetField("OnDialogueRestored", flags).GetValue(component);
                Assert.That(handlers.GetInvocationList().Count(h => h.Target == late), Is.Zero);
                component.SelectOption("again"); Assert.That(lip.IsLipsyncActive, Is.True);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
        [Test]
        public void OptionalBackButtonPreservesAuthoredDisabledState()
        {
            var component = Start(Import("same-node"));
            var ui = NewObject("UI").AddComponent<StoryFlowDefaultDialogueUI>();
            var button = NewObject("Back", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            button.interactable = false; ui.backButton = button; ui.InitializeWithComponent(component);
            component.SelectOption("again"); Assert.That(component.CanGoBack(), Is.True); Assert.That(button.interactable, Is.False);
            ui.SetBackAllowed(true); Assert.That(button.interactable, Is.True);
            button.onClick.Invoke(); Assert.That(component.GetIntVariable("Visits", true), Is.Zero); Assert.That(button.interactable, Is.False);
        }
        [Test]
        public void OptionalRestorationHookDoesNotBreakExistingUiInterface()
        {
            Assert.That(typeof(StoryFlowDialogueUI).GetMethod("HandleDialogueRestored"), Is.Not.Null);
            Assert.That(typeof(IStoryFlowDialogueUI).GetMethod("HandleDialogueRestored"), Is.Null);
        }
        [Test]
        public void BackButtonWorksAcrossScriptCallsAndFreshTraversal()
        {
            var component = Start(Import("nested-loops"));
            var ui = NewObject("UI").AddComponent<StoryFlowDefaultDialogueUI>();
            var button = NewObject("Back", typeof(RectTransform), typeof(Button)).GetComponent<Button>();
            ui.backButton = button; ui.InitializeWithComponent(component); ui.InitializeWithComponent(component);
            Assert.That(button.interactable, Is.False); component.AdvanceDialogue(); Assert.That(button.interactable, Is.True);
            int restored = 0, normal = 0; component.OnDialogueRestored += _ => restored++; component.OnDialogueUpdated += _ => normal++;
            button.onClick.Invoke(); Assert.That(restored, Is.EqualTo(1)); Assert.That(normal, Is.Zero);
            Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo("Visit"));
            component.AdvanceDialogue(); Assert.That(normal, Is.EqualTo(1));
            component.AdvanceDialogue(); component.AdvanceDialogue(); component.AdvanceDialogue();
            Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo("Done"));
            button.onClick.Invoke(); Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo("Visit"));
            component.StopDialogue(); Assert.That(button.interactable, Is.False);
        }
        [Test]
        public void DisabledPlaybackHasNoControllerOrRng()
        {
            var project = Import("same-node"); project.DialogueRollback.Enabled = false;
            var component = Start(project); var context = Context(component);
            for (int i = 0; i < 10; i++) component.SelectOption("again");
            Assert.That(typeof(StoryFlowComponent).GetField("rollback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(component), Is.Null);
            Assert.That(typeof(StoryFlow.Execution.StoryFlowExecutionContext).GetField("RollbackRandomState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(context), Is.Null);
            Assert.That(component.GoBack().Reason, Is.EqualTo("disabled")); project.DialogueRollback.Enabled = true;
        }
        [Test]
        public void BackCancelsVoiceCompletionAndStaleDelay()
        {
            var component = Start(Import("barrier")); component.AdvanceDialogue();
            var clip = AudioClip.Create("Test voice", 44100, 1, 44100, false);
            try
            {
                component.PlayDialogueAudio(clip, false); component.SetAudioAdvanceState(true, false);
                var delayed = (System.Collections.IEnumerator)typeof(StoryFlowComponent).GetMethod("ResumeAfterDelay", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, new object[] { 0f });
                delayed.MoveNext(); Assert.That(component.GoBack().Ok, Is.True);
                Assert.That(component.CurrentDialogueAudioClip, Is.Null);
                typeof(StoryFlowComponent).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
                delayed.MoveNext(); Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo("A"));
                component.AdvanceDialogue(); Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo("B"));
            }
            finally { Object.DestroyImmediate(clip); }
        }
        [Test]
        public void SerializedSettingsSurviveRealAssetReload()
        {
            var project = Import("same-node"); var path = AssetDatabase.GetAssetPath(project);
            project.DialogueRollback.HistoryLimit = 7; EditorUtility.SetDirty(project); AssetDatabase.SaveAssets();
            Resources.UnloadAsset(project); var loaded = AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>(path);
            Assert.That(loaded.DialogueRollback.Enabled, Is.True); Assert.That(loaded.DialogueRollback.HistoryLimit, Is.EqualTo(7));
            // The importer always reapplies authoritative metadata, including on cached imports.
            Assert.That(Import("same-node").DialogueRollback.HistoryLimit, Is.EqualTo(100));
        }
        [Test]
        public void FailedBackPreservesNativeVoiceAndControlState()
        {
            var component = Start(Import("same-node")); component.SelectOption("again"); component.PauseDialogue();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var fault = typeof(StoryFlowManager).GetField("CommitFault", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(fault, Is.Not.Null, "scratch host enables test fault seam");
            var clip = AudioClip.Create("Recovery voice", 44100 * 10, 1, 44100, false);
            try
            {
                component.PlayDialogueAudio(clip, false); component.SetAudioAdvanceState(true, true);
                var source = (AudioSource)typeof(StoryFlowComponent).GetField("_dialogueAudioSource", flags).GetValue(component);
                Context(component).CurrentDialogueState.Audio = clip;
                var lip = NewObject("Recovery Face").AddComponent<StoryFlowLipsync>(); lip.Source = component;
                typeof(StoryFlowLipsync).GetMethod("OnEnable", flags).Invoke(lip, null);
                var speaking = typeof(StoryFlowLipsync).GetField("_speaking", flags);
                Assert.That(lip.IsLipsyncActive, Is.True); Assert.That(speaking.GetValue(lip), Is.SameAs(source));
                int restored = 0; component.OnDialogueRestored += _ => restored++;
                source.time = 2f; var beforeTime = source.time;
                var generation = typeof(StoryFlowComponent).GetField("mediaGeneration", flags).GetValue(component);
                fault.SetValue(null, (Action)(() => { fault.SetValue(null, null); throw new InvalidOperationException("native recovery probe"); }));
                Assert.That(component.GoBack().Ok, Is.False);
                Assert.That(restored, Is.Zero); Assert.That(lip.IsLipsyncActive, Is.True);
                Assert.That(speaking.GetValue(lip), Is.SameAs(source));
                Assert.That(component.IsPaused(), Is.True);
                Assert.That(component.CurrentDialogueAudioClip, Is.SameAs(clip));
                Assert.That(source.clip, Is.SameAs(clip)); Assert.That(source.loop, Is.False);
                Assert.That(source.time, Is.EqualTo(beforeTime).Within(.05f));
                Assert.That(typeof(StoryFlowComponent).GetField("_waitingForAudioAdvance", flags).GetValue(component), Is.True);
                Assert.That(typeof(StoryFlowComponent).GetField("_audioAdvanceAllowSkip", flags).GetValue(component), Is.True);
                Assert.That(typeof(StoryFlowComponent).GetField("mediaGeneration", flags).GetValue(component), Is.EqualTo(generation));
            }
            finally { fault.SetValue(null, null); component.StopDialogueAudio(); Object.DestroyImmediate(clip); }
        }
        [Test]
        public void HostSetterRestartDoesNotRewindExternalWrite()
        {
            var component = Start(Import("same-node")); component.SelectOption("again"); bool once = false;
            component.OnRollbackAvailabilityChanged += value => { if (!once && !value.CanGoBack) { once = true; component.StopDialogue(); component.StartDialogue(); } };
            component.SetIntVariable("Visits", 42, true); component.SelectOption("again");
            Assert.That(component.CanGoBack(), Is.False); component.SelectOption("again"); Assert.That(component.GoBack().Ok, Is.True);
            Assert.That(component.GetIntVariable("Visits", true), Is.EqualTo(43));
        }
        [Test]
        public void RestoredPresentationUsesDetachedOverlayAndLocalizedCharacterIds()
        {
            var build = Path.Combine(Directory.GetCurrentDirectory(), "RollbackPresentationProbe"); Directory.CreateDirectory(build);
            foreach (var file in Directory.GetFiles(Path.Combine(Directory.GetCurrentDirectory(), "RollbackFixtures", "same-node")))
                File.Copy(file, Path.Combine(build, Path.GetFileName(file)), true);
            var scriptPath = Path.Combine(build, "main.json"); var script = JObject.Parse(File.ReadAllText(scriptPath));
            script["variables"]["asset"] = JObject.Parse("{'id':'asset','name':'Asset','type':'dataAsset','value':'child'}");
            script["variables"]["friend"] = JObject.Parse("{'id':'friend','name':'Friend','type':'character','value':'da_friend'}");
            script["nodes"]["A"]["character"] = "characters/hero.sfc"; script["nodes"]["A"]["title"] = "A.title";
            script["nodes"]["A"]["textBlocks"] = JArray.Parse("[{'id':'block','text':'A.block'}]");
            script["strings"]["en"]["A.text"] = "Hello {Character.Name}: {Asset.Text} / {Friend.Name}";
            script["strings"]["en"]["hero.name"] = "SCRIPT HERO";
            script["strings"]["en"]["friend.name"] = "SCRIPT SHADOW";
            foreach (var key in new[] { "A.title", "A.block", "again" }) script["strings"]["en"][key] = "{Asset.Text} / {Friend.Name}";
            File.WriteAllText(scriptPath, script.ToString());
            File.WriteAllText(Path.Combine(build, "characters.json"), "{'characters':{'characters/hero.sfc':{'name':'hero.name','image':'','variables':{}},'characters/friend.sfc':{'name':'friend.name','image':'','variables':{}},'characters/missing.sfc':{'name':'missing.name','image':'','variables':{}}},'strings':{'en':{'hero.name':'Hero','friend.name':'Friend'}},'assets':{}}");
            File.WriteAllText(Path.Combine(build, "character-index.json"), "{'schemaVersion':'1','characters':{'da_friend':'characters/friend.sfc'}}");
            File.WriteAllText(Path.Combine(build, "data-assets.json"), "{'localizationVersion':2,'dataAssets':{'base':{'name':'Base','parent':null,'variables':[{'id':'text','name':'Text','type':'string','value':'base.text'}],'overrides':{}},'child':{'name':'Child','parent':'base','variables':[],'overrides':{'text':'child.text'}}},'strings':{'en':{'base.text':'Base text','child.text':'Child text'}}}");
            File.WriteAllText(Path.Combine(build, "localization.json"), "{'schemaVersion':'1','sourceLanguage':'en','languages':[{'code':'fr','name':'French'}],'strings':{'fr':{'hero.name':'Heros','friend.name':'Ami','child.text':'Texte enfant','A.text':'Bonjour {Character.Name}: {Asset.Text} / {Friend.Name}','A.title':'{Asset.Text} / {Friend.Name}','A.block':'{Asset.Text} / {Friend.Name}','again':'{Asset.Text} / {Friend.Name}'}}}");
            var project = StoryFlowImporter.ImportProject(build, "Assets/RollbackPresentationProbe", out var report, force: true);
            Assert.That(report.HasFailures, Is.False); var component = Start(project); var manager = StoryFlowManager.Instance;
            Context(component).FindCharacter("characters/missing.sfc").Name = "STALE CACHED NAME";
            var overlay = (Dictionary<string, Dictionary<string, StoryFlowVariant>>)typeof(StoryFlowManager).GetField("DataAssetOverlay", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
            overlay["child"] = new Dictionary<string, StoryFlowVariant> { ["text"] = new StoryFlowVariant { Type = StoryFlowVariableType.String, StringValue = "Target overlay", IsLiteralString = true } };
            component.SelectOption("again"); overlay["child"]["text"].StringValue = "Live overlay";
            component.SelectOption("again"); Assert.That(component.GoBack().Ok, Is.True);
            var sourceState = component.GetCurrentDialogue();
            Assert.That(sourceState.Text, Is.EqualTo("Hello Hero: Target overlay / Friend"));
            Assert.That(sourceState.Title, Is.EqualTo("Target overlay / Friend"));
            Assert.That(sourceState.Options[0].Text, Is.EqualTo("Target overlay / Friend"));
            Assert.That(sourceState.TextBlocks[0].Text, Is.EqualTo("Target overlay / Friend"));
            Assert.That(Context(component).FindCharacter("da_friend").Name, Is.EqualTo("Friend"));
            Assert.That(Context(component).FindCharacter("characters/missing.sfc").Name, Is.EqualTo("missing.name"));
            component.SelectOption("again"); manager.SetLanguage("fr"); Assert.That(component.GoBack().Ok, Is.True);
            var state = component.GetCurrentDialogue(); Assert.That(state.Text, Is.EqualTo("Bonjour Heros: Target overlay / Ami"));
            Assert.That(state.Title, Is.EqualTo("Target overlay / Ami")); Assert.That(state.Options[0].Text, Is.EqualTo("Target overlay / Ami"));
            Assert.That(state.TextBlocks[0].Text, Is.EqualTo("Target overlay / Ami"));
            Assert.That(Context(component).FindCharacter("da_friend").Name, Is.EqualTo("Ami"));
            Assert.That(component.GoBack().Ok, Is.True); Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo("Bonjour Heros: Texte enfant / Ami"));
        }
        [Test]
        public void StopObserverKeepsReplacementController()
        {
            var component = Start(Import("same-node")); component.SelectOption("again"); bool once = false;
            component.OnRollbackAvailabilityChanged += value => { if (!once && !value.CanGoBack) { once = true; component.StartDialogue(); } };
            component.StopDialogue(); component.SelectOption("again"); Assert.That(component.GoBack().Ok, Is.True);
            Assert.That(component.GetIntVariable("Visits", true), Is.EqualTo(1));
        }
        [Test]
        public void ImportObserverReplacementRemainsInvalidUntilRestart()
        {
            var project = Import("same-node"); var component = Start(project); component.SelectOption("again"); bool once = false;
            component.OnRollbackAvailabilityChanged += value => { if (!once && value.Reason == "contentChanged") { once = true; component.StopDialogue(); component.StartDialogue(); } };
            StoryFlowImporter.ImportProject(Path.Combine(Directory.GetCurrentDirectory(), "RollbackFixtures", "same-node"), "Assets/RollbackImported/same-node", out _, force: true);
            component.SelectOption("again"); Assert.That(component.GetRollbackAvailability().Reason, Is.EqualTo("contentChanged"));
            Assert.That(component.GoBack().Ok, Is.False);
            component.StopDialogue(); component.StartDialogue(); component.SelectOption("again"); Assert.That(component.CanGoBack(), Is.True);
        }
        [Test]
        public void EnabledCaptureAndRestoreCostIsMeasured()
        {
            var component = Start(Import("same-node"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) component.SelectOption("again");
            var capture = watch.Elapsed.TotalMilliseconds; watch.Restart();
            for (int i = 0; i < 100; i++) Assert.That(component.GoBack().Ok, Is.True);
            Debug.Log("ROLLBACK_NATIVE_COST 100 forward/capture ms=" + capture + " 100 restore ms=" + watch.Elapsed.TotalMilliseconds);
        }
        [Test]
        public void RollbackDoesNotConsumeUnityGlobalRandomStream()
        {
            var component = Start(Import("random"));
            UnityEngine.Random.InitState(1337); float expected = UnityEngine.Random.value;
            UnityEngine.Random.InitState(1337);
            component.AdvanceDialogue(); Assert.That(component.GoBack().Ok, Is.True); component.AdvanceDialogue();
            Assert.That(UnityEngine.Random.value, Is.EqualTo(expected));
        }
        [Test]
        public void DisabledAndEnabledRetainedHeapIsMeasuredSeparately()
        {
            var project = Import("same-node"); project.DialogueRollback.Enabled = false;
            var component = Start(project);
            for (int i = 0; i < 10; i++) component.SelectOption("again");
            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 100; i++) component.SelectOption("again");
            long disabledBytes = GC.GetTotalMemory(true) - before;
            component.StopDialogue(); project.DialogueRollback.Enabled = true; component.StartDialogue();
            for (int i = 0; i < 10; i++) component.SelectOption("again");
            before = GC.GetTotalMemory(true);
            for (int i = 0; i < 100; i++) component.SelectOption("again");
            long enabledBytes = GC.GetTotalMemory(true) - before;
            Debug.Log("ROLLBACK_NATIVE_RETAINED_HEAP 100 forward disabled bytes=" + disabledBytes + " enabled bytes=" + enabledBytes + " (process managed retained heap delta, not allocation counter)");
            Assert.That(enabledBytes, Is.GreaterThan(disabledBytes));
        }
        [TestCase("purchase")][TestCase("nested-loops")][TestCase("same-node")][TestCase("random")][TestCase("barrier")]
        public void ExportedTraceRunsInUnity(string name)
        {
            var component = Start(Import(name));
            var traces = JObject.Parse(File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "RollbackFixtures", "expected-traces.json")));
            var remembered = new Dictionary<string, int>();
            foreach (var action in traces[name])
            {
                switch ((string)action["op"])
                {
                    case "choose": component.SelectOption((string)action["id"]); break;
                    case "advance": component.AdvanceDialogue(); break;
                    case "back": Assert.That(component.GoBack().Ok, Is.True); break;
                    case "block": component.BlockRollback(); break;
                    case "assert":
                        var state = action["state"];
                        if (state["dialogue"] != null) Assert.That(component.GetCurrentDialogue().Text, Is.EqualTo((string)state["dialogue"]));
                        if (state["canGoBack"] != null) Assert.That(component.CanGoBack(), Is.EqualTo((bool)state["canGoBack"]));
                        if (state["callDepth"] != null) Assert.That(Context(component).CallStackDepth, Is.EqualTo((int)state["callDepth"]));
                        if (state["globals"] is JObject globals)
                            foreach (var pair in globals)
                            {
                                if (pair.Value.Type == JTokenType.Integer) Assert.That(component.GetIntVariable(pair.Key, true), Is.EqualTo((int)pair.Value));
                                else if (pair.Key == "Inventory") CollectionAssert.AreEqual(pair.Value.Values<string>().ToArray(), component.GetStringArrayVariable(pair.Key, true));
                                else if (pair.Key == "Counts") foreach (var entry in pair.Value) Assert.That(component.GetStringToIntMap("Counts", true)[(string)entry["key"]], Is.EqualTo((int)entry["value"]));
                            }
                        if (state["rememberGlobals"] is JArray remember) foreach (string key in remember) remembered[key] = component.GetIntVariable(key, true);
                        if (state["sameGlobals"] is JArray same) foreach (string key in same) Assert.That(component.GetIntVariable(key, true), Is.EqualTo(remembered[key]));
                        break;
                }
            }
        }
    }
    public class RollbackPersistentUiSpy : StoryFlowDialogueUI
    {
        public string Showing;
        public override void HandleDialogueUpdated(StoryFlowDialogueState state) { Showing = state.Text; }
    }
}
#endif
