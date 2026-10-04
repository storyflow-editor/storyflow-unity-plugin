#if UNITY_EDITOR
using System;
using System.IO;
using StoryFlow.Data;
using StoryFlow.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class UnityRollbackVerification
{
    public static void ConfigureRollbackTestSeam()
    {
        var group = BuildTargetGroup.Standalone;
        var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        if (!Array.Exists(symbols.Split(';'), symbol => symbol == "STORYFLOW_ROLLBACK_TESTS"))
            PlayerSettings.SetScriptingDefineSymbolsForGroup(group, symbols + ";STORYFLOW_ROLLBACK_TESTS");
        AssetDatabase.SaveAssets();
    }
    public static void ConfigurePlayerBuild()
    {
        var group = BuildTargetGroup.Standalone;
        var symbols = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
        var retained = Array.FindAll(symbols.Split(';'), symbol => symbol != "STORYFLOW_ROLLBACK_TESTS");
        PlayerSettings.SetScriptingDefineSymbolsForGroup(group, string.Join(";", retained));
        AssetDatabase.SaveAssets();
    }
    public static void BuildPlayer()
    {
        var project = StoryFlowImporter.ImportProject(Path.Combine(Directory.GetCurrentDirectory(), "RollbackFixtures", "same-node"), "Assets/RollbackPlayer");
        if (!project.DialogueRollback.Enabled) throw new Exception("rollback metadata missing");
        StoryFlowSettings.Instance.DefaultProject = project; EditorUtility.SetDirty(StoryFlowSettings.Instance); AssetDatabase.SaveAssets();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var manager = new GameObject("Manager").AddComponent<StoryFlow.StoryFlowManager>(); manager.Project = project;
        var component = new GameObject("Dialogue").AddComponent<StoryFlow.StoryFlowComponent>();
        component.Script = project.StartupScript; component.UIStyle = StoryFlow.BuiltInUIStyle.None; component.TraceEnabled = false;
        component.gameObject.AddComponent<RollbackPlayerProbe>();
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/RollbackPlayer.unity");
        var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { "Assets/RollbackPlayer.unity" },
            locationPathName = Path.Combine(Directory.GetCurrentDirectory(), "Player", "RollbackProbe.exe"),
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development });
        if (result.summary.result != BuildResult.Succeeded) throw new Exception("Player build failed: " + result.summary.result);
        Debug.Log("ROLLBACK_PLAYER_BUILD_PASS " + result.summary.totalSize);
    }
    public static void VerifyReload()
    {
        var project = AssetDatabase.LoadAssetAtPath<StoryFlowProjectAsset>("Assets/RollbackPlayer/Project.asset");
        if (project == null || project.DialogueRollback == null || !project.DialogueRollback.Enabled || project.DialogueRollback.HistoryLimit != 100)
            throw new Exception("rollback settings did not survive editor restart/domain reload");
        Debug.Log("ROLLBACK_DOMAIN_RELOAD_PASS");
    }
}
#endif
