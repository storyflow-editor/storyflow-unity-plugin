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

                    if (charField == "Name")
                        return character.Name ?? match.Value;

                    // Check character variables
                    if (character.Variables != null &&
                        character.Variables.TryGetValue(charField, out var charVar))
                        return charVar.ToString();

                    return match.Value;
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
    }
}
