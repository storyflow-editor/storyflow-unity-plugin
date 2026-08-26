namespace StoryFlow.Utilities
{
    /// <summary>
    /// The two token vocabularies P4 character resolution runs on, in ONE home so no lane
    /// can drift from another (characters engine contract §4 + amendments A1/A2(a)).
    /// </summary>
    public static class StoryFlowCharacterTokens
    {
        /// <summary>
        /// True when the string is shaped like a character FILE id, mirroring the editor
        /// exporter's isCharacterIdRef: startsWith("da_"), CASE-SENSITIVE — ids preserve
        /// case per the V2 case rules. Shape only: .sfd data-asset ids share the prefix,
        /// so whether a da_ string names a CHARACTER is decided by the character id
        /// bridge, never by this test.
        /// </summary>
        public static bool IsCharacterIdRef(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.StartsWith("da_", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// True when a character-variable access names the Name builtin: the display
        /// spelling or the contract-reserved cf_name id (amendment A1 — character-variable
        /// access stays NAME-keyed; the cf_ ids do nothing more than alias the two builtin
        /// rows). CASE-INSENSITIVE like the display spelling it aliases, deliberately
        /// unlike <see cref="IsCharacterIdRef"/> above: these are authored variable names,
        /// not stored ids, and the two vocabularies carry different case rules (the Unreal
        /// arc's F3 lesson — mixing them up is invisible until an authored spelling misses).
        /// </summary>
        public static bool IsCharacterNameBuiltin(string variableName)
        {
            return string.Equals(variableName, "Name", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(variableName, "cf_name", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Image twin of <see cref="IsCharacterNameBuiltin"/> (Image | cf_image, A1).</summary>
        public static bool IsCharacterImageBuiltin(string variableName)
        {
            return string.Equals(variableName, "Image", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(variableName, "cf_image", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
