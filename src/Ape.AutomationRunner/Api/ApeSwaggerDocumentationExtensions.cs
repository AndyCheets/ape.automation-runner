using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Swagger;

namespace Ape.AutomationRunner.Api;

public static class ApeSwaggerDocumentationExtensions
{
    public const string OpenApiPath = "/openapi.json";
    public const string ReDocSpecUrl = "openapi.json";
    public const string SwaggerUiOpenApiPath = "../openapi.json";
    public const string SwaggerUiPath = "/docs";
    public const string ReDocPath = "/redoc";
    public const string GatewayPathPrefix = "/api/workflows";

    public static WebApplication UseApeSwaggerDocumentation(this WebApplication app)
    {
        app.MapGet(OpenApiPath, WriteOpenApiJson)
            .ExcludeFromDescription();

        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "docs";
            options.SwaggerEndpoint(SwaggerUiOpenApiPath, "APE Automation Runner Workflow API v1");
        });
        app.MapGet(ReDocPath, () => Results.Content(GetReDocHtml(), "text/html; charset=utf-8"))
            .ExcludeFromDescription();

        return app;
    }

    public static string GetReDocHtml(string specUrl = ReDocSpecUrl)
        => $$"""
            <!doctype html>
            <html>
            <head>
                <title>APE Automation Runner Workflow API</title>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <style>body { margin: 0; padding: 0; }</style>
            </head>
            <body>
                <redoc spec-url="{{specUrl}}"></redoc>
                <script src="https://cdn.jsdelivr.net/npm/redoc@next/bundles/redoc.standalone.js"></script>
            </body>
            </html>
            """;

    private static async Task WriteOpenApiJson(
        ISwaggerProvider swaggerProvider,
        HttpResponse response,
        CancellationToken cancellationToken
    )
    {
        response.ContentType = "application/json; charset=utf-8";
        OpenApiDocument document = swaggerProvider.GetSwagger("v1");
        ApplyGatewayPathPrefix(document);

        using MemoryStream stream = new();
        document.SerializeAsJson(stream, OpenApiSpecVersion.OpenApi3_0);
        response.ContentLength = stream.Length;

        stream.Position = 0;
        await stream.CopyToAsync(response.Body, cancellationToken);
    }

    public static void ApplyGatewayPathPrefix(OpenApiDocument document)
    {
        OpenApiPaths prefixedPaths = [];
        foreach (KeyValuePair<string, OpenApiPathItem> path in document.Paths)
        {
            string prefixedPath = path.Key.Equals("/", StringComparison.Ordinal)
                ? GatewayPathPrefix
                : $"{GatewayPathPrefix}{path.Key}";

            prefixedPaths[prefixedPath] = path.Value;
        }

        document.Paths = prefixedPaths;
    }
}
