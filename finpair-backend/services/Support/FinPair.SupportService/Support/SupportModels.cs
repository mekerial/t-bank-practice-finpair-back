namespace FinPair.SupportService.Support;

public sealed record FaqItem(string Id, string Question, string Answer);

public sealed record ContactsResult(string Email, string Telegram);

public sealed record CreateSupportMessageRequest(string? Subject, string? Message);

public sealed record SupportTicketResult(Guid TicketId, string Status);
