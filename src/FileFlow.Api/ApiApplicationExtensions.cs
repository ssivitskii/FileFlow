using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace FileFlow.Api;

public static class ApiApplicationExtensions
{
    public const int MaxRequestBodyBytes = 8192;

    private const string ClientHeader = "X-FileFlow-Client";
    private const string ClientHeaderValue = "web";

    public static IServiceCollection AddFileFlowApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddSingleton<RootedWorkspace>();
        services.AddSingleton<PathPolicy>();
        services.AddSingleton<WorkspaceReader>();
        services.AddSingleton<DuplicateScanner>();
        services.AddSingleton<HistoryReader>();
        services.AddSingleton<IOperationPreviewer, OperationPreviewer>();
        return services;
    }

    public static WebApplication UseFileFlowApi(this WebApplication app)
    {
        app.Use(AddSecurityHeadersAsync);
        app.UseExceptionHandler(errorApp => errorApp.Run(WriteProblemAsync));
        app.Use(RequireApiClientHeaderAsync);
        app.Use(LimitOperationPreviewBodyAsync);
        app.MapGet("/api/workspace", (string? path, WorkspaceReader reader) => reader.List(path));
        app.MapGet("/api/files/preview", PreviewFileAsync);
        app.MapGet("/api/duplicates", ScanDuplicatesAsync);
        app.MapGet("/api/history", (HistoryReader reader) => reader.Read());
        app.MapPost("/api/operations/preview", (OperationPreviewRequest request, IOperationPreviewer previewer) => previewer.Preview(request));
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        return app;
    }

    private static async Task AddSecurityHeadersAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(
            static state =>
            {
                AddSecurityHeaders((HttpResponse)state);
                return Task.CompletedTask;
            },
            context.Response);
        await next(context);
    }

    private static void AddSecurityHeaders(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.XContentTypeOptions = "nosniff";
        response.Headers.XFrameOptions = "DENY";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        response.Headers["Referrer-Policy"] = "no-referrer";
    }

    private static async Task LimitOperationPreviewBodyAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Method == HttpMethods.Post
            && context.Request.Path.Equals("/api/operations/preview", StringComparison.OrdinalIgnoreCase))
        {
            if (context.Request.ContentLength > MaxRequestBodyBytes)
            {
                await WriteRequestTooLargeAsync(context);
                return;
            }

            IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false })
                sizeFeature.MaxRequestBodySize = MaxRequestBodyBytes;
        }

        await next(context);
    }

    private static async Task RequireApiClientHeaderAsync(HttpContext context, RequestDelegate next)
    {
        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
            && (!context.Request.Headers.TryGetValue(ClientHeader, out Microsoft.Extensions.Primitives.StringValues value)
                || value.Count != 1
                || !string.Equals(value[0], ClientHeaderValue, StringComparison.Ordinal)))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "API client header required",
                detail: "Use the local FileFlow client to access API resources.")
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static async Task<FilePreviewResponse> PreviewFileAsync(
        string? path,
        WorkspaceReader reader,
        CancellationToken cancellationToken)
    {
        return await reader.PreviewAsync(path, cancellationToken);
    }

    private static async Task<DuplicateResponse> ScanDuplicatesAsync(
        string? path,
        DuplicateScanner scanner,
        CancellationToken cancellationToken)
    {
        return await scanner.ScanAsync(path, cancellationToken);
    }

    private static async Task WriteProblemAsync(HttpContext context)
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            return;
        var safe = exception as ApiProblemException;
        if (exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge })
        {
            await WriteRequestTooLargeAsync(context);
            return;
        }

        int status = safe?.StatusCode ?? StatusCodes.Status500InternalServerError;
        await Results.Problem(
            statusCode: status,
            title: safe?.Title ?? "Request failed",
            detail: safe?.Detail ?? "The request could not be completed safely.")
            .ExecuteAsync(context);
    }

    private static async Task WriteRequestTooLargeAsync(HttpContext context)
    {
        await Results.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge,
            title: "Request body too large",
            detail: "The operation preview request body cannot exceed 8192 bytes.")
            .ExecuteAsync(context);
    }
}
