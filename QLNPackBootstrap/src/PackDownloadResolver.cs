using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace QLNPackBootstrap
{
    /// <summary>
    /// Turns common share-page URLs into direct file URLs.
    ///
    /// Google Drive: share links become drive.usercontent.google.com download
    /// links; if Google returns its virus-scan/confirm HTML page, the caller can
    /// parse it with TryResolveGoogleDriveHtml and retry.
    ///
    /// Dropbox: dl=0 becomes dl=1.
    /// OneDrive: download=1 is added.
    /// Other HTTPS URLs are passed through unchanged.
    /// </summary>
    internal static class PackDownloadResolver
    {
        private static readonly Regex GoogleFilePathRegex = new Regex(
            @"/file/d/([A-Za-z0-9_-]+)",
            RegexOptions.Compiled);

        private static readonly Regex GoogleIdRegex = new Regex(
            @"[?&]id=([A-Za-z0-9_-]+)",
            RegexOptions.Compiled);

        private static readonly Regex InputTagRegex = new Regex(
            @"<input\b[^>]*>",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DirectDriveLinkRegex = new Regex(
            @"https?://drive\.usercontent\.google\.com/download\?[^""'\s<]+",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static string Normalize(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return url;
            }

            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri))
            {
                return url;
            }

            string host = uri.Host.ToLowerInvariant();

            if (host == "drive.google.com" || host == "docs.google.com")
            {
                return GoogleDriveDirect(url);
            }

            if (host == "drive.usercontent.google.com")
            {
                return EnsureGoogleConfirm(url);
            }

            if (host == "www.dropbox.com" || host == "dropbox.com" || host == "db.tt")
            {
                return SetQueryValue(url, "dl", "1");
            }

            if (host == "dl.dropboxusercontent.com" || host == "dl.dropbox.com")
            {
                return url;
            }

            if (host == "1drv.ms" ||
                host == "onedrive.live.com" ||
                host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase))
            {
                return SetQueryValue(url, "download", "1");
            }

            return url;
        }

        public static string GetGoogleDriveFileId(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return "";
            }

            Match match = GoogleFilePathRegex.Match(url);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            match = GoogleIdRegex.Match(url);
            return match.Success ? match.Groups[1].Value : "";
        }

        public static bool IsHtml(byte[] data)
        {
            if (data == null)
            {
                return false;
            }

            for (int index = 0; index < data.Length && index < 256; index++)
            {
                byte value = data[index];

                if (value == 0xEF || value == 0xBB || value == 0xBF)
                {
                    continue;
                }

                if (char.IsWhiteSpace((char)value))
                {
                    continue;
                }

                return value == (byte)'<';
            }

            return false;
        }

        public static bool IsHtmlFile(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[512];
                    int read = stream.Read(buffer, 0, buffer.Length);
                    if (read <= 0)
                    {
                        return false;
                    }

                    byte[] sample = new byte[read];
                    Array.Copy(buffer, sample, read);
                    return IsHtml(sample);
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses a Google Drive "virus scan warning" / confirm page. Returns the
        /// direct download URL when one can be built.
        /// </summary>
        public static bool TryResolveGoogleDriveHtml(string html, string currentUrl, out string nextUrl)
        {
            nextUrl = null;

            if (string.IsNullOrEmpty(html))
            {
                return false;
            }

            bool looksLikeDrive =
                html.IndexOf("drive.usercontent.google.com/download", StringComparison.OrdinalIgnoreCase) >= 0 ||
                html.IndexOf("download-form", StringComparison.OrdinalIgnoreCase) >= 0 ||
                html.IndexOf("Virus scan", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!looksLikeDrive)
            {
                return false;
            }

            string id = InputValue(html, "id");
            if (string.IsNullOrEmpty(id))
            {
                id = GetGoogleDriveFileId(currentUrl);
            }

            if (!string.IsNullOrEmpty(id))
            {
                string export = InputValue(html, "export");
                string confirm = InputValue(html, "confirm");
                string uuid = InputValue(html, "uuid");

                if (string.IsNullOrEmpty(export))
                {
                    export = "download";
                }

                if (string.IsNullOrEmpty(confirm))
                {
                    confirm = "t";
                }

                StringBuilder builder = new StringBuilder();
                builder.Append("https://drive.usercontent.google.com/download?id=")
                    .Append(Uri.EscapeDataString(id))
                    .Append("&export=").Append(Uri.EscapeDataString(export))
                    .Append("&confirm=").Append(Uri.EscapeDataString(confirm));

                if (!string.IsNullOrEmpty(uuid))
                {
                    builder.Append("&uuid=").Append(Uri.EscapeDataString(uuid));
                }

                string resourceKey = InputValue(html, "resourcekey");
                if (string.IsNullOrEmpty(resourceKey))
                {
                    resourceKey = QueryValue(currentUrl, "resourcekey");
                }

                if (!string.IsNullOrEmpty(resourceKey))
                {
                    builder.Append("&resourcekey=").Append(Uri.EscapeDataString(resourceKey));
                }

                nextUrl = builder.ToString();
                return true;
            }

            Match direct = DirectDriveLinkRegex.Match(html);
            if (direct.Success)
            {
                nextUrl = WebUtility.HtmlDecode(direct.Value);
                return true;
            }

            return false;
        }

        private static string GoogleDriveDirect(string url)
        {
            string id = GetGoogleDriveFileId(url);
            if (string.IsNullOrEmpty(id))
            {
                return url;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("https://drive.usercontent.google.com/download?id=")
                .Append(Uri.EscapeDataString(id))
                .Append("&export=download&confirm=t");

            string resourceKey = QueryValue(url, "resourcekey");
            if (!string.IsNullOrEmpty(resourceKey))
            {
                builder.Append("&resourcekey=").Append(Uri.EscapeDataString(resourceKey));
            }

            return builder.ToString();
        }

        private static string QueryValue(string url, string key)
        {
            int queryIndex = url.IndexOf('?');
            if (queryIndex < 0)
            {
                return "";
            }

            string query = url.Substring(queryIndex + 1);
            int hashIndex = query.IndexOf('#');
            if (hashIndex >= 0)
            {
                query = query.Substring(0, hashIndex);
            }

            foreach (string pair in query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                string name = equals >= 0 ? pair.Substring(0, equals) : pair;

                if (string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase))
                {
                    return equals >= 0 ? Uri.UnescapeDataString(pair.Substring(equals + 1)) : "";
                }
            }

            return "";
        }

        private static string EnsureGoogleConfirm(string url)
        {
            string result = url;

            if (result.IndexOf("export=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result = SetQueryValue(result, "export", "download");
            }

            if (result.IndexOf("confirm=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                result = SetQueryValue(result, "confirm", "t");
            }

            return result;
        }

        private static string SetQueryValue(string url, string key, string value)
        {
            int hashIndex = url.IndexOf('#');
            string fragment = "";
            if (hashIndex >= 0)
            {
                fragment = url.Substring(hashIndex);
                url = url.Substring(0, hashIndex);
            }

            int queryIndex = url.IndexOf('?');
            string prefix = queryIndex >= 0 ? url.Substring(0, queryIndex) : url;
            string query = queryIndex >= 0 ? url.Substring(queryIndex + 1) : "";

            StringBuilder builder = new StringBuilder();
            bool replaced = false;

            if (query.Length > 0)
            {
                foreach (string pair in query.Split('&'))
                {
                    if (pair.Length == 0)
                    {
                        continue;
                    }

                    int equals = pair.IndexOf('=');
                    string name = equals >= 0 ? pair.Substring(0, equals) : pair;

                    if (string.Equals(Uri.UnescapeDataString(name), key, StringComparison.OrdinalIgnoreCase))
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append('&');
                        }

                        builder.Append(name).Append('=').Append(Uri.EscapeDataString(value));
                        replaced = true;
                    }
                    else
                    {
                        if (builder.Length > 0)
                        {
                            builder.Append('&');
                        }

                        builder.Append(pair);
                    }
                }
            }

            if (!replaced)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                builder.Append(key).Append('=').Append(Uri.EscapeDataString(value));
            }

            return prefix + "?" + builder + fragment;
        }

        private static string InputValue(string html, string name)
        {
            foreach (Match tag in InputTagRegex.Matches(html))
            {
                string inputName = Attribute(tag.Value, "name");
                if (!string.Equals(inputName, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return WebUtility.HtmlDecode(Attribute(tag.Value, "value"));
            }

            return "";
        }

        private static string Attribute(string tag, string name)
        {
            Match match = Regex.Match(
                tag,
                @"\b" + Regex.Escape(name) + @"\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s>]+))",
                RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                return "";
            }

            if (match.Groups[1].Success)
            {
                return match.Groups[1].Value;
            }

            if (match.Groups[2].Success)
            {
                return match.Groups[2].Value;
            }

            return match.Groups[3].Value;
        }
    }
}
