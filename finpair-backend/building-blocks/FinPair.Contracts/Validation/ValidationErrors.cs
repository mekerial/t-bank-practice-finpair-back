namespace FinPair.Contracts.Validation;

public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public bool HasErrors => _errors.Count > 0;

    public int Count => _errors.Count;

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            messages = [];
            _errors[field] = messages;
        }

        messages.Add(message);
    }

    public void AddIf(bool condition, string field, string message)
    {
        if (condition)
        {
            Add(field, message);
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() =>
        _errors.ToDictionary(item => item.Key, item => item.Value.ToArray(), StringComparer.Ordinal);
}
