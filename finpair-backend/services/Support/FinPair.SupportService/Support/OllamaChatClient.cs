using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FinPair.SupportService.Support;

public interface ISupportChatClient
{
    Task<SupportChatResponse> AskAsync(string message, CancellationToken cancellationToken);
}

public sealed class OllamaChatClient(HttpClient httpClient, IOptions<SupportAiOptions> options) : ISupportChatClient
{
    private const string SystemPrompt = """
Ты — помощник раздела поддержки FinPair.
FinPair — приложение для пар, которое помогает двум партнёрам вести общий финансовый учёт: смотреть финансовую нагрузку, добавлять доходы и расходы, анализировать траты, создавать общие цели и подключать партнёра.

Отвечай на том языке, на котором пишет пользователь: русский или татарский.

## Что ты знаешь о FinPair

- В приложении есть разделы: «Финансовая нагрузка», «Транзакции», «Аналитика», «Цели», «Настройки», «Профиль» и «Помощь».
- В «Транзакциях» пользователь добавляет доходы и расходы: тип операции, категорию, сумму и данные операции.
- В «Аналитике» отображаются графики и сводки по финансовым данным. Если операций нет, графики могут быть пустыми.
- В «Целях» можно создать финансовую цель, указать сумму и срок, а затем отслеживать прогресс.
- В «Настройках» можно создать пару, получить код приглашения, обновить его и подключить партнёра.
- Данные пары видны партнёрам в рамках общего пространства FinPair.
- Если пользователь не вошёл в аккаунт, ему нужно зарегистрироваться или авторизоваться.

## Правила ответа

- Общайся тепло, кратко и по делу.
- Отвечай только на вопросы о FinPair, его разделах и финансовом учёте внутри приложения.
- Не давай инвестиционных, юридических, налоговых или банковских рекомендаций.
- Не придумывай функции, которых нет в описании выше.
- Если вопрос не относится к FinPair, вежливо скажи, что можешь помочь только по работе приложения.
- Если не знаешь точный ответ, предложи обратиться в поддержку через контакты в разделе «Помощь».

## Формат ответа

Ты ВСЕГДА должен возвращать ответ СТРОГО в формате валидного JSON (без markdown, без ```json, без пояснений вне JSON), со следующей структурой:

{
  "outputText": "текст ответа пользователю"
}

Никогда не пиши ничего вне JSON. Поле outputText — единственное поле, которое увидит пользователь.
""";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SupportAiOptions _options = options.Value;

    public async Task<SupportChatResponse> AskAsync(string message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatUri());

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        var payload = new OllamaChatRequest(
            _options.Model,
            [
                new OllamaMessage("system", SystemPrompt),
                new OllamaMessage("user", message)
            ],
            Stream: false,
            Format: "json");

        request.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOptions),
            Encoding.UTF8,
            "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Ollama request failed with status {(int)response.StatusCode}: {TrimForLog(responseBody)}");
        }

        var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaChatResponse>(
            JsonOptions,
            cancellationToken);

        var content = ollamaResponse?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("AI response is empty.");
        }

        return ParseResponse(content);
    }

    private Uri BuildChatUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://ollama.com"
            : _options.BaseUrl.TrimEnd('/');

        if (baseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = baseUrl[..^4];
        }

        return new Uri($"{baseUrl}/api/chat");
    }

    private static SupportChatResponse ParseResponse(string content)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<SupportChatResponse>(content, JsonOptions);
            if (!string.IsNullOrWhiteSpace(parsed?.OutputText))
            {
                return new SupportChatResponse(parsed.OutputText.Trim());
            }
        }
        catch (JsonException)
        {
            // The endpoint still returns a clean user-facing fallback if the model breaks JSON mode.
        }

        return new SupportChatResponse(
            "Извините, сейчас не получилось корректно подготовить ответ. Пожалуйста, уточните вопрос по телефону или попробуйте ещё раз.");
    }

    private static string TrimForLog(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= 500 ? trimmed : trimmed[..500];
    }

    private sealed record OllamaChatRequest(
        string Model,
        IReadOnlyList<OllamaMessage> Messages,
        bool Stream,
        string Format);

    private sealed record OllamaMessage(string Role, string Content);

    private sealed record OllamaChatResponse(OllamaMessage? Message);
}
