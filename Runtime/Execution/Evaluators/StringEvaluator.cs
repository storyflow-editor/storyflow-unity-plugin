using System.Globalization;
using StoryFlow.Data;
using StoryFlow.Utilities;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates string values from expression node chains.
    /// Handles concatenation, case conversion, type conversions, and variable lookups.
    /// </summary>
    internal static class StringEvaluator
    {
        /// <summary>
        /// Evaluates the string value arriving at a specific input handle of a node.
        /// </summary>
        internal static string Evaluate(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            if (ctx?.CurrentScript == null) return "";

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return "";

            var sourceNode = ctx.ResolveInputNode(edge);
            if (sourceNode == null) return "";

            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            string result = EvaluateFromNode(ctx, sourceNode);
            ctx.LastSourceHandle = prevHandle;
            return result;
        }

        /// <summary>
        /// Evaluates a node as a string value based on its type.
        /// </summary>
        internal static string EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null || ctx == null) return "";

            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                ctx.FailResolution();
                Debug.LogWarning("[StoryFlow] String evaluation depth exceeded. Possible circular reference.");
                return "";
            }

            try
            {
                // The exemptions and why each one exists live on ShouldSkipCache.
                // Runtime Name is finished text refreshed from NameKey on language changes,
                // or a literal player name. Read it live and never interpret it as another key.
                bool characterName = (node.Type == StoryFlowNodeType.GetCharacterVar ||
                    node.Type == StoryFlowNodeType.SetCharacterVar) &&
                    StoryFlowCharacterTokens.IsCharacterNameBuiltin(node.GetData("variableName"));
                bool skipCache = EvaluatorHelpers.ShouldSkipCache(node.Type) || characterName;
                bool arrayElement = node.Type == StoryFlowNodeType.GetStringArrayElement ||
                    node.Type == StoryFlowNodeType.GetRandomStringArrayElement ||
                    node.Type == StoryFlowNodeType.ForEachStringLoop;
                bool mapValue = node.Type == StoryFlowNodeType.GetMapValue ||
                    (node.Type == StoryFlowNodeType.ForEachMap && (ctx.LastSourceHandle ?? "").EndsWith("-value"));
                bool finishedText = EvaluatorHelpers.IsDataAssetAccessor(node.Type) || arrayElement || characterName || mapValue;
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (!skipCache && state.CachedOutput != null)
                    return arrayElement ? ctx.ResolveArrayString(state.CachedOutput)
                        : ctx.ResolveStringKey(state.CachedOutput.GetString());

                string result = EvaluateFromNodeInternal(ctx, node);
                if (!skipCache && !arrayElement)
                    state.CachedOutput = StoryFlowVariant.String(result);

                if (ctx.TraceEnabled)
                {
                    var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                    Debug.Log($"[SF-TRACE] EVAL {node.Id} {typeName} result={result}");
                }

                // Data-asset reads settle localization in the store; array and map value
                // readers honor the element's provenance. Their finished text must not be
                // interpreted as another key, including opted-out fields and session writes.
                if (finishedText) return result;

                return ctx.ResolveStringKey(result);
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static string EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            // Forward-compat: warn once per dialogue run when a script wires an
            // unrecognized node type into a string input.
            if (ctx.MaybeWarnUnknownNode(node))
                return "";

            switch (node.Type)
            {
                case StoryFlowNodeType.GetString:
                case StoryFlowNodeType.SetString:
                {
                    var variableId = node.GetData("variable");
                    var variable = ctx.ReadVariable(variableId, StoryFlowVariableType.String, false);
                    string val = variable?.Value?.GetString() ?? "";
                    if (ctx.TraceEnabled && variable != null)
                    {
                        bool isGlobal = !ctx.LocalVariables.ContainsKey(variable.Id);
                        Debug.Log($"[SF-TRACE] VAR GET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={val}");
                    }
                    return val;
                }

                case StoryFlowNodeType.ConcatenateString:
                {
                    string a = EvaluatorHelpers.EvaluateStringInput1(ctx, node);
                    string b = EvaluatorHelpers.EvaluateStringInput2(ctx, node);
                    return a + b;
                }

                case StoryFlowNodeType.ToUpperCase:
                {
                    string str = Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return str?.ToUpper() ?? "";
                }

                case StoryFlowNodeType.ToLowerCase:
                {
                    string str = Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return str?.ToLower() ?? "";
                }

                case StoryFlowNodeType.IntToString:
                {
                    int intValue = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    return intValue.ToString();
                }

                case StoryFlowNodeType.FloatToString:
                {
                    float floatValue = FloatEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Float);
                    return floatValue.ToString(CultureInfo.InvariantCulture);
                }

                // GetStringArrayElement. The export dialect stores the inline index in
                // the "value" field (see the IntegerEvaluator's GetIntArrayElement note).
                case StoryFlowNodeType.GetStringArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    int idx = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("value"));
                    if (arr != null && idx >= 0 && idx < arr.Count)
                    {
                        // Cache the key and provenance, not this language's resolved text.
                        ctx.GetNodeRuntimeState(node.Id).CachedOutput = new StoryFlowVariant(arr[idx]);
                        return ctx.ResolveArrayString(arr[idx]);
                    }
                    return "";
                }

                case StoryFlowNodeType.GetRandomStringArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    if (arr == null || arr.Count == 0) return "";
                    int idx = ctx.RandomRange(0, arr.Count);
                    ctx.GetNodeRuntimeState(node.Id).CachedOutput = new StoryFlowVariant(arr[idx]);
                    return ctx.ResolveArrayString(arr[idx]);
                }

                case StoryFlowNodeType.ForEachStringLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return ctx.ResolveArrayString(runtimeState.LoopArray[runtimeState.LoopIndex]);
                    }
                    return "";
                }

                // Image/Audio/Character variable nodes return string paths
                case StoryFlowNodeType.GetImage:
                case StoryFlowNodeType.SetImage:
                case StoryFlowNodeType.GetAudio:
                case StoryFlowNodeType.SetAudio:
                case StoryFlowNodeType.GetCharacter:
                case StoryFlowNodeType.SetCharacter:
                {
                    var variableId = node.GetData("variable");
                    var expected = node.Type == StoryFlowNodeType.GetImage || node.Type == StoryFlowNodeType.SetImage
                        ? StoryFlowVariableType.Image
                        : node.Type == StoryFlowNodeType.GetAudio || node.Type == StoryFlowNodeType.SetAudio
                            ? StoryFlowVariableType.Audio : StoryFlowVariableType.Character;
                    var variable = ctx.ReadVariable(variableId, expected, false);
                    return variable?.Value?.GetString() ?? "";
                }

                // As an image source the node exposes the image it sets:
                // connected image input first, then the dropdown value
                case StoryFlowNodeType.SetBackgroundImage:
                {
                    return StoryFlowEvaluator.EvaluateStringWithDefault(
                        ctx, node.Id, StoryFlowHandles.In_ImageInput, node.GetData("value"));
                }

                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    var charVar = ctx.ReadValue(EvaluatorHelpers.EvaluateCharacterVariable(ctx, node), StoryFlowVariableType.String);
                    return charVar?.GetString() ?? "";
                }

                // Get/SetDataAssetVariable returning a string-family value. GetString covers
                // String / Image / Audio / Character (StoryFlowVariant:111-119), which is the
                // same four the .sfd seed stores in StringValue; enum reads through the enum
                // evaluator instead, because it stores in EnumValue.
                case StoryFlowNodeType.GetDataAssetVariable:
                case StoryFlowNodeType.SetDataAssetVariable:
                {
                    var dataAssetVar = ctx.ReadValue(EvaluatorHelpers.EvaluateDataAssetVariable(ctx, node), StoryFlowVariableType.String);
                    return dataAssetVar?.GetString() ?? "";
                }

                // Image/Audio/Character array elements return string paths
                case StoryFlowNodeType.GetImageArrayElement:
                case StoryFlowNodeType.GetAudioArrayElement:
                case StoryFlowNodeType.GetCharacterArrayElement:
                {
                    string arraySuffix = EvaluatorHelpers.GetArrayHandleSuffix(node.Type);
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, arraySuffix);
                    int idx = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("value"));
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetString();
                    return "";
                }

                case StoryFlowNodeType.GetRandomImageArrayElement:
                case StoryFlowNodeType.GetRandomAudioArrayElement:
                case StoryFlowNodeType.GetRandomCharacterArrayElement:
                {
                    string arraySuffix = EvaluatorHelpers.GetArrayHandleSuffix(node.Type);
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, arraySuffix);
                    if (arr == null || arr.Count == 0) return "";
                    int idx = ctx.RandomRange(0, arr.Count);
                    return arr[idx].GetString();
                }

                case StoryFlowNodeType.ForEachImageLoop:
                case StoryFlowNodeType.ForEachAudioLoop:
                case StoryFlowNodeType.ForEachCharacterLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return runtimeState.LoopArray[runtimeState.LoopIndex].GetString();
                    }
                    return "";
                }

                // Map op branches on the node's valueType data (K/V in node data — see the
                // BooleanEvaluator map arms for the pattern note). All string-family value
                // types funnel through here; enum values live in EnumValue, hence the
                // type-gated read via MapEvaluator.AsText.
                case StoryFlowNodeType.GetMapValue:
                {
                    var mapValueType = node.GetData("valueType");
                    if (mapValueType == "string" || mapValueType == "enum" || mapValueType == "image" ||
                        mapValueType == "character" || mapValueType == "audio")
                    {
                        MapEvaluator.ComputeGetMapValue(ctx, node, out var mapValue);
                        return ResolveMapValueText(ctx, mapValue);
                    }
                    return "";
                }

                // forEachMap Key/Value (string-family) — discriminate by SourceHandle suffix;
                // see the BooleanEvaluator's ForEachMap arm for the full pattern note. Keys
                // cover string/enum; values cover the full string family.
                case StoryFlowNodeType.ForEachMap:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    string sourceHandle = ctx.LastSourceHandle ?? "";
                    var mapKeyType = node.GetData("keyType");
                    var mapValueType = node.GetData("valueType");
                    if (sourceHandle.EndsWith("-key") && runtimeState.LoopKey != null &&
                        (mapKeyType == "string" || mapKeyType == "enum"))
                    {
                        return MapEvaluator.AsText(runtimeState.LoopKey);
                    }
                    if (sourceHandle.EndsWith("-value") && runtimeState.LoopValue != null &&
                        (mapValueType == "string" || mapValueType == "enum" || mapValueType == "image" ||
                         mapValueType == "character" || mapValueType == "audio"))
                    {
                        return ResolveMapValueText(ctx, runtimeState.LoopValue);
                    }
                    return "";
                }

                case StoryFlowNodeType.Dialogue:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.OutputValues != null)
                    {
                        foreach (var kvp in runtimeState.OutputValues)
                        {
                            return kvp.Value?.GetString() ?? "";
                        }
                    }
                    return "";
                }

                case StoryFlowNodeType.RunScript:
                {
                    var outputValue = ctx.ReadValue(EvaluatorHelpers.ResolveRunScriptOutput(ctx, node), StoryFlowVariableType.String);
                    return outputValue?.GetString() ?? "";
                }

                case StoryFlowNodeType.EnumToString:
                {
                    string enumVal = EnumEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Enum);
                    return enumVal ?? "";
                }

                default:
                    ctx.FailResolution();
                    return "";
            }
        }

        private static string ResolveMapValueText(StoryFlowExecutionContext ctx, StoryFlowVariant value)
        {
            if (value == null) return "";
            var text = MapEvaluator.AsText(value);
            return value.IsLiteralString ? text : ctx.ResolveStringKey(text);
        }
    }
}
