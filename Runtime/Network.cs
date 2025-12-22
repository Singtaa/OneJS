using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace OneJS {
    /// <summary>
    /// Provides fetch API functionality for JavaScript.
    /// Wraps UnityWebRequest to provide a Promise-based HTTP client.
    /// </summary>
    public static class Network {
        /// <summary>
        /// Performs an HTTP request. Called from JavaScript via fetch().
        /// Returns JSON string that JS can parse directly.
        /// </summary>
        /// <param name="url">The URL to fetch</param>
        /// <param name="method">HTTP method (GET, POST, PUT, DELETE, etc.)</param>
        /// <param name="body">Request body (for POST/PUT)</param>
        /// <param name="headersJson">JSON object of headers</param>
        /// <returns>JSON string with response data</returns>
        public static async Task<string> FetchAsync(string url, string method, string body, string headersJson) {
            method = method?.ToUpperInvariant() ?? "GET";

            using (var request = CreateRequest(url, method, body)) {
                // Apply headers
                if (!string.IsNullOrEmpty(headersJson)) {
                    ApplyHeaders(request, headersJson);
                }

                // Send request and await completion
                var operation = request.SendWebRequest();
                while (!operation.isDone) {
                    await Task.Yield();
                }

                // Build JSON response
                bool ok = request.result == UnityWebRequest.Result.Success;
                int status = (int)request.responseCode;
                string statusText = GetStatusText(request.responseCode);
                string responseUrl = request.url ?? "";
                string responseBody = request.downloadHandler?.text ?? "";
                var headers = ExtractHeaders(request);

                return BuildResponseJson(ok, status, statusText, responseUrl, responseBody, headers);
            }
        }

        static string BuildResponseJson(bool ok, int status, string statusText, string url, string body, Dictionary<string, string> headers) {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"ok\":{(ok ? "true" : "false")},");
            sb.Append($"\"status\":{status},");
            sb.Append($"\"statusText\":{EscapeJsonString(statusText)},");
            sb.Append($"\"url\":{EscapeJsonString(url)},");
            sb.Append($"\"body\":{EscapeJsonString(body)},");
            sb.Append("\"headers\":{");

            bool first = true;
            foreach (var kvp in headers) {
                if (!first) sb.Append(",");
                sb.Append($"{EscapeJsonString(kvp.Key)}:{EscapeJsonString(kvp.Value)}");
                first = false;
            }

            sb.Append("}}");
            return sb.ToString();
        }

        static string EscapeJsonString(string s) {
            if (s == null) return "null";
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s) {
                switch (c) {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32) {
                            sb.Append($"\\u{(int)c:x4}");
                        } else {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        static UnityWebRequest CreateRequest(string url, string method, string body) {
            UnityWebRequest request;

            switch (method) {
                case "POST":
                    request = new UnityWebRequest(url, "POST");
                    if (!string.IsNullOrEmpty(body)) {
                        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                    }
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;

                case "PUT":
                    request = new UnityWebRequest(url, "PUT");
                    if (!string.IsNullOrEmpty(body)) {
                        request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                    }
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;

                case "DELETE":
                    request = UnityWebRequest.Delete(url);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    break;

                case "HEAD":
                    request = UnityWebRequest.Head(url);
                    break;

                case "GET":
                default:
                    request = UnityWebRequest.Get(url);
                    break;
            }

            return request;
        }

        static void ApplyHeaders(UnityWebRequest request, string headersJson) {
            try {
                // Parse simple JSON object { "key": "value", ... }
                // Using a simple parser to avoid external dependencies
                var headers = ParseHeadersJson(headersJson);
                foreach (var kvp in headers) {
                    request.SetRequestHeader(kvp.Key, kvp.Value);
                }
            } catch (Exception ex) {
                Debug.LogWarning($"[Network] Failed to parse headers: {ex.Message}");
            }
        }

        static Dictionary<string, string> ParseHeadersJson(string json) {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json) || json == "null" || json == "{}") {
                return result;
            }

            // Simple JSON object parser for { "key": "value" } format
            json = json.Trim();
            if (!json.StartsWith("{") || !json.EndsWith("}")) {
                return result;
            }

            json = json.Substring(1, json.Length - 2).Trim();
            if (string.IsNullOrEmpty(json)) {
                return result;
            }

            // Split by comma, handling quoted strings
            int depth = 0;
            int start = 0;
            var pairs = new List<string>();

            for (int i = 0; i < json.Length; i++) {
                char c = json[i];
                if (c == '"') {
                    // Skip to end of string
                    i++;
                    while (i < json.Length && json[i] != '"') {
                        if (json[i] == '\\') i++; // Skip escaped char
                        i++;
                    }
                } else if (c == '{' || c == '[') {
                    depth++;
                } else if (c == '}' || c == ']') {
                    depth--;
                } else if (c == ',' && depth == 0) {
                    pairs.Add(json.Substring(start, i - start).Trim());
                    start = i + 1;
                }
            }
            if (start < json.Length) {
                pairs.Add(json.Substring(start).Trim());
            }

            foreach (var pair in pairs) {
                var colonIdx = pair.IndexOf(':');
                if (colonIdx > 0) {
                    var key = UnquoteString(pair.Substring(0, colonIdx).Trim());
                    var value = UnquoteString(pair.Substring(colonIdx + 1).Trim());
                    if (!string.IsNullOrEmpty(key)) {
                        result[key] = value;
                    }
                }
            }

            return result;
        }

        static string UnquoteString(string s) {
            if (s.Length >= 2 && s.StartsWith("\"") && s.EndsWith("\"")) {
                return s.Substring(1, s.Length - 2)
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t");
            }
            return s;
        }

        static Dictionary<string, string> ExtractHeaders(UnityWebRequest request) {
            var headers = new Dictionary<string, string>();
            var responseHeaders = request.GetResponseHeaders();
            if (responseHeaders != null) {
                foreach (var kvp in responseHeaders) {
                    headers[kvp.Key] = kvp.Value;
                }
            }
            return headers;
        }

        static string GetStatusText(long code) {
            switch (code) {
                case 200: return "OK";
                case 201: return "Created";
                case 204: return "No Content";
                case 301: return "Moved Permanently";
                case 302: return "Found";
                case 304: return "Not Modified";
                case 400: return "Bad Request";
                case 401: return "Unauthorized";
                case 403: return "Forbidden";
                case 404: return "Not Found";
                case 405: return "Method Not Allowed";
                case 500: return "Internal Server Error";
                case 502: return "Bad Gateway";
                case 503: return "Service Unavailable";
                default: return "";
            }
        }
    }

}
