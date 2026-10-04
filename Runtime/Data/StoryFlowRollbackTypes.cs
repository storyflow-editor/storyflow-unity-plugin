using System;

namespace StoryFlow.Data
{
    [Serializable]
    public struct StoryFlowRollbackAvailability
    {
        public bool CanGoBack;
        public int Steps;
        public string Reason;
        public StoryFlowRollbackAvailability(bool canGoBack, int steps, string reason)
        { CanGoBack = canGoBack; Steps = steps; Reason = reason; }
    }

    [Serializable]
    public struct StoryFlowRollbackResult
    {
        public bool Ok;
        public string Reason;
        public StoryFlowRollbackResult(bool ok, string reason = null) { Ok = ok; Reason = reason; }
    }
}
