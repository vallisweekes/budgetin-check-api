using System.Text.RegularExpressions;
using System.Net.Http.Headers;

namespace BudgetinCheck.Api.Features.Logo;

internal static class LogoEndpoints
{
    public static async Task<IResult> GetAsync(HttpRequest request, IHttpClientFactory httpClientFactory, CancellationToken cancellationToken)
    {
        var domain = SanitizeDomain(request.Query["domain"]);
        if (domain is null)
        {
            return Results.Json(new { error = "Invalid domain" }, statusCode: StatusCodes.Status400BadRequest);
        }

        var themeRaw = request.Query["theme"].ToString().Trim().ToLowerInvariant();
        var theme = themeRaw is "light" or "auto" or "dark" ? themeRaw : "dark";
        var debug = request.Query["debug"].ToString().Trim() == "1";
        var upstreamCandidates = BuildUpstreamCandidates(domain, theme);
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/avif"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/webp"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/apng"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/svg+xml"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/*"));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; BudgetInCheck/1.0; +https://budgetincheck.com)");

        var attempts = new List<object>();

        try
        {
            foreach (var candidate in upstreamCandidates)
            {
                using var response = await client.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                var contentType = response.Content.Headers.ContentType?.MediaType;

                attempts.Add(new
                {
                    url = RedactToken(candidate),
                    status = (int)response.StatusCode,
                    contentType,
                });

                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                if (debug)
                {
                    return Results.Json(new { ok = true, domain, selected = RedactToken(candidate), attempts });
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                var safeContentType = !string.IsNullOrWhiteSpace(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    ? contentType
                    : "image/png";

                return Results.File(bytes, safeContentType, enableRangeProcessing: false, lastModified: null, entityTag: null);
            }

            return debug
                ? Results.Json(new { ok = false, domain, error = "Logo not found", attempts }, statusCode: StatusCodes.Status404NotFound)
                : Results.Json(new { error = "Logo not found" }, statusCode: StatusCodes.Status404NotFound);
        }
        catch
        {
            return debug
                ? Results.Json(new { ok = false, domain, error = "Failed to fetch logo", attempts }, statusCode: StatusCodes.Status502BadGateway)
                : Results.Json(new { error = "Failed to fetch logo" }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static List<string> BuildUpstreamCandidates(string domain, string theme)
    {
        var candidates = new List<string>();
        var logoDevToken =
            Environment.GetEnvironmentVariable("LOGO_DEV_PUBLISHABLE_KEY")?.Trim() ??
            Environment.GetEnvironmentVariable("LOGO_DEV_TOKEN")?.Trim() ??
            string.Empty;

        if (!string.IsNullOrWhiteSpace(logoDevToken))
        {
            candidates.Add(
                $"https://img.logo.dev/{Uri.EscapeDataString(domain)}" +
                $"?token={Uri.EscapeDataString(logoDevToken)}" +
                "&format=png&size=128&retina=true" +
                $"&theme={Uri.EscapeDataString(theme)}&fallback=404");
        }

        candidates.Add($"https://logo.clearbit.com/{Uri.EscapeDataString(domain)}?size=128");
        candidates.Add($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(domain)}&sz=128");
        return candidates;
    }

    private static string? SanitizeDomain(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var normalized = input
            .Trim()
            .ToLowerInvariant()
            .Replace("http://", string.Empty, StringComparison.Ordinal)
            .Replace("https://", string.Empty, StringComparison.Ordinal)
            .Replace("www.", string.Empty, StringComparison.Ordinal)
            .Split('/')[0]
            .Split('?')[0]
            .Split('#')[0];

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return Regex.IsMatch(normalized, @"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}$", RegexOptions.IgnoreCase)
            ? normalized
            : null;
    }

    private static string RedactToken(string url) => Regex.Replace(url, @"([?&]token=)[^&]+", "$1***", RegexOptions.IgnoreCase);
}