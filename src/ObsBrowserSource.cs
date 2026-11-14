using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FysZeRewardOverlay;

internal sealed class ObsBrowserSource : IDisposable
{
    private const string Origin = "http://127.0.0.1:31339/";
    internal const string Url = Origin + "overlay";

    private readonly HttpListener listener = new();
    private Frame? latest;

    internal ObsBrowserSource()
    {
        try
        {
            listener.Prefixes.Add(Origin);
            listener.Start();
        }
        catch
        {
            listener.Close();
            throw;
        }
        _ = Task.Run(ServeAsync);
    }

    internal void Publish(byte[] png) => Volatile.Write(ref latest, new Frame(png, $"\"{Guid.NewGuid():N}\""));

    private async Task ServeAsync()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException) { return; }

            try { await RespondAsync(context); }
            catch (Exception ex) when (ex is IOException or HttpListenerException or ObjectDisposedException or TimeoutException)
            { context.Response.Abort(); }
        }
    }

    private async Task RespondAsync(HttpListenerContext context)
    {
        var request = context.Request;
        using var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        byte[]? body = null;

        if (request.RemoteEndPoint is not { Address: var address } || !IPAddress.IsLoopback(address) || request.Url?.Host != "127.0.0.1")
        {
            response.StatusCode = (int)HttpStatusCode.Forbidden;
        }
        else if (request.HttpMethod != "GET")
        {
            response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
            response.Headers["Allow"] = "GET";
        }
        else if (request.Url.AbsolutePath == "/overlay")
        {
            response.ContentType = "text/html; charset=utf-8";
            response.Headers["Content-Security-Policy"] = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src blob:; connect-src 'self'";
            body = Page;
        }
        else if (request.Url.AbsolutePath == "/overlay.png")
        {
            var frame = Volatile.Read(ref latest);
            if (frame is null) response.StatusCode = (int)HttpStatusCode.NoContent;
            else
            {
                response.Headers["ETag"] = frame.Tag;
                if (request.Headers["If-None-Match"] == frame.Tag) response.StatusCode = (int)HttpStatusCode.NotModified;
                else
                {
                    response.ContentType = "image/png";
                    body = frame.Png;
                }
            }
        }
        else response.StatusCode = (int)HttpStatusCode.NotFound;

        response.ContentLength64 = body?.Length ?? 0;
        if (body is not null) await response.OutputStream.WriteAsync(body).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
    }

    public void Dispose() => listener.Close();

    private sealed record Frame(byte[] Png, string Tag);

    private static readonly byte[] Page = Encoding.UTF8.GetBytes("""
        <!doctype html>
        <html lang="zh-CN">
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>ZE 奖励悬浮窗</title>
        <style>
          html, body { margin: 0; background: transparent; overflow: hidden; }
          img:not([hidden]) { display: block; max-width: 100vw; max-height: 100vh; }
        </style>
        <body>
        <img id="card" alt="" hidden>
        <script>
          const card = document.getElementById('card');
          let etag = '';
          let imageUrl = '';

          function show(url, tag) {
            if (url) card.src = url;
            else card.removeAttribute('src');
            card.hidden = !url;
            if (imageUrl) URL.revokeObjectURL(imageUrl);
            imageUrl = url;
            etag = tag;
          }

          async function refresh() {
            const controller = new AbortController();
            const timeout = setTimeout(() => controller.abort(), 3000);
            let delay = 400;
            try {
              const headers = etag ? { 'If-None-Match': etag } : {};
              const response = await fetch('/overlay.png', { headers, cache: 'no-store', signal: controller.signal });
              if (response.status === 200) show(URL.createObjectURL(await response.blob()), response.headers.get('ETag') || '');
              else if (response.status === 204) show('', '');
              else if (response.status !== 304) throw new Error('Overlay unavailable');
            } catch {
              show('', '');
              delay = 1000;
            } finally {
              clearTimeout(timeout);
              setTimeout(refresh, delay);
            }
          }

          refresh();
        </script>
        </body>
        </html>
        """);
}
