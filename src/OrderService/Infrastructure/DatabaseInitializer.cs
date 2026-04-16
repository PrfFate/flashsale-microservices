using Npgsql;

namespace OrderService.Infrastructure;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            CREATE TABLE IF NOT EXISTS orders (
                id UUID PRIMARY KEY,
                product_id UUID NOT NULL,
                quantity INTEGER NOT NULL CHECK (quantity > 0),
                unit_price NUMERIC(18,2) NOT NULL CHECK (unit_price >= 0),
                status VARCHAR(32) NOT NULL,
                correlation_id UUID NOT NULL,
                created_on_utc TIMESTAMPTZ NOT NULL,
                updated_on_utc TIMESTAMPTZ NOT NULL
            );

            CREATE TABLE IF NOT EXISTS inventory (
                product_id UUID PRIMARY KEY,
                available_quantity INTEGER NOT NULL CHECK (available_quantity >= 0),
                version BIGINT NOT NULL DEFAULT 1,
                updated_on_utc TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS outbox_messages (
                id UUID PRIMARY KEY,
                event_type VARCHAR(256) NOT NULL,
                payload JSONB NOT NULL,
                correlation_id UUID NOT NULL,
                occurred_on_utc TIMESTAMPTZ NOT NULL,
                processed_at_utc TIMESTAMPTZ NULL,
                error_count INTEGER NOT NULL DEFAULT 0,
                last_error TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_outbox_messages_processed_at_utc
                ON outbox_messages (processed_at_utc);

            CREATE INDEX IF NOT EXISTS ix_orders_status
                ON orders (status);
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
