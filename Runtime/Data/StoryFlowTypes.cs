using System;
using System.Collections.Generic;

namespace StoryFlow.Data
{
    public enum StoryFlowNodeType
    {
        Unknown = 0,

        // Control flow
        Start,
        End,
        Branch,
        RunScript,
        RunFlow,
        EntryFlow,

        // Dialogue
        Dialogue,

        // Boolean
        GetBool,
        SetBool,
        AndBool,
        OrBool,
        NotBool,
        EqualBool,

        // Integer
        GetInt,
        SetInt,
        PlusInt,
        MinusInt,
        MultiplyInt,
        DivideInt,
        RandomInt,
        GreaterInt,
        GreaterOrEqualInt,
        LessInt,
        LessOrEqualInt,
        EqualInt,

        // Float
        GetFloat,
        SetFloat,
        PlusFloat,
        MinusFloat,
        MultiplyFloat,
        DivideFloat,
        RandomFloat,
        GreaterFloat,
        GreaterOrEqualFloat,
        LessFloat,
        LessOrEqualFloat,
        EqualFloat,

        // String
        GetString,
        SetString,
        ConcatenateString,
        EqualString,
        ContainsString,
        ToUpperCase,
        ToLowerCase,
        LengthString,

        // Enum
        GetEnum,
        SetEnum,
        EqualEnum,
        SwitchOnEnum,
        RandomBranch,

        // Type conversions
        IntToBoolean,
        FloatToBoolean,
        IntToString,
        FloatToString,
        StringToInt,
        StringToFloat,
        IntToFloat,
        FloatToInt,
        IntToEnum,
        BooleanToInt,
        BooleanToFloat,
        StringToEnum,
        EnumToString,

        // Boolean arrays
        GetBoolArray,
        SetBoolArray,
        GetBoolArrayElement,
        SetBoolArrayElement,
        GetRandomBoolArrayElement,
        AddBoolArrayElement,
        RemoveBoolArrayElement,
        ClearBoolArray,
        BoolArrayLength,
        BoolArrayContains,
        FindInBoolArray,
        ForEachBoolLoop,

        // Integer arrays
        GetIntArray,
        SetIntArray,
        GetIntArrayElement,
        SetIntArrayElement,
        GetRandomIntArrayElement,
        AddIntArrayElement,
        RemoveIntArrayElement,
        ClearIntArray,
        IntArrayLength,
        IntArrayContains,
        FindInIntArray,
        ForEachIntLoop,

        // Float arrays
        GetFloatArray,
        SetFloatArray,
        GetFloatArrayElement,
        SetFloatArrayElement,
        GetRandomFloatArrayElement,
        AddFloatArrayElement,
        RemoveFloatArrayElement,
        ClearFloatArray,
        FloatArrayLength,
        FloatArrayContains,
        FindInFloatArray,
        ForEachFloatLoop,

        // String arrays
        GetStringArray,
        SetStringArray,
        GetStringArrayElement,
        SetStringArrayElement,
        GetRandomStringArrayElement,
        AddStringArrayElement,
        RemoveStringArrayElement,
        ClearStringArray,
        StringArrayLength,
        StringArrayContains,
        FindInStringArray,
        ForEachStringLoop,

        // Image arrays
        GetImageArray,
        SetImageArray,
        GetImageArrayElement,
        SetImageArrayElement,
        GetRandomImageArrayElement,
        AddImageArrayElement,
        RemoveImageArrayElement,
        ClearImageArray,
        ImageArrayLength,
        ImageArrayContains,
        FindInImageArray,
        ForEachImageLoop,

        // Character arrays
        GetCharacterArray,
        SetCharacterArray,
        GetCharacterArrayElement,
        SetCharacterArrayElement,
        GetRandomCharacterArrayElement,
        AddCharacterArrayElement,
        RemoveCharacterArrayElement,
        ClearCharacterArray,
        CharacterArrayLength,
        CharacterArrayContains,
        FindInCharacterArray,
        ForEachCharacterLoop,

        // Audio arrays
        GetAudioArray,
        SetAudioArray,
        GetAudioArrayElement,
        SetAudioArrayElement,
        GetRandomAudioArrayElement,
        AddAudioArrayElement,
        RemoveAudioArrayElement,
        ClearAudioArray,
        AudioArrayLength,
        AudioArrayContains,
        FindInAudioArray,
        ForEachAudioLoop,

        // Media
        GetImage,
        SetImage,
        SetBackgroundImage,
        GetAudio,
        SetAudio,
        PlayAudio,
        GetCharacter,
        SetCharacter,

