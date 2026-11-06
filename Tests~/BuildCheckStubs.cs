// The build callback interface StoryFlowBuildCheck implements, which the shared Unity stubs do not model.
namespace UnityEditor.Build
{
    public interface IOrderedCallback
    {
        int callbackOrder { get; }
    }

    public interface IPreprocessBuildWithReport : IOrderedCallback
    {
        void OnPreprocessBuild(Reporting.BuildReport report);
    }
}

namespace UnityEditor.Build.Reporting
{
    public class BuildReport
    {
    }
}
