using System;
using System.Threading.Tasks;

using Bannerlord.ServerInfo;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(ServerInfoHostingStartup))]

namespace Bannerlord.ServerInfo;

/// <summary>
/// Hosting startup hook loaded by ASP.NET Core via <c>ASPNETCORE_HOSTINGSTARTUPASSEMBLIES</c>.
/// Injects the ServerInfo startup filter into the web host. See <see cref="SubModule"/>.
/// </summary>
public sealed class ServerInfoHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) =>
        builder.ConfigureServices(services => services.AddTransient<IStartupFilter, ServerInfoStartupFilter>());
}

/// <summary>
/// Startup filter executed before the dedicated server web panel's <c>Startup.Configure</c>.
/// Maps the public ServerInfo endpoint ahead of the web panel's authentication middleware.
/// </summary>
internal sealed class ServerInfoStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Map(ServerInfoEndpoint.Path, branch => branch.Run(ServerInfoEndpoint.HandleAsync));
        next(app);
    };
}

internal static class ServerInfoEndpoint
{
    public const string Path = "/butr/v1/server";

    public static async Task HandleAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
        {
            context.Response.StatusCode = StatusCodes.Status405MethodNotAllowed;
            context.Response.Headers["Allow"] = "GET, HEAD";
            return;
        }

        if (SubModule.Provider is not { } provider)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            var body = provider.Response;
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-cache";
            context.Response.ContentLength = body.Length;
            if (!HttpMethods.IsHead(context.Request.Method))
                await context.Response.Body.WriteAsync(body, 0, body.Length, context.RequestAborted);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to serve {Path}: {e}");
            if (!context.Response.HasStarted)
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        }
    }
}
