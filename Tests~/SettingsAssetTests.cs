using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StoryFlow.Data;
using StoryFlow.Editor;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Tests
{
    // The settings asset tells player builds which project to load. When Unity cannot create it, the settings page and
    // the importer say so. Neither carries on with a copy that was never saved, whose changes would quietly be lost.
    internal static partial class Program
    {
        private const string SettingsAsset = "Assets/Resources/StoryFlowSettings.asset";

        private static void RunSettingsAssetTests()
        {
            Run("The importer creates the settings in Resources", () => InEmptyProject(() =>
            {
                var settings = StoryFlowEditorHelpers.FindOrCreateSettings();
                AssertEqual(SettingsAsset, AssetDatabase.GetAssetPath(settings), "the settings are saved where player builds load them");
            }));

            Run("The importer gets no settings when they could not be saved", () => InEmptyProject(() =>
            {
                EditorStubs.CreateAssetFailurePaths.Add(SettingsAsset);
                StoryFlowSettings settings = null;
                var errors = LoggedErrors(() => settings = StoryFlowEditorHelpers.FindOrCreateSettings());
                AssertTrue(settings == null, "no unsaved settings come back");
                AssertEqual(1, errors.Count(error => error.Contains(SettingsAsset)), "one error names the settings asset");
            }));

            Run("An import finishes and warns when the settings could not be saved", () =>
            {
                var warnings = new List<string>();
                Action<object> collect = message => warnings.Add(message.ToString());
                Debug.OnWarning += collect;
                try
                {
                    WithImportedLegacyFixture(false, project =>
                    {
                        AssertEqual(1, warnings.Count(warning => warning.Contains("Could not find or create StoryFlowSettings")),
                            "the import warns that player builds will not find the project");
                        AssertEqual(0, warnings.Count(warning => warning.Contains("not inside a Resources folder")),
                            "no warning about where unsaved settings are");
                    }, () => EditorStubs.CreateAssetFailurePaths.Add(SettingsAsset));
                }
                finally { Debug.OnWarning -= collect; }
            });

            Run("The settings page shows its error when the settings could not be saved", () => InEmptyProject(() =>
            {
                EditorStubs.CreateAssetFailurePaths.Add(SettingsAsset);
                var page = StoryFlowSettingsProvider.CreateProvider();
                var errors = LoggedErrors(() =>
                {
                    page.activateHandler("", null);
                    for (int repaint = 0; repaint < 3; repaint++) page.guiHandler("");
                });
                AssertEqual(1, errors.Count, "one error while the page stays open, not one per repaint");
                AssertEqual(3, EditorStubs.HelpBoxes.Count(box => box.StartsWith("Error: ") && box.Contains(SettingsAsset)),
                    "every repaint shows the error");

                EditorStubs.CreateAssetFailurePaths.Clear();
                page.activateHandler("", null);
                AssertTrue(AssetDatabase.LoadAssetAtPath<StoryFlowSettings>(SettingsAsset) != null,
                    "opening the page again creates the settings once they can be saved");
            }));

            Run("The settings page creates the settings and shows them", () => InEmptyProject(() =>
            {
                var page = StoryFlowSettingsProvider.CreateProvider();
                page.activateHandler("", null);
                page.guiHandler("");
                AssertTrue(AssetDatabase.LoadAssetAtPath<StoryFlowSettings>(SettingsAsset) != null,
                    "the settings are saved where player builds load them");
                AssertEqual(0, EditorStubs.HelpBoxes.Count, "no error on the page");
            }));
        }

        // Runs a test in an empty Unity project folder, since the asset stubs write the files they create.
        private static void InEmptyProject(Action test)
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            var projectRoot = Path.Combine(Path.GetTempPath(), "storyflow-unity-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
            Directory.SetCurrentDirectory(projectRoot);
            EditorStubs.Reset();
            try { test(); }
            finally
            {
                EditorStubs.Reset();
                Directory.SetCurrentDirectory(originalDirectory);
                TryDeleteDirectory(projectRoot);
            }
        }

        private static List<string> LoggedErrors(Action action)
        {
            var errors = new List<string>();
            Action<object> collect = message => errors.Add(message.ToString());
            Debug.OnError += collect;
            try { action(); }
            finally { Debug.OnError -= collect; }
            return errors;
        }
    }
}
