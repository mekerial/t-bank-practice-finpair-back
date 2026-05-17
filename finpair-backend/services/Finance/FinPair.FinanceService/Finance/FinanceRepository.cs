using System.Text;
using System.Text.Json;
using FinPair.Contracts;
using FinPair.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace FinPair.FinanceService.Finance;

public sealed class FinanceRepository(NpgsqlDataSource dataSource, PostgresConnectionString postgres)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        FinPairSchema.EnsureCoreSchemaAsync(postgres, cancellationToken);

    public async Task<UserProfileResult?> GetUserProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, email, name, income
            FROM users
            WHERE id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadUserProfile(reader)
            : null;
    }

    public async Task<FinanceMutationResult<UserProfileResult>> UpdateUserProfileAsync(
        Guid userId,
        decimal? income,
        string? name,
        CancellationToken cancellationToken)
    {
        var sets = new List<string>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (income is not null)
        {
            sets.Add("income = @income");
            command.Parameters.AddWithValue("income", income.Value);
        }

        if (name is not null)
        {
            sets.Add("name = @name");
            command.Parameters.AddWithValue("name", name.Trim());
        }

        if (sets.Count == 0)
        {
            return new FinanceMutationResult<UserProfileResult>(
                FinanceMutationStatus.Success,
                await GetUserProfileAsync(userId, cancellationToken));
        }

        sets.Add("updated_at = now()");
        command.CommandText = $"""
            UPDATE users
            SET {string.Join(", ", sets)}
            WHERE id = @user_id
            RETURNING id, email, name, income;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FinanceMutationResult<UserProfileResult>(FinanceMutationStatus.Success, ReadUserProfile(reader))
            : new FinanceMutationResult<UserProfileResult>(FinanceMutationStatus.UserNotFound, null);
    }

    public async Task<FinanceProfileResult?> GetFinanceProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.income, h.currency, h.notifications::text
            FROM users u
            JOIN households h ON h.id = u.household_id
            WHERE u.id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new FinanceProfileResult(
                reader.GetDecimal(0),
                reader.GetString(1),
                DeserializeNotifications(reader.GetString(2)))
            : null;
    }

    public async Task<FinanceProfileResult?> UpdateFinanceProfileAsync(
        Guid userId,
        decimal? income,
        string? currency,
        IReadOnlyDictionary<string, bool>? notifications,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var current = await GetHouseholdContextAsync(connection, transaction, userId, cancellationToken);
        if (current is null)
        {
            return null;
        }

        if (income is not null)
        {
            await using var updateUserCommand = connection.CreateCommand();
            updateUserCommand.Transaction = transaction;
            updateUserCommand.CommandText = """
                UPDATE users
                SET income = @income,
                    updated_at = now()
                WHERE id = @user_id;
                """;
            updateUserCommand.Parameters.AddWithValue("income", income.Value);
            updateUserCommand.Parameters.AddWithValue("user_id", userId);
            await updateUserCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var nextCurrency = string.IsNullOrWhiteSpace(currency) ? current.Currency : currency.Trim().ToUpperInvariant();
        var nextNotifications = notifications ?? current.Notifications;

        await using var updateHouseholdCommand = connection.CreateCommand();
        updateHouseholdCommand.Transaction = transaction;
        updateHouseholdCommand.CommandText = """
            UPDATE households
            SET currency = @currency,
                notifications = CAST(@notifications AS jsonb),
                updated_at = now()
            WHERE id = @household_id;
            """;
        updateHouseholdCommand.Parameters.AddWithValue("household_id", current.Id);
        updateHouseholdCommand.Parameters.AddWithValue("currency", nextCurrency);
        updateHouseholdCommand.Parameters.AddWithValue("notifications", JsonSerializer.Serialize(nextNotifications));
        await updateHouseholdCommand.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return await GetFinanceProfileAsync(userId, cancellationToken);
    }

    public async Task<SettingsResult?> GetSettingsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        return household is null
            ? null
            : new SettingsResult(household.Currency, household.Notifications);
    }

    public async Task<SettingsResult?> UpdateSettingsAsync(
        Guid userId,
        string? currency,
        IReadOnlyDictionary<string, bool>? notifications,
        CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        if (household is null)
        {
            return null;
        }

        var nextCurrency = string.IsNullOrWhiteSpace(currency) ? household.Currency : currency.Trim().ToUpperInvariant();
        var nextNotifications = notifications ?? household.Notifications;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE households
            SET currency = @currency,
                notifications = CAST(@notifications AS jsonb),
                updated_at = now()
            WHERE id = @household_id
            RETURNING currency, notifications::text;
            """;
        command.Parameters.AddWithValue("household_id", household.Id);
        command.Parameters.AddWithValue("currency", nextCurrency);
        command.Parameters.AddWithValue("notifications", JsonSerializer.Serialize(nextNotifications));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SettingsResult(reader.GetString(0), DeserializeNotifications(reader.GetString(1)))
            : null;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(string? type, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(type)
            ? """
                SELECT id, name, type
                FROM categories
                ORDER BY type, name;
                """
            : """
                SELECT id, name, type
                FROM categories
                WHERE type = @type
                ORDER BY name;
                """;

        if (!string.IsNullOrWhiteSpace(type))
        {
            command.Parameters.AddWithValue("type", NormalizeType(type));
        }

        var categories = new List<CategoryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            categories.Add(new CategoryDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
        }

        return categories;
    }

    public async Task<FinanceMutationResult<CategoryDto>> CreateCategoryAsync(
        string name,
        string type,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO categories (id, name, type, created_at, updated_at)
            VALUES (@id, @name, @type, now(), now())
            RETURNING id, name, type;
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("name", name.Trim());
        command.Parameters.AddWithValue("type", NormalizeType(type));

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new FinanceMutationResult<CategoryDto>(
                    FinanceMutationStatus.Success,
                    new CategoryDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)))
                : new FinanceMutationResult<CategoryDto>(FinanceMutationStatus.NotFound, null);
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            return new FinanceMutationResult<CategoryDto>(FinanceMutationStatus.Conflict, null);
        }
    }

    public async Task<CategoryDto?> UpdateCategoryAsync(
        Guid categoryId,
        string? name,
        string? type,
        CancellationToken cancellationToken)
    {
        var sets = new List<string>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(name))
        {
            sets.Add("name = @name");
            command.Parameters.AddWithValue("name", name.Trim());
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            sets.Add("type = @type");
            command.Parameters.AddWithValue("type", NormalizeType(type));
        }

        if (sets.Count == 0)
        {
            await using var readCommand = connection.CreateCommand();
            readCommand.CommandText = "SELECT id, name, type FROM categories WHERE id = @id LIMIT 1;";
            readCommand.Parameters.AddWithValue("id", categoryId);
            await using var reader = await readCommand.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new CategoryDto(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))
                : null;
        }

        sets.Add("updated_at = now()");
        command.CommandText = $"""
            UPDATE categories
            SET {string.Join(", ", sets)}
            WHERE id = @id
            RETURNING id, name, type;
            """;
        command.Parameters.AddWithValue("id", categoryId);

        await using var updatedReader = await command.ExecuteReaderAsync(cancellationToken);
        return await updatedReader.ReadAsync(cancellationToken)
            ? new CategoryDto(updatedReader.GetGuid(0), updatedReader.GetString(1), updatedReader.GetString(2))
            : null;
    }

    public async Task<FinanceMutationStatus> DeleteCategoryAsync(Guid categoryId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM categories WHERE id = @id;";
        command.Parameters.AddWithValue("id", categoryId);

        try
        {
            var affected = await command.ExecuteNonQueryAsync(cancellationToken);
            return affected > 0 ? FinanceMutationStatus.Success : FinanceMutationStatus.NotFound;
        }
        catch (PostgresException exception) when (exception.SqlState == "23503")
        {
            return FinanceMutationStatus.Conflict;
        }
    }

    public async Task<FinanceMutationResult<TransactionDto>> CreateTransactionAsync(
        Guid userId,
        CreateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        if (household is null)
        {
            return new FinanceMutationResult<TransactionDto>(FinanceMutationStatus.HouseholdNotFound, null);
        }

        var type = NormalizeType(request.Type!);
        var categoryId = await ResolveCategoryIdAsync(request.CategoryId, request.Category, type, cancellationToken);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO transactions (id, household_id, user_id, category_id, type, amount, description, date, created_at, updated_at)
            VALUES (@id, @household_id, @user_id, @category_id, @type, @amount, @description, @date, now(), now())
            RETURNING id;
            """;
        var transactionId = Guid.NewGuid();
        command.Parameters.AddWithValue("id", transactionId);
        command.Parameters.AddWithValue("household_id", household.Id);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.Add("category_id", NpgsqlDbType.Uuid).Value = categoryId is null ? DBNull.Value : categoryId.Value;
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("amount", request.Amount!.Value);
        command.Parameters.AddWithValue("description", GetDescription(request.Description, request.Title));
        command.Parameters.AddWithValue("date", request.Date!.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);

        var created = await GetTransactionAsync(userId, transactionId, cancellationToken);
        return new FinanceMutationResult<TransactionDto>(FinanceMutationStatus.Success, created);
    }

    public async Task<PagedItemsResponse<TransactionDto>?> GetTransactionsAsync(
        Guid userId,
        TransactionQuery query,
        CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        if (household is null)
        {
            return null;
        }

        var conditions = new List<string> { "t.household_id = @household_id" };
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            conditions.Add("t.type = @type");
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            conditions.Add(Guid.TryParse(query.Category, out _)
                ? "t.category_id = @category_id"
                : "lower(c.name) = lower(@category)");
        }

        if (query.UserId is not null)
        {
            conditions.Add("t.user_id = @filter_user_id");
        }

        if (query.From is not null)
        {
            conditions.Add("t.date >= @date_from");
        }

        if (query.To is not null)
        {
            conditions.Add("t.date <= @date_to");
        }

        var whereSql = string.Join(" AND ", conditions);

        await using var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"""
            SELECT count(*)
            FROM transactions t
            LEFT JOIN categories c ON c.id = t.category_id
            WHERE {whereSql};
            """;
        AddTransactionQueryParameters(countCommand, household.Id, query);
        var totalItems = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));

        var orderColumn = query.SortBy switch
        {
            "amount" => "t.amount",
            "created_at" => "t.created_at",
            _ => "t.date"
        };
        var orderDirection = string.Equals(query.SortOrder, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var offset = (query.Page - 1) * query.PageSize;

        await using var listCommand = connection.CreateCommand();
        listCommand.CommandText = $"""
            SELECT t.id, t.type, t.amount, h.currency, t.category_id, c.name, t.description, t.user_id, t.date
            FROM transactions t
            JOIN households h ON h.id = t.household_id
            LEFT JOIN categories c ON c.id = t.category_id
            WHERE {whereSql}
            ORDER BY {orderColumn} {orderDirection}, t.created_at DESC
            LIMIT @limit OFFSET @offset;
            """;
        AddTransactionQueryParameters(listCommand, household.Id, query);
        listCommand.Parameters.AddWithValue("limit", query.PageSize);
        listCommand.Parameters.AddWithValue("offset", offset);

        var items = new List<TransactionDto>();
        await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(ReadTransaction(reader));
        }

        return new PagedItemsResponse<TransactionDto>(
            items,
            Pagination.Create(query.Page, query.PageSize, totalItems));
    }

    public async Task<TransactionDto?> GetTransactionAsync(Guid userId, Guid transactionId, CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        if (household is null)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.id, t.type, t.amount, h.currency, t.category_id, c.name, t.description, t.user_id, t.date
            FROM transactions t
            JOIN households h ON h.id = t.household_id
            LEFT JOIN categories c ON c.id = t.category_id
            WHERE t.id = @transaction_id
              AND t.household_id = @household_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("transaction_id", transactionId);
        command.Parameters.AddWithValue("household_id", household.Id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadTransaction(reader) : null;
    }

    public async Task<FinanceMutationResult<TransactionDto>> UpdateTransactionAsync(
        Guid userId,
        Guid transactionId,
        UpdateTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        if (household is null)
        {
            return new FinanceMutationResult<TransactionDto>(FinanceMutationStatus.HouseholdNotFound, null);
        }

        var sets = new List<string>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var type = string.IsNullOrWhiteSpace(request.Type) ? null : NormalizeType(request.Type);
        if (type is not null)
        {
            sets.Add("type = @type");
            command.Parameters.AddWithValue("type", type);
        }

        if (request.Amount is not null)
        {
            sets.Add("amount = @amount");
            command.Parameters.AddWithValue("amount", request.Amount.Value);
        }

        if (request.Date is not null)
        {
            sets.Add("date = @date");
            command.Parameters.AddWithValue("date", request.Date.Value);
        }

        if (request.Description is not null || request.Title is not null)
        {
            sets.Add("description = @description");
            command.Parameters.AddWithValue("description", GetDescription(request.Description, request.Title));
        }

        if (request.CategoryId is not null || !string.IsNullOrWhiteSpace(request.Category))
        {
            var categoryType = type ?? await GetTransactionTypeAsync(connection, household.Id, transactionId, cancellationToken);
            if (categoryType is null)
            {
                return new FinanceMutationResult<TransactionDto>(FinanceMutationStatus.NotFound, null);
            }

            var categoryId = await ResolveCategoryIdAsync(request.CategoryId, request.Category, categoryType, cancellationToken);
            sets.Add("category_id = @category_id");
            command.Parameters.Add("category_id", NpgsqlDbType.Uuid).Value = categoryId is null ? DBNull.Value : categoryId.Value;
        }

        if (sets.Count == 0)
        {
            return new FinanceMutationResult<TransactionDto>(
                FinanceMutationStatus.Success,
                await GetTransactionAsync(userId, transactionId, cancellationToken));
        }

        sets.Add("updated_at = now()");
        command.CommandText = $"""
            UPDATE transactions
            SET {string.Join(", ", sets)}
            WHERE id = @transaction_id
              AND household_id = @household_id;
            """;
        command.Parameters.AddWithValue("transaction_id", transactionId);
        command.Parameters.AddWithValue("household_id", household.Id);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            return new FinanceMutationResult<TransactionDto>(FinanceMutationStatus.NotFound, null);
        }

        return new FinanceMutationResult<TransactionDto>(
            FinanceMutationStatus.Success,
            await GetTransactionAsync(userId, transactionId, cancellationToken));
    }

    public async Task<FinanceMutationStatus> DeleteTransactionAsync(
        Guid userId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        if (household is null)
        {
            return FinanceMutationStatus.HouseholdNotFound;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM transactions
            WHERE id = @transaction_id
              AND household_id = @household_id;
            """;
        command.Parameters.AddWithValue("transaction_id", transactionId);
        command.Parameters.AddWithValue("household_id", household.Id);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0 ? FinanceMutationStatus.Success : FinanceMutationStatus.NotFound;
    }

    public async Task<DashboardResult?> GetDashboardAsync(Guid userId, CancellationToken cancellationToken)
    {
        var household = await GetHouseholdContextAsync(userId, cancellationToken);
        if (household is null)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var totalsCommand = connection.CreateCommand();
        totalsCommand.CommandText = """
            SELECT
                COALESCE(sum(t.amount) FILTER (WHERE t.type = 'income'), 0),
                COALESCE(sum(t.amount) FILTER (WHERE t.type = 'expense'), 0),
                COALESCE((SELECT sum(income) FROM users WHERE household_id = @household_id), 0)
            FROM transactions t
            WHERE t.household_id = @household_id;
            """;
        totalsCommand.Parameters.AddWithValue("household_id", household.Id);

        decimal totalIncome;
        decimal totalExpense;
        await using (var reader = await totalsCommand.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            totalIncome = reader.GetDecimal(0);
            totalExpense = reader.GetDecimal(1);
            var profileIncome = reader.GetDecimal(2);
            if (totalIncome == 0)
            {
                totalIncome = profileIncome;
            }
        }

        await using var partnersCommand = connection.CreateCommand();
        partnersCommand.CommandText = """
            SELECT
                u.id,
                CASE
                    WHEN COALESCE(sum(t.amount) FILTER (WHERE t.type = 'income'), 0) = 0
                    THEN u.income
                    ELSE COALESCE(sum(t.amount) FILTER (WHERE t.type = 'income'), 0)
                END AS income,
                COALESCE(sum(t.amount) FILTER (WHERE t.type = 'expense'), 0) AS expense
            FROM users u
            LEFT JOIN transactions t ON t.user_id = u.id AND t.household_id = @household_id
            WHERE u.household_id = @household_id
            GROUP BY u.id, u.income
            ORDER BY u.created_at, u.id;
            """;
        partnersCommand.Parameters.AddWithValue("household_id", household.Id);

        var partnerTotals = new List<PartnerDashboardTotals>();
        await using (var reader = await partnersCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                partnerTotals.Add(new PartnerDashboardTotals(
                    reader.GetGuid(0),
                    reader.GetDecimal(1),
                    reader.GetDecimal(2)));
            }
        }

        var partnerShares = CalculatePartnerShares(partnerTotals, household.SplitType);
        var partners = partnerTotals
            .Select((partner, index) => new PartnerSummary(
                partner.UserId,
                partner.Income,
                partner.Expense,
                partnerShares[index]))
            .ToArray();

        return new DashboardResult(
            household.Currency,
            totalIncome,
            totalExpense,
            totalIncome - totalExpense,
            totalIncome <= 0 ? 0 : Math.Round(totalExpense / totalIncome * 100, 2),
            household.SplitType,
            partners);
    }

    private static decimal[] CalculatePartnerShares(IReadOnlyList<PartnerDashboardTotals> partners, string splitType)
    {
        if (partners.Count == 0)
        {
            return [];
        }

        if (partners.Count == 1)
        {
            return [100m];
        }

        var normalizedSplitType = splitType.Trim().ToLowerInvariant();
        var weights = normalizedSplitType switch
        {
            "income" or "income_ratio" => partners.Select(partner => Math.Max(0, partner.Income)).ToArray(),
            "custom" => partners.Select(partner => Math.Max(0, partner.Expense)).ToArray(),
            _ => partners.Select(_ => 1m).ToArray()
        };

        if (weights.Sum() <= 0)
        {
            weights = partners.Select(_ => 1m).ToArray();
        }

        return ToRoundedPercentages(weights);
    }

    private static decimal[] ToRoundedPercentages(IReadOnlyList<decimal> weights)
    {
        var total = weights.Sum();
        if (total <= 0)
        {
            return weights.Select(_ => 0m).ToArray();
        }

        var percentages = new decimal[weights.Count];
        var assigned = 0m;
        for (var i = 0; i < weights.Count; i++)
        {
            if (i == weights.Count - 1)
            {
                percentages[i] = Math.Round(100m - assigned, 2);
                continue;
            }

            percentages[i] = Math.Round(weights[i] / total * 100m, 2);
            assigned += percentages[i];
        }

        return percentages;
    }

    private async Task<HouseholdContext?> GetHouseholdContextAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await GetHouseholdContextAsync(connection, null, userId, cancellationToken);
    }

    private static async Task<HouseholdContext?> GetHouseholdContextAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT h.id, h.currency, h.split_type, h.notifications::text
            FROM users u
            JOIN households h ON h.id = u.household_id
            WHERE u.id = @user_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("user_id", userId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new HouseholdContext(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                DeserializeNotifications(reader.GetString(3)))
            : null;
    }

    private async Task<Guid?> ResolveCategoryIdAsync(
        Guid? categoryId,
        string? category,
        string type,
        CancellationToken cancellationToken)
    {
        if (categoryId is not null)
        {
            return categoryId;
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            return null;
        }

        if (Guid.TryParse(category, out var parsedCategoryId))
        {
            return parsedCategoryId;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var findCommand = connection.CreateCommand();
        findCommand.CommandText = """
            SELECT id
            FROM categories
            WHERE lower(name) = lower(@name)
              AND type = @type
            LIMIT 1;
            """;
        findCommand.Parameters.AddWithValue("name", category.Trim());
        findCommand.Parameters.AddWithValue("type", NormalizeType(type));

        var existing = await findCommand.ExecuteScalarAsync(cancellationToken);
        if (existing is Guid existingId)
        {
            return existingId;
        }

        await using var insertCommand = connection.CreateCommand();
        insertCommand.CommandText = """
            INSERT INTO categories (id, name, type, created_at, updated_at)
            VALUES (@id, @name, @type, now(), now())
            RETURNING id;
            """;
        insertCommand.Parameters.AddWithValue("id", Guid.NewGuid());
        insertCommand.Parameters.AddWithValue("name", category.Trim());
        insertCommand.Parameters.AddWithValue("type", NormalizeType(type));

        return (Guid?)await insertCommand.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<string?> GetTransactionTypeAsync(
        NpgsqlConnection connection,
        Guid householdId,
        Guid transactionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT type
            FROM transactions
            WHERE id = @transaction_id
              AND household_id = @household_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("transaction_id", transactionId);
        command.Parameters.AddWithValue("household_id", householdId);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static void AddTransactionQueryParameters(
        NpgsqlCommand command,
        Guid householdId,
        TransactionQuery query)
    {
        command.Parameters.AddWithValue("household_id", householdId);

        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            command.Parameters.AddWithValue("type", NormalizeType(query.Type));
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            if (Guid.TryParse(query.Category, out var categoryId))
            {
                command.Parameters.AddWithValue("category_id", categoryId);
            }
            else
            {
                command.Parameters.AddWithValue("category", query.Category.Trim());
            }
        }

        if (query.UserId is not null)
        {
            command.Parameters.AddWithValue("filter_user_id", query.UserId.Value);
        }

        if (query.From is not null)
        {
            command.Parameters.AddWithValue("date_from", query.From.Value);
        }

        if (query.To is not null)
        {
            command.Parameters.AddWithValue("date_to", query.To.Value);
        }
    }

    private static TransactionDto ReadTransaction(NpgsqlDataReader reader)
    {
        var description = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
        return new TransactionDto(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetDecimal(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            description,
            description,
            reader.GetGuid(7),
            reader.GetFieldValue<DateOnly>(8));
    }

    private static string GetDescription(string? description, string? title)
    {
        return (description ?? title ?? string.Empty).Trim();
    }

    private static UserProfileResult ReadUserProfile(NpgsqlDataReader reader) =>
        new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            reader.GetDecimal(3));

    private static string NormalizeType(string type) => type.Trim().ToLowerInvariant();

    private static IReadOnlyDictionary<string, bool> DeserializeNotifications(string json)
    {
        return JsonSerializer.Deserialize<Dictionary<string, bool>>(json) ??
               new Dictionary<string, bool>();
    }

    private sealed record PartnerDashboardTotals(Guid UserId, decimal Income, decimal Expense);
}
