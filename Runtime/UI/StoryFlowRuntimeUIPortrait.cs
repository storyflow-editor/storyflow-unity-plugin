using System.Collections.Generic;
using StoryFlow.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StoryFlow.UI
{
    /// <summary>
    /// Alternative dialogue UI that renders background and character images as 2D sprites
    /// in world space, with only the dialogue panel as UI overlay. The character portrait
    /// is anchored to the bottom center of the screen, scaled to 95% of viewport height,
    /// with the bottom 11.6% extending below the screen edge.
    ///
    /// Requires an orthographic camera (uses Camera.main).
    /// </summary>
    public class StoryFlowRuntimeUIPortrait : StoryFlowDialogueUI
    {
        private TextMeshProUGUI _charNameText;
        private TextMeshProUGUI _bodyText;
        private Transform _optionsContainer;
        private GameObject _dialoguePanel;
        private GameObject _canvasObj;
        private readonly List<GameObject> _optionButtons = new();

        // 2D sprite rendering
        private SpriteRenderer _bgRenderer;
        private SpriteRenderer _charRenderer;
        private GameObject _spriteRoot;

        // =====================================================================
        // Initialization
        // =====================================================================

        public void Build()
        {
            // EventSystem
#if UNITY_2023_1_OR_NEWER
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
#else
            if (Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
#endif
            {
                var esObj = new GameObject("EventSystem");
                esObj.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esObj.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            }

            // Sprite root (parent for 2D sprites, cleaned up on destroy)
            _spriteRoot = new GameObject("[StoryFlow Sprites]");

            // Background sprite (full screen, behind character)
            var bgObj = new GameObject("Background");
            bgObj.transform.SetParent(_spriteRoot.transform);
            _bgRenderer = bgObj.AddComponent<SpriteRenderer>();
            _bgRenderer.sortingOrder = -1;
            bgObj.SetActive(false);

            // Character sprite (bottom center, in front of background)
            var charObj = new GameObject("CharacterPortrait");
            charObj.transform.SetParent(_spriteRoot.transform);
            _charRenderer = charObj.AddComponent<SpriteRenderer>();
            _charRenderer.sortingOrder = 0;
            charObj.SetActive(false);

            // Canvas (UI overlay for dialogue panel only)
            _canvasObj = new GameObject("[StoryFlow UI Portrait]");
            var canvas = _canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            _canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasObj.AddComponent<GraphicRaycaster>();

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

            // Character name
            var charNameObj = new GameObject("CharName");
            charNameObj.transform.SetParent(_dialoguePanel.transform, false);
            charNameObj.AddComponent<RectTransform>();
            _charNameText = charNameObj.AddComponent<TextMeshProUGUI>();
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
#if UNITY_2023_1_OR_NEWER
            _bodyText.textWrappingMode = TMPro.TextWrappingModes.Normal;
#else
            _bodyText.enableWordWrapping = true;
#endif
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
        // Sprite Positioning
        // =====================================================================

        private void UpdateSpritePositions()
        {
            var cam = Camera.main;
            if (cam == null || !cam.orthographic) return;

            float viewHeight = cam.orthographicSize * 2f;
            float viewWidth = viewHeight * cam.aspect;
            float camY = cam.transform.position.y;
            float camX = cam.transform.position.x;

            // Background: scale to cover entire viewport
            if (_bgRenderer != null && _bgRenderer.sprite != null && _bgRenderer.gameObject.activeSelf)
            {
                float ppu = _bgRenderer.sprite.pixelsPerUnit;
                float bgW = _bgRenderer.sprite.texture.width / ppu;
                float bgH = _bgRenderer.sprite.texture.height / ppu;
                float scaleX = viewWidth / bgW;
                float scaleY = viewHeight / bgH;
                float bgScale = Mathf.Max(scaleX, scaleY); // cover (fill entire screen)
                // Offset for BottomCenter pivot: shift down by half the scaled height to center
                float bgWorldHeight = bgH * bgScale;
                _bgRenderer.transform.position = new Vector3(camX, camY - bgWorldHeight * 0.5f, 1f);
                _bgRenderer.transform.localScale = new Vector3(bgScale, bgScale, 1f);
            }

            // Character: 95% of viewport height, bottom center, 11.6% below screen
            if (_charRenderer != null && _charRenderer.sprite != null && _charRenderer.gameObject.activeSelf)
            {
                float charHeight = viewHeight * 0.95f;
                // Use texture height (always full 1024), not sprite bounds (which may be trimmed)
                float textureHeight = _charRenderer.sprite.texture.height / _charRenderer.sprite.pixelsPerUnit;
                float charScale = charHeight / textureHeight;
                float offsetBelow = viewHeight * 0.116f;
                float screenBottom = camY - cam.orthographicSize;
                // Sprite pivot is BottomCenter, so position = bottom edge of sprite
                float charY = screenBottom - offsetBelow;
                _charRenderer.transform.position = new Vector3(camX, charY, 0.5f);
                _charRenderer.transform.localScale = new Vector3(charScale, charScale, 1f);
            }
        }

        private void LateUpdate()
        {
            UpdateSpritePositions();
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

            // Character portrait (2D sprite)
            if (_charRenderer != null)
            {
                var portrait = state.Character != null ? state.Character.Image : null;
                _charRenderer.sprite = portrait;
                _charRenderer.gameObject.SetActive(portrait != null);
            }

            // Body text
            if (_bodyText != null)
                _bodyText.text = state.Text ?? "";

            // Background image (2D sprite)
            if (_bgRenderer != null)
            {
                if (state.Image != null)
                {
                    _bgRenderer.sprite = state.Image;
                    _bgRenderer.gameObject.SetActive(true);
                }
                else
                {
                    _bgRenderer.gameObject.SetActive(false);
                }
            }

            // Clear old buttons and text blocks
            foreach (var btn in _optionButtons)
                if (btn != null) Destroy(btn);
            _optionButtons.Clear();

            // Text blocks
            if (state.TextBlocks != null && state.TextBlocks.Count > 0)
            {
                foreach (var block in state.TextBlocks)
                {
                    var obj = new GameObject("TextBlock");
                    obj.transform.SetParent(_optionsContainer, false);
                    obj.AddComponent<RectTransform>();
                    obj.AddComponent<LayoutElement>().minHeight = 24;
                    var tmp = obj.AddComponent<TextMeshProUGUI>();
                    tmp.text = block.Text ?? "";
                    tmp.fontSize = 16;
                    tmp.color = Color.white;
#if UNITY_2023_1_OR_NEWER
                    tmp.textWrappingMode = TMPro.TextWrappingModes.Normal;
#else
                    tmp.enableWordWrapping = true;
#endif
                    _optionButtons.Add(obj);
                }
            }

            // Option buttons
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

            UpdateSpritePositions();
        }

        public override void OnDialogueEnded()
        {
            if (_dialoguePanel != null)
                _dialoguePanel.SetActive(false);
            if (_bgRenderer != null)
                _bgRenderer.gameObject.SetActive(false);
            if (_charRenderer != null)
                _charRenderer.gameObject.SetActive(false);

            foreach (var btn in _optionButtons)
                if (btn != null) Destroy(btn);
            _optionButtons.Clear();
        }

        public override void OnBackgroundImageChanged(Sprite bgImage)
        {
            if (_bgRenderer == null) return;
            if (bgImage != null)
            {
                _bgRenderer.sprite = bgImage;
                _bgRenderer.gameObject.SetActive(true);
                UpdateSpritePositions();
            }
            else
            {
                _bgRenderer.gameObject.SetActive(false);
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
            if (_spriteRoot != null)
                Destroy(_spriteRoot);
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
