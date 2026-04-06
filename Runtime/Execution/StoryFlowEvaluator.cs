using System.Collections.Generic;
using StoryFlow.Data;

namespace StoryFlow.Execution
{
    /// <summary>
    /// Public facade that delegates to domain-specific evaluator classes.
    /// Recursively walks input edges backwards to compute typed values
    /// from expression node chains. Results are cached in NodeRuntimeState to avoid duplicate
    /// evaluation within a single processing pass.
    /// </summary>
    public static class StoryFlowEvaluator
    {
        // =====================================================================
        // Boolean Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates the boolean value arriving at a specific input handle of a node.
        /// Follows the input edge backwards to find the source node and evaluates it.
        /// </summary>
        public static bool EvaluateBoolean(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return BooleanEvaluator.Evaluate(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates a node as a boolean value based on its type.
        /// </summary>
        public static bool EvaluateBooleanFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            return BooleanEvaluator.EvaluateFromNode(ctx, node);
        }

        // =====================================================================
        // Integer Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates the integer value arriving at a specific input handle of a node.
        /// </summary>
        public static int EvaluateInteger(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return IntegerEvaluator.Evaluate(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates a node as an integer value based on its type.
        /// </summary>
        public static int EvaluateIntegerFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            return IntegerEvaluator.EvaluateFromNode(ctx, node);
        }

        // =====================================================================
        // Float Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates the float value arriving at a specific input handle of a node.
        /// </summary>
        public static float EvaluateFloat(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return FloatEvaluator.Evaluate(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates a node as a float value based on its type.
        /// </summary>
        public static float EvaluateFloatFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            return FloatEvaluator.EvaluateFromNode(ctx, node);
        }

        // =====================================================================
        // String Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates the string value arriving at a specific input handle of a node.
        /// </summary>
        public static string EvaluateString(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return StringEvaluator.Evaluate(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates a node as a string value based on its type.
        /// </summary>
        public static string EvaluateStringFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            return StringEvaluator.EvaluateFromNode(ctx, node);
        }

        // =====================================================================
        // Enum Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates the enum value arriving at a specific input handle of a node.
        /// </summary>
        public static string EvaluateEnum(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return EnumEvaluator.Evaluate(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates a node as an enum value based on its type.
        /// </summary>
        public static string EvaluateEnumFromNode(StoryFlowExecutionContext ctx, StoryFlowNode node)
        {
            return EnumEvaluator.EvaluateFromNode(ctx, node);
        }

        // =====================================================================
        // Array Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates a boolean array from an input edge.
        /// </summary>
        public static List<StoryFlowVariant> EvaluateBoolArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return ArrayEvaluator.EvaluateBoolArray(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates an integer array from an input edge.
        /// </summary>
        public static List<StoryFlowVariant> EvaluateIntArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return ArrayEvaluator.EvaluateIntArray(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates a float array from an input edge.
        /// </summary>
        public static List<StoryFlowVariant> EvaluateFloatArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return ArrayEvaluator.EvaluateFloatArray(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates a string array from an input edge.
        /// </summary>
        public static List<StoryFlowVariant> EvaluateStringArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return ArrayEvaluator.EvaluateStringArray(ctx, nodeId, targetHandleSuffix);
        }

        /// <summary>
        /// Evaluates an untyped array from an input edge. Returns the raw list.
        /// </summary>
        public static List<StoryFlowVariant> EvaluateArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix)
        {
            return ArrayEvaluator.EvaluateArray(ctx, nodeId, targetHandleSuffix);
        }

        public static List<StoryFlowVariant> EvaluateArray(
            StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, StoryFlowVariableType expectedType)
        {
            return ArrayEvaluator.EvaluateTypedArray(ctx, nodeId, targetHandleSuffix, expectedType);
        }

        /// <summary>
        /// Evaluates an array-producing node. Looks up the array variable by variableId.
        /// </summary>
        public static List<StoryFlowVariant> EvaluateArrayFromNode(
            StoryFlowExecutionContext ctx, StoryFlowNode node, StoryFlowVariableType expectedType)
        {
            return ArrayEvaluator.EvaluateArrayFromNode(ctx, node, expectedType);
        }

        // =====================================================================
        // Boolean Chain Pre-Processing
        // =====================================================================

        /// <summary>
        /// Pre-caches boolean evaluation results for all nodes feeding into a branch.
        /// Walks the expression graph from comparison/logic nodes to ensure their
        /// outputValue fields are populated before the branch reads them.
        /// </summary>
        public static void ProcessBooleanChain(StoryFlowExecutionContext ctx, string nodeId)
        {
            BooleanEvaluator.ProcessBooleanChain(ctx, nodeId);
        }

        // =====================================================================
        // Option Visibility Evaluation
        // =====================================================================

        /// <summary>
        /// Evaluates the visibility of a dialogue option. Returns true if visible.
        /// Looks for an input edge with suffix "boolean-{optionId}".
        /// If no visibility edge exists, the option is visible by default.
        /// </summary>
        public static bool EvaluateOptionVisibility(StoryFlowExecutionContext ctx, string nodeId, string optionId)
        {
            return BooleanEvaluator.EvaluateOptionVisibility(ctx, nodeId, optionId);
        }

        /// <summary>
        /// Overload accepting a StoryFlowNode directly (used by DialogueNodeHandler).
        /// </summary>
        public static bool EvaluateOptionVisibility(StoryFlowExecutionContext ctx, StoryFlowNode node, string optionId)
        {
            return BooleanEvaluator.EvaluateOptionVisibility(ctx, node.Id, optionId);
        }

        // =====================================================================
        // WithDefault Overloads
        // =====================================================================

        /// <summary>
        /// Evaluate boolean with fallback to a default value when no input edge is connected.
        /// </summary>
        public static bool EvaluateBooleanWithDefault(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, bool defaultValue)
        {
            var edge = ctx?.CurrentScript?.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return defaultValue;
            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return defaultValue;
            return BooleanEvaluator.EvaluateFromNode(ctx, sourceNode);
        }

        /// <summary>
        /// Evaluate integer with fallback to a default value when no input edge is connected.
        /// </summary>
        public static int EvaluateIntegerWithDefault(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, int defaultValue)
        {
            var edge = ctx?.CurrentScript?.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return defaultValue;
            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return defaultValue;
            return IntegerEvaluator.EvaluateFromNode(ctx, sourceNode);
        }

        /// <summary>
        /// Evaluate float with fallback to a default value when no input edge is connected.
        /// </summary>
        public static float EvaluateFloatWithDefault(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, float defaultValue)
        {
            var edge = ctx?.CurrentScript?.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return defaultValue;
            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return defaultValue;
            return FloatEvaluator.EvaluateFromNode(ctx, sourceNode);
        }

        /// <summary>
        /// Evaluate string with fallback to a default value when no input edge is connected.
        /// </summary>
        public static string EvaluateStringWithDefault(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, string defaultValue)
        {
            var edge = ctx?.CurrentScript?.FindInputEdge(nodeId, targetHandleSuffix);
            if (edge == null) return ctx != null ? ctx.ResolveStringKey(defaultValue) : defaultValue;
            var sourceNode = ctx.CurrentScript.GetNode(edge.Source);
            if (sourceNode == null) return ctx.ResolveStringKey(defaultValue);
            return StringEvaluator.EvaluateFromNode(ctx, sourceNode);
        }

        // =====================================================================
        // EvaluateTyped — used by ControlFlowNodeHandler for RunScript params
        // =====================================================================

        /// <summary>
        /// Evaluates a typed value from an input edge based on the variable type enum.
        /// </summary>
        public static StoryFlowVariant EvaluateTyped(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, StoryFlowVariableType type)
        {
            switch (type)
            {
                case StoryFlowVariableType.Boolean:
                    return StoryFlowVariant.Bool(EvaluateBoolean(ctx, nodeId, targetHandleSuffix));
                case StoryFlowVariableType.Integer:
                    return StoryFlowVariant.Int(EvaluateInteger(ctx, nodeId, targetHandleSuffix));
                case StoryFlowVariableType.Float:
                    return StoryFlowVariant.Float(EvaluateFloat(ctx, nodeId, targetHandleSuffix));
                case StoryFlowVariableType.String:
                    return StoryFlowVariant.String(EvaluateString(ctx, nodeId, targetHandleSuffix));
                case StoryFlowVariableType.Enum:
                    return StoryFlowVariant.Enum(EvaluateEnum(ctx, nodeId, targetHandleSuffix));
                case StoryFlowVariableType.Image:
                case StoryFlowVariableType.Audio:
                case StoryFlowVariableType.Character:
                    // Image, audio, and character types are stored as string paths/keys
                    return StoryFlowVariant.String(EvaluateString(ctx, nodeId, targetHandleSuffix));
                default:
                    return new StoryFlowVariant();
            }
        }

        /// <summary>
        /// Evaluates a typed value from an input edge based on a type name string
        /// (e.g. "boolean", "integer", "float", "string", "enum", "image", "audio", "character").
        /// </summary>
        public static StoryFlowVariant EvaluateTyped(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, string typeName)
        {
            switch (typeName)
            {
                case "boolean":
                    return StoryFlowVariant.Bool(EvaluateBoolean(ctx, nodeId, targetHandleSuffix));
                case "integer":
                    return StoryFlowVariant.Int(EvaluateInteger(ctx, nodeId, targetHandleSuffix));
                case "float":
                    return StoryFlowVariant.Float(EvaluateFloat(ctx, nodeId, targetHandleSuffix));
                case "string":
                    return StoryFlowVariant.String(EvaluateString(ctx, nodeId, targetHandleSuffix));
                case "enum":
                    return StoryFlowVariant.Enum(EvaluateEnum(ctx, nodeId, targetHandleSuffix));
                case "image":
                case "audio":
                case "character":
                    // Image, audio, and character types are stored as string paths/keys
                    return StoryFlowVariant.String(EvaluateString(ctx, nodeId, targetHandleSuffix));
                default:
                    return new StoryFlowVariant();
            }
        }

        /// <summary>
        /// Evaluates a typed array value from an input edge based on a type name string.
        /// Returns a StoryFlowVariant with ArrayValue populated, or null if no input edge.
        /// </summary>
        public static StoryFlowVariant EvaluateTypedArray(StoryFlowExecutionContext ctx, string nodeId, string targetHandleSuffix, string typeName)
        {
            StoryFlowVariableType elementType;
            switch (typeName)
            {
                case "boolean": elementType = StoryFlowVariableType.Boolean; break;
                case "integer": elementType = StoryFlowVariableType.Integer; break;
                case "float":   elementType = StoryFlowVariableType.Float;   break;
                case "string":  elementType = StoryFlowVariableType.String;  break;
                case "enum":    elementType = StoryFlowVariableType.Enum;    break;
                case "image":   elementType = StoryFlowVariableType.Image;   break;
                case "audio":   elementType = StoryFlowVariableType.Audio;   break;
                case "character": elementType = StoryFlowVariableType.Character; break;
                default:        elementType = StoryFlowVariableType.String;  break;
            }

            var array = ArrayEvaluator.EvaluateTypedArray(ctx, nodeId, targetHandleSuffix, elementType);
            if (array == null || array.Count == 0)
            {
                // Check if there's actually an input edge — if not, return null to signal "no connection"
                var edge = ctx?.CurrentScript?.FindInputEdge(nodeId, targetHandleSuffix);
                if (edge == null) return null;
            }

            var variant = new StoryFlowVariant { Type = elementType, ArrayValue = array };
            return variant;
        }
    }
}
