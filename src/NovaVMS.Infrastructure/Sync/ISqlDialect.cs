using System.Data.Common;
using NovaVMS.Core.Models;

namespace NovaVMS.Infrastructure.Sync;

/// <summary>
/// Server-specific connection and SQL statements of the settings database. Parameters use the @name syntax.
/// </summary>
internal interface ISqlDialect
{
    DbConnection CreateConnection(SettingsSyncOptions options);

    string CreateMetaTable { get; }

    string CreateVersionsTable { get; }

    /// <summary>
    /// Inserts @key / @value unless the key exists; concurrent first connections keep the first value.
    /// </summary>
    string InsertMetaIfMissing { get; }

    /// <summary>
    /// Inserts a version and returns its generated identifier.
    /// </summary>
    string InsertVersion { get; }
}
