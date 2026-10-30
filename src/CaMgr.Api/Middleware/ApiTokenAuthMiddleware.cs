using System.Security.Claims;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;

namespace CaMgr.Api.Middleware;

/// <summary>
/// Machine-to-machine authentication: accepts "Authorization: Bearer cam_..." API
/// tokens on /api/* (except /api/auth/*) and maps them to an Operator-role principal.
/// Cookie sessions are unaffected - this only engages when a Bearer token is present.
/// </summary>
public sealed class ApiTokenAuthMiddleware(
    RequestDelegate next,
    ILogger<ApiTokenAuthMiddleware> log)
{
    private const string SchemeName = "ApiToken";

    public async Task InvokeAsync(HttpContext context, ApiTokenService tokens)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        var path = context.Request.Path;

        // only engage for API paths carrying a Bearer token with our prefix
        if (path.StartsWithSegments("/api") &&
            !path.StartsWithSegments("/api/auth") &&
            authHeader.StartsWith("Bearer cam_", StringComparison.OrdinalIgnoreCase))
        {
            var plaintext = authHeader["Bearer ".Length..].Trim();
            var token = await tokens.ValidateAsync(plaintext);

            if (token is null)
            {
                log.LogWarning("Rejected API request with invalid/expired token from {Ip}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { error = "invalid or expired API token" });
                return;
            }

            var identity = new ClaimsIdentity(SchemeName);
            identity.AddClaim(new Claim(ClaimTypes.Name, $"token:{token.Name}"));
            identity.AddClaim(new Claim(ClaimTypes.Role, ((AppRole)token.Role).ToString()));
            identity.AddClaim(new Claim("source", "Token"));
            context.User = new ClaimsPrincipal(identity);
        }

        await next(context);
    }
}
