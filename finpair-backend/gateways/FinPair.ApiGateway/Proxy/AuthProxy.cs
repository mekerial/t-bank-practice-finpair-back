using System.Net.Http.Headers;

namespace FinPair.ApiGateway.Proxy;

public static class AuthProxy
{
    private static readonly string[] HopByHopHeaders =
    [
        "Connection",
        "Host",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    ];

    public static async Task ForwardAsync(
        HttpContext context,
        string? path,
        IConfiguration configuration,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Services:Auth:BaseUrl"] ?? "http://localhost:5222";
        var targetUri = BuildTargetUri(baseUrl, path, context.Request.QueryString);

        using var proxyRequest = CreateProxyRequest(context, targetUri);
        using var proxyResponse = await httpClient.SendAsync(
            proxyRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        context.Response.StatusCode = (int)proxyResponse.StatusCode;

        CopyHeaders(proxyResponse.Headers, context.Response.Headers);
        CopyHeaders(proxyResponse.Content.Headers, context.Response.Headers);

        foreach (var header in HopByHopHeaders)
        {
            context.Response.Headers.Remove(header);
        }

        await proxyResponse.Content.CopyToAsync(context.Response.Body, cancellationToken);
    }

    private static Uri BuildTargetUri(string baseUrl, string? path, QueryString queryString)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/') + "/";
        var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.TrimStart('/');
        return new Uri(new Uri(normalizedBaseUrl), $"api/v1/auth/{normalizedPath}{queryString}");
    }

    private static HttpRequestMessage CreateProxyRequest(HttpContext context, Uri targetUri)
    {
        var request = context.Request;
        var proxyRequest = new HttpRequestMessage(new HttpMethod(request.Method), targetUri);

        if (request.ContentLength > 0 || request.Headers.ContainsKey("Transfer-Encoding"))
        {
            proxyRequest.Content = new StreamContent(request.Body);
        }

        foreach (var header in request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!proxyRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) &&
                proxyRequest.Content is not null)
            {
                proxyRequest.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return proxyRequest;
    }

    private static void CopyHeaders(HttpHeaders sourceHeaders, IHeaderDictionary targetHeaders)
    {
        foreach (var header in sourceHeaders)
        {
            targetHeaders[header.Key] = header.Value.ToArray();
        }
    }
}
