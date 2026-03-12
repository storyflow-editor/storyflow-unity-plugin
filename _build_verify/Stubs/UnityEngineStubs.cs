// =============================================================================
// Unity Engine Stubs for Build Verification
// =============================================================================
// Minimal type definitions so that the StoryFlow Unity plugin compiles with
// `dotnet build` without requiring a Unity installation.
// These stubs contain NO real behavior — only signatures the compiler needs.
// =============================================================================

using System;
using System.Collections;
using System.Collections.Generic;

// =============================================================================
// UnityEngine
// =============================================================================

namespace UnityEngine
{
    // -------------------------------------------------------------------------
    // Core Object hierarchy
    // -------------------------------------------------------------------------

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static void Destroy(Object obj, float delay) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object obj) { }
        public static T Instantiate<T>(T original) where T : Object => default;
        public static T Instantiate<T>(T original, Transform parent) where T : Object => default;
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object => default;
        public static implicit operator bool(Object exists) => exists != null;
        public int GetInstanceID() => 0;
        public override string ToString() => name ?? base.ToString();
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>() => default;
        public T GetComponentInChildren<T>(bool includeInactive) => default;
        public T[] GetComponentsInChildren<T>() => default;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => default;
        public T GetComponentInParent<T>() => default;
        public T AddComponent<T>() where T : Component => default;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => enabled;
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => null;
        public Coroutine StartCoroutine(string methodName) => null;
        public void StopCoroutine(IEnumerator routine) { }
        public void StopCoroutine(Coroutine routine) { }
        public void StopCoroutine(string methodName) { }
        public void StopAllCoroutines() { }
        public void Invoke(string methodName, float time) { }
        public void InvokeRepeating(string methodName, float time, float repeatRate) { }
        public void CancelInvoke() { }
        public void CancelInvoke(string methodName) { }
        public bool IsInvoking() => false;
        public bool IsInvoking(string methodName) => false;
        public static void print(object message) { }
    }

    public class Coroutine : YieldInstruction { }

    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public WaitForSeconds(float seconds) { } }

    // -------------------------------------------------------------------------
    // ScriptableObject
    // -------------------------------------------------------------------------

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => default;
        public static ScriptableObject CreateInstance(Type type) => default;
    }

    // -------------------------------------------------------------------------
    // GameObject & Transform
    // -------------------------------------------------------------------------

    public class GameObject : Object
    {
        public Transform transform => null;
        public bool activeSelf => false;
        public bool activeInHierarchy => false;
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>() => default;
        public T GetComponentInChildren<T>(bool includeInactive) => default;
        public T AddComponent<T>() where T : Component => default;
        public void SetActive(bool value) { }
        public GameObject() { }
        public GameObject(string name) { }
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 forward => Vector3.zero;
        public Vector3 right => Vector3.zero;
        public Vector3 up => Vector3.zero;
        public Transform parent { get; set; }
        public int childCount => 0;
        public Transform GetChild(int index) => null;
        public void SetParent(Transform parent) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public IEnumerator GetEnumerator() => null;
    }

    public class RectTransform : Transform
    {
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Rect rect => default;
    }

    // -------------------------------------------------------------------------
    // Assets: Sprite, Texture2D, AudioClip, Material, Shader
    // -------------------------------------------------------------------------

    public class Sprite : Object
    {
        public Texture2D texture => null;
        public Rect rect => default;
        public Rect textureRect => default;
        public Vector2 pivot => default;
        public float pixelsPerUnit => 100f;
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) => null;
    }

    public class Texture : Object
    {
        public int width => 0;
        public int height => 0;
        public virtual int mipmapCount => 0;
    }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public void Apply() { }
        public Color GetPixel(int x, int y) => default;
        public void SetPixel(int x, int y, Color color) { }
        public byte[] EncodeToPNG() => null;
        public void LoadImage(byte[] data) { }
    }

    public class AudioClip : Object
    {
        public float length => 0f;
        public int channels => 0;
        public int frequency => 0;
        public int samples => 0;
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Color color { get; set; }
        public Shader shader { get; set; }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => null;
    }

    // -------------------------------------------------------------------------
    // AudioSource
    // -------------------------------------------------------------------------

    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public bool loop { get; set; }
        public bool playOnAwake { get; set; }
        public float spatialBlend { get; set; }
        public bool isPlaying => false;
        public UnityEngine.Audio.AudioMixerGroup outputAudioMixerGroup { get; set; }
        public void Play() { }
        public void Stop() { }
        public void Pause() { }
        public void UnPause() { }
        public static void PlayClipAtPoint(AudioClip clip, Vector3 position) { }
    }

    // -------------------------------------------------------------------------
    // Math types
    // -------------------------------------------------------------------------

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 down => new Vector2(0, -1);
        public static Vector2 left => new Vector2(-1, 0);
        public static Vector2 right => new Vector2(1, 0);
        public float magnitude => 0f;
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float magnitude => 0f;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion Euler(Vector3 euler) => default;
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height)
        {
            this.x = x; this.y = y; this.width = width; this.height = height;
        }
        public Vector2 position
        {
            get => new Vector2(x, y);
            set { x = value.x; y = value.y; }
        }
        public Vector2 size
        {
            get => new Vector2(width, height);
            set { width = value.x; height = value.y; }
        }
        public Vector2 center => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color red => new Color(1, 0, 0, 1);
        public static Color green => new Color(0, 1, 0, 1);
        public static Color blue => new Color(0, 0, 1, 1);
        public static Color yellow => new Color(1, 1, 0, 1);
        public static Color cyan => new Color(0, 1, 1, 1);
        public static Color magenta => new Color(1, 0, 1, 1);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1);
        public static Color grey => gray;
        public static Color clear => new Color(0, 0, 0, 0);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), (byte)(c.a * 255));
    }

    // -------------------------------------------------------------------------
    // Static utility classes
    // -------------------------------------------------------------------------

    public static class Debug
    {
        public static void Log(object message) { }
        public static void Log(object message, Object context) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
        public static void LogException(Exception exception) { }
        public static void LogException(Exception exception, Object context) { }
        public static void LogFormat(string format, params object[] args) { }
        public static void LogWarningFormat(string format, params object[] args) { }
        public static void LogErrorFormat(string format, params object[] args) { }
    }

    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public const float Epsilon = 1.401298E-45f;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int value) => Math.Abs(value);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static bool Approximately(float a, float b) => Math.Abs(b - a) < Math.Max(1E-06f * Math.Max(Math.Abs(a), Math.Abs(b)), Epsilon * 8f);
    }

    public static class Application
    {
        public static string persistentDataPath => "";
        public static string dataPath => "";
        public static string streamingAssetsPath => "";
        public static string temporaryCachePath => "";
        public static RuntimePlatform platform => RuntimePlatform.WindowsPlayer;
        public static bool isPlaying => false;
        public static bool isEditor => false;
        public static string version => "";
        public static string productName => "";
        public static string companyName => "";
        public static string unityVersion => "";
        public static void Quit() { }
        public static void Quit(int exitCode) { }
    }

    public enum RuntimePlatform
    {
        OSXEditor, OSXPlayer, WindowsPlayer, WindowsEditor, IPhonePlayer,
        Android, LinuxPlayer, LinuxEditor, WebGLPlayer, PS4, XboxOne, Switch
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => default;
        public static Object Load(string path) => null;
        public static T[] FindObjectsOfTypeAll<T>() where T : Object => new T[0];
        public static Object[] LoadAll(string path) => new Object[0];
        public static void UnloadUnusedAssets() { }
    }

    public static class Random
    {
        public static int Range(int min, int max) => min;
        public static float Range(float min, float max) => min;
        public static float value => 0f;
    }

    // -------------------------------------------------------------------------
    // GUILayout (runtime — also used in editor via inheritance)
    // -------------------------------------------------------------------------

    public static class GUILayout
    {
        public static bool Button(string text) => false;
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static bool Button(GUIContent content, params GUILayoutOption[] options) => false;
        public static bool Button(string text, GUIStyle style, params GUILayoutOption[] options) => false;
        public static void Label(string text) { }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static string TextField(string text, params GUILayoutOption[] options) => text;
        public static void Space(float pixels) { }
        public static void FlexibleSpace() { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) => scrollPosition;
        public static void EndScrollView() { }
        public static GUILayoutOption Width(float width) => null;
        public static GUILayoutOption MinWidth(float minWidth) => null;
        public static GUILayoutOption MaxWidth(float maxWidth) => null;
        public static GUILayoutOption Height(float height) => null;
        public static GUILayoutOption MinHeight(float minHeight) => null;
        public static GUILayoutOption MaxHeight(float maxHeight) => null;
        public static GUILayoutOption ExpandWidth(bool expand) => null;
        public static GUILayoutOption ExpandHeight(bool expand) => null;
    }

    public class GUILayoutOption { }

    public static class GUI
    {
        public static Color color { get; set; }
        public static bool changed { get; set; }
        public static void FocusControl(string name) { }
        public static bool Button(Rect position, string text) => false;
    }

    public class GUIContent
    {
        public string text;
        public Texture image;
        public string tooltip;
        public GUIContent() { }
        public GUIContent(string text) { this.text = text; }
        public GUIContent(string text, string tooltip) { this.text = text; this.tooltip = tooltip; }
        public GUIContent(string text, Texture image) { this.text = text; this.image = image; }
        public GUIContent(string text, Texture image, string tooltip) { this.text = text; this.image = image; this.tooltip = tooltip; }
        public static GUIContent none => new GUIContent();
    }

    public class GUIStyle
    {
        public string name;
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
    }

    // -------------------------------------------------------------------------
    // Attributes
    // -------------------------------------------------------------------------

    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class DisallowMultipleComponent : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute
    {
        public int order;
        public DefaultExecutionOrder(int order) { this.order = order; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class AddComponentMenu : Attribute
    {
        public string menuName;
        public int order;
        public AddComponentMenu(string menuName) { this.menuName = menuName; }
        public AddComponentMenu(string menuName, int order) { this.menuName = menuName; this.order = order; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public class HideInInspector : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public class HeaderAttribute : PropertyAttribute
    {
        public string header;
        public HeaderAttribute(string header) { this.header = header; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : PropertyAttribute
    {
        public string tooltip;
        public TooltipAttribute(string tooltip) { this.tooltip = tooltip; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class RangeAttribute : PropertyAttribute
    {
        public float min;
        public float max;
        public RangeAttribute(float min, float max) { this.min = min; this.max = max; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class SpaceAttribute : PropertyAttribute
    {
        public float height;
        public SpaceAttribute() { }
        public SpaceAttribute(float height) { this.height = height; }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class TextAreaAttribute : PropertyAttribute
    {
        public int minLines;
        public int maxLines;
        public TextAreaAttribute() { }
        public TextAreaAttribute(int minLines, int maxLines) { this.minLines = minLines; this.maxLines = maxLines; }
    }

    public class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeLoadType loadType;
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { this.loadType = loadType; }
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad,
        BeforeSceneLoad,
        AfterAssembliesLoaded,
        BeforeSplashScreen,
        SubsystemRegistration
    }
}

// =============================================================================
// UnityEngine.Audio
// =============================================================================

namespace UnityEngine.Audio
{
    public class AudioMixerGroup : UnityEngine.Object
    {
    }

    public class AudioMixer : UnityEngine.Object
    {
        public AudioMixerGroup[] FindMatchingGroups(string subPath) => null;
        public bool SetFloat(string name, float value) => false;
        public bool GetFloat(string name, out float value) { value = 0; return false; }
    }
}

// =============================================================================
// UnityEngine.Events
// =============================================================================

namespace UnityEngine.Events
{
    public class UnityEvent
    {
        public void Invoke() { }
        public void AddListener(UnityAction call) { }
        public void RemoveListener(UnityAction call) { }
        public void RemoveAllListeners() { }
    }

    public class UnityEvent<T0>
    {
        public void Invoke(T0 arg0) { }
        public void AddListener(UnityAction<T0> call) { }
        public void RemoveListener(UnityAction<T0> call) { }
        public void RemoveAllListeners() { }
    }

    public class UnityEvent<T0, T1>
    {
        public void Invoke(T0 arg0, T1 arg1) { }
        public void AddListener(UnityAction<T0, T1> call) { }
        public void RemoveListener(UnityAction<T0, T1> call) { }
        public void RemoveAllListeners() { }
    }

    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);
    public delegate void UnityAction<T0, T1, T2>(T0 arg0, T1 arg1, T2 arg2);
    public delegate void UnityAction<T0, T1, T2, T3>(T0 arg0, T1 arg1, T2 arg2, T3 arg3);
}

// =============================================================================
// UnityEngine.UI
// =============================================================================

namespace UnityEngine.UI
{
    public class Graphic : UnityEngine.Behaviour
    {
        public UnityEngine.Color color { get; set; }
        public UnityEngine.Material material { get; set; }
        public bool raycastTarget { get; set; }
    }

    public class MaskableGraphic : Graphic
    {
    }

    public class Image : MaskableGraphic
    {
        public UnityEngine.Sprite sprite { get; set; }
        public Type type { get; set; }
        public bool preserveAspect { get; set; }
        public float fillAmount { get; set; }

        public new enum Type
        {
            Simple,
            Sliced,
            Tiled,
            Filled
        }
    }

    public class Selectable : UnityEngine.Behaviour
    {
        public bool interactable { get; set; }
    }

    public class Button : Selectable
    {
        public ButtonClickedEvent onClick { get; set; } = new ButtonClickedEvent();

        public class ButtonClickedEvent : UnityEngine.Events.UnityEvent
        {
        }
    }
}

// =============================================================================
// TMPro (TextMeshPro)
// =============================================================================

namespace TMPro
{
    public class TMP_Text : UnityEngine.UI.MaskableGraphic
    {
        public string text { get; set; }
        public float fontSize { get; set; }
        public UnityEngine.Color color { get; set; }
        public TMP_FontAsset font { get; set; }
        public TextAlignmentOptions alignment { get; set; }
    }

    public class TextMeshProUGUI : TMP_Text
    {
    }

    public class TextMeshPro : TMP_Text
    {
    }

    public class TMP_FontAsset : UnityEngine.Object
    {
    }

    public enum TextAlignmentOptions
    {
        TopLeft, Top, TopRight,
        Left, Center, Right,
        BottomLeft, Bottom, BottomRight,
        Justified, Flush
    }
}
