using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using StoryFlow.Editor;
using StoryFlow.Execution;
using UnityEditor;

namespace StoryFlow.Tests
{
    internal static partial class Program
    {
        private const string NestingOutputPath = "Assets/ScriptNesting";

        private static void RunScriptNestingTests()
        {
            Run(nameof(ImportedScriptNestingControlsDirectPushBoundary), ImportedScriptNestingControlsDirectPushBoundary);
            Run(nameof(ImportedScriptNestingControlsRecursiveRunScript), ImportedScriptNestingControlsRecursiveRunScript);
            Run(nameof(InvalidScriptNestingImportsUseLegacyDefault), InvalidScriptNestingImportsUseLegacyDefault);
            Run(nameof(ScriptNestingReimportResetsMissingSetting), ScriptNestingReimportResetsMissingSetting);
            Run(nameof(ScriptNestingUpgradePersistsExistingImports), ScriptNestingUpgradePersistsExistingImports);
            Run(nameof(LiveSyncImportsScriptNestingAndLegacyReset), LiveSyncImportsScriptNestingAndLegacyReset);
            Run(nameof(ScriptNestingLeavesFlowLimitUnchanged), ScriptNestingLeavesFlowLimitUnchanged);
        }

        private static void ImportedScriptNestingControlsDirectPushBoundary()
        {
            foreach (var limit in new[] { 1, 3, 25, 100 })
                WithScriptNestingExport(limit.ToString(), (_, project) => AssertDirectNestingBoundary(project, limit));
            AssertDirectNestingBoundary(null, 20);
            foreach (var invalid in new[] { 0, -1, 101, int.MaxValue })
                AssertDirectNestingBoundary(new StoryFlowProjectAsset { MaxScriptNesting = invalid }, 20);
        }

        private static void AssertDirectNestingBoundary(StoryFlowProjectAsset project, int expected)
        {
            var context = new StoryFlowExecutionContext { Project = project };
            int accepted = 0;
            while (accepted < 101 && context.PushCallFrame("return")) accepted++;
            AssertEqual(expected, accepted, "direct pushes stop at the imported limit");
            AssertEqual(expected, context.CallStackDepth, "rejected direct push leaves the stack intact");
            context.PopCallFrame();
            AssertTrue(context.PushCallFrame("return"), "returning frees a slot for another script call");
            AssertEqual(expected, context.CallStackDepth, "the freed slot is reusable");
        }

        private static void ImportedScriptNestingControlsRecursiveRunScript()
        {
            foreach (var limit in new[] { 1, 3, 25, 100 })
                WithScriptNestingExport(limit.ToString(), (_, project) => AssertRecursiveNestingBoundary(project, limit));
        }

        private static void AssertRecursiveNestingBoundary(StoryFlowProjectAsset project, int expected)
        {
            SetManagerProject(project);
            try
            {
                var component = new StoryFlowComponent { UIStyle = BuiltInUIStyle.None };
                var errors = new List<string>();
                int calls = 0;
                component.OnError += errors.Add;
                component.OnScriptStarted += _ => calls++;
                component.StartDialogue();
                AssertEqual(expected, component.GetContext().CallStackDepth, "recursive calls stop at the configured limit");
                AssertEqual(expected + 1, calls, "root plus exactly the allowed nested scripts enter");
                AssertEqual(1, errors.Count, "overflow is reported once");
                AssertTrue(errors.Count == 1 && errors[0].Contains(expected.ToString()), "overflow reports the configured limit");
                component.StopDialogue();
            }
            finally { ClearManager(); }
        }

        private static void InvalidScriptNestingImportsUseLegacyDefault()
        {
            foreach (var value in new[] { null, "null", "0", "-1", "101", "1.5", "\"3\"", "true", "{}", "[]", "NaN", "Infinity", "-Infinity", "1e999", "999999999999999999999999999999999999999999999999999" })
                WithScriptNestingExport(value, (_, project) => AssertDirectNestingBoundary(project, 20));
            // JSON has one number type: integral decimal/exponent spellings are valid.
            WithScriptNestingExport("3.0", (_, project) => AssertDirectNestingBoundary(project, 3));
            WithScriptNestingExport("1e2", (_, project) => AssertDirectNestingBoundary(project, 100));
        }

        private static void ScriptNestingReimportResetsMissingSetting()
        {
            WithScriptNestingExport("3", (root, project) =>
            {
                AssertDirectNestingBoundary(project, 3);
                WriteNestingProject(root, null);
                var reimported = StoryFlowImporter.ImportProject(Path.Combine(root, "build"), NestingOutputPath);
                AssertTrue(ReferenceEquals(project, reimported), "reimport updates the existing project asset");
                AssertDirectNestingBoundary(reimported, 20);
                AssertRecursiveNestingBoundary(reimported, 20);

                WriteNestingProject(root, "3");
                StoryFlowImporter.ImportProject(Path.Combine(root, "build"), NestingOutputPath);
                WriteNestingProject(root, "\"25\"");
                StoryFlowImporter.ImportProject(Path.Combine(root, "build"), NestingOutputPath);
                AssertDirectNestingBoundary(project, 20);
            });
        }

