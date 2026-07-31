using System;
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
    ///
    /// Counts are per FILE, not per attempt. One media file is routinely referenced by
    /// several scripts and by the project pool, so a locked texture reaches the copy path
    /// once per reference; counting those separately would tell the user that three files
    /// could not be written and send them looking for two that do not exist.
    /// </summary>
    public class StoryFlowImportReport
    {
        public enum FailureKind
        {
            Media,
            Asset
        }

        /// <summary>
        /// One failed file. The path is kept as data rather than baked into a sentence so
        /// callers can act on it — retry it, check it out, point the user's editor at it —
        /// while <see cref="ToString"/> still yields the one-line form logs want.
        /// </summary>
        public class Failure
        {
            public readonly string Path;
            public readonly string Reason;
            public readonly FailureKind Kind;

            public Failure(string path, string reason, FailureKind kind)
            {
                Path = path;
                Reason = reason;
                Kind = kind;
            }

            public override string ToString()
            {
                return $"{Path}: {Reason}";
            }
        }

        // Destinations already counted this run. Ordinal, not OrdinalIgnoreCase: every media
        // path here is built by the same helper from the same exported relative path, so
        // equal files produce equal strings, and two paths differing only in case are two
        // different files on a case-sensitive filesystem.
        private readonly HashSet<string> _countedMediaPaths = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Media files copied into the project this run.</summary>
        public int MediaWritten { get; private set; }

        /// <summary>Media files whose destination already held identical bytes.</summary>
        public int MediaUpToDate { get; private set; }

        /// <summary>Media files that could not be written.</summary>
        public int MediaFailed { get; private set; }

        /// <summary>Assets serialized to disk this run.</summary>
        public int AssetsWritten { get; private set; }

        /// <summary>Assets whose serialized data had not changed since the last import.</summary>
        public int AssetsUpToDate { get; private set; }

        /// <summary>Assets that could not be written.</summary>
        public int AssetsFailed { get; private set; }

        /// <summary>One actionable entry per failed file, in the order they failed.</summary>
        public readonly List<Failure> Failures = new List<Failure>();

        public int WrittenCount => MediaWritten + AssetsWritten;
        public int UpToDateCount => MediaUpToDate + AssetsUpToDate;
        public int FailedCount => MediaFailed + AssetsFailed;
        public bool HasFailures => FailedCount > 0;

        public void RecordMediaWritten(string destinationPath)
        {
            if (ClaimMediaPath(destinationPath)) MediaWritten++;
        }

        public void RecordMediaUpToDate(string destinationPath)
        {
            if (ClaimMediaPath(destinationPath)) MediaUpToDate++;
        }

        public void RecordMediaFailure(string destinationPath, string reason)
        {
            if (!ClaimMediaPath(destinationPath)) return;

            MediaFailed++;
            Failures.Add(new Failure(destinationPath, reason, FailureKind.Media));
        }

        public void RecordAssetWritten()
        {
            AssetsWritten++;
        }

        public void RecordAssetUpToDate()
        {
            AssetsUpToDate++;
        }

        public void RecordAssetFailure(string assetPath, string reason)
        {
            AssetsFailed++;
            Failures.Add(new Failure(assetPath, reason, FailureKind.Asset));
        }

        /// <summary>
        /// True the first time a media destination is seen this run. The first outcome for a
        /// path wins: a later reference to the same file has, by definition, already been
        /// settled, and re-counting it would inflate every number in the summary.
        /// Assets need no equivalent — each one is saved exactly once per import.
        /// </summary>
        private bool ClaimMediaPath(string destinationPath)
        {
            return _countedMediaPaths.Add(destinationPath ?? string.Empty);
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
