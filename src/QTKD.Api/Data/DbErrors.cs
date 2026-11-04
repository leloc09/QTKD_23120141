using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace QTKD.Api.Data;

public static class DbErrors
{
    // 23505 = unique_violation của PostgreSQL
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static string? ConstraintName(DbUpdateException ex) =>
        (ex.InnerException as PostgresException)?.ConstraintName;
}
