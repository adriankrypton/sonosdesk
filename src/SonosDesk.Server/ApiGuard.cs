using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using SonosDesk.Core.Localization;
using SonosDesk.Core.Models;
using SonosDesk.Infrastructure.Persistence;

namespace SonosDesk.Server;

public sealed record ApiToken(string Value);

public static class ApiVersion
{
    public static string Current { get; } =
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "0.0.0";
}

public static class Json
{
    // Same naming as settings.json: camelCase properties and enum values.
    public static void Configure(JsonSerializerOptions options)
    {
        var settings = JsonConfigurationService.SerializerOptions;
        options.PropertyNamingPolicy = settings.PropertyNamingPolicy;
        options.DictionaryKeyPolicy = settings.DictionaryKeyPolicy;
        options.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
        options.PropertyNameCaseInsensitive = true;
        foreach (var converter in settings.Converters)
        {
            options.Converters.Add(converter);
        }
    }

    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }
}

// Only the Electron app of this session may talk to the server: loopback, expected host, bearer token.
// CORS is open for the token-protected API because the UI is loaded from a file:// page.
public sealed class ApiGuard(RequestDelegate next, ApiToken token, ILogger<ApiGuard> logger)
{
    private readonly byte[] _token = Encoding.UTF8.GetBytes(token.Value);

    public async Task InvokeAsync(HttpContext context)
    {
        var response = context.Response;
        response.Headers.AccessControlAllowOrigin = "*";
        response.Headers.AccessControlAllowHeaders = "Authorization, Content-Type";
        response.Headers.AccessControlAllowMethods = "GET, POST, PUT, PATCH, DELETE, OPTIONS";
        response.Headers.XContentTypeOptions = "nosniff";

        if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote) || !IsLocalHost(context.Request.Host.Host))
        {
            response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        if (!HasToken(context.Request))
        {
            response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        try
        {
            await next(context);
        }
        catch (SonosException ex)
        {
            await WriteErrorAsync(context, StatusFor(ex.Kind), ex.Message, ex.Kind.ToString());
        }
        catch (BadHttpRequestException ex)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message, "rejected");
        }
        catch (JsonException ex)
        {
            await WriteErrorAsync(context, StatusCodes.Status400BadRequest, ex.Message, "rejected");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client went away.
        }
        catch (OperationCanceledException)
        {
            await WriteErrorAsync(context, StatusCodes.Status504GatewayTimeout, L.T("vm.timeout"), "timeout");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled API error on {Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status500InternalServerError, L.T("app.unexpected_error"), "unknown");
        }
    }

    private static bool IsLocalHost(string host) => host is "127.0.0.1" or "localhost";

    private bool HasToken(HttpRequest request)
    {
        string? presented = null;
        if (request.Headers.Authorization.ToString() is { Length: > 7 } header && header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            presented = header[7..];
        }
        else if (request.Query.TryGetValue("token", out var query))
        {
            presented = query.ToString();
        }

        return presented is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), _token);
    }

    private static int StatusFor(SonosErrorKind kind) => kind switch
    {
        SonosErrorKind.NotFound => StatusCodes.Status404NotFound,
        SonosErrorKind.Rejected => StatusCodes.Status400BadRequest,
        SonosErrorKind.NotSupported => StatusCodes.Status409Conflict,
        SonosErrorKind.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status502BadGateway,
    };

    private static async Task WriteErrorAsync(HttpContext context, int status, string message, string kind)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        var camel = JsonNamingPolicy.CamelCase.ConvertName(kind);
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { message, kind = camel }, Json.Options));
    }
}
