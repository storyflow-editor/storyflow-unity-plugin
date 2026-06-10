using System.Collections.Generic;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates integer values from expression node chains.
    /// Handles arithmetic, conversions, array operations, and variable lookups.
    /// </summary>
    internal static class IntegerEvaluator
    {
        /// <summary>
        /// Evaluates the integer value arriving at a specific input handle of a node.
        /// </summary>
        internal static int Evaluate(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            if (ctx?.CurrentScript == null) return 0;

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return 0;

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return 0;

            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            int result = EvaluateFromNode(ctx, sourceNode);
            ctx.LastSourceHandle = prevHandle;
            return result;
        }

        /// <summary>
        /// Evaluates a node as an integer value based on its type.
        /// </summary>
        internal static int EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null || ctx == null) return 0;

            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                Debug.LogWarning("[StoryFlow] Integer evaluation depth exceeded. Possible circular reference.");
                return 0;
            }

            try
            {
                // ForEach nodes and map reads — skip evaluation cache (cross-type conflicts /
                // live map storage; see the matching block in BooleanEvaluator for the rationale)
                bool skipCache = EvaluatorHelpers.IsForEachNode(node.Type) ||
                                 EvaluatorHelpers.IsMapReadNode(node.Type);
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (!skipCache && state.CachedOutput != null)
                    return state.CachedOutput.GetInt();

                int result = EvaluateFromNodeInternal(ctx, node);
                if (!skipCache)
                    state.CachedOutput = StoryFlowVariant.Int(result);

                if (ctx.TraceEnabled)
                {
                    var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                    Debug.Log($"[SF-TRACE] EVAL {node.Id} {typeName} result={result}");
                }

                return result;
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static int EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            // Forward-compat: warn once per dialogue run when a script wires an
            // unrecognized node type into an integer input. The silent default would
            // otherwise let SetInt and friends quietly store 0 from a newer node type.
            if (ctx.MaybeWarnUnknownNode(node))
                return 0;

            switch (node.Type)
            {
                case StoryFlowNodeType.GetInt:
                case StoryFlowNodeType.SetInt:
                {
                    var variableId = node.GetData("variable");
                    var variable = ctx.FindVariable(variableId);
                    int val = variable?.Value?.GetInt() ?? 0;
                    if (ctx.TraceEnabled && variable != null)
                    {
                        bool isGlobal = !ctx.LocalVariables.ContainsKey(variable.Id);
                        Debug.Log($"[SF-TRACE] VAR GET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={val}");
                    }
                    return val;
                }

                case StoryFlowNodeType.PlusInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a + b;
                }

                case StoryFlowNodeType.MinusInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a - b;
                }

                case StoryFlowNodeType.MultiplyInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return a * b;
                }

                case StoryFlowNodeType.DivideInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    return b != 0 ? a / b : 0;
                }

                case StoryFlowNodeType.RandomInt:
                {
                    int a = EvaluatorHelpers.EvaluateIntegerInput1(ctx, node);
                    int b = EvaluatorHelpers.EvaluateIntegerInput2(ctx, node);
                    int min = Mathf.Min(a, b);
                    int max = Mathf.Max(a, b);
                    return Random.Range(min, max + 1);
                }

                case StoryFlowNodeType.StringToInt:
                {
                    string str = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return int.TryParse(str, out int val) ? val : 0;
                }

                case StoryFlowNodeType.FloatToInt:
                {
                    float f = FloatEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Float);
                    return Mathf.FloorToInt(f);
                }

                // Array length nodes
                case StoryFlowNodeType.BoolArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.IntArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.FloatArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateFloatArray(ctx, node.Id, StoryFlowHandles.In_FloatArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.StringArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.ImageArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_ImageArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.CharacterArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_CharacterArray);
                    return arr?.Count ?? 0;
                }
                case StoryFlowNodeType.AudioArrayLength:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_AudioArray);
                    return arr?.Count ?? 0;
                }

                // FindInArray nodes return index
                case StoryFlowNodeType.FindInBoolArray:
                {
                    var arr = ArrayEvaluator.EvaluateBoolArray(ctx, node.Id, StoryFlowHandles.In_BoolArray);
                    bool val = StoryFlowEvaluator.EvaluateBooleanWithDefault(ctx, node.Id, StoryFlowHandles.In_Boolean, node.GetDataBool("value"));
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetBool() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInIntArray:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    int val = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("value"));
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetInt() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInFloatArray:
                {
                    var arr = ArrayEvaluator.EvaluateFloatArray(ctx, node.Id, StoryFlowHandles.In_FloatArray);
                    float val = StoryFlowEvaluator.EvaluateFloatWithDefault(ctx, node.Id, StoryFlowHandles.In_Float, node.GetDataFloat("value"));
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (Mathf.Approximately(arr[i].GetFloat(), val)) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInStringArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_StringArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_String, node.GetData("value"));
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInImageArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_ImageArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_Image, node.GetData("value"));
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInCharacterArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_CharacterArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_Character, node.GetData("value"));
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }
                case StoryFlowNodeType.FindInAudioArray:
                {
                    var arr = ArrayEvaluator.EvaluateStringArray(ctx, node.Id, StoryFlowHandles.In_AudioArray);
                    string val = StoryFlowEvaluator.EvaluateStringWithDefault(ctx, node.Id, StoryFlowHandles.In_Audio, node.GetData("value"));
                    if (arr == null) return -1;
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (arr[i].GetString() == val) return i;
                    }
                    return -1;
                }

                // GetIntArrayElement
                case StoryFlowNodeType.GetIntArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    int idx = StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, StoryFlowHandles.In_Integer, node.GetDataInt("index"));
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetInt();
                    return 0;
                }

                case StoryFlowNodeType.GetRandomIntArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateIntArray(ctx, node.Id, StoryFlowHandles.In_IntArray);
                    if (arr == null || arr.Count == 0) return 0;
                    int idx = Random.Range(0, arr.Count);
                    return arr[idx].GetInt();
                }

                // ForEach loop — returns current index or element depending on source handle
                case StoryFlowNodeType.ForEachBoolLoop:
                case StoryFlowNodeType.ForEachIntLoop:
                case StoryFlowNodeType.ForEachFloatLoop:
                case StoryFlowNodeType.ForEachStringLoop:
                case StoryFlowNodeType.ForEachImageLoop:
                case StoryFlowNodeType.ForEachCharacterLoop:
                case StoryFlowNodeType.ForEachAudioLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    string sourceHandle = ctx.LastSourceHandle ?? "";
                    if (sourceHandle.Contains(StoryFlowHandles.In_IntegerIndex))
                        return runtimeState.LoopIndex;
                    // ForEachIntLoop: return element value when not requesting index
                    if (node.Type == StoryFlowNodeType.ForEachIntLoop &&
                        runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return runtimeState.LoopArray[runtimeState.LoopIndex].GetInt();
                    }
                    return runtimeState.LoopIndex;
                }

                // Map ops branch on the node's keyType/valueType data (K/V in node data —
                // see the BooleanEvaluator map arms for the pattern note)
                case StoryFlowNodeType.MapSize:
                {
                    // Unresolved/missing-K-V map input falls through to 0 (HTML runtime parity)
                    var map = MapEvaluator.EvaluateMapInput(ctx, node, "1");
                    return map?.Count ?? 0;
                }

                case StoryFlowNodeType.GetMapValue:
                {
                    if (node.GetData("valueType") == "integer")
                    {
                        MapEvaluator.ComputeGetMapValue(ctx, node, out var mapValue);
                        return mapValue?.GetInt() ?? 0;
                    }
                    return 0;
                }

                // forEachMap Key/Value (integer) — discriminate by SourceHandle suffix
                // ("-key"/"-value"); reads come from the iteration snapshot, see the
                // BooleanEvaluator's ForEachMap arm for the full pattern note
                case StoryFlowNodeType.ForEachMap:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    string sourceHandle = ctx.LastSourceHandle ?? "";
                    if (sourceHandle.EndsWith("-key") && node.GetData("keyType") == "integer")
                        return runtimeState.LoopKey?.GetInt() ?? 0;
                    if (sourceHandle.EndsWith("-value") && node.GetData("valueType") == "integer")
                        return runtimeState.LoopValue?.GetInt() ?? 0;
                    return 0;
                }

                // GetCharacterVar / SetCharacterVar returning integer
                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node);
                    return charVar?.GetInt() ?? 0;
                }

                // Dialogue input option value
                case StoryFlowNodeType.Dialogue:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.OutputValues != null)
                    {
                        foreach (var kvp in runtimeState.OutputValues)
                        {
                            return kvp.Value?.GetInt() ?? 0;
                        }
                    }
                    return 0;
                }

                case StoryFlowNodeType.RunScript:
                {
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.GetInt() ?? 0;
                }

                case StoryFlowNodeType.BooleanToInt:
                {
                    bool boolVal = BooleanEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean);
                    return boolVal ? 1 : 0;
                }

                case StoryFlowNodeType.LengthString:
                {
                    string str = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return str?.Length ?? 0;
                }

                default:
                    return 0;
            }
        }
    }
}
