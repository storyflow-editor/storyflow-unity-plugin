namespace StoryFlow.Utilities
{
    public static class StoryFlowPathNormalizer
    {
        /// <summary>
        /// Normalizes a character path for consistent storage and lookup.
        /// CRITICAL: Must be applied both when storing AND when looking up.
        /// </summary>
        public static string NormalizeCharacterPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.ToLowerInvariant().Replace("\\", "/");
        }

        /// <summary>
        /// Normalizes a script path (strips .json extension if present).
        /// </summary>
        public static string NormalizeScriptPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var result = path;
            if (result.EndsWith(".json"))
                result = result.Substring(0, result.Length - 5);
            return result;
        }

        /// <summary>
        /// Normalizes an asset path for consistent lookup.
        /// </summary>
        public static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            return path.Replace("\\", "/");
        }
    }
}
