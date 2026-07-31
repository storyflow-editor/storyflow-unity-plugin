using System.Collections.Generic;

namespace StoryFlow.Editor
{
    /// <summary>
    /// Per-file outcome of one import run.
    ///
    /// The importer never aborts on a single unwritable file: every media copy and every
    /// asset save is attempted and its outcome lands here, so a caller can report what
    /// actually reached disk instead of claiming success after a partial write. Failures
    /// carry a message that names the file and the most likely cause, because Unity's own
    /// error for these is "Access is denied", which tells nobody what to do next.
    /// </summary>
    public class StoryFlowImportReport
    {
        /// <summary>Media files copied into the project this run.</summary>
        public int MediaWritten;

        /// <summary>Media files whose destination already held identical bytes.</summary>
        public int MediaUpToDate;

        /// <summary>Media files that could not be written.</summary>
        public int MediaFailed;

        /// <summary>Assets serialized to disk this run.</summary>
        public int AssetsWritten;

        /// <summary>Assets whose serialized data had not changed since the last import.</summary>
        public int AssetsUpToDate;

        /// <summary>Assets that could not be written.</summary>
        public int AssetsFailed;

        /// <summary>One actionable line per failed file, in the order they failed.</summary>
        public readonly List<string> Failures = new List<string>();

        public int WrittenCount => MediaWritten + AssetsWritten;
        public int UpToDateCount => MediaUpToDate + AssetsUpToDate;
        public int SucceededCount => WrittenCount + UpToDateCount;
        public int FailedCount => MediaFailed + AssetsFailed;
        public bool HasFailures => FailedCount > 0;

        public void RecordMediaFailure(string path, string reason)
        {
            MediaFailed++;
            Failures.Add($"{path}: {reason}");
        }

        public void RecordAssetFailure(string path, string reason)
        {
            AssetsFailed++;
            Failures.Add($"{path}: {reason}");
        }

        /// <summary>
        /// "12 written, 40 up to date, 2 failed" — the line every caller appends to its own
        /// message so no log claims an unqualified success while files are still stale.
        /// </summary>
        public string Summarize()
        {
            return $"{WrittenCount} written, {UpToDateCount} up to date, {FailedCount} failed";
        }
    }
}
