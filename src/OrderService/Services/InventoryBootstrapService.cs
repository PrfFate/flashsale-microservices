using Npgsql;
using OrderService.Contracts;

namespace OrderService.Services;

public sealed class InventoryBootstrapService(string connectionString)
{
    public async Task UpsertAsync(UpsertInventoryRequest request, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO inventory (product_id, available_quantity, version, updated_on_utc)
            VALUES (@product_id, @available_quantity, 1, @updated_on_utc)
            ON CONFLICT (product_id)
            DO UPDATE SET
                available_quantity = EXCLUDED.available_quantity,
                version = inventory.version + 1,
                updated_on_utc = EXCLUDED.updated_on_utc;
            """;

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("product_id", request.ProductId);
        command.Parameters.AddWithValue("available_quantity", request.AvailableQuantity);
        command.Parameters.AddWithValue("updated_on_utc", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
