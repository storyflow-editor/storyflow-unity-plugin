using StoryFlow.Data;

namespace StoryFlow.UI
{
    /// <summary>
    /// Interface for dialogue UI implementations. Use this when you cannot inherit from
    /// StoryFlowDialogueUI (e.g., your class already extends another MonoBehaviour base).
    /// For most cases, extending StoryFlowDialogueUI is preferred as it provides
    /// convenience methods and automatic event subscription.
    /// </summary>
    public interface IStoryFlowDialogueUI
    {
        void OnDialogueStarted();
        void HandleDialogueUpdated(StoryFlowDialogueState state);
        void OnDialogueEnded();
        void OnVariableChanged(StoryFlowVariable variable, bool isGlobal);
    }
}
