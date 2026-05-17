namespace FinPair.SupportService.Tests;

using System.Reflection;
using FinPair.SupportService.Support;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

public class SupportEndpointTests
{
    [Fact]
    public void MapSupportEndpoints_RegistersFaqContactsChatAndMessageRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<SupportRepository>(_ => null!);
        builder.Services.AddSingleton<ISupportChatClient, StubSupportChatClient>();
        var app = builder.Build();

        app.MapSupportEndpoints();

        var routes = GetRouteTexts(app);

        Assert.Contains("/api/v1/support/faq", routes);
        Assert.Contains("/api/v1/support/contacts", routes);
        Assert.Contains("/api/v1/support/chat", routes);
        Assert.Contains("/api/v1/support/message", routes);
        Assert.Contains("/api/v1/support/messages", routes);
    }

    [Fact]
    public void ValidateChat_RequiresMessageBody()
    {
        var request = new SupportChatRequest(" ");

        var errors = InvokeValidateChat(request);

        Assert.Contains("message", errors.Keys);
    }

    [Fact]
    public void ValidateChat_AcceptsMinimalValidMessage()
    {
        var request = new SupportChatRequest("Как добавить транзакцию?");

        var errors = InvokeValidateChat(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateMessage_RequiresMessageBody()
    {
        var request = new CreateSupportMessageRequest("Question", " ");

        var errors = InvokeValidateMessage(request);

        Assert.Contains("message", errors.Keys);
    }

    [Fact]
    public void ValidateMessage_RejectsSubjectOverLimit()
    {
        var request = new CreateSupportMessageRequest(new string('a', 121), "Help me");

        var errors = InvokeValidateMessage(request);

        Assert.Contains("subject", errors.Keys);
    }

    [Fact]
    public void ValidateMessage_AcceptsMinimalValidMessage()
    {
        var request = new CreateSupportMessageRequest(null, "How do I change split type?");

        var errors = InvokeValidateMessage(request);

        Assert.Empty(errors);
    }

    private static IReadOnlyDictionary<string, string[]> InvokeValidateMessage(CreateSupportMessageRequest request)
    {
        var method = typeof(SupportEndpoints).GetMethod("ValidateMessage", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(SupportEndpoints), "ValidateMessage");

        return (IReadOnlyDictionary<string, string[]>)method.Invoke(null, [request])!;
    }

    private static IReadOnlyDictionary<string, string[]> InvokeValidateChat(SupportChatRequest request)
    {
        var method = typeof(SupportEndpoints).GetMethod("ValidateChat", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(SupportEndpoints), "ValidateChat");

        return (IReadOnlyDictionary<string, string[]>)method.Invoke(null, [request])!;
    }

    private static IReadOnlySet<string?> GetRouteTexts(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .ToHashSet();

    private sealed class StubSupportChatClient : ISupportChatClient
    {
        public Task<SupportChatResponse> AskAsync(string message, CancellationToken cancellationToken) =>
            Task.FromResult(new SupportChatResponse("Ответ"));
    }
}
