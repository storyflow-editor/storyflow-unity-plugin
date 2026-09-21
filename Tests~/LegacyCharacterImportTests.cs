using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using StoryFlow.Editor;
using UnityEditor;

namespace StoryFlow.Tests
{
    internal static partial class Program
    {
        private const string LegacyCharacterId = "da_11111111111111111111111111111111";
        private const string LegacyCharacterPath = "characters/legacy_hero.sfc";

        private static void RunLegacyCharacterImportTests()
        {
            Run(nameof(PreIndexCharacterImportFallsBackToPath), PreIndexCharacterImportFallsBackToPath);
            Run(nameof(CurrentCharacterIndexKeepsIdAndPathLookup), CurrentCharacterIndexKeepsIdAndPathLookup);
        }

        private static void PreIndexCharacterImportFallsBackToPath()
        {
            WithImportedLegacyFixture(false, project =>
            {
                AssertEqual(0, project.CharacterIdBridge.Count, "pre-index export keeps the id bridge empty");
                AssertImportedCharacterKeys(project);
                AssertImportedDialogue(project, useCharacterId: false);
            });
        }

        private static void CurrentCharacterIndexKeepsIdAndPathLookup()
        {
            WithImportedLegacyFixture(true, project =>
            {
                AssertEqual(1, project.CharacterIdBridge.Count, "current export imports one id bridge entry");
                AssertImportedCharacterKeys(project);

                SetManagerProject(project);
                try
                {
                    var path = StoryFlowManager.Instance.GetCharacterPathById(LegacyCharacterId, out var found);
                    AssertTrue(found, "current character id resolves");
                    AssertEqual(LegacyCharacterPath, path, "current character id resolves to the imported path");
                    AssertImportedDialogue(project, useCharacterId: true, managerAlreadyInstalled: true);
                }
                finally { ClearManager(); }
            });
        }

        private static void AssertImportedCharacterKeys(StoryFlowProjectAsset project)
        {
            var character = project.GetCharacterAsset(LegacyCharacterPath);
            AssertTrue(character != null, "legacy path imports the character");
            AssertEqual("char_hero_name", character.CharacterName, "legacy character name key imports");
            AssertEqual(1, character.Variables.Count, "legacy character variable count imports");
            AssertEqual("Title", character.Variables[0].Name, "legacy character variable name imports");
            AssertEqual("char_title_value", character.Variables[0].Value.GetString(), "legacy variable key imports");
        }

        private static void AssertImportedDialogue(
            StoryFlowProjectAsset project, bool useCharacterId, bool managerAlreadyInstalled = false)
        {
            if (!managerAlreadyInstalled) SetManagerProject(project);
            try
            {
                var component = new StoryFlowComponent { UIStyle = BuiltInUIStyle.None };
                component.StartDialogue(project.GetStartupScriptAsset());
                var state = component.GetCurrentDialogue();
                AssertTrue(state != null && state.IsValid, "imported startup script reaches a dialogue");
                AssertEqual("Legacy path dialogue, Captain.", state.Text,
                    "imported dialogue resolves source text and the legacy character variable");
                AssertEqual("Legacy Hero", state.Character.Name,
                    useCharacterId ? "current id dialogue resolves its character" : "legacy path dialogue resolves its character");
                AssertEqual("char_title_value", component.GetCharacterVariable(LegacyCharacterPath, "Title").GetString(),
                    "host character reads preserve the stored authored key");
            }
            finally
            {
                if (!managerAlreadyInstalled) ClearManager();
            }
        }

        private static void WithImportedLegacyFixture(bool includeIndex, Action<StoryFlowProjectAsset> assertion)
        {
            var originalDirectory = Directory.GetCurrentDirectory();
            var fixtureSource = Path.Combine(originalDirectory, "Tests~", "Fixtures", "pre-character-index");
            var fixtureCopy = Path.Combine(Path.GetTempPath(), "storyflow-unity-legacy-fixture-" + Guid.NewGuid().ToString("N"));
            var projectRoot = Path.Combine(Path.GetTempPath(), "storyflow-unity-legacy-project-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyDirectory(fixtureSource, fixtureCopy);
                Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));
                if (includeIndex) AddCurrentCharacterIndex(fixtureCopy);

                Directory.SetCurrentDirectory(projectRoot);
                EditorStubs.Reset();
                var project = StoryFlowImporter.ImportProject(
                    fixtureCopy, "Assets/LegacyCharacterProbe", out var report, force: true);

                AssertTrue(project != null, "legacy fixture imports a project asset");
                AssertTrue(!report.HasFailures, "legacy fixture imports without file failures");
                assertion(project);
            }
            finally
            {
                ClearManager();
                Directory.SetCurrentDirectory(originalDirectory);
                TryDeleteDirectory(fixtureCopy);
                TryDeleteDirectory(projectRoot);
            }
        }

        private static void AddCurrentCharacterIndex(string fixtureDirectory)
        {
            File.WriteAllText(Path.Combine(fixtureDirectory, "character-index.json"), new JObject
            {
                ["schemaVersion"] = "1",
                ["characters"] = new JObject { [LegacyCharacterId] = "characters\\legacy_hero.sfc" }
            }.ToString());

            var scriptPath = Path.Combine(fixtureDirectory, "scripts", "intro.json");
            var script = JObject.Parse(File.ReadAllText(scriptPath));
            ((JObject)script["nodes"]["dialogue"])["characterId"] = LegacyCharacterId;
            File.WriteAllText(scriptPath, script.ToString());
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }
        }

        private static void TryDeleteDirectory(string directory)
        {
            try
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
