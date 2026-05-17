using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BudgetinCheck.Api.Infrastructure.Legacy;

internal sealed class LegacyBffClient(HttpClient httpClient)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public bool IsConfigured => httpClient.BaseAddress is not null;

    public async Task<LegacyMeResponse?> GetProfileAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, "/api/bff/me");
        CopyAuthHeaders(request, upstreamRequest);
        upstreamRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<LegacyMeResponse>(content, JsonOptions, cancellationToken);
    }

    public async Task ProxyAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var targetPath = context.Request.Path.Value;
        var targetQuery = context.Request.QueryString.Value;
        using var upstreamRequest = await CreateUpstreamRequestAsync(
            context.Request,
            $"{targetPath}{targetQuery}",
            cancellationToken);

        using var upstreamResponse = await httpClient.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        CopyResponseHeaders(upstreamResponse, context.Response);

        await upstreamResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static async Task<HttpRequestMessage> CreateUpstreamRequestAsync(HttpRequest request, string targetPathAndQuery, CancellationToken cancellationToken)
    {
        var upstreamRequest = new HttpRequestMessage(new HttpMethod(request.Method), targetPathAndQuery);

        if (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
        {
            var memory = new MemoryStream();
            await request.Body.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;
            upstreamRequest.Content = new StreamContent(memory);
        }

        foreach (var header in request.Headers)
        {
            if (string.Equals(header.Key, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!upstreamRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
            {
                upstreamRequest.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return upstreamRequest;
    }

    private static void CopyAuthHeaders(HttpRequest request, HttpRequestMessage upstreamRequest)
    {
        if (request.Headers.TryGetValue("Authorization", out var authorizationValues))
        {
            upstreamRequest.Headers.TryAddWithoutValidation("Authorization", authorizationValues.ToArray());
        }

        if (request.Headers.TryGetValue("Cookie", out var cookieValues))
        {
            upstreamRequest.Headers.TryAddWithoutValidation("Cookie", cookieValues.ToArray());
        }
    }

    private static void CopyResponseHeaders(HttpResponseMessage upstreamResponse, HttpResponse response)
    {
        foreach (var header in upstreamResponse.Headers)
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            response.Headers[header.Key] = header.Value.ToArray();
        }

        response.Headers.Remove("transfer-encoding");
    }
}

internal sealed class LegacyMeResponse
{
    public string? Id { get; init; }

    public List<LegacyPlanResponse>? Plans { get; init; }
}

internal sealed class LegacyPlanResponse
{
    public string? Id { get; init; }

    public string? Kind { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}