using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StoryFlow.Editor;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Tests
{
    // StoryFlow Editor sends sync-failed when a sync an engine asked for did not run there. The Live Sync window logs it
    // with the reason, rather than as a message type it does not know.
    internal static partial class Program
    {
        private static void RunLiveSyncMessageTests()
        {
            Run("Live Sync logs a sync StoryFlow Editor could not run, with its reason", () =>
            {
                var log = LiveSyncLogAfter("{\"type\":\"sync-failed\",\"payload\":{\"message\":\"The build folder is read-only\"}}");
                AssertTrue(log.Any(line => line.EndsWith("Sync failed in StoryFlow Editor: The build folder is read-only")), "the reason is logged");
                AssertTrue(!log.Any(line => line.Contains("Unknown message type")), "not as an unknown message type");
            });
        }

        // The window's log after it handles one message from StoryFlow Editor.
        private static List<string> LiveSyncLogAfter(string message)
        {
            EditorApplication.delayCall = null;
            var window = ScriptableObject.CreateInstance<StoryFlowLiveSyncServer>();
            typeof(StoryFlowLiveSyncServer).GetMethod("ProcessMessage", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(window, new object[] { message });
            // The window adds lines on the main thread, through delayCall
            EditorApplication.delayCall?.Invoke();
            EditorApplication.delayCall = null;
            return (List<string>)typeof(StoryFlowLiveSyncServer).GetField("logMessages", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
        }
    }
}
