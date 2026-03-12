using System.Collections.Generic;
using StoryFlow.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoryFlow.UI
{
    /// <summary>
    /// A simple uGUI-based default dialogue UI implementation.
    /// Assign the required UI references in the Inspector, then bind to a StoryFlowComponent
    /// either via the component's DialogueUI field or by calling InitializeWithComponent().
    ///
    /// This is intended as a quick-start / prototype UI. For production games,
    /// extend StoryFlowDialogueUI with your own visuals and animations.
    /// </summary>
    public class StoryFlowDefaultDialogueUI : StoryFlowDialogueUI
    {
        // -------------------------------------------------------------------
        // Inspector fields
        // -------------------------------------------------------------------

        [Header("Dialogue Panel")]
        [Tooltip("Root panel that is shown/hidden when dialogue starts/ends.")]
        public GameObject dialoguePanel;

        [Header("Text")]
        [Tooltip("Displays the dialogue title (optional).")]
        public TextMeshProUGUI titleText;

        [Tooltip("Displays the dialogue body text.")]
        public TextMeshProUGUI bodyText;

        [Header("Character")]
        [Tooltip("Displays the speaking character's name (optional).")]
        public TextMeshProUGUI characterNameText;

        [Tooltip("Displays the speaking character's portrait image (optional).")]
        public Image characterPortrait;

        [Header("Image")]
        [Tooltip("Displays a dialogue-specific image (optional).")]
        public Image dialogueImage;

        [Header("Background Image")]
        [Tooltip("Displays the persistent background image set by SetBackgroundImage nodes (optional).")]
        public Image backgroundImage;

        [Header("Options")]
        [Tooltip("Container transform where option buttons are spawned as children.")]
        public Transform optionsContainer;

        [Tooltip("Prefab for option buttons. Must have a Button component and a TextMeshProUGUI child.")]
        public GameObject optionButtonPrefab;

        [Header("Continue")]
        [Tooltip("Button shown when dialogue has no options (narrative-only). Calls AdvanceDialogue().")]
        public Button continueButton;

        // -------------------------------------------------------------------
        // Internal state
        // -------------------------------------------------------------------

        private readonly List<GameObject> buttonPool = new();

        // -------------------------------------------------------------------
        // MonoBehaviour
        // -------------------------------------------------------------------

        private void Awake()
        {
            // Start hidden
            if (dialoguePanel != null)
                dialoguePanel.SetActive(false);

            // Bind continue button
            if (continueButton != null)
                continueButton.onClick.AddListener(OnContinueClicked);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (continueButton != null)
                continueButton.onClick.RemoveListener(OnContinueClicked);

            DestroyPool();
        }

        // -------------------------------------------------------------------
        // Dialogue event overrides
        // -------------------------------------------------------------------

        public override void OnDialogueStarted()
        {
            if (dialoguePanel != null)
                dialoguePanel.SetActive(true);
        }

        public override void HandleDialogueUpdated(StoryFlowDialogueState state)
        {
            if (state == null || !state.IsValid)
                return;

            // --- Title ---
            if (titleText != null)
            {
                bool hasTitle = !string.IsNullOrEmpty(state.Title);
                titleText.gameObject.SetActive(hasTitle);
                if (hasTitle)
                    titleText.text = state.Title;
            }

            // --- Body text ---
            if (bodyText != null)
            {
                bodyText.text = state.Text ?? "";
            }

            // --- Character ---
            bool hasCharacter = state.Character != null && !string.IsNullOrEmpty(state.Character.Name);

            if (characterNameText != null)
            {
                characterNameText.gameObject.SetActive(hasCharacter);
                if (hasCharacter)
                    characterNameText.text = state.Character.Name;
            }

            if (characterPortrait != null)
            {
                bool hasPortrait = hasCharacter && state.Character.Image != null;
                characterPortrait.gameObject.SetActive(hasPortrait);
                if (hasPortrait)
                    characterPortrait.sprite = state.Character.Image;
            }

            // --- Dialogue image ---
            if (dialogueImage != null)
            {
                bool hasImage = state.Image != null;
                dialogueImage.gameObject.SetActive(hasImage);
                if (hasImage)
                    dialogueImage.sprite = state.Image;
            }

            // --- Options ---
            ClearOptionButtons();

            if (state.Options != null && state.Options.Count > 0)
            {
                foreach (var option in state.Options)
                {
                    CreateOptionButton(option);
                }
            }

            // --- Continue button ---
            if (continueButton != null)
            {
                // Show continue only when there are no options and the dialogue can advance
                continueButton.gameObject.SetActive(state.CanAdvance);
            }
        }

        public override void OnDialogueEnded()
        {
            if (dialoguePanel != null)
                dialoguePanel.SetActive(false);

            DestroyPool();
        }

        public override void OnBackgroundImageChanged(Sprite bgImage)
        {
            if (backgroundImage != null)
            {
                bool hasImage = bgImage != null;
                backgroundImage.gameObject.SetActive(hasImage);
                if (hasImage)
                    backgroundImage.sprite = bgImage;
            }
        }

        // -------------------------------------------------------------------
        // Option button management
        // -------------------------------------------------------------------

        private GameObject GetOrCreateButton()
        {
            // Try to reuse a pooled button
            for (int i = 0; i < buttonPool.Count; i++)
            {
                if (!buttonPool[i].activeInHierarchy)
                {
                    buttonPool[i].SetActive(true);
                    return buttonPool[i];
                }
            }

            // No available pooled button, create new
            var newButton = Instantiate(optionButtonPrefab, optionsContainer);
            buttonPool.Add(newButton);
            return newButton;
        }

        private void CreateOptionButton(StoryFlowOption option)
        {
            if (optionButtonPrefab == null || optionsContainer == null)
                return;

            var buttonObj = GetOrCreateButton();

            // Set button text
            var tmpText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (tmpText != null)
            {
                tmpText.text = option.Text ?? "";
            }

            // Bind click
            var button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                // Determine behaviour based on input type
                if (!string.IsNullOrEmpty(option.InputType) && option.InputType != "button")
                {
                    // For typed input options, the button itself is not the primary interaction.
                    // A full implementation would create input fields here. For the default UI
                    // we simply display the label; typed input support requires a custom UI.
                    button.interactable = false;
                }
                else
                {
                    // Standard button option
                    string capturedId = option.Id;
                    button.onClick.AddListener(() => OnOptionClicked(capturedId));
                }
            }
        }

        private void ClearOptionButtons()
        {
            foreach (var button in buttonPool)
            {
                if (button != null)
                {
                    // Remove listeners from previous use before deactivating
                    var btn = button.GetComponent<Button>();
                    if (btn != null)
                        btn.onClick.RemoveAllListeners();

                    button.SetActive(false);
                }
            }
        }

        private void DestroyPool()
        {
            foreach (var button in buttonPool)
            {
                if (button != null)
                    Destroy(button);
            }
            buttonPool.Clear();
        }

        // -------------------------------------------------------------------
        // Click handlers
        // -------------------------------------------------------------------

        private void OnOptionClicked(string optionId)
        {
            SelectOption(optionId);
        }

        private void OnContinueClicked()
        {
            AdvanceDialogue();
        }
    }
}
