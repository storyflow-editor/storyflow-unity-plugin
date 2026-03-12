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
    }
}