        private static void ScriptNestingUpgradePersistsExistingImports()
        {
            try
            {
                StoryFlowImporter.ParseSchemaVersionForTests = "7";
                WithScriptNestingExport("3", (root, project) =>
                {
                    project.MaxScriptNesting = 20;
                    StoryFlowImporter.ParseSchemaVersionForTests = null;
                    EditorStubs.Calls.Clear();
                    StoryFlowImporter.ImportProject(Path.Combine(root, "build"), NestingOutputPath);
                    AssertTrue(EditorStubs.HasCall("SaveAssetIfDirty:" + NestingOutputPath + "/Project.asset"),
                        "upgrading from an importer without the setting persists it even when the export is unchanged");
                    AssertDirectNestingBoundary(project, 3);
                });
            }
            finally { StoryFlowImporter.ParseSchemaVersionForTests = null; }
        }

        private static void LiveSyncImportsScriptNestingAndLegacyReset()
        {
            WithScriptNestingExport("3", (root, project) =>
            {
                var server = new StoryFlowLiveSyncServer();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(StoryFlowLiveSyncServer).GetField("outputPath", flags).SetValue(server, NestingOutputPath);
                foreach (var limit in new[] { 25, 20 })
                {
                    WriteNestingProject(root, limit == 20 ? null : "25");
                    typeof(StoryFlowLiveSyncServer).GetMethod("HandleProjectUpdated", flags)
                        .Invoke(server, new object[] { new JObject { ["projectPath"] = root } });
                    typeof(StoryFlowLiveSyncServer).GetMethod("EditorUpdate", flags).Invoke(server, null);
                    var imported = AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>(NestingOutputPath + "/Project.asset");
                    AssertDirectNestingBoundary(imported, limit);
                }
            });
        }

        private static void ScriptNestingLeavesFlowLimitUnchanged()
        {
            WithScriptNestingExport("1", (_, project) =>
            {
                var context = new StoryFlowExecutionContext { Project = project };
                int accepted = 0;
                while (accepted < 101 && context.PushFlowFrame("flow")) accepted++;
                AssertEqual(50, accepted, "script nesting does not change the flow guard");
                AssertTrue(context.PushCallFrame("return"), "flow depth does not consume script depth");
                AssertEqual(0, context.FlowStackDepth, "entering a script still saves and clears its caller's flows");
                context.PopCallFrame();
                AssertEqual(50, context.FlowStackDepth, "returning still restores caller flow depth");
            });
        }

        private static void WithScriptNestingExport(string value, Action<string, StoryFlowProjectAsset> assertion)
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            var temporaryRoot = Path.Combine(Path.GetTempPath(), "storyflow-unity-script-nesting-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(temporaryRoot, "Assets"));
                Directory.CreateDirectory(Path.Combine(temporaryRoot, "build", "scripts"));
                WriteNestingProject(temporaryRoot, value);
                File.WriteAllText(Path.Combine(temporaryRoot, "build", "scripts", "recursive.json"), @"{
                    'startNode': 'start',
                    'nodes': {
                        'start': { 'id': 'start', 'type': 'start' },
                        'call': { 'id': 'call', 'type': 'runScript', 'script': 'scripts/recursive.json' }
                    },
                    'connections': [{ 'id': 'start-call', 'source': 'start', 'target': 'call',
                        'sourceHandle': 'source-start-', 'targetHandle': 'target-call-' }],
                    'variables': {}, 'strings': {}, 'assets': {}
                }");
                Directory.SetCurrentDirectory(temporaryRoot);
                EditorStubs.Reset();
                var project = StoryFlowImporter.ImportProject(Path.Combine(temporaryRoot, "build"), NestingOutputPath, out var report);
                AssertTrue(project != null && !report.HasFailures, "script nesting fixture imports successfully");
                assertion(temporaryRoot, project);
            }
            finally
            {
                ClearManager();
                Directory.SetCurrentDirectory(originalDirectory);
                TryDeleteDirectory(temporaryRoot);
            }
        }

        private static void WriteNestingProject(string root, string value)
        {
            File.WriteAllText(Path.Combine(root, "build", "project.json"),
                "{\"version\":\"1\",\"metadata\":{\"title\":\"Script Nesting\"" +
                (value == null ? "" : ",\"maxScriptNesting\":" + value) +
                "},\"startupScript\":\"scripts/recursive.json\"}");
        }
    }
}
