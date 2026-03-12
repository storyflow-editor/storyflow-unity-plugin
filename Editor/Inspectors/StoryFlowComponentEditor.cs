using StoryFlow.Data;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Custom inspector for StoryFlowComponent.
    /// Draws the default inspector plus runtime debug information in play mode.
    /// </summary>
    [CustomEditor(typeof(StoryFlowComponent))]
    public class StoryFlowComponentEditor : UnityEditor.Editor
    {
        private bool showRuntimeInfo = true;

        public override void OnInspectorGUI()
        {
            // Draw the default inspector fields
            DrawDefaultInspector();

            var component = (StoryFlowComponent)target;

            EditorGUILayout.Space(8);

            // --- Runtime Debug Section (play mode only) ---
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Runtime debug information is available in Play mode.",
                    MessageType.Info);
                return;
            }

            showRuntimeInfo = EditorGUILayout.Foldout(showRuntimeInfo, "Runtime State", true, EditorStyles.foldoutHeader);
            if (showRuntimeInfo)
            {
                EditorGUI.indentLevel++;

                // Dialogue state
                bool isActive = component.IsDialogueActive();
                EditorGUILayout.Toggle("Dialogue Active", isActive);

                if (isActive)
                {
                    bool isWaiting = component.IsWaitingForInput();
                    bool isPaused = component.IsPaused();

                    EditorGUILayout.Toggle("Waiting for Input", isWaiting);
                    EditorGUILayout.Toggle("Paused", isPaused);

                    var dialogueState = component.GetCurrentDialogue();
                    if (dialogueState != null && dialogueState.IsValid)
                    {
                        EditorGUILayout.Space(4);
                        EditorGUILayout.LabelField("Current Dialogue", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;

                        EditorGUILayout.TextField("Node ID", dialogueState.NodeId ?? "");

                        if (!string.IsNullOrEmpty(dialogueState.Title))
                            EditorGUILayout.TextField("Title", dialogueState.Title);

                        if (!string.IsNullOrEmpty(dialogueState.Text))
                        {
                            EditorGUILayout.LabelField("Text:");
                            EditorGUI.indentLevel++;
                            EditorGUILayout.LabelField(dialogueState.Text, EditorStyles.wordWrappedLabel);
                            EditorGUI.indentLevel--;
                        }

                        if (dialogueState.Character != null && !string.IsNullOrEmpty(dialogueState.Character.Name))
                        {
                            EditorGUILayout.TextField("Character", dialogueState.Character.Name);
                        }

                        if (dialogueState.Options != null && dialogueState.Options.Count > 0)
                        {
                            EditorGUILayout.LabelField($"Options ({dialogueState.Options.Count}):");
                            EditorGUI.indentLevel++;
                            foreach (var option in dialogueState.Options)
                            {
                                string label = option.Text ?? "(empty)";
                                if (option.IsOnceOnly) label += " [once-only]";
                                if (!string.IsNullOrEmpty(option.InputType)) label += $" [{option.InputType}]";
                                EditorGUILayout.LabelField($"  [{option.Id}] {label}");
                            }
                            EditorGUI.indentLevel--;
                        }

                        EditorGUILayout.Toggle("Can Advance", dialogueState.CanAdvance);

                        EditorGUI.indentLevel--;
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // --- Control Buttons ---
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            {
                EditorGUI.BeginDisabledGroup(component.IsDialogueActive());
                if (GUILayout.Button("Start Dialogue"))
                {
                    component.StartDialogue();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!component.IsDialogueActive());
                if (GUILayout.Button("Stop Dialogue"))
                {
                    component.StopDialogue();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            if (component.IsDialogueActive())
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUI.BeginDisabledGroup(!component.IsWaitingForInput() || !component.GetCurrentDialogue()?.CanAdvance == true);
                    if (GUILayout.Button("Advance"))
                    {
                        component.AdvanceDialogue();
                    }
                    EditorGUI.EndDisabledGroup();

                    if (!component.IsPaused())
                    {
                        if (GUILayout.Button("Pause"))
                            component.PauseDialogue();
                    }
                    else
                    {
                        if (GUILayout.Button("Resume"))
                            component.ResumeDialogue();
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            // Force repaint while dialogue is active so we see live updates
            if (component.IsDialogueActive())
            {
                Repaint();
            }
        }
    }
}
