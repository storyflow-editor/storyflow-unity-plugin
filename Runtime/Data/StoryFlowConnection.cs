using System;

namespace StoryFlow.Data
{
    [Serializable]
    public class StoryFlowConnection
    {
        public string Id;
        public string Source;
        public string Target;
        public string SourceHandle;
        public string TargetHandle;
    }
}
