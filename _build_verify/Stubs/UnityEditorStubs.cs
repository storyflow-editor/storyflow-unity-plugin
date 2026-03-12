// =============================================================================
// Unity Editor Stubs for Build Verification
// =============================================================================
// Minimal type definitions for UnityEditor types used by the StoryFlow plugin's
// Editor/ scripts. Only signatures needed for compilation — no real behavior.
// =============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor
{
    // -------------------------------------------------------------------------
    // EditorWindow
    // -------------------------------------------------------------------------

    public class EditorWindow : ScriptableObject
    {
        public Vector2 minSize { get; set; }
        public Vector2 maxSize { get; set; }
        public string titleContent { get; set; }
        public static T GetWindow<T>() where T : EditorWindow => default;
        public static T GetWindow<T>(string title) where T : EditorWindow => default;
        public static T GetWindow<T>(bool utility) where T : EditorWindow => default;
        public static T GetWindow<T>(string title, bool focus) where T : EditorWindow => default;
        public void Repaint() { }
        public void Close() { }
        public void Focus() { }
        public void Show() { }
        public void ShowUtility() { }
        protected virtual void OnGUI() { }
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        protected virtual void OnDestroy() { }
    }

    // -------------------------------------------------------------------------
    // Editor (custom inspector base)
    // -------------------------------------------------------------------------

    public class Editor : ScriptableObject
    {
        public UnityEngine.Object target { get; set; }
        public UnityEngine.Object[] targets { get; set; }
        public SerializedObject serializedObject => null;
        public virtual void OnInspectorGUI() { }
        public bool DrawDefaultInspector() => false;
        public void Repaint() { }
        public static Editor CreateEditor(UnityEngine.Object obj) => null;
        public static Editor CreateEditor(UnityEngine.Object obj, Type editorType) => null;
    }

    // -------------------------------------------------------------------------
    // EditorGUILayout
    // -------------------------------------------------------------------------

    public static class EditorGUILayout
    {
        public static void Space(float width = 0) { }
        public static void LabelField(string label) { }
        public static void LabelField(string label, GUIStyle style) { }
        public static void LabelField(string label, string label2) { }
        public static void LabelField(string label, params GUILayoutOption[] options) { }
        public static void LabelField(string label, GUIStyle style, params GUILayoutOption[] options) { }
        public static string TextField(string text) => text;
        public static string TextField(string text, params GUILayoutOption[] options) => text;
        public static string TextField(string label, string text) => text;
        public static int IntField(int value) => value;
        public static int IntField(int value, params GUILayoutOption[] options) => value;
        public static int IntField(string label, int value) => value;
        public static float FloatField(float value) => value;
        public static float FloatField(string label, float value) => value;
        public static bool Toggle(string label, bool value) => value;
        public static bool Toggle(bool value) => value;
        public static bool Foldout(bool foldout, string content) => foldout;
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick) => foldout;
        public static bool Foldout(bool foldout, string content, bool toggleOnLabelClick, GUIStyle style) => foldout;
        public static void HelpBox(string message, MessageType type) { }
        public static void PropertyField(SerializedProperty property) { }
        public static void PropertyField(SerializedProperty property, GUIContent label) { }
        public static void PropertyField(SerializedProperty property, bool includeChildren) { }
        public static void PropertyField(SerializedProperty property, GUIContent label, bool includeChildren) { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 scrollPosition, params GUILayoutOption[] options) => scrollPosition;
        public static void EndScrollView() { }
        public static UnityEngine.Object ObjectField(string label, UnityEngine.Object obj, Type objType, bool allowSceneObjects) => obj;
        public static void Separator() { }
        public static int Popup(string label, int selectedIndex, string[] displayedOptions) => selectedIndex;
        public static Enum EnumPopup(string label, Enum selected) => selected;
    }

    // -------------------------------------------------------------------------
    // EditorGUI
    // -------------------------------------------------------------------------

    public static class EditorGUI
    {
        public static int indentLevel { get; set; }
        public static bool showMixedValue { get; set; }
        public static void BeginDisabledGroup(bool disabled) { }
        public static void EndDisabledGroup() { }
        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() => false;
        public static void LabelField(Rect position, string label) { }
        public static void LabelField(Rect position, string label, GUIStyle style) { }
        public static string TextField(Rect position, string text) => text;
        public static string TextField(Rect position, string label, string text) => text;
        public static bool Foldout(Rect position, bool foldout, string content) => foldout;
        public static void PropertyField(Rect position, SerializedProperty property) { }
        public static void PropertyField(Rect position, SerializedProperty property, GUIContent label) { }
        public static float GetPropertyHeight(SerializedProperty property) => 18f;
        public static float GetPropertyHeight(SerializedProperty property, GUIContent label) => 18f;
        public static float GetPropertyHeight(SerializedProperty property, GUIContent label, bool includeChildren) => 18f;
    }

    // -------------------------------------------------------------------------
    // EditorUtility
    // -------------------------------------------------------------------------

    public static class EditorUtility
    {
        public static void SetDirty(UnityEngine.Object target) { }
        public static void DisplayProgressBar(string title, string info, float progress) { }
        public static void ClearProgressBar() { }
        public static bool DisplayDialog(string title, string message, string ok) => true;
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => true;
        public static int DisplayDialogComplex(string title, string message, string ok, string cancel, string alt) => 0;
        public static string OpenFolderPanel(string title, string folder, string defaultName) => "";
        public static string OpenFilePanel(string title, string directory, string extension) => "";
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => "";
    }

    // -------------------------------------------------------------------------
    // EditorGUIUtility
    // -------------------------------------------------------------------------

    public static class EditorGUIUtility
    {
        public static float singleLineHeight => 18f;
        public static float standardVerticalSpacing => 2f;
        public static float labelWidth { get; set; }
        public static void PingObject(UnityEngine.Object obj) { }
        public static Texture2D IconContent(string name) => null;
    }

    // -------------------------------------------------------------------------
    // EditorApplication
    // -------------------------------------------------------------------------

    public static class EditorApplication
    {
        public static event Action update;
        public static Action delayCall { get; set; }
        public static bool isPlaying { get; set; }
        public static bool isPlayingOrWillChangePlaymode => false;
        public static bool isCompiling => false;
        public static void RepaintHierarchyWindow() { }
        public static void RepaintProjectWindow() { }
    }

    // -------------------------------------------------------------------------
    // EditorStyles
    // -------------------------------------------------------------------------

    public static class EditorStyles
    {
        public static GUIStyle boldLabel => new GUIStyle();
        public static GUIStyle label => new GUIStyle();
        public static GUIStyle miniLabel => new GUIStyle();
        public static GUIStyle miniButton => new GUIStyle();
        public static GUIStyle wordWrappedLabel => new GUIStyle();
        public static GUIStyle foldout => new GUIStyle();
        public static GUIStyle foldoutHeader => new GUIStyle();
        public static GUIStyle toolbar => new GUIStyle();
        public static GUIStyle toolbarButton => new GUIStyle();
        public static GUIStyle helpBox => new GUIStyle();
        public static GUIStyle objectField => new GUIStyle();
        public static GUIStyle textField => new GUIStyle();
    }

    // -------------------------------------------------------------------------
    // AssetDatabase
    // -------------------------------------------------------------------------

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => default;
        public static UnityEngine.Object LoadAssetAtPath(string assetPath, Type type) => null;
        public static string GetAssetPath(UnityEngine.Object assetObject) => "";
        public static string[] FindAssets(string filter) => new string[0];
        public static string[] FindAssets(string filter, string[] searchInFolders) => new string[0];
        public static string GUIDToAssetPath(string guid) => "";
        public static string AssetPathToGUID(string path) => "";
        public static void CreateAsset(UnityEngine.Object asset, string path) { }
        public static void SaveAssets() { }
        public static void Refresh() { }
        public static void ImportAsset(string path) { }
        public static void ImportAsset(string path, ImportAssetOptions options) { }
        public static bool DeleteAsset(string path) => false;
        public static string[] GetSubFolders(string path) => new string[0];
        public static bool IsValidFolder(string path) => false;
        public static string CreateFolder(string parentFolder, string newFolderName) => "";
        public static bool CopyAsset(string path, string newPath) => false;
        public static string MoveAsset(string oldPath, string newPath) => "";
        public static string GenerateUniqueAssetPath(string path) => path;
        public static void AddObjectToAsset(UnityEngine.Object objectToAdd, UnityEngine.Object assetObject) { }
        public static void AddObjectToAsset(UnityEngine.Object objectToAdd, string path) { }
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string assetPath) => new UnityEngine.Object[0];
    }

    public enum ImportAssetOptions
    {
        Default,
        ForceUpdate,
        ForceSynchronousImport,
        ImportRecursive,
        DontDownloadFromCacheServer,
        ForceUncompressedImport
    }

    // -------------------------------------------------------------------------
    // Selection
    // -------------------------------------------------------------------------

    public static class Selection
    {
        public static UnityEngine.Object activeObject { get; set; }
        public static GameObject activeGameObject { get; set; }
        public static Transform activeTransform { get; set; }
        public static UnityEngine.Object[] objects { get; set; }
        public static GameObject[] gameObjects => new GameObject[0];
    }

    // -------------------------------------------------------------------------
    // SerializedObject / SerializedProperty
    // -------------------------------------------------------------------------

    public class SerializedObject : IDisposable
    {
        public UnityEngine.Object targetObject => null;
        public UnityEngine.Object[] targetObjects => null;
        public SerializedObject(UnityEngine.Object obj) { }
        public SerializedObject(UnityEngine.Object[] objs) { }
        public void Update() { }
        public bool ApplyModifiedProperties() => false;
        public bool ApplyModifiedPropertiesWithoutUndo() => false;
        public SerializedProperty FindProperty(string propertyPath) => new SerializedProperty();
        public SerializedProperty GetIterator() => new SerializedProperty();
        public void Dispose() { }
    }

    public class SerializedProperty
    {
        public string name => "";
        public string displayName => "";
        public string propertyPath => "";
        public SerializedPropertyType propertyType => SerializedPropertyType.Integer;
        public bool isArray => false;
        public int arraySize { get; set; }
        public bool isExpanded { get; set; }
        public int intValue { get; set; }
        public float floatValue { get; set; }
        public bool boolValue { get; set; }
        public string stringValue { get; set; }
        public UnityEngine.Object objectReferenceValue { get; set; }
        public Vector2 vector2Value { get; set; }
        public Vector3 vector3Value { get; set; }
        public Color colorValue { get; set; }
        public Rect rectValue { get; set; }
        public int enumValueIndex { get; set; }
        public bool hasChildren => false;
        public int depth => 0;
        public bool Next(bool enterChildren) => false;
        public bool NextVisible(bool enterChildren) => false;
        public SerializedProperty GetArrayElementAtIndex(int index) => new SerializedProperty();
        public void InsertArrayElementAtIndex(int index) { }
        public void DeleteArrayElementAtIndex(int index) { }
        public SerializedProperty FindPropertyRelative(string relativePropertyPath) => new SerializedProperty();
        public SerializedProperty Copy() => new SerializedProperty();
    }

    public enum SerializedPropertyType
    {
        Integer, Boolean, Float, String, Color, ObjectReference, LayerMask,
        Enum, Vector2, Vector3, Vector4, Rect, ArraySize, Character,
        AnimationCurve, Bounds, Gradient, Quaternion, ExposedReference,
        FixedBufferSize, Vector2Int, Vector3Int, RectInt, BoundsInt
    }

    // -------------------------------------------------------------------------
    // Attributes
    // -------------------------------------------------------------------------

    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItemAttribute : Attribute
    {
        public string menuItem;
        public bool validate;
        public int priority;
        public MenuItemAttribute(string itemName) { this.menuItem = itemName; }
        public MenuItemAttribute(string itemName, bool isValidateFunction) { this.menuItem = itemName; this.validate = isValidateFunction; }
        public MenuItemAttribute(string itemName, bool isValidateFunction, int priority) { this.menuItem = itemName; this.validate = isValidateFunction; this.priority = priority; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CustomEditorAttribute : Attribute
    {
        public Type inspectedType;
        public bool editorForChildClasses;
        public CustomEditorAttribute(Type inspectedType) { this.inspectedType = inspectedType; }
        public CustomEditorAttribute(Type inspectedType, bool editorForChildClasses) { this.inspectedType = inspectedType; this.editorForChildClasses = editorForChildClasses; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class InitializeOnLoadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class CanEditMultipleObjects : Attribute { }

    // -------------------------------------------------------------------------
    // SettingsProvider
    // -------------------------------------------------------------------------

    public class SettingsProvider
    {
        public string label { get; set; }
        public string settingsPath { get; set; }
        public string[] keywords { get; set; }
        public Action<string> guiHandler { get; set; }
        public Action<string, VisualElement> activateHandler { get; set; }

        public SettingsProvider(string path, SettingsScope scope)
        {
            settingsPath = path;
        }

        public SettingsProvider(string path, SettingsScope scope, IEnumerable<string> keywords)
        {
            settingsPath = path;
        }
    }

    // Stub for UIElements VisualElement referenced by SettingsProvider activateHandler
    public class VisualElement { }

    public enum SettingsScope
    {
        User,
        Project
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class SettingsProviderAttribute : Attribute { }

    // -------------------------------------------------------------------------
    // EditorPrefs
    // -------------------------------------------------------------------------

    public static class EditorPrefs
    {
        public static bool GetBool(string key, bool defaultValue = false) => defaultValue;
        public static void SetBool(string key, bool value) { }
        public static int GetInt(string key, int defaultValue = 0) => defaultValue;
        public static void SetInt(string key, int value) { }
        public static float GetFloat(string key, float defaultValue = 0f) => defaultValue;
        public static void SetFloat(string key, float value) { }
        public static string GetString(string key, string defaultValue = "") => defaultValue;
        public static void SetString(string key, string value) { }
        public static bool HasKey(string key) => false;
        public static void DeleteKey(string key) { }
        public static void DeleteAll() { }
    }

    // -------------------------------------------------------------------------
    // SettingsService
    // -------------------------------------------------------------------------

    public static class SettingsService
    {
        public static void OpenProjectSettings(string settingsPath) { }
        public static void OpenUserPreferences(string settingsPath) { }
    }

    // -------------------------------------------------------------------------
    // MessageType (for HelpBox)
    // -------------------------------------------------------------------------

    public enum MessageType
    {
        None,
        Info,
        Warning,
        Error
    }

    // -------------------------------------------------------------------------
    // AssetImporter / TextureImporter
    // -------------------------------------------------------------------------

    public class AssetPostprocessor
    {
        public string assetPath => "";
    }

    public class AssetImporter : UnityEngine.Object
    {
        public string assetPath => "";
        public static AssetImporter GetAtPath(string path) => null;
        public void SaveAndReimport() { }
    }

    public class TextureImporter : AssetImporter
    {
        public TextureImporterType textureType { get; set; }
        public SpriteImportMode spriteImportMode { get; set; }
        public int maxTextureSize { get; set; }
        public TextureImporterCompression textureCompression { get; set; }
        public bool alphaIsTransparency { get; set; }
        public bool isReadable { get; set; }
        public bool mipmapEnabled { get; set; }
    }

    public enum TextureImporterType
    {
        Default,
        NormalMap,
        GUI,
        Sprite,
        Cursor,
        Cookie,
        Lightmap,
        SingleChannel,
        Shadowmask,
        DirectionalLightmap
    }

    public enum SpriteImportMode
    {
        None,
        Single,
        Multiple,
        Polygon
    }

    public enum TextureImporterCompression
    {
        Uncompressed,
        Compressed,
        CompressedHQ,
        CompressedLQ
    }

    // -------------------------------------------------------------------------
    // HandleUtility
    // -------------------------------------------------------------------------

    public static class HandleUtility
    {
        public static float GetHandleSize(Vector3 position) => 1f;
        public static void Repaint() { }
    }

    // -------------------------------------------------------------------------
    // Undo
    // -------------------------------------------------------------------------

    public static class Undo
    {
        public static void RecordObject(UnityEngine.Object objectToUndo, string name) { }
        public static void RegisterCreatedObjectUndo(UnityEngine.Object objectToUndo, string name) { }
        public static void DestroyObjectImmediate(UnityEngine.Object objectToDestroy) { }
        public static void SetCurrentGroupName(string name) { }
        public static int GetCurrentGroup() => 0;
        public static void CollapseUndoOperations(int groupIndex) { }
    }
}

// Provide the System.Collections.Generic.IEnumerable<string> used in SettingsProvider constructor
// (The using directive above should handle this, but ensure it's available)
