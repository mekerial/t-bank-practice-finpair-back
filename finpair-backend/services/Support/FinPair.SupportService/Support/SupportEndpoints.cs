using FinPair.Contracts;
using FinPair.Contracts.Validation;
using FinPair.Infrastructure.Auth;

namespace FinPair.SupportService.Support;

public static class SupportEndpoints
{
    private static readonly FaqItem[] FaqItems =
    [
        new("faq_001", "How do I invite a partner?", "Create a couple and share the invite code from couple settings."),
        new("faq_002", "How is financial load calculated?", "Financial load is expenses divided by income, multiplied by 100."),
        new("faq_003", "Can goals be shared?", "Yes. Create a shared goal and both partners can add contributions.")
    ];

    public static RouteGroupBuilder MapSupportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/support").WithTags("Support");

        group.MapGet("/faq", GetFaq)
            .WithName("GetFaq")
            .Produces<ApiResponse<ItemsResponse<FaqItem>>>();

        group.MapGet("/contacts", GetContacts)
            .WithName("GetSupportContacts")
            .Produces<ApiResponse<ContactsResult>>();

        group.MapPost("/chat", CreateChatResponseAsync)
            .WithName("CreateSupportChatResponse")
            .Produces<ApiResponse<SupportChatResponse>>();

        group.MapPost("/message", CreateMessageAsync)
            .WithName("CreateSupportMessage")
            .Produces<ApiResponse<SupportTicketResult>>(StatusCodes.Status201Created);

        group.MapPost("/messages", CreateMessageAsync)
            .WithName("CreateSupportMessages")
            .Produces<ApiResponse<SupportTicketResult>>(StatusCodes.Status201Created);

        return group;
    }

    private static IResult GetFaq() =>
        Results.Json(ApiResponse<ItemsResponse<FaqItem>>.Ok(new ItemsResponse<FaqItem>(FaqItems)));

    private static IResult GetContacts() =>
        Results.Json(ApiResponse<ContactsResult>.Ok(new ContactsResult("support@finpair.app", "@finpair_support")));

    private static async Task<IResult> CreateChatResponseAsync(
        SupportChatRequest request,
        ISupportChatClient chatClient,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateChat(request);
        if (validationErrors.Count > 0)
        {
            return Results.Json(
                ApiResponse<object>.Fail(
                    "VALIDATION_ERROR",
                    "Invalid request.",
                    validationErrors),
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var answer = await chatClient.AskAsync(request.Message!.Trim(), cancellationToken);
            return Results.Json(ApiResponse<SupportChatResponse>.Ok(answer));
        }
        catch (Exception exception)
        {
            loggerFactory.CreateLogger("FinPair.SupportChat")
                .LogWarning(exception, "Support chat AI request failed.");

            return Results.Json(
                ApiResponse<SupportChatResponse>.Ok(
                    new SupportChatResponse(
                        "Извините, сейчас чат-бот недоступен. Пожалуйста, уточните вопрос по телефону или попробуйте ещё раз позже.")));
        }
    }

    private static async Task<IResult> CreateMessageAsync(
        CreateSupportMessageRequest request,
        HttpContext httpContext,
        SupportRepository repository,
        CancellationToken cancellationToken)
    {
        var validationErrors = ValidateMessage(request);
        if (validationErrors.Count > 0)
        {
            return Results.Json(
                ApiResponse<object>.Fail(
                    "VALIDATION_ERROR",
                    "Invalid request.",
                    validationErrors),
                statusCode: StatusCodes.Status400BadRequest);
        }

        var userId = httpContext.TryGetUserId(out var parsedUserId) ? parsedUserId : (Guid?)null;
        var ticket = await repository.CreateMessageAsync(
            userId,
            request.Subject?.Trim() ?? string.Empty,
            request.Message!.Trim(),
            cancellationToken);

        return Results.Json(ApiResponse<SupportTicketResult>.Ok(ticket), statusCode: StatusCodes.Status201Created);
    }

    private static IReadOnlyDictionary<string, string[]> ValidateChat(SupportChatRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.RequiredText(errors, "message", request.Message, maxLength: 2000);
        return errors.ToDictionary();
    }

    private static IReadOnlyDictionary<string, string[]> ValidateMessage(CreateSupportMessageRequest request)
    {
        var errors = new ValidationErrors();
        DomainValidation.OptionalText(errors, "subject", request.Subject, maxLength: 120);
        DomainValidation.RequiredText(errors, "message", request.Message, maxLength: 2000);
        return errors.ToDictionary();
    }
}
