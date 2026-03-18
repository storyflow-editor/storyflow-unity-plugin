using System.Collections.Generic;
using StoryFlow.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoryFlow.UI
{
    /// <summary>
    /// A programmatically-built fallback dialogue UI. Auto-created by StoryFlowComponent
    /// when no DialogueUI is assigned. Provides a functional dark-themed panel with
    /// character portrait, name, dialogue text, option buttons, and background image.
    ///
    /// For production games, assign a custom StoryFlowDialogueUI implementation instead.
    /// </summary>
    public class StoryFlowRuntimeUI : StoryFlowDialogueUI
    {
        private TextMeshProUGUI _charNameText;
        private Image _charPortrait;
        private TextMeshProUGUI _bodyText;
        private Transform _optionsContainer;
        private GameObject _dialoguePanel;
        private Image _backgroundImage;
        private GameObject _canvasObj;
        private readonly List<GameObject> _optionButtons = new();

        // =====================================================================
        // Initialization
        // =====================================================================

        /// <summary>
        /// Builds the entire UI hierarchy programmatically. Called once during setup.
        /// </summary>
        public void Build()
        {
            // EventSystem (required for UI clicks)
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Canvas
            _canvasObj = new GameObject("[StoryFlow UI]");
            var canvas = _canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            _canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasObj.AddComponent<GraphicRaycaster>();

            // Background image (full viewport, behind dialogue panel)
            var bgObj = new GameObject("BackgroundImage");
            bgObj.transform.SetParent(_canvasObj.transform, false);
            var bgRect = bgObj.AddComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
            _backgroundImage = bgObj.AddComponent<Image>();
            _backgroundImage.color = Color.white;
            _backgroundImage.preserveAspect = false;
            bgObj.SetActive(false);

            // Dialogue panel (bottom of screen)
            _dialoguePanel = new GameObject("DialoguePanel");
            _dialoguePanel.transform.SetParent(_canvasObj.transform, false);
            var panelRect = _dialoguePanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(1, 0);
            panelRect.pivot = new Vector2(0.5f, 0);
            panelRect.offsetMin = new Vector2(20, 20);
            panelRect.offsetMax = new Vector2(-20, 280);
            var panelImg = _dialoguePanel.AddComponent<Image>();
            panelImg.color = new Color(0.1f, 0.1f, 0.12f, 0.92f);

            var layout = _dialoguePanel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 8;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            _dialoguePanel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Character row (portrait + name)
            var charRow = new GameObject("CharRow");
            charRow.transform.SetParent(_dialoguePanel.transform, false);
            charRow.AddComponent<RectTransform>();
            var charRowLayout = charRow.AddComponent<HorizontalLayoutGroup>();
            charRowLayout.spacing = 10;
            charRowLayout.childForceExpandWidth = false;
            charRowLayout.childForceExpandHeight = false;
            charRowLayout.childAlignment = TextAnchor.MiddleLeft;

            // Character portrait
            var portraitObj = new GameObject("CharPortrait");
            portraitObj.transform.SetParent(charRow.transform, false);
            portraitObj.AddComponent<RectTransform>();
            _charPortrait = portraitObj.AddComponent<Image>();
            _charPortrait.preserveAspect = true;
            var portraitLayout = portraitObj.AddComponent<LayoutElement>();
            portraitLayout.preferredWidth = 48;
            portraitLayout.preferredHeight = 48;
            portraitObj.SetActive(false);

            // Character name
            var charObj = new GameObject("CharName");
            charObj.transform.SetParent(charRow.transform, false);
            charObj.AddComponent<RectTransform>();
            _charNameText = charObj.AddComponent<TextMeshProUGUI>();
            _charNameText.fontSize = 18;
            _charNameText.fontStyle = FontStyles.Bold;
            _charNameText.color = Color.white;

            // Body text
            var bodyObj = new GameObject("BodyText");
            bodyObj.transform.SetParent(_dialoguePanel.transform, false);
            bodyObj.AddComponent<RectTransform>();
            _bodyText = bodyObj.AddComponent<TextMeshProUGUI>();
            _bodyText.fontSize = 16;
            _bodyText.color = Color.white;
            _bodyText.enableWordWrapping = true;
            var bodyLayout = bodyObj.AddComponent<LayoutElement>();
            bodyLayout.minHeight = 40;

            // Options container
            var optionsObj = new GameObject("Options");
            optionsObj.transform.SetParent(_dialoguePanel.transform, false);
            optionsObj.AddComponent<RectTransform>();
            var optLayout = optionsObj.AddComponent<VerticalLayoutGroup>();
            optLayout.spacing = 4;
            optLayout.childForceExpandWidth = true;
            optLayout.childForceExpandHeight = false;
            _optionsContainer = optionsObj.transform;

            _dialoguePanel.SetActive(false);
        }

        // =====================================================================
        // Dialogue Event Overrides
        // =====================================================================

        public override void OnDialogueStarted()
        {
            if (_dialoguePanel != null)
                _dialoguePanel.SetActive(true);
        }

        public override void HandleDialogueUpdated(StoryFlowDialogueState state)
        {
            if (state == null || !state.IsValid) return;

            // Character name
            if (_charNameText != null)
            {
                var charName = state.Character != null ? state.Character.Name : "";
                _charNameText.text = charName ?? "";
                _charNameText.gameObject.SetActive(!string.IsNullOrEmpty(charName));
            }

            // Character portrait
            if (_charPortrait != null)
            {
                var portrait = state.Character != null ? state.Character.Image : null;
                _charPortrait.sprite = portrait;
                _charPortrait.gameObject.SetActive(portrait != null);
            }

            // Body text
            if (_bodyText != null)
                _bodyText.text = state.Text ?? "";

            // Background image
            if (_backgroundImage != null)
            {
                if (state.Image != null)
                {
                    _backgroundImage.sprite = state.Image;
                    _backgroundImage.gameObject.SetActive(true);
                }
                else
                {
                    _backgroundImage.gameObject.SetActive(false);
                }
            }

            // Clear old buttons
            foreach (var btn in _optionButtons)
                if (btn != null) Destroy(btn);
            _optionButtons.Clear();

            // Build option buttons
            if (state.Options != null && state.Options.Count > 0)
            {
                foreach (var option in state.Options)
                {
                    var btnObj = CreateButton(_optionsContainer, option.Text);
                    string capturedId = option.Id;
                    btnObj.GetComponent<Button>().onClick.AddListener(() => SelectOption(capturedId));
                    _optionButtons.Add(btnObj);
                }
            }
            else if (state.CanAdvance)
            {
                string label = state.AudioAdvanceOnEnd && !state.AudioAllowSkip ? "" :
                               state.AudioAdvanceOnEnd && state.AudioAllowSkip ? "Skip" : "Continue";
                if (!string.IsNullOrEmpty(label))
                {
                    var btnObj = CreateButton(_optionsContainer, label);
                    btnObj.GetComponent<Button>().onClick.AddListener(() => AdvanceDialogue());
                    _optionButtons.Add(btnObj);
                }
            }
        }

        public override void OnDialogueEnded()
        {
            if (_dialoguePanel != null)
                _dialoguePanel.SetActive(false);
            if (_backgroundImage != null)
                _backgroundImage.gameObject.SetActive(false);

            foreach (var btn in _optionButtons)
                if (btn != null) Destroy(btn);
            _optionButtons.Clear();
        }

        public override void OnBackgroundImageChanged(Sprite bgImage)
        {
            if (_backgroundImage == null) return;
            if (bgImage != null)
            {
                _backgroundImage.sprite = bgImage;
                _backgroundImage.gameObject.SetActive(true);
            }
            else
            {
                _backgroundImage.gameObject.SetActive(false);
            }
        }

        // =====================================================================
        // Cleanup
        // =====================================================================

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (_canvasObj != null)
                Destroy(_canvasObj);
        }

        // =====================================================================
        // UI Helpers
        // =====================================================================

        private static GameObject CreateButton(Transform parent, string label)
        {
            var obj = new GameObject("Btn_" + label);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            var img = obj.AddComponent<Image>();
            img.color = new Color(0.25f, 0.25f, 0.3f, 1f);
            var btn = obj.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.35f, 0.45f, 1f);
            colors.pressedColor = new Color(0.2f, 0.2f, 0.25f, 1f);
            btn.colors = colors;
            obj.AddComponent<LayoutElement>().minHeight = 36;

            var textObj = new GameObject("Text");
            textObj.transform.SetParent(obj.transform, false);
            var textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 0);
            textRect.offsetMax = new Vector2(-10, 0);
            var tmp = textObj.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 16;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.verticalAlignment = VerticalAlignmentOptions.Middle;

            return obj;
        }
    }
}
