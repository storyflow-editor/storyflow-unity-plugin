using System.Globalization;
using StoryFlow.Data;
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

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
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
                Debug.LogWarning("[StoryFlow] String evaluation depth exceeded. Possible circular reference.");
                return "";
            }

            try
            {
                // ForEach nodes and map reads — skip evaluation cache (cross-type conflicts /
                // live map storage; see the matching block in BooleanEvaluator for the rationale)
                bool skipCache = EvaluatorHelpers.IsForEachNode(node.Type) ||
                                 EvaluatorHelpers.IsMapReadNode(node.Type);
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (!skipCache && state.CachedOutput != null)
                    return ctx.ResolveStringKey(state.CachedOutput.GetString());

                string result = EvaluateFromNodeInternal(ctx, node);
                if (!skipCache)
                    state.CachedOutput = StoryFlowVariant.String(result);

                if (ctx.TraceEnabled)
                {
                    var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                    Debug.Log($"[SF-TRACE] EVAL {node.Id} {typeName} result={result}");
                }

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
                    var variable = ctx.FindVariable(variableId);
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

                // GetStringArrayElement
                case StoryFlowNodeType.GetStringArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    int idx = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("index"));
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetString();
                    return "";
                }

                case StoryFlowNodeType.GetRandomStringArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    if (arr == null || arr.Count == 0) return "";
                    int idx = Random.Range(0, arr.Count);
                    return arr[idx].GetString();
                }

                case StoryFlowNodeType.ForEachStringLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return runtimeState.LoopArray[runtimeState.LoopIndex].GetString();
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
                    var variable = ctx.FindVariable(variableId);
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
                    var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node);
                    return charVar?.GetString() ?? "";
                }

                // Image/Audio/Character array elements return string paths
                case StoryFlowNodeType.GetImageArrayElement:
                case StoryFlowNodeType.GetAudioArrayElement:
                case StoryFlowNodeType.GetCharacterArrayElement:
                {
                    string arraySuffix = EvaluatorHelpers.GetArrayHandleSuffix(node.Type);
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, arraySuffix);
                    int idx = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("index"));
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
                    int idx = Random.Range(0, arr.Count);
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
                        return mapValue != null ? MapEvaluator.AsText(mapValue) : "";
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
                        return MapEvaluator.AsText(runtimeState.LoopValue);
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
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.GetString() ?? "";
                }

                case StoryFlowNodeType.EnumToString:
                {
                    string enumVal = EnumEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Enum);
                    return enumVal ?? "";
                }

                default:
                    return "";
            }
        }
    }
}
