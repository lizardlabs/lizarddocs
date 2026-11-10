using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace LizardDocs;

public static class PreviewServer
{
    public static WebApplication Create(string output, int port)
    {
        if (port is < 0 or > 65535) throw new ArgumentException("Port must be between 0 and 65535");
        output = Data.Resolve(output);
        var result = SiteChecks.Check(output);
        if (result.Errors.Count > 0) throw new InvalidDataException(string.Join('\n', result.Errors));
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { WebRootPath = output, ContentRootPath = output });
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.Run(async context =>
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/html; charset=utf-8";
            var path = Path.Combine(output, "404.html");
            if (File.Exists(path)) await context.Response.SendFileAsync(path, context.RequestAborted);
            else await context.Response.WriteAsync("Page not found", context.RequestAborted);
        });
        return app;
    }

    public static void Run(string output, int port)
    {
        var app = Create(output, port);
        try
        {
            app.StartAsync().GetAwaiter().GetResult();
            Console.WriteLine("Documentation preview: " + string.Join(", ", app.Urls) + " (Ctrl+C to stop)");
            app.WaitForShutdownAsync().GetAwaiter().GetResult();
        }
        finally { app.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
