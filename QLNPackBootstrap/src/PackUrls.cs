using System;

namespace QLNPackBootstrap
{
    internal static class PackUrls
    {
        /// <summary>
        /// Adds the shared pack token to a URL, preserving any existing query.
        /// The server checks it on the manifest and on every zip request.
        /// </summary>
        public static string WithToken(string url, string token)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
            {
                return url;
            }

            if (url.IndexOf("token=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return url;
            }

            string separator = url.IndexOf('?') >= 0 ? "&" : "?";
            return url + separator + "token=" + Uri.EscapeDataString(token);
        }
    }
}
