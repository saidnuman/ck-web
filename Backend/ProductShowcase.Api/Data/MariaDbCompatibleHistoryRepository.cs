using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace ProductShowcase.Api.Data;

/// <summary>
/// XAMPP'in kullandığı MariaDB sürümleri GET_LOCK('__EFMigrationsLock', -1) çağrısında
/// negatif zaman aşımı nedeniyle NULL döndürdüğü için Oracle MySql.EntityFrameworkCore sürücüsü
/// DBNull -> Int64 cast hatası verir.
/// Bu HistoryRepository, GET_LOCK için pozitif zaman aşımı (60 sn) kullanarak
/// hem XAMPP/MariaDB hem MySQL 8+ ile sorunsuz migration uygular.
/// </summary>
public class MariaDbCompatibleHistoryRepository(HistoryRepositoryDependencies dependencies)
    : HistoryRepository(dependencies)
{
    private const string LockName = "__EFMigrationsLock";

    protected override string ExistsSql
    {
        get
        {
            var stringTypeMapping = Dependencies.TypeMappingSource.GetMapping(typeof(string));
            var sb = new StringBuilder();
            sb.Append("SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = ");
            sb.Append(stringTypeMapping.GenerateSqlLiteral(TableName));
            sb.Append(';');
            return sb.ToString();
        }
    }

    protected override bool InterpretExistsResult(object? value)
        => value is not null && value != DBNull.Value;

    public override LockReleaseBehavior LockReleaseBehavior => LockReleaseBehavior.Connection;

    public override string GetCreateIfNotExistsScript()
    {
        var script = GetCreateScript();
        return script.Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.OrdinalIgnoreCase);
    }

    public override string GetBeginIfNotExistsScript(string migrationId)
        => $"-- Migration {migrationId}";

    public override string GetBeginIfExistsScript(string migrationId)
        => $"-- Revert {migrationId}";

    public override string GetEndIfScript()
        => string.Empty;

    public override IMigrationsDatabaseLock AcquireDatabaseLock()
    {
        var sql = $"SELECT GET_LOCK('{LockName}', 60);";
        var result = Dependencies.RawSqlCommandBuilder.Build(sql).ExecuteScalar(
            new RelationalCommandParameterObject(
                Dependencies.Connection,
                null,
                null,
                Dependencies.CurrentContext.Context,
                Dependencies.CommandLogger));

        if (result is null || result == DBNull.Value || Convert.ToInt64(result) != 1)
        {
            throw new InvalidOperationException("Veritabanı migration kilidi (GET_LOCK) alınamadı.");
        }

        return new MariaDbMigrationLock(this);
    }

    public override async Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(
        CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT GET_LOCK('{LockName}', 60);";
        var result = await Dependencies.RawSqlCommandBuilder.Build(sql).ExecuteScalarAsync(
            new RelationalCommandParameterObject(
                Dependencies.Connection,
                null,
                null,
                Dependencies.CurrentContext.Context,
                Dependencies.CommandLogger),
            cancellationToken);

        if (result is null || result == DBNull.Value || Convert.ToInt64(result) != 1)
        {
            throw new InvalidOperationException("Veritabanı migration kilidi (GET_LOCK) alınamadı.");
        }

        return new MariaDbMigrationLock(this);
    }

    private void ReleaseLock()
    {
        var sql = $"SELECT RELEASE_LOCK('{LockName}');";
        Dependencies.RawSqlCommandBuilder.Build(sql).ExecuteScalar(
            new RelationalCommandParameterObject(
                Dependencies.Connection,
                null,
                null,
                Dependencies.CurrentContext.Context,
                Dependencies.CommandLogger));
    }

    private async Task ReleaseLockAsync()
    {
        var sql = $"SELECT RELEASE_LOCK('{LockName}');";
        await Dependencies.RawSqlCommandBuilder.Build(sql).ExecuteScalarAsync(
            new RelationalCommandParameterObject(
                Dependencies.Connection,
                null,
                null,
                Dependencies.CurrentContext.Context,
                Dependencies.CommandLogger));
    }

    private sealed class MariaDbMigrationLock(MariaDbCompatibleHistoryRepository repository) : IMigrationsDatabaseLock
    {
        public IHistoryRepository HistoryRepository => repository;

        public void Dispose() => repository.ReleaseLock();

        public async ValueTask DisposeAsync() => await repository.ReleaseLockAsync();
    }
}
