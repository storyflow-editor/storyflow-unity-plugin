using System;
using StoryFlow.Data;
using StoryFlow.Editor;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Tests
{
    // The build check warns when a player build would find no StoryFlow project. Without Default Project set, the
    // player loads the project with Resources.Load("Project"): an asset named Project directly inside a Resources folder.
    internal static partial class Program
    {
        private static void RunBuildCheckTests()
        {
            foreach (var path in new[] { "Assets/Resources/Project.asset", "Assets/Game/Resources/Project.asset" })
                Run("Build check passes a project the player loads from " + path, () => AssertEqual(0, BuildCheckWarnings(path), "warnings"));
            foreach (var path in new[] { "Assets/Resources/Story/Project.asset", "Assets/Resources/Story.asset", "Assets/StoryFlow/Project.asset" })
                Run("Build check warns about a project the player never loads from " + path, () => AssertEqual(1, BuildCheckWarnings(path), "warnings"));
            Run("Build check passes when one of several projects is the one the player loads",
                () => AssertEqual(0, BuildCheckWarnings("Assets/Resources/Story/Draft.asset", "Assets/Resources/Project.asset"), "warnings"));
        }

        // Runs the check with a project asset at each path and counts the warnings it gives.
        private static int BuildCheckWarnings(params string[] paths)
        {
            EditorStubs.Reset();
            foreach (var path in paths) EditorStubs.Register(ScriptableObject.CreateInstance<StoryFlowProjectAsset>(), path);
            int warnings = 0;
            Action<object> count = message => warnings++;
            Debug.OnWarning += count;
            try { new StoryFlowBuildCheck().OnPreprocessBuild(null); }
            finally { Debug.OnWarning -= count; EditorStubs.Reset(); }
            return warnings;
        }
    }
}
