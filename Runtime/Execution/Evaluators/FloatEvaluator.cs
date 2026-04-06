using System.Globalization;
using StoryFlow.Data;
using UnityEngine;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Evaluates float values from expression node chains.
    /// Handles arithmetic, conversions, array element access, and variable lookups.
    /// </summary>
    internal static class FloatEvaluator
    {
        /// <summary>
        /// Evaluates the float value arriving at a specific input handle of a node.
        /// </summary>
        internal static float Evaluate(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            if (ctx?.CurrentScript == null) return 0f;

            var edge = ctx.CurrentScript.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return 0f;

            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return 0f;

            var prevHandle = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            float result = EvaluateFromNode(ctx, sourceNode);
            ctx.LastSourceHandle = prevHandle;
            return result;
        }

        /// <summary>
        /// Evaluates a node as a float value based on its type.
        /// </summary>
        internal static float EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (node == null || ctx == null) return 0f;

            ctx.EvaluationDepth++;
            if (ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            {
                ctx.EvaluationDepth--;
                Debug.LogWarning("[StoryFlow] Float evaluation depth exceeded. Possible circular reference.");
                return 0f;
            }

            try
            {
                // ForEach nodes — skip evaluation cache to avoid cross-type conflicts
                bool isForEach = EvaluatorHelpers.IsForEachNode(node.Type);
                var state = ctx.GetNodeRuntimeState(node.Id);
                if (!isForEach && state.CachedOutput != null)
                    return state.CachedOutput.GetFloat();

                float result = EvaluateFromNodeInternal(ctx, node);
                if (!isForEach)
                    state.CachedOutput = StoryFlowVariant.Float(result);

                if (ctx.TraceEnabled)
                {
                    var typeName = !string.IsNullOrEmpty(node.RawType) ? node.RawType : node.Type.ToString();
                    Debug.Log($"[SF-TRACE] EVAL {node.Id} {typeName} result={result.ToString(CultureInfo.InvariantCulture)}");
                }

                return result;
            }
            finally
            {
                ctx.EvaluationDepth--;
            }
        }

        private static float EvaluateFromNodeInternal(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            switch (node.Type)
            {
                case StoryFlowNodeType.GetFloat:
                case StoryFlowNodeType.SetFloat:
                {
                    var variableId = node.GetData("variable");
                    var variable = ctx.FindVariable(variableId);
                    float val = variable?.Value?.GetFloat() ?? 0f;
                    if (ctx.TraceEnabled && variable != null)
                    {
                        bool isGlobal = !ctx.LocalVariables.ContainsKey(variable.Id);
                        Debug.Log($"[SF-TRACE] VAR GET \"{variable.Name}\" global={isGlobal.ToString().ToLower()} value={val.ToString(CultureInfo.InvariantCulture)}");
                    }
                    return val;
                }

                case StoryFlowNodeType.PlusFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return a + b;
                }

                case StoryFlowNodeType.MinusFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return a - b;
                }

                case StoryFlowNodeType.MultiplyFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return a * b;
                }

                case StoryFlowNodeType.DivideFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    return !Mathf.Approximately(b, 0f) ? a / b : 0f;
                }

                case StoryFlowNodeType.RandomFloat:
                {
                    float a = EvaluatorHelpers.EvaluateFloatInput1(ctx, node);
                    float b = EvaluatorHelpers.EvaluateFloatInput2(ctx, node);
                    float min = Mathf.Min(a, b);
                    float max = Mathf.Max(a, b);
                    // Unity Random.Range(float,float) is exclusive on max.
                    // Other runtimes (Editor/Godot/Unreal) are inclusive.
                    // Difference is one ULP — negligible in practice.
                    return Random.Range(min, max);
                }

                case StoryFlowNodeType.IntToFloat:
                {
                    int intValue = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    return (float)intValue;
                }

                case StoryFlowNodeType.StringToFloat:
                {
                    string str = StringEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_String);
                    return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out float val) ? val : 0f;
                }

                // GetFloatArrayElement
                case StoryFlowNodeType.GetFloatArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateFloatArray(ctx, node.Id, StoryFlowHandles.In_FloatArray);
                    int idx = IntegerEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Integer);
                    if (arr != null && idx >= 0 && idx < arr.Count)
                        return arr[idx].GetFloat();
                    return 0f;
                }

                case StoryFlowNodeType.GetRandomFloatArrayElement:
                {
                    var arr = ArrayEvaluator.EvaluateFloatArray(ctx, node.Id, StoryFlowHandles.In_FloatArray);
                    if (arr == null || arr.Count == 0) return 0f;
                    int idx = Random.Range(0, arr.Count);
                    return arr[idx].GetFloat();
                }

                case StoryFlowNodeType.ForEachFloatLoop:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.LoopArray != null && runtimeState.LoopIndex >= 0 &&
                        runtimeState.LoopIndex < runtimeState.LoopArray.Count)
                    {
                        return runtimeState.LoopArray[runtimeState.LoopIndex].GetFloat();
                    }
                    return 0f;
                }

                case StoryFlowNodeType.GetCharacterVar:
                case StoryFlowNodeType.SetCharacterVar:
                {
                    var charVar = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node);
                    return charVar?.GetFloat() ?? 0f;
                }

                case StoryFlowNodeType.Dialogue:
                {
                    var runtimeState = ctx.GetNodeRuntimeState(node.Id);
                    if (runtimeState.OutputValues != null)
                    {
                        foreach (var kvp in runtimeState.OutputValues)
                        {
                            return kvp.Value?.GetFloat() ?? 0f;
                        }
                    }
                    return 0f;
                }

                case StoryFlowNodeType.RunScript:
                {
                    var outputValue = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node);
                    return outputValue?.GetFloat() ?? 0f;
                }

                case StoryFlowNodeType.BooleanToFloat:
                {
                    bool boolVal = BooleanEvaluator.Evaluate(ctx, node.Id, StoryFlowHandles.In_Boolean);
                    return boolVal ? 1f : 0f;
                }

                default:
                    return 0f;
            }
        }
    }
}
