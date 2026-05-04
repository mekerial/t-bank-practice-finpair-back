using Npgsql;

namespace FinPair.Infrastructure;

public static class FinPairSchema
{
    public static async Task EnsureCoreSchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS users (
                id uuid PRIMARY KEY,
                email text NOT NULL,
                password_hash text NOT NULL,
                name text NOT NULL DEFAULT '',
                household_id uuid NULL,
                email_verified boolean NOT NULL DEFAULT false,
                income numeric(12,2) NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash text;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS name text NOT NULL DEFAULT '';
            ALTER TABLE users ADD COLUMN IF NOT EXISTS household_id uuid NULL;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS email_verified boolean NOT NULL DEFAULT false;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS income numeric(12,2) NOT NULL DEFAULT 0;
            ALTER TABLE users ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
            ALTER TABLE users ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();
            CREATE UNIQUE INDEX IF NOT EXISTS ux_users_email_lower ON users (lower(email));

            CREATE TABLE IF NOT EXISTS households (
                id uuid PRIMARY KEY,
                invite_code text NOT NULL UNIQUE,
                currency text NOT NULL DEFAULT 'RUB',
                split_type text NOT NULL DEFAULT 'equal',
                notifications jsonb NOT NULL DEFAULT '{"transactions":true,"goals":true,"reports":true}'::jsonb,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            ALTER TABLE households ADD COLUMN IF NOT EXISTS invite_code text;
            ALTER TABLE households ADD COLUMN IF NOT EXISTS currency text NOT NULL DEFAULT 'RUB';
            ALTER TABLE households ADD COLUMN IF NOT EXISTS split_type text NOT NULL DEFAULT 'equal';
            ALTER TABLE households ADD COLUMN IF NOT EXISTS notifications jsonb NOT NULL DEFAULT '{"transactions":true,"goals":true,"reports":true}'::jsonb;
            ALTER TABLE households ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
            ALTER TABLE households ADD COLUMN IF NOT EXISTS updated_at timestamptz NOT NULL DEFAULT now();
            CREATE UNIQUE INDEX IF NOT EXISTS ux_households_invite_code ON households (invite_code);

            CREATE TABLE IF NOT EXISTS categories (
                id uuid PRIMARY KEY,
                name text NOT NULL,
                type text NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE UNIQUE INDEX IF NOT EXISTS ux_categories_name_type_lower ON categories (lower(name), type);

            INSERT INTO categories (id, name, type, created_at, updated_at)
            VALUES
                ('11111111-1111-1111-1111-111111111111', 'products', 'expense', now(), now()),
                ('11111111-1111-1111-1111-111111111112', 'transport', 'expense', now(), now()),
                ('11111111-1111-1111-1111-111111111113', 'mortgage', 'expense', now(), now()),
                ('11111111-1111-1111-1111-111111111114', 'entertainment', 'expense', now(), now()),
                ('11111111-1111-1111-1111-111111111115', 'utilities', 'expense', now(), now()),
                ('11111111-1111-1111-1111-111111111116', 'other', 'expense', now(), now()),
                ('11111111-1111-1111-1111-111111111117', 'salary', 'income', now(), now()),
                ('11111111-1111-1111-1111-111111111118', 'bonus', 'income', now(), now())
            ON CONFLICT DO NOTHING;

            CREATE TABLE IF NOT EXISTS transactions (
                id uuid PRIMARY KEY,
                household_id uuid NOT NULL REFERENCES households(id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                category_id uuid NULL REFERENCES categories(id) ON DELETE SET NULL,
                type text NOT NULL,
                amount numeric(12,2) NOT NULL,
                description text NOT NULL DEFAULT '',
                date date NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS ix_transactions_household_date ON transactions (household_id, date DESC);
            CREATE INDEX IF NOT EXISTS ix_transactions_user_id ON transactions (user_id);
            CREATE INDEX IF NOT EXISTS ix_transactions_category_id ON transactions (category_id);
            CREATE INDEX IF NOT EXISTS ix_transactions_type ON transactions (type);

            CREATE TABLE IF NOT EXISTS goals (
                id uuid PRIMARY KEY,
                household_id uuid NOT NULL REFERENCES households(id) ON DELETE CASCADE,
                title text NOT NULL,
                target_amount numeric(12,2) NOT NULL,
                current_amount numeric(12,2) NOT NULL DEFAULT 0,
                monthly_contribution numeric(12,2) NOT NULL DEFAULT 0,
                is_shared boolean NOT NULL DEFAULT true,
                deadline date NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            ALTER TABLE goals ADD COLUMN IF NOT EXISTS current_amount numeric(12,2) NOT NULL DEFAULT 0;
            ALTER TABLE goals ADD COLUMN IF NOT EXISTS monthly_contribution numeric(12,2) NOT NULL DEFAULT 0;
            ALTER TABLE goals ADD COLUMN IF NOT EXISTS is_shared boolean NOT NULL DEFAULT true;
            CREATE INDEX IF NOT EXISTS ix_goals_household_id ON goals (household_id);

            CREATE TABLE IF NOT EXISTS goal_contributions (
                id uuid PRIMARY KEY,
                goal_id uuid NOT NULL REFERENCES goals(id) ON DELETE CASCADE,
                user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
                amount numeric(12,2) NOT NULL,
                date date NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS ix_goal_contributions_goal_id ON goal_contributions (goal_id);
            CREATE INDEX IF NOT EXISTS ix_goal_contributions_user_id ON goal_contributions (user_id);

            CREATE TABLE IF NOT EXISTS support_messages (
                id uuid PRIMARY KEY,
                user_id uuid NULL REFERENCES users(id) ON DELETE SET NULL,
                subject text NOT NULL DEFAULT '',
                message text NOT NULL,
                status text NOT NULL DEFAULT 'created',
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
