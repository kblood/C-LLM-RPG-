namespace RPGWeb.Services;

/// <summary>
/// A server-side, per-browser identifier used to isolate web save files.
/// The value originates in an HttpOnly cookie and is captured once for the
/// lifetime of the Blazor circuit.
/// </summary>
public sealed class BrowserSaveSlot
{
    internal const string CookieName = "RPGWeb.SaveSlot";
    internal const string ContextItemName = "RPGWeb.ValidatedSaveSlot";

    public string Id { get; }

    public BrowserSaveSlot(IHttpContextAccessor httpContextAccessor)
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("A browser save slot requires an active HTTP context.");
        var candidate = context.Items[ContextItemName] as string
            ?? context.Request.Cookies[CookieName];

        if (!TryNormalize(candidate, out var normalized))
            throw new InvalidOperationException("The browser save slot cookie was not initialized.");

        Id = normalized;
    }

    internal static bool TryNormalize(string? candidate, out string normalized)
    {
        if (Guid.TryParse(candidate, out var slotId) && slotId != Guid.Empty)
        {
            normalized = slotId.ToString("N");
            return true;
        }

        normalized = string.Empty;
        return false;
    }
}

/// <summary>
/// Creates or validates the anonymous save-slot cookie before Razor components
/// and their scoped services are resolved.
/// </summary>
public sealed class BrowserSaveSlotCookieMiddleware
{
    private static readonly TimeSpan CookieLifetime = TimeSpan.FromDays(3650);
    private readonly RequestDelegate _next;

    public BrowserSaveSlotCookieMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Cookies[BrowserSaveSlot.CookieName];
        if (!BrowserSaveSlot.TryNormalize(supplied, out var slotId))
            slotId = Guid.NewGuid().ToString("N");

        context.Items[BrowserSaveSlot.ContextItemName] = slotId;
        context.Response.Cookies.Append(
            BrowserSaveSlot.CookieName,
            slotId,
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                MaxAge = CookieLifetime,
                Path = "/",
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps
            });

        await _next(context);
    }
}
