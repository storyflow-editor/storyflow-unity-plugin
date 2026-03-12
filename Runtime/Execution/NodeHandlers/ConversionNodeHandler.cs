namespace StoryFlow.Execution.NodeHandlers
{
    /// <summary>
    /// All conversion nodes (IntToBoolean, FloatToBoolean, IntToString, FloatToString,
    /// StringToInt, StringToFloat, IntToFloat, FloatToInt, IntToEnum) are pure evaluations
    /// with no side effects. They are evaluated lazily by StoryFlowEvaluator and are
    /// registered as no-ops in StoryFlowNodeDispatcher.
    ///
    /// This file exists as a placeholder to document the design decision.
    /// </summary>
    public static class ConversionNodeHandler
    {
        // All conversions are handled by StoryFlowEvaluator on demand.
        // No active processing is needed when the execution flow reaches these nodes.
    }
}
