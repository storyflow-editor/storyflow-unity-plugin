using System;
using System.Text.RegularExpressions;
using StoryFlow.Data;
using StoryFlow.Execution;

namespace StoryFlow.Utilities
{
    public static class StoryFlowInterpolation
    {
        private static readonly Regex VariablePattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

        /// <summary>
        /// Interpolates {varname} placeholders in text using variables from the execution context.
        /// Special: {Character.Name} resolves to the current dialogue character's name.
        /// Special: {Character.VarName} resolves to a character variable value.
        /// Special: {charVar.Name} / {charVar.InnerVar} reaches through a character-TYPE
        /// variable to a field on the character it points to.
        /// </summary>
        public static string Interpolate(string text, StoryFlowExecutionContext context)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return VariablePattern.Replace(text, match =>
            {
                var varName = match.Groups[1].Value;

                // Handle Character.X pattern
                if (varName.StartsWith("Character."))
                {
                    var charField = varName.Substring("Character.".Length);
                    var character = context.CurrentDialogueState?.Character;
                    if (character == null) return match.Value;

                    if (charField.Equals("Name", StringComparison.OrdinalIgnoreCase))
                        return character.Name ?? match.Value;

                    // Check character variables
                    if (character.Variables != null &&
                        character.Variables.TryGetValue(charField, out var charVar))
                        return ResolveVariantText(charVar, context);

                    return match.Value;
                }

                // Handle {charVarName.innerField}: reach through a character-TYPE variable to
                // a field on the character it points to (e.g. {player1.Name}). Mirrors the HTML
                // runtime's {charVarName.innerVarName} interpolation. Only a token whose left
                // side resolves to a character-type variable is handled here; anything else
                // falls through to the flat-variable lookup below.
                int dotIndex = varName.IndexOf('.');
                if (dotIndex > 0)
                {
                    var charVarName = varName.Substring(0, dotIndex);
                    var charTypeVar = context.FindVariableByName(charVarName, searchLocal: true, searchGlobal: true);
                    if (charTypeVar != null && charTypeVar.Type == StoryFlowVariableType.Character)
                    {
                        var innerField = varName.Substring(dotIndex + 1);
                        var character = context.FindCharacter(charTypeVar.Value.GetString());
                        if (character == null) return match.Value;

                        if (innerField.Equals("Name", StringComparison.OrdinalIgnoreCase))
                            return character.Name ?? match.Value;

                        if (character.Variables != null &&
                            character.Variables.TryGetValue(innerField, out var innerVar))
                            return ResolveVariantText(innerVar, context);

                        return match.Value;
                    }
                }

                // Try local variables first
                var localVar = context.FindVariableByName(varName, searchLocal: true, searchGlobal: false);
                if (localVar != null)
                {
                    var value = localVar.Value.ToString();
                    if (localVar.Type == StoryFlowVariableType.String)
                        value = context.ResolveStringKey(value);
                    return value;
                }

                // Then global variables
                var globalVar = context.FindVariableByName(varName, searchLocal: false, searchGlobal: true);
                if (globalVar != null)
                {
                    var value = globalVar.Value.ToString();
                    if (globalVar.Type == StoryFlowVariableType.String)
                        value = context.ResolveStringKey(value);
                    return value;
                }

                // Not found — return original placeholder
                return match.Value;
            });
        }

        /// <summary>
        /// Renders a character variable's value as display text. String-typed values are
        /// strings-table keys (the editor export keys them), so they resolve through the
        /// current language — same rule as a plain {stringVar}. Other types stringify directly.
        /// </summary>
        private static string ResolveVariantText(StoryFlowVariant variant, StoryFlowExecutionContext context)
        {
            var text = variant.ToString();
            if (variant.Type == StoryFlowVariableType.String)
                text = context.ResolveStringKey(text);
            return text;
        }
    }
}
