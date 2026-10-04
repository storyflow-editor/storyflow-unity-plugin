using System;
using StoryFlow.Data;
using UnityEngine;
using UnityEngine.UI;

namespace StoryFlow.UI
{
    /// <summary>
    /// Abstract base class for dialogue UI implementations.
    /// Extend this class and override the virtual methods to create custom dialogue UIs.
    /// Alternatively, subscribe directly to StoryFlowComponent events.
    /// </summary>
    public abstract class StoryFlowDialogueUI : MonoBehaviour, IStoryFlowDialogueUI
    {
        protected StoryFlowComponent storyFlowComponent;

        [Tooltip("Optional reusable Back button. Leave empty to keep the existing dialogue controls.")]
        public Button backButton;
        private Button boundBackButton;
        private bool authorAllowsBack;
        private Action<StoryFlowDialogueState> restoredHandler;
        private ulong bindingSerial;

        /// <summary>Changes the author's Back permission without overriding history availability.</summary>
        public void SetBackAllowed(bool allowed)
        {
            authorAllowsBack = allowed;
            UpdateBackAvailability(default);
        }

        private void BindBackButton()
        {
            if (boundBackButton == backButton) return;
            if (boundBackButton != null) boundBackButton.onClick.RemoveListener(GoBack);
            boundBackButton = backButton;
            if (boundBackButton == null) return;
            authorAllowsBack = boundBackButton.interactable;
            boundBackButton.onClick.AddListener(GoBack);
        }
        private void GoBack() { if (authorAllowsBack) storyFlowComponent?.GoBack(); }
        private void UpdateBackAvailability(StoryFlowRollbackAvailability availability)
        { if (boundBackButton != null) boundBackButton.interactable = authorAllowsBack && storyFlowComponent != null && storyFlowComponent.CanGoBack(); }

        /// <summary>
        /// Returns true if this UI is already bound to the given component.
        /// </summary>
        public bool IsBoundTo(StoryFlowComponent component)
        {
            return storyFlowComponent == component;
        }

        /// <summary>
        /// Binds this UI to a StoryFlowComponent, subscribing to all dialogue events.
        /// Called automatically if the component's DialogueUI field references this instance,
        /// or call manually from your own code.
        /// </summary>
        public void InitializeWithComponent(StoryFlowComponent component)
        {
            if (component == null)
            {
                Debug.LogWarning("[StoryFlow] Cannot initialize dialogue UI with a null component.");
                return;
            }

            // Unsubscribe from previous component if switching
            if (storyFlowComponent != null)
            {
                UnsubscribeFromComponent(storyFlowComponent);
            }

            storyFlowComponent = component;
            SubscribeToComponent(storyFlowComponent);
            BindBackButton();
            UpdateBackAvailability(component.GetRollbackAvailability());
        }

        protected virtual void OnDestroy()
        {
            if (boundBackButton != null) boundBackButton.onClick.RemoveListener(GoBack);
            if (storyFlowComponent != null)
            {
                UnsubscribeFromComponent(storyFlowComponent);
            }
        }

        private void SubscribeToComponent(StoryFlowComponent component)
        {
            component.OnDialogueStarted += OnDialogueStarted;
            component.OnDialogueUpdated += HandleDialogueUpdated;
            component.OnDialogueEnded += OnDialogueEnded;
            component.OnVariableChanged += OnVariableChanged;
            component.OnBackgroundImageChanged += OnBackgroundImageChanged;
            component.OnRollbackAvailabilityChanged += UpdateBackAvailability;
            var binding = ++bindingSerial;
            restoredHandler = state => {
                if (binding != bindingSerial || storyFlowComponent != component || !component.IsDialogueActive() ||
                    !component.IsCurrentDialogueRestored || !ReferenceEquals(state, component.GetCurrentDialogue())) return;
                HandleDialogueRestored(state);
            };
            component.OnDialogueRestored += restoredHandler;
        }

        private void UnsubscribeFromComponent(StoryFlowComponent component)
        {
            component.OnDialogueStarted -= OnDialogueStarted;
            component.OnDialogueUpdated -= HandleDialogueUpdated;
            component.OnDialogueEnded -= OnDialogueEnded;
            component.OnVariableChanged -= OnVariableChanged;
            component.OnBackgroundImageChanged -= OnBackgroundImageChanged;
            component.OnRollbackAvailabilityChanged -= UpdateBackAvailability;
            component.OnDialogueRestored -= restoredHandler;
            restoredHandler = null;
            bindingSerial++;
        }

        // -------------------------------------------------------------------
        // Override these in your derived class
        // -------------------------------------------------------------------

        /// <summary>Called when dialogue execution begins (before the first dialogue node).</summary>
        public virtual void OnDialogueStarted() { }

        /// <summary>
        /// Called whenever the dialogue state changes (new node, re-render after variable change, etc.).
        /// This is the primary method to override for displaying dialogue content.
        /// </summary>
        public virtual void HandleDialogueUpdated(StoryFlowDialogueState state) { }

        /// <summary>Optional presentation-only restore hook. No normal story events are replayed.</summary>
        public virtual void HandleDialogueRestored(StoryFlowDialogueState state)
        {
            StopAllCoroutines();
            HandleDialogueUpdated(state);
            OnBackgroundImageChanged(storyFlowComponent?.GetContext()?.PersistentBackgroundImage);
            UpdateBackAvailability(storyFlowComponent.GetRollbackAvailability());
        }

        /// <summary>Called when dialogue execution finishes (end node reached or stopped).</summary>
        public virtual void OnDialogueEnded() { }

        /// <summary>Called when a variable changes value during execution.</summary>
        public virtual void OnVariableChanged(StoryFlowVariable variable, bool isGlobal) { }

        /// <summary>Called when a SetBackgroundImage node changes the persistent background.</summary>
        public virtual void OnBackgroundImageChanged(Sprite backgroundImage) { }

        // -------------------------------------------------------------------
        // Convenience methods for interacting with the component
        // -------------------------------------------------------------------

        /// <summary>Selects a dialogue option by its ID.</summary>
        protected void SelectOption(string optionId)
        {
            storyFlowComponent?.SelectOption(optionId);
        }

        /// <summary>Advances past a narrative-only dialogue (no options).</summary>
        protected void AdvanceDialogue()
        {
            storyFlowComponent?.AdvanceDialogue();
        }

        /// <summary>Sends an input value change for a typed input option.</summary>
        protected void InputChanged(string optionId, string value)
        {
            storyFlowComponent?.InputChanged(optionId, value);
        }

        /// <summary>Resolves a localized string using the component's language code.</summary>
        protected string GetLocalizedString(string key)
        {
            return storyFlowComponent?.GetLocalizedString(key) ?? key;
        }

        /// <summary>Gets the current dialogue state snapshot.</summary>
        protected StoryFlowDialogueState GetCurrentDialogue()
        {
            return storyFlowComponent?.GetCurrentDialogue();
        }

        /// <summary>Returns true if dialogue is currently active.</summary>
        protected bool IsDialogueActive()
        {
            return storyFlowComponent != null && storyFlowComponent.IsDialogueActive();
        }
    }
}
