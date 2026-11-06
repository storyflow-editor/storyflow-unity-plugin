using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace StoryFlow.Tests
{
    // A message that tells the user to open a menu must name one that exists. A stale path sends them looking for a menu
    // that is not there, as the settings page did with an Assets > Create entry removed long before.
    internal static partial class Program
    {
        private static void RunMenuPathTests()
        {
            Run("Messages name only menus that exist", () =>
            {
                var root = PackageRoot();
                var sources = new[] { "Runtime", "Editor" }
                    .SelectMany(folder => Directory.GetFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
                    .Select(File.ReadAllText).ToList();
                var menus = new HashSet<string>();
                foreach (var text in sources)
                {
                    foreach (Match match in Regex.Matches(text, "\\[MenuItem\\(\"([^\"]+)\""))
                        menus.Add(match.Groups[1].Value.Replace("/", " > "));
                    foreach (Match match in Regex.Matches(text, "menuName = \"([^\"]+)\""))
                        menus.Add("Assets > Create > " + match.Groups[1].Value.Replace("/", " > "));
                    foreach (Match match in Regex.Matches(text, "SettingsPath = \"Project/([^\"]+)\""))
                        menus.Add("Edit > Project Settings > " + match.Groups[1].Value);
                }
                var named = sources.SelectMany(text => Regex.Matches(text, "(?:Tools|Assets|Edit)(?: > [A-Z][A-Za-z]*(?: [A-Z][A-Za-z]*)*)+")
                    .Cast<Match>().Select(match => match.Value)).Distinct().ToList();
                AssertTrue(named.Count > 0, "messages name menus");
                foreach (var menu in named)
                    AssertTrue(menus.Contains(menu), "a message names the menu " + menu);
            });
        }

        // The package folder: the one holding package.json and Runtime, above wherever the tests were built.
        private static string PackageRoot()
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);
            while (folder != null && !(File.Exists(Path.Combine(folder.FullName, "package.json")) && Directory.Exists(Path.Combine(folder.FullName, "Runtime"))))
                folder = folder.Parent;
            return folder?.FullName ?? throw new InvalidOperationException("The package folder was not found above " + AppContext.BaseDirectory);
        }
    }
}
