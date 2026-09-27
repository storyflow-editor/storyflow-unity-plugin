using StoryFlow.Data;
namespace StoryFlow.Execution
{
    // Data IDs are typed references, never localization keys or presentation strings.
    internal static class DataReferenceEvaluator
    {
        internal static bool IsArrayVariableNode(StoryFlowNode node) => node != null &&
            (node.Type == StoryFlowNodeType.GetDataAssetArray || node.Type == StoryFlowNodeType.SetDataAssetArray);

        // Exported variable IDs can coincide across scopes. The authored scope is exact:
        // a missing local reference must never fall through to a global with the same ID.
        internal static StoryFlowVariable ReadVariable(StoryFlowExecutionContext ctx, StoryFlowNode node, bool array)
        {
            var variables = node.GetDataBool("isGlobal") ? ctx.GlobalVariables : ctx.LocalVariables;
            if (!variables.TryGetValue(node.GetData("variable"), out var variable) ||
                variable.Type != StoryFlowVariableType.DataAsset || variable.IsArray != array)
            {
                ctx.FailResolution();
                return null;
            }
            return variable;
        }

        internal static string Evaluate(StoryFlowExecutionContext ctx, string nodeId, string suffix, string fallback = "")
        {
            var edge = ctx?.CurrentScript?.FindInputEdge(nodeId, suffix);
            if (edge == null) return fallback ?? "";
            var previous = ctx.LastSourceHandle;
            ctx.LastSourceHandle = edge.SourceHandle;
            try { return EvaluateFromNode(ctx, ctx.ResolveInputNode(edge)); }
            finally { ctx.LastSourceHandle = previous; }
        }

        internal static string EvaluateFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            if (ctx == null || node == null) return "";
            if (++ctx.EvaluationDepth > StoryFlowExecutionContext.MaxEvaluationDepth)
            { ctx.EvaluationDepth--; ctx.FailResolution(); return ""; }
            try
            {
                StoryFlowVariant value = null;
                switch (node.Type)
                {
                    case StoryFlowNodeType.GetDataAsset: return node.GetData("assetId");
                    case StoryFlowNodeType.GetDataAssetRef:
                    case StoryFlowNodeType.SetDataAssetRef:
                        value = ReadVariable(ctx, node, false)?.Value; break;
                    case StoryFlowNodeType.GetCharacterVar:
                    case StoryFlowNodeType.SetCharacterVar:
                        value = EvaluatorHelpers.EvaluateCharacterVariable(ctx, node); break;
                    case StoryFlowNodeType.GetDataAssetVariable:
                    case StoryFlowNodeType.SetDataAssetVariable:
                        value = EvaluatorHelpers.EvaluateDataAssetVariable(ctx, node); break;
                    case StoryFlowNodeType.GetDataAssetArrayElement:
                    case StoryFlowNodeType.GetRandomDataAssetArrayElement:
                        var array = ArrayEvaluator.EvaluateTypedArray(ctx, node.Id, "dataAsset-array", StoryFlowVariableType.DataAsset);
                        int index = node.Type == StoryFlowNodeType.GetRandomDataAssetArrayElement
                            ? UnityEngine.Random.Range(0, array.Count)
                            : StoryFlowEvaluator.EvaluateIntegerWithDefault(ctx, node.Id, "integer", node.GetDataInt("value"));
                        if (index >= 0 && index < array.Count) value = array[index];
                        break;
                    case StoryFlowNodeType.ForEachDataAssetLoop:
                        var loop = ctx.GetNodeRuntimeState(node.Id);
                        if (loop.LoopArray != null && loop.LoopIndex >= 0 && loop.LoopIndex < loop.LoopArray.Count) value = loop.LoopArray[loop.LoopIndex];
                        break;
                    case StoryFlowNodeType.GetMapValue:
                        if (node.GetData("valueType") == "dataAsset") MapEvaluator.ComputeGetMapValue(ctx, node, out value);
                        break;
                    case StoryFlowNodeType.ForEachMap:
                        if ((ctx.LastSourceHandle ?? "").EndsWith("-value") && node.GetData("valueType") == "dataAsset") value = ctx.GetNodeRuntimeState(node.Id).LoopValue;
                        break;
                    case StoryFlowNodeType.RunScript:
                        value = EvaluatorHelpers.ResolveRunScriptOutput(ctx, node); break;
                }
                return ctx.ReadValue(value, StoryFlowVariableType.DataAsset)?.GetString() ?? "";
            }
            finally { ctx.EvaluationDepth--; }
        }
    }
}
