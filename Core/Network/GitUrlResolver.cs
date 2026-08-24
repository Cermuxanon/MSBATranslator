namespace MSBATranslator.Core.Network
{
    public static class GitUrlResolver
    {
        public static string ToRawDownloadUrl(string webOrRawUrl)
        {
            if (string.IsNullOrWhiteSpace(webOrRawUrl)) return string.Empty;
            string url = webOrRawUrl.Trim();

            if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            {
                if (url.Contains("/blob/", StringComparison.OrdinalIgnoreCase))
                    url = url.Replace("github.com", "raw.githubusercontent.com").Replace("/blob/", "/");
                else if (url.Contains("/tree/", StringComparison.OrdinalIgnoreCase))
                    url = url.Replace("github.com", "raw.githubusercontent.com").Replace("/tree/", "/");
            }

            else if (url.Contains("gitlab", StringComparison.OrdinalIgnoreCase) && url.Contains("/-/blob/", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Replace("/-/blob/", "/-/raw/");
            }

            else if (url.Contains("/src/branch/", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Replace("/src/branch/", "/raw/branch/");
            }
            else if (url.Contains("/src/tag/", StringComparison.OrdinalIgnoreCase))
            {
                url = url.Replace("/src/tag/", "/raw/tag/");
            }

            return url;
        }
    }
}