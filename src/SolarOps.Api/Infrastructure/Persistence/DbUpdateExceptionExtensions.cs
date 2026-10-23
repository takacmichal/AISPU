using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SolarOps.Api.Infrastructure.Persistence;

public static class DbUpdateExceptionExtensions
{
    /// <summary>
    /// True when the save failed on a PostgreSQL unique constraint (SQLSTATE 23505), optionally only for
    /// the given constraint/index name. Services map it to 409 to cover races after their own pre-check.
    /// </summary>
    public static bool IsUniqueViolation(this DbUpdateException exception, string? constraintName = null) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && (constraintName is null || string.Equals(pg.ConstraintName, constraintName, StringComparison.Ordinal));
}
