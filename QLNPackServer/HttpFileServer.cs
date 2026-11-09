using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QLNPackServer
{
    public sealed class HttpFileServer : IDisposable
    {
        private readonly ServerConfig _config;
        private readonly PackCatalog _catalog;
        private TcpListener _listener;
        private Task _acceptTask;
        private volatile bool _running;

        public HttpFileServer(ServerConfig config, PackCatalog catalog)
        {
            _config = config;
            _catalog = catalog;
        }

        public void Start()
        {
            IPAddress address = ResolveAddress(_config.bindAddress);
            _listener = new TcpListener(address, _config.port);
            _listener.Start(100);
            _running = true;
            _acceptTask = Task.Run(AcceptLoop);

            Log.Info("Listening on http://" + _config.bindAddress + ":" + _config.port + "/");
            Log.Info("Client manifest URL: " + GetPublicManifestUrl());
        }

        public void Stop()
        {
            _running = false;

            if (_listener != null)
            {
                try
                {
                    _listener.Stop();
                }
                catch
                {
                    // Best effort.
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private string GetPublicManifestUrl()
        {
            if (!string.IsNullOrWhiteSpace(_config.publicBaseUrl))
            {
                return _config.publicBaseUrl + "/manifest.json";
            }

            string host = _config.bindAddress;
            if (host == "0.0.0.0" || host == "::")
            {
                host = "YOUR.SERVER.IP";
            }

            return "http://" + host + ":" + _config.port + "/manifest.json";
        }

        private async Task AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException exception)
                {
                    if (!_running)
                    {
                        break;
                    }

                    Log.Warn("Accept failed: " + exception.Message);
                    continue;
                }

                _ = Task.Run(() => HandleClient(client));
            }
        }

        private void HandleClient(TcpClient client)
        {
            string remote = "?";

            try
            {
                remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                client.NoDelay = true;
                client.ReceiveTimeout = 15000;
                client.SendTimeout = 60000;

                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    string requestLine;
                    Dictionary<string, string> headers;

                    if (!ReadRequest(stream, out requestLine, out headers))
                    {
                        SendError(stream, 400, "Bad Request", false);
                        return;
                    }

                    string[] parts = requestLine.Split(' ');
                    if (parts.Length < 2)
                    {
                        SendError(stream, 400, "Bad Request", false);
                        return;
                    }

                    string method = parts[0].ToUpperInvariant();
                    bool head = method == "HEAD";

                    if (method != "GET" && !head)
                    {
                        SendError(stream, 405, "Method Not Allowed", head);
                        return;
                    }

                    string rawTarget = parts[1];
                    string query = "";
                    int queryIndex = rawTarget.IndexOf('?');

                    if (queryIndex >= 0)
                    {
                        query = rawTarget.Substring(queryIndex + 1);
                        rawTarget = rawTarget.Substring(0, queryIndex);
                    }

                    string path;
                    try
                    {
                        path = Uri.UnescapeDataString(rawTarget);
                    }
                    catch
                    {
                        SendError(stream, 400, "Bad Request", head);
                        return;
                    }

                    if (path == "/health")
                    {
                        SendBytes(stream, 200, "OK", "text/plain", Encoding.UTF8.GetBytes("ok"), head, "no-cache");
                        return;
                    }

                    if (!IsAuthorized(query, headers))
                    {
                        Log.Info(remote + " " + method + " " + path + " -> 403");
                        SendError(stream, 403, "Forbidden", head);
                        return;
                    }

                    string baseUrl = GetBaseUrl(headers);

                    if (path == "/manifest.json")
                    {
                        byte[] manifest = _catalog.BuildManifest(baseUrl);
                        Log.Info(remote + " " + method + " /manifest.json -> 200 (" + manifest.Length + " bytes)");
                        SendBytes(stream, 200, "OK", "application/json", manifest, head, "no-cache");
                        return;
                    }

                    if (path == "/manifest.json.sig")
                    {
                        byte[] manifest = _catalog.BuildManifest(baseUrl);
                        byte[] signature = _catalog.GetSignature(manifest);

                        if (signature == null)
                        {
                            SendError(stream, 404, "Manifest is not signed", head);
                            return;
                        }

                        byte[] body = Encoding.ASCII.GetBytes(Convert.ToBase64String(signature));
                        Log.Info(remote + " " + method + " /manifest.json.sig -> 200 (" + body.Length + " bytes)");
                        SendBytes(stream, 200, "OK", "text/plain", body, head, "no-cache");
                        return;
                    }

                    if (path.StartsWith("/mods/", StringComparison.Ordinal))
                    {
                        string fileName = path.Substring("/mods/".Length);
                        string zipPath = _catalog.GetZipPath(fileName);

                        if (zipPath == null)
                        {
                            Log.Info(remote + " " + method + " " + path + " -> 404");
                            SendError(stream, 404, "Not Found", head);
                            return;
                        }

                        long length = new FileInfo(zipPath).Length;
                        Log.Info(remote + " " + method + " " + path + " -> 200 (" + length + " bytes)");
                        SendFile(stream, zipPath, length, head);
                        return;
                    }

                    Log.Info(remote + " " + method + " " + path + " -> 404");
                    SendError(stream, 404, "Not Found", head);
                }
            }
            catch (IOException)
            {
                // Client disconnected or timed out.
            }
            catch (SocketException)
            {
                // Client disconnected.
            }
            catch (Exception exception)
            {
                Log.Exception("Request from " + remote + " failed", exception);
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch
                {
                    // Best effort.
                }
            }
        }

        private bool ReadRequest(
            NetworkStream stream,
            out string requestLine,
            out Dictionary<string, string> headers)
        {
            requestLine = null;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            using (StreamReader reader = new StreamReader(
                stream,
                Encoding.ASCII,
                false,
                4096,
                true))
            {
                requestLine = reader.ReadLine();
                if (string.IsNullOrEmpty(requestLine))
                {
                    return false;
                }

                int count = 0;
                while (true)
                {
                    string line = reader.ReadLine();
                    if (line == null || line.Length == 0)
                    {
                        break;
                    }

                    if (++count > 100)
                    {
                        return false;
                    }

                    int colon = line.IndexOf(':');
                    if (colon > 0)
                    {
                        string name = line.Substring(0, colon).Trim();
                        string value = line.Substring(colon + 1).Trim();
                        headers[name] = value;
                    }
                }
            }

            return true;
        }

        private bool IsAuthorized(string query, Dictionary<string, string> headers)
        {
            if (string.IsNullOrEmpty(_config.accessToken))
            {
                return true;
            }

            string token = GetQueryValue(query, "token");

            if (string.IsNullOrEmpty(token))
            {
                string header;
                if (headers.TryGetValue("X-Pack-Token", out header))
                {
                    token = header;
                }
            }

            return string.Equals(token, _config.accessToken, StringComparison.Ordinal);
        }

        private string GetBaseUrl(Dictionary<string, string> headers)
        {
            if (!string.IsNullOrWhiteSpace(_config.publicBaseUrl))
            {
                return _config.publicBaseUrl.TrimEnd('/');
            }

            string scheme = Header(headers, "X-Forwarded-Proto");
            if (!string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "http";
            }

            string host = Header(headers, "X-Forwarded-Host");
            if (string.IsNullOrEmpty(host))
            {
                host = Header(headers, "Host");
            }

            if (string.IsNullOrEmpty(host) || !Regex.IsMatch(host, @"^[A-Za-z0-9\.\-:\[\]]+$"))
            {
                host = "127.0.0.1:" + _config.port;
            }

            return scheme + "://" + host;
        }

        private static string Header(Dictionary<string, string> headers, string name)
        {
            string value;
            return headers.TryGetValue(name, out value) ? value : "";
        }

        private static string GetQueryValue(string query, string name)
        {
            if (string.IsNullOrEmpty(query))
            {
                return "";
            }

            foreach (string pair in query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                string key = equals >= 0 ? pair.Substring(0, equals) : pair;

                try
                {
                    if (!string.Equals(Uri.UnescapeDataString(key), name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string value = equals >= 0 ? pair.Substring(equals + 1) : "";
                    return Uri.UnescapeDataString(value);
                }
                catch
                {
                    return "";
                }
            }

            return "";
        }

        private static void SendBytes(
            NetworkStream stream,
            int status,
            string reason,
            string contentType,
            byte[] body,
            bool head,
            string cacheControl)
        {
            WriteHeaders(stream, status, reason, contentType, body.Length, cacheControl);

            if (!head)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        private static void SendFile(NetworkStream stream, string path, long length, bool head)
        {
            WriteHeaders(stream, 200, "OK", "application/zip", length, "public, max-age=3600");

            if (head)
            {
                return;
            }

            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file.CopyTo(stream, 65536);
            }
        }

        private static void SendError(NetworkStream stream, int status, string reason, bool head)
        {
            byte[] body = Encoding.UTF8.GetBytes(reason);
            WriteHeaders(stream, status, reason, "text/plain", body.Length, "no-cache");

            if (!head)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        private static void WriteHeaders(
            NetworkStream stream,
            int status,
            string reason,
            string contentType,
            long length,
            string cacheControl)
        {
            string header =
                "HTTP/1.1 " + status + " " + reason + "\r\n" +
                "Content-Type: " + contentType + "\r\n" +
                "Content-Length: " + length + "\r\n" +
                "Cache-Control: " + cacheControl + "\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Connection: close\r\n" +
                "\r\n";

            byte[] bytes = Encoding.ASCII.GetBytes(header);
            stream.Write(bytes, 0, bytes.Length);
        }

        private static IPAddress ResolveAddress(string bindAddress)
        {
            if (string.IsNullOrWhiteSpace(bindAddress))
            {
                return IPAddress.Any;
            }

            if (bindAddress == "0.0.0.0" || bindAddress == "*")
            {
                return IPAddress.Any;
            }

            if (bindAddress == "::")
            {
                return IPAddress.IPv6Any;
            }

            if (string.Equals(bindAddress, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return IPAddress.Loopback;
            }

            IPAddress address;
            return IPAddress.TryParse(bindAddress, out address) ? address : IPAddress.Any;
        }
    }
}
