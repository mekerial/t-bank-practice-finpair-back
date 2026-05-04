using FinPair.Infrastructure;
using Npgsql;
using NpgsqlTypes;

namespace FinPair.GoalService.Goals;

public sealed class GoalRepository(NpgsqlDataSource dataSource, PostgresConnectionString postgres)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default) =>
        FinPairSchema.EnsureCoreSchemaAsync(postgres, cancellationToken);

    public async Task<IReadOnlyList<GoalDto>?> GetGoalsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, household_id, title, target_amount, current_amount, monthly_contribution, deadline, is_shared
            FROM goals
            WHERE household_id = @household_id
            ORDER BY created_at DESC, id;
            """;
        command.Parameters.AddWithValue("household_id", householdId.Value);

        var goals = new List<GoalDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            goals.Add(ToDto(ReadGoal(reader)));
        }

        return goals;
    }

    public async Task<GoalDto?> GetGoalAsync(Guid userId, Guid goalId, CancellationToken cancellationToken)
    {
        var goal = await GetGoalRecordAsync(userId, goalId, cancellationToken);
        return goal is null ? null : ToDto(goal);
    }

    public async Task<GoalMutationResult<GoalDto>> CreateGoalAsync(
        Guid userId,
        CreateGoalRequest request,
        CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return new GoalMutationResult<GoalDto>(GoalMutationStatus.HouseholdNotFound, null);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO goals (
                id,
                household_id,
                title,
                target_amount,
                current_amount,
                monthly_contribution,
                deadline,
                is_shared,
                created_at,
                updated_at)
            VALUES (
                @id,
                @household_id,
                @title,
                @target_amount,
                @current_amount,
                @monthly_contribution,
                @deadline,
                @is_shared,
                now(),
                now())
            RETURNING id, household_id, title, target_amount, current_amount, monthly_contribution, deadline, is_shared;
            """;
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("household_id", householdId.Value);
        command.Parameters.AddWithValue("title", request.Title!.Trim());
        command.Parameters.AddWithValue("target_amount", request.TargetAmount!.Value);
        command.Parameters.AddWithValue("current_amount", request.CurrentAmount ?? 0);
        command.Parameters.AddWithValue("monthly_contribution", request.MonthlyContribution ?? 0);
        command.Parameters.Add("deadline", NpgsqlDbType.Date).Value = request.Deadline is null ? DBNull.Value : request.Deadline.Value;
        command.Parameters.AddWithValue("is_shared", request.IsShared ?? true);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new GoalMutationResult<GoalDto>(GoalMutationStatus.Success, ToDto(ReadGoal(reader)))
            : new GoalMutationResult<GoalDto>(GoalMutationStatus.GoalNotFound, null);
    }

    public async Task<GoalMutationResult<GoalDto>> UpdateGoalAsync(
        Guid userId,
        Guid goalId,
        UpdateGoalRequest request,
        CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return new GoalMutationResult<GoalDto>(GoalMutationStatus.HouseholdNotFound, null);
        }

        var sets = new List<string>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        if (!string.IsNullOrWhiteSpace(request.Title))
        {
            sets.Add("title = @title");
            command.Parameters.AddWithValue("title", request.Title.Trim());
        }

        if (request.TargetAmount is not null)
        {
            sets.Add("target_amount = @target_amount");
            command.Parameters.AddWithValue("target_amount", request.TargetAmount.Value);
        }

        if (request.CurrentAmount is not null)
        {
            sets.Add("current_amount = @current_amount");
            command.Parameters.AddWithValue("current_amount", request.CurrentAmount.Value);
        }

        if (request.MonthlyContribution is not null)
        {
            sets.Add("monthly_contribution = @monthly_contribution");
            command.Parameters.AddWithValue("monthly_contribution", request.MonthlyContribution.Value);
        }

        if (request.Deadline is not null)
        {
            sets.Add("deadline = @deadline");
            command.Parameters.AddWithValue("deadline", request.Deadline.Value);
        }

        if (request.IsShared is not null)
        {
            sets.Add("is_shared = @is_shared");
            command.Parameters.AddWithValue("is_shared", request.IsShared.Value);
        }

        if (sets.Count == 0)
        {
            var current = await GetGoalAsync(userId, goalId, cancellationToken);
            return current is null
                ? new GoalMutationResult<GoalDto>(GoalMutationStatus.GoalNotFound, null)
                : new GoalMutationResult<GoalDto>(GoalMutationStatus.Success, current);
        }

        sets.Add("updated_at = now()");
        command.CommandText = $"""
            UPDATE goals
            SET {string.Join(", ", sets)}
            WHERE id = @goal_id
              AND household_id = @household_id
            RETURNING id, household_id, title, target_amount, current_amount, monthly_contribution, deadline, is_shared;
            """;
        command.Parameters.AddWithValue("goal_id", goalId);
        command.Parameters.AddWithValue("household_id", householdId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new GoalMutationResult<GoalDto>(GoalMutationStatus.Success, ToDto(ReadGoal(reader)))
            : new GoalMutationResult<GoalDto>(GoalMutationStatus.GoalNotFound, null);
    }

    public async Task<GoalMutationStatus> DeleteGoalAsync(Guid userId, Guid goalId, CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return GoalMutationStatus.HouseholdNotFound;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM goals
            WHERE id = @goal_id
              AND household_id = @household_id;
            """;
        command.Parameters.AddWithValue("goal_id", goalId);
        command.Parameters.AddWithValue("household_id", householdId.Value);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0 ? GoalMutationStatus.Success : GoalMutationStatus.GoalNotFound;
    }

    public async Task<GoalMutationResult<GoalContributionResult>> AddContributionAsync(
        Guid userId,
        Guid goalId,
        decimal amount,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return new GoalMutationResult<GoalContributionResult>(GoalMutationStatus.HouseholdNotFound, null);
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var insertCommand = connection.CreateCommand();
        insertCommand.Transaction = transaction;
        insertCommand.CommandText = """
            INSERT INTO goal_contributions (id, goal_id, user_id, amount, date, created_at)
            SELECT @id, g.id, @user_id, @amount, @date, now()
            FROM goals g
            WHERE g.id = @goal_id
              AND g.household_id = @household_id;
            """;
        insertCommand.Parameters.AddWithValue("id", Guid.NewGuid());
        insertCommand.Parameters.AddWithValue("goal_id", goalId);
        insertCommand.Parameters.AddWithValue("household_id", householdId.Value);
        insertCommand.Parameters.AddWithValue("user_id", userId);
        insertCommand.Parameters.AddWithValue("amount", amount);
        insertCommand.Parameters.AddWithValue("date", date);

        var inserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        if (inserted == 0)
        {
            return new GoalMutationResult<GoalContributionResult>(GoalMutationStatus.GoalNotFound, null);
        }

        await using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE goals
            SET current_amount = current_amount + @amount,
                updated_at = now()
            WHERE id = @goal_id
              AND household_id = @household_id
            RETURNING id, household_id, title, target_amount, current_amount, monthly_contribution, deadline, is_shared;
            """;
        updateCommand.Parameters.AddWithValue("goal_id", goalId);
        updateCommand.Parameters.AddWithValue("household_id", householdId.Value);
        updateCommand.Parameters.AddWithValue("amount", amount);

        GoalDto? updatedGoal = null;
        await using (var reader = await updateCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                updatedGoal = ToDto(ReadGoal(reader));
            }
        }

        await transaction.CommitAsync(cancellationToken);

        return updatedGoal is null
            ? new GoalMutationResult<GoalContributionResult>(GoalMutationStatus.GoalNotFound, null)
            : new GoalMutationResult<GoalContributionResult>(
                GoalMutationStatus.Success,
                new GoalContributionResult(updatedGoal.Id, updatedGoal.CurrentAmount, updatedGoal.ProgressPercent));
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

    private async Task<GoalRecord?> GetGoalRecordAsync(
        Guid userId,
        Guid goalId,
        CancellationToken cancellationToken)
    {
        var householdId = await GetHouseholdIdAsync(userId, cancellationToken);
        if (householdId is null)
        {
            return null;
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, household_id, title, target_amount, current_amount, monthly_contribution, deadline, is_shared
            FROM goals
            WHERE id = @goal_id
              AND household_id = @household_id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("goal_id", goalId);
        command.Parameters.AddWithValue("household_id", householdId.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadGoal(reader) : null;
    }

    private static GoalRecord ReadGoal(NpgsqlDataReader reader)
    {
        return new GoalRecord(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetDecimal(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
            reader.GetBoolean(7));
    }

    private static GoalDto ToDto(GoalRecord goal)
    {
        var remainingAmount = Math.Max(0, goal.TargetAmount - goal.CurrentAmount);
        var progressPercent = goal.TargetAmount <= 0
            ? 0
            : Math.Round(Math.Min(goal.CurrentAmount / goal.TargetAmount * 100, 100), 2);

        return new GoalDto(
            goal.Id,
            goal.Title,
            goal.TargetAmount,
            goal.CurrentAmount,
            goal.MonthlyContribution,
            goal.Deadline,
            goal.IsShared,
            progressPercent,
            remainingAmount,
            CalculateForecastDate(remainingAmount, goal.MonthlyContribution));
    }

    private static DateOnly? CalculateForecastDate(decimal remainingAmount, decimal monthlyContribution)
    {
        if (remainingAmount <= 0)
        {
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        if (monthlyContribution <= 0)
        {
            return null;
        }

        var months = (int)Math.Ceiling(remainingAmount / monthlyContribution);
        return DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(months);
    }
}
