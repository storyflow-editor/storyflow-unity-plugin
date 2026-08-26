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

        /// <summary>
        /// Rewrites ONLY the reserved cf_ tokens to their builtin display spellings
        /// (cf_name → Name, cf_image → Image); every other input passes through
        /// BYTE-UNTOUCHED. This is the second tier of a deliberate two-tier design:
        ///
        ///  - Lanes that carried case-INSENSITIVE builtin arms before P4 (the evaluator,
        ///    the node write arms, public GetCharacterVariable, interpolation's Name arm)
        ///    use the full predicates above — nothing downstream of those arms sees the
        ///    token again, so the wide match changes nothing pre-P4.
        ///  - Lanes that were case-SENSITIVE or arm-less (interpolation's custom-variable
        ///    dictionary lookup, public SetCharacterVariable's list search) use THIS
        ///    rewrite instead: a pre-P4 custom variable named "image" (lowercase) is a
        ///    different variable from "Image" on those lanes and has to stay one, so a
        ///    native spelling must never be rewritten there.
        ///
        /// The cf_ match itself is safely case-insensitive: the editor reserves the cf_
        /// ids absolutely, so no custom variable can carry them in any casing.
        /// </summary>
        public static string CanonicalizeCfToken(string variableName)
        {
            if (string.Equals(variableName, "cf_name", System.StringComparison.OrdinalIgnoreCase))
                return "Name";
            if (string.Equals(variableName, "cf_image", System.StringComparison.OrdinalIgnoreCase))
                return "Image";
            return variableName;
        }
    }
}
