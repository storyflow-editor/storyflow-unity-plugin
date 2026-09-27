using System;
using System.Text.RegularExpressions;
using StoryFlow.Data;
using StoryFlow.Execution;

namespace StoryFlow.Utilities
{
    public static class StoryFlowInterpolation
    {
        private static readonly Regex VariablePattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

        public static string Interpolate(string text, StoryFlowExecutionContext context)
        {
            if (string.IsNullOrEmpty(text) || context == null) return text;
            // Regex replacement visits authored tokens once; inserted strings remain literal.
            return VariablePattern.Replace(text, match => Resolve(match.Groups[1].Value.Trim(), context) ?? match.Value);
        }

        private static string Resolve(string path, StoryFlowExecutionContext ctx)
        {
            Func<string, StoryFlowVariable> fields;
            string remaining;
            if (path.StartsWith("Character.", StringComparison.Ordinal))
            {
                var state = ctx.CurrentDialogueState;
                var character = string.IsNullOrEmpty(state?.CharacterReference) ? state?.Character : ctx.FindCharacter(state.CharacterReference);
                fields = CharacterFields(character, ctx);
                remaining = path.Substring(10).Trim();
            }
            else
            {
                int dot = path.IndexOf('.');
                if (dot < 0) return Leaf(ctx.FindVariableByName(path), ctx);
                var root = ctx.FindVariableByName(path.Substring(0, dot));
                fields = Fields(root, ctx);
                remaining = path.Substring(dot + 1);
            }
            while (fields != null)
            {
                var exact = fields(remaining);
                if (exact != null) return Leaf(exact, ctx);
                int dot = remaining.IndexOf('.');
                if (dot < 0) return null;
                fields = Fields(fields(remaining.Substring(0, dot)), ctx);
                remaining = remaining.Substring(dot + 1);
            }
            return null;
        }

        private static Func<string, StoryFlowVariable> Fields(StoryFlowVariable reference, StoryFlowExecutionContext ctx)
        {
            if (reference == null || reference.IsArray || reference.Value == null || reference.Value.ArrayValue != null) return null;
            string id = reference.Value.GetString();
            if (string.IsNullOrEmpty(id)) return null;
            if (reference.Type == StoryFlowVariableType.Character) return CharacterFields(ctx.FindCharacter(id), ctx);
            var store = ctx.DataAssetStore;
            if (reference.Type != StoryFlowVariableType.DataAsset || store == null || !store.IsValid || !StoryFlowDataAssetStore.HasAsset(store.Seed, id)) return null;
            return name =>
            {
                var declaration = StoryFlowDataAssetStore.FindDeclarationByName(store.Seed, id, name);
                if (declaration == null || !StoryFlowDataAssetStore.TryRead(store.Seed, store.Overlay, ctx.Project, ctx.ActiveLanguageCode, id, declaration.Id, out var value)) return null;
                if (value.Type == StoryFlowVariableType.String) value.IsLiteralString = true;
                var field = new StoryFlowVariable(declaration); field.Value = value; return field;
            };
        }

        private static Func<string, StoryFlowVariable> CharacterFields(StoryFlowCharacterData character, StoryFlowExecutionContext ctx)
        {
            if (character == null) return null;
            return name =>
            {
                name = StoryFlowCharacterTokens.RewriteCfTokensOnly(name);
                if (StoryFlowCharacterTokens.IsCharacterNameBuiltin(name))
                    return new StoryFlowVariable { Type = StoryFlowVariableType.String, Value = new StoryFlowVariant { Type = StoryFlowVariableType.String, StringValue = character.Name, IsLiteralString = true } };
                var declaration = character.FindVariableByName(name);
                if (declaration != null) return declaration;
                return character.Variables != null && character.Variables.TryGetValue(name, out var value)
                    ? new StoryFlowVariable { Type = value.Type, IsArray = value.ArrayValue != null, Value = value } : null;
            };
        }

        private static string Leaf(StoryFlowVariable field, StoryFlowExecutionContext ctx)
        {
            if (field == null || field.IsArray || field.Value == null || field.Value.ArrayValue != null) return null;
            switch (field.Type)
            {
                case StoryFlowVariableType.Boolean: return field.Value.GetBool() ? "true" : "false";
                case StoryFlowVariableType.Integer:
                case StoryFlowVariableType.Float:
                case StoryFlowVariableType.Enum: return field.Value.ToString();
                case StoryFlowVariableType.String: return field.Value.IsLiteralString ? field.Value.GetString() : ctx.ResolveStringKey(field.Value.GetString());
                default: return null;
            }
        }
    }
}
