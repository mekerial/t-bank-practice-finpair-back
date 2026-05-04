using FinPair.Infrastructure;
using Npgsql;

namespace FinPair.AnalyticsService.Analytics;

public sealed class AnalyticsRepository(NpgsqlDataSource dataSource, PostgresConnectionString postgres)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        FinPairSchema.EnsureCoreSchemaAsync(postgres, cancellationToken);

    public async Task<SummaryResult?> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return null;
        }

        var (income, expenses) = await GetIncomeAndExpensesAsync(householdId.Value, cancellationToken);
        return new SummaryResult(
            income,
            expenses,
            income - expenses,
            income <= 0 ? 0 : Math.Round(expenses / income * 100, 2));
    }

    public async Task<IReadOnlyList<CategoryAmount>?> GetCategoriesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(c.name, 'other') AS category, sum(t.amount) AS amount
            FROM transactions t
            LEFT JOIN categories c ON c.id = t.category_id
            WHERE t.household_id = @household_id
              AND t.type = 'expense'
            GROUP BY COALESCE(c.name, 'other')
            ORDER BY amount DESC;
            """;
        command.Parameters.AddWithValue("household_id", householdId.Value);

        var categories = new List<CategoryAmount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new CategoryAmount(reader.GetString(0), reader.GetDecimal(1)));
        }

        return categories;
    }

    public async Task<IReadOnlyList<MonthlyDynamicsPoint>?> GetDynamicsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT to_char(date_trunc('month', t.date), 'YYYY-MM') AS month,
                   t.user_id,
                   sum(t.amount) AS amount
            FROM transactions t
            WHERE t.household_id = @household_id
              AND t.type = 'expense'
            GROUP BY date_trunc('month', t.date), t.user_id
            ORDER BY month;
            """;
        command.Parameters.AddWithValue("household_id", householdId.Value);

        var byMonth = new Dictionary<string, Dictionary<Guid, decimal>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var month = reader.GetString(0);
            if (!byMonth.TryGetValue(month, out var users))
            {
                users = new Dictionary<Guid, decimal>();
                byMonth[month] = users;
            }

            users[reader.GetGuid(1)] = reader.GetDecimal(2);
        }

        return byMonth
            .Select(item => new MonthlyDynamicsPoint(item.Key, item.Value))
            .ToArray();
    }

    public async Task<IReadOnlyList<InsightResult>?> GetInsightsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var summary = await GetSummaryAsync(userId, cancellationToken);
        if (summary is null)
        {
            return null;
        }

        var categories = await GetCategoriesAsync(userId, cancellationToken) ?? [];
        var insights = new List<InsightResult>();

        if (summary.LoadPercent > 80)
        {
            insights.Add(new InsightResult("Expenses are above 80% of income. Review recurring costs first."));
        }
        else if (summary.LoadPercent < 50)
        {
            insights.Add(new InsightResult("Financial load is below 50%. This is a good moment to increase savings."));
        }
        else
        {
            insights.Add(new InsightResult("Financial load is moderate. Keep tracking category changes."));
        }

        var topCategory = categories.FirstOrDefault();
        if (topCategory is not null)
        {
            insights.Add(new InsightResult($"Top expense category is {topCategory.Category}: {topCategory.Amount}."));
        }

        return insights;
    }

    public async Task<OverviewResult?> GetOverviewAsync(Guid userId, CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return null;
        }

        var summary = await GetSummaryAsync(userId, cancellationToken);
        var categories = await GetCategoriesAsync(userId, cancellationToken) ?? [];
        var partnerComparison = await GetPartnerComparisonAsync(householdId.Value, cancellationToken);
        var expenseTrend = await GetExpenseTrendAsync(householdId.Value, cancellationToken);
        var averageExpenses = expenseTrend.Count == 0 ? 0 : Math.Round(expenseTrend.Average(item => item.Amount), 2);

        return new OverviewResult(
            averageExpenses,
            categories.FirstOrDefault(),
            summary is null || summary.Income <= 0 ? 0 : Math.Round((summary.Income - summary.Expenses) / summary.Income * 100, 2),
            summary?.LoadPercent ?? 0,
            partnerComparison,
            expenseTrend,
            categories);
    }

    private async Task<IReadOnlyList<UserAmount>> GetPartnerComparisonAsync(
        Guid householdId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.id, COALESCE(sum(t.amount), 0) AS amount
            FROM users u
            LEFT JOIN transactions t ON t.user_id = u.id
                AND t.household_id = @household_id
                AND t.type = 'expense'
            WHERE u.household_id = @household_id
            GROUP BY u.id, u.created_at
            ORDER BY u.created_at, u.id;
            """;
        command.Parameters.AddWithValue("household_id", householdId);

        var result = new List<UserAmount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new UserAmount(reader.GetGuid(0), reader.GetDecimal(1)));
        }

        return result;
    }

    private async Task<IReadOnlyList<DateAmount>> GetExpenseTrendAsync(
        Guid householdId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT date, sum(amount) AS amount
            FROM transactions
            WHERE household_id = @household_id
              AND type = 'expense'
            GROUP BY date
            ORDER BY date;
            """;
        command.Parameters.AddWithValue("household_id", householdId);

        var result = new List<DateAmount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new DateAmount(reader.GetFieldValue<DateOnly>(0), reader.GetDecimal(1)));
        }

        return result;
    }

    private async Task<(decimal Income, decimal Expenses)> GetIncomeAndExpensesAsync(
        Guid householdId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                COALESCE(sum(amount) FILTER (WHERE type = 'income'), 0),
                COALESCE(sum(amount) FILTER (WHERE type = 'expense'), 0),
                COALESCE((SELECT sum(income) FROM users WHERE household_id = @household_id), 0)
            FROM transactions
            WHERE household_id = @household_id;
            """;
        command.Parameters.AddWithValue("household_id", householdId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var income = reader.GetDecimal(0);
        var expenses = reader.GetDecimal(1);
        var profileIncome = reader.GetDecimal(2);

        return (income == 0 ? profileIncome : income, expenses);
    }

    private async Task<Guid?> GetHouseholdIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT household_id
            FROM users
            WHERE id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid householdId ? householdId : null;
    }
}
