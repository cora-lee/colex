using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Mammoth
{
    /// <summary>
    /// Tiny static file server, menu-driven, so the Web/ camera page (and the
    /// settings.json it reads) can be served over http://localhost without
    /// anyone needing to open a terminal and run `python3 -m http.server`.
    ///
    /// Serves files relative to the project root (one level above Assets/), so
    /// "/Web/index.html" and "/Assets/StreamingAssets/MammothMapping/settings.json"
    /// resolve exactly like they do when started with the Python static server
    /// described in Web/README.md. Loopback only.
    /// </summary>
    public static class MammothWebServer
    {
        const int Port = 8080;
        static HttpListener listener;
        static Thread thread;

        [MenuItem("Mammoth/Web Server/Start (port 8080)", false, 100)]
        public static void Start()
        {
            if (listener != null && listener.IsListening)
            {
                Debug.Log("[Mammoth] Web server already running at http://localhost:" + Port + "/Web/index.html");
                return;
            }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Prefixes.Add($"http://localhost:{Port}/");
            try
            {
                listener.Start();
            }
            catch (Exception e)
            {
                Debug.LogError("[Mammoth] could not start web server on port " + Port + ": " + e.Message);
                listener = null;
                return;
            }

            thread = new Thread(() => Serve(listener, root)) { IsBackground = true };
            thread.Start();
            Debug.Log("[Mammoth] Web server running. Open http://localhost:" + Port + "/Web/index.html in a browser.");
        }

        [MenuItem("Mammoth/Web Server/Stop", false, 101)]
        public static void Stop()
        {
            if (listener == null) return;
            try { listener.Stop(); listener.Close(); } catch { /* ignore */ }
            listener = null;
            Debug.Log("[Mammoth] Web server stopped.");
        }

        static void Serve(HttpListener server, string root)
        {
            while (server.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = server.GetContext(); }
                catch (Exception) { return; } // listener stopped

                try
                {
                    string path = ctx.Request.Url.AbsolutePath;
                    if (path == "/") path = "/Web/index.html";
                    // Prevent escaping the project root.
                    string relative = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                    string full = Path.GetFullPath(Path.Combine(root, relative));
                    if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full))
                    {
                        ctx.Response.StatusCode = 404;
                        ctx.Response.Close();
                        continue;
                    }

                    byte[] bytes = File.ReadAllBytes(full);
                    ctx.Response.ContentType = ContentTypeFor(full);
                    ctx.Response.ContentLength64 = bytes.Length;
                    ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    ctx.Response.Close();
                }
                catch (Exception e)
                {
                    try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* ignore */ }
                    Debug.LogWarning("[Mammoth] web server request failed: " + e.Message);
                }
            }
        }

        static string ContentTypeFor(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            switch (ext)
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "application/javascript; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                default: return "application/octet-stream";
            }
        }
    }
}
