namespace StoryFlow.Data
{
    /// <summary>
    /// Standardized handle string builders matching StoryFlow Editor format.
    /// Source handles: "source-{nodeId}-{suffix}"
    /// Target handles: "target-{nodeId}-{suffix}"
    /// </summary>
    public static class StoryFlowHandles
    {
        // Source output suffixes
        public const string Out_Default = "";
        public const string Out_True = "true";
        public const string Out_False = "false";
        public const string Out_Flow = "1";
        public const string Out_Output = "output";
        public const string Out_Boolean = "boolean-";
        public const string Out_Integer = "integer-";
        public const string Out_Float = "float-";
        public const string Out_String = "string-";
        public const string Out_Enum = "enum-";
        public const string Out_LoopBody = "loopBody";
        public const string Out_LoopCompleted = "completed";

        // Target input suffixes — single typed inputs
        public const string In_Default = "";
        public const string In_Boolean = "boolean";
        public const string In_Integer = "integer";
        public const string In_Float = "float";
        public const string In_String = "string";
        public const string In_Enum = "enum";
        public const string In_Image = "image";
        public const string In_Audio = "audio";
        public const string In_Character = "character";

        // Numbered inputs (binary operations: and, or, equal, arithmetic, etc.)
        public const string In_Boolean1 = "boolean-1";
        public const string In_Boolean2 = "boolean-2";
        public const string In_BooleanCondition = "boolean-condition";
        public const string In_Integer1 = "integer-1";
        public const string In_Integer2 = "integer-2";
        public const string In_IntegerIndex = "integer-index";
        public const string In_IntegerValue = "integer-value";
        public const string In_Float1 = "float-1";
        public const string In_Float2 = "float-2";
        public const string In_String1 = "string-1";
        public const string In_String2 = "string-2";
        public const string In_Enum1 = "enum-1";
        public const string In_Enum2 = "enum-2";

        // Array inputs
        public const string In_BoolArray = "boolean-array";
        public const string In_IntArray = "integer-array";
        public const string In_FloatArray = "float-array";
        public const string In_StringArray = "string-array";
        public const string In_ImageArray = "image-array";
        public const string In_CharacterArray = "character-array";
        public const string In_AudioArray = "audio-array";

        // Media node inputs
        public const string In_ImageInput = "image-image-input";
        public const string In_AudioInput = "audio-audio-input";
        public const string In_CharacterInput = "character-character-input";

        public static string Source(string nodeId, string suffix = "")
        {
            return string.Concat("source-", nodeId, "-", suffix);
        }

        public static string Target(string nodeId, string suffix = "")
        {
            return string.Concat("target-", nodeId, "-", suffix);
        }

        public static string SourceOption(string nodeId, string optionId)
        {
            return string.Concat("source-", nodeId, "-", optionId);
        }

        public static string SourceExit(string nodeId, string flowId)
        {
            return string.Concat("source-", nodeId, "-exit-", flowId);
        }

        public static string SourceTypedValue(string nodeId, string type, string optionId)
        {
            return string.Concat("source-", nodeId, "-", type, "-value-", optionId);
        }
    }
}
