using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.LinkAdder.Services;

/// <summary>
/// Adds the plugin's client script to Jellyfin's web entry page at response time.
/// The installed jellyfin-web files are never changed.
/// </summary>
public sealed class HeaderButtonStartupFilter : IStartupFilter
{
    private const string ScriptTag = "<script defer src=\"../LocalIspFtpLoader/client.js?v=2.5.2-search3\"></script>";

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(InjectClientScriptAsync);
            next(app);
        };
    }

    private static async Task InjectClientScriptAsync(HttpContext context, RequestDelegate next)
    {
        if (!IsWebEntryRequest(context.Request))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        Stream originalBody = context.Response.Body;
        await using var bufferedBody = new MemoryStream();
        context.Response.Body = bufferedBody;

        try
        {
            await next(context).ConfigureAwait(false);
            bufferedBody.Position = 0;

            if (context.Response.StatusCode == StatusCodes.Status200OK
                && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                string html;
                using (var reader = new StreamReader(bufferedBody, Encoding.UTF8, true, 1024, leaveOpen: true))
                {
                    html = await reader.ReadToEndAsync().ConfigureAwait(false);
                }

                if (!html.Contains("LocalIspFtpLoader/client.js", StringComparison.Ordinal))
                {
                    var closingBody = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                    html = closingBody >= 0
                        ? html.Insert(closingBody, ScriptTag)
                        : html + ScriptTag;
                }

                var output = Encoding.UTF8.GetBytes(html);
                context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                context.Response.ContentLength = output.Length;
                await originalBody.WriteAsync(output, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            bufferedBody.Position = 0;
            context.Response.ContentLength = bufferedBody.Length;
            await bufferedBody.CopyToAsync(originalBody, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }
    }

    private static bool IsWebEntryRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method))
        {
            return false;
        }

        var path = request.Path.Value?.TrimEnd('/');
        return string.Equals(path, "/web", StringComparison.OrdinalIgnoreCase)
            || path?.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase) == true;
    }
}