        // Character variables
        GetCharacterVar,
        SetCharacterVar,

        // Map variables
        GetMap,
        SetMap,
        GetMapValue,
        SetMapValue,
        HasMapKey,
        MapSize,
        MapKeys,
        MapValues,
        RemoveMapKey,
        ClearMap,
        ForEachMap,

        // Modulo (appended at the end — enum values are serialized as integers
        // into imported .asset files, so new members must never be inserted
        // mid-enum)
        ModuloInt,
        ModuloFloat,

        // Data Assets (.sfd) — the reference pill and its two accessors. Appended, per
        // the note above: these members are serialized as integers into imported assets.
        GetDataAsset,
        GetDataAssetVariable,
        SetDataAssetVariable,

        // Get Variable Names (contract §11.1): the pure enumeration over a .sfd chain's
        // declarations. Appended, same serialized-integer rule.
        GetDataAssetVariableNames,

        // Appended to preserve imported Unity enum values.
        GetDataAssetRef,
        SetDataAssetRef,
        GetDataAssetArray,
        SetDataAssetArray,
        GetDataAssetArrayElement,
        SetDataAssetArrayElement,
        GetRandomDataAssetArrayElement,
        AddDataAssetArrayElement,
        RemoveDataAssetArrayElement,
        ClearDataAssetArray,
        DataAssetArrayLength,
        DataAssetArrayContains,
        FindInDataAssetArray,
        ForEachDataAssetLoop,
        BlockRollback,
    }

    public enum StoryFlowVariableType
    {
        Boolean,
        Integer,
        Float,
        String,
        Enum,
        Image,
        Audio,
        Character,
        Map,
        DataAsset,
    }

    /// <summary>
    /// THE wire-type table: the exporter's variable-type strings mapped to
    /// <see cref="StoryFlowVariableType"/>. There is exactly ONE of these in the plugin.
    ///
    /// It lives Runtime-side rather than in the importer because both halves need it and
    /// Runtime cannot reference Editor: the data-asset store compares an accessor node's
    /// spawn-time type SNAPSHOT (wire strings, as exported) against a seed declaration
    /// (an enum), while the importer converts the same strings while building assets.
    /// Two tables would be two chances to drift, and a type this table did not know would
    /// silently become a different type on one side than on the other. The runtime
    /// evaluators (EvaluateTyped/EvaluateTypedArray, map value inputs) and the character
    /// write gate resolve their tokens here too — they used to hand-list the vocabulary,
    /// which was four extra chances to drift when a type is added.
    ///
    /// Try-shaped on purpose. Callers disagree about what an unknown type means: the
    /// importer warns and falls back to Boolean (an old export with a type this build
    /// predates still imports), while the store treats it as "no match", so a garbled
    /// snapshot degrades to the type default instead of resolving to something.
    ///
    /// MATCHING IS EXACT (StringComparer.Ordinal), including the camel-cased dataAsset token —
    /// that is the wire rule per the engine contract. The exporter writes these tokens and
    /// only these; a differently cased string is not a type this format has, and accepting
    /// one would make the plugin resolve payloads the other runtimes reject.
    /// </summary>
    public static class StoryFlowWireTypes
    {
        private static readonly Dictionary<string, StoryFlowVariableType> Table =
            new Dictionary<string, StoryFlowVariableType>(StringComparer.Ordinal)
            {
                { "boolean", StoryFlowVariableType.Boolean },
                { "integer", StoryFlowVariableType.Integer },
                { "float", StoryFlowVariableType.Float },
                { "string", StoryFlowVariableType.String },
                { "enum", StoryFlowVariableType.Enum },
                { "image", StoryFlowVariableType.Image },
                { "audio", StoryFlowVariableType.Audio },
                { "character", StoryFlowVariableType.Character },
                { "map", StoryFlowVariableType.Map },
                { "dataAsset", StoryFlowVariableType.DataAsset },
            };

        /// <summary>
        /// Converts an exported type string. Returns false for null, empty and anything not
        /// in the table, including "category", which is a section header in the editor's
        /// table and never a value.
        ///
        /// CHECK THE BOOL. On failure <paramref name="type"/> is left at default(
        /// StoryFlowVariableType), which is Boolean — member 0, a perfectly ordinary type,
        /// not a None sentinel this enum has no room for. A caller that ignores the return
        /// value silently treats every unknown type as a boolean.
        /// </summary>
        public static bool TryParseWireType(string typeString, out StoryFlowVariableType type)
        {
            if (!string.IsNullOrEmpty(typeString) && Table.TryGetValue(typeString, out type))
                return true;

            type = default;
            return false;
        }
    }
}
