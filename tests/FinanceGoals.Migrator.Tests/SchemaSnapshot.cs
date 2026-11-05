namespace FinanceGoals.Migrator.Tests;

/// <summary>
/// Everything the scripts can change, as sorted text rows: objects, columns, constraints,
/// indexes, code definitions, principals, role members, permissions, journals, and a row count
/// and checksum for every user table. Two databases with equal snapshots have the same schema,
/// security and data.
/// </summary>
internal static class SchemaSnapshot
{
    // A contained database's catalog collation differs from its data collation, so every row is
    // converted to one collation before the UNION.
    private const string _structureSql = """
        SELECT CONVERT(nvarchar(800), row) COLLATE Latin1_General_100_BIN2 AS row
        FROM (
            SELECT CONCAT('object|', SCHEMA_NAME(o.schema_id), '.', o.name, '|', o.type) AS row
            FROM sys.objects AS o WHERE o.is_ms_shipped = 0 AND o.parent_object_id = 0
            UNION ALL
            SELECT CONCAT('column|', SCHEMA_NAME(t.schema_id), '.', t.name, '.', c.name, '|', TYPE_NAME(c.user_type_id),
                '(', c.max_length, ',', c.precision, ',', c.scale, ')|null=', c.is_nullable, '|identity=', c.is_identity,
                '|collation=', c.collation_name, '|default=', dc.definition)
            FROM sys.columns AS c
            JOIN sys.tables AS t ON t.object_id = c.object_id
            LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
            WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('constraint|', SCHEMA_NAME(o.schema_id), '.', OBJECT_NAME(o.parent_object_id), '|', o.name, '|', o.type,
                '|', cc.definition, '|', fk_ref.name)
            FROM sys.objects AS o
            LEFT JOIN sys.check_constraints AS cc ON cc.object_id = o.object_id
            LEFT JOIN sys.foreign_keys AS fk ON fk.object_id = o.object_id
            LEFT JOIN sys.objects AS fk_ref ON fk_ref.object_id = fk.referenced_object_id
            WHERE o.is_ms_shipped = 0 AND o.parent_object_id <> 0
            UNION ALL
            SELECT CONCAT('index|', SCHEMA_NAME(t.schema_id), '.', t.name, '|', i.name, '|', i.type_desc, '|unique=', i.is_unique,
                '|', (SELECT STRING_AGG(CONCAT(c.name, IIF(ic.is_descending_key = 1, ' desc', ''), IIF(ic.is_included_column = 1, ' incl', '')), ',')
                          WITHIN GROUP (ORDER BY ic.is_included_column, ic.key_ordinal, c.name)
                      FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id),
                '|filter=', i.filter_definition)
            FROM sys.indexes AS i JOIN sys.tables AS t ON t.object_id = i.object_id
            WHERE t.is_ms_shipped = 0 AND i.type > 0
            UNION ALL
            SELECT CONCAT('module|', SCHEMA_NAME(o.schema_id), '.', o.name, '|', CONVERT(varchar(64), HASHBYTES('SHA2_256', m.definition), 2))
            FROM sys.sql_modules AS m JOIN sys.objects AS o ON o.object_id = m.object_id WHERE o.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('principal|', p.name, '|', p.type, '|', p.authentication_type_desc)
            FROM sys.database_principals AS p WHERE p.principal_id > 4 AND p.is_fixed_role = 0
            UNION ALL
            SELECT CONCAT('member|', r.name, '|', m.name)
            FROM sys.database_role_members AS rm
            JOIN sys.database_principals AS r ON r.principal_id = rm.role_principal_id
            JOIN sys.database_principals AS m ON m.principal_id = rm.member_principal_id
            UNION ALL
            SELECT CONCAT('permission|', USER_NAME(dp.grantee_principal_id), '|', dp.permission_name, '|', dp.state_desc, '|',
                dp.class_desc, ':', CASE dp.class WHEN 3 THEN SCHEMA_NAME(dp.major_id) WHEN 1 THEN OBJECT_NAME(dp.major_id) END)
            FROM sys.database_permissions AS dp WHERE dp.grantee_principal_id > 4
            UNION ALL
            SELECT CONCAT('journal|SchemaVersions|', ScriptName) FROM dbo.SchemaVersions
            UNION ALL
            SELECT CONCAT('journal|SeedVersions|', ScriptName) FROM dbo.SeedVersions
        ) AS snapshot (row)
        ORDER BY 1;
        """;

    // Object timestamps: equal after an idempotent re-run, but naturally different between an
    // upgraded database and a fresh build.
    private const string _timestampsSql = """
        SELECT CONVERT(nvarchar(800), CONCAT('modified|', SCHEMA_NAME(o.schema_id), '.', o.name, '|',
            CONVERT(varchar(30), o.modify_date, 126))) COLLATE Latin1_General_100_BIN2
        FROM sys.objects AS o WHERE o.is_ms_shipped = 0
        ORDER BY 1;
        """;

    // A row count and an order-independent checksum of every user table except the journals,
    // whose Applied timestamps differ by design. The journals' script names are compared above.
    private const string _tablesSql = """
        SELECT QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name)
        FROM sys.tables AS t
        WHERE t.is_ms_shipped = 0 AND NOT (t.schema_id = SCHEMA_ID('dbo') AND t.name IN ('SchemaVersions', 'SeedVersions'))
        ORDER BY 1;
        """;

    public static async Task<List<string>> TakeAsync(string connectionString, bool includeTimestamps)
    {
        var rows = await SqlServer.QueryAsync(connectionString, _structureSql);

        foreach (var table in await SqlServer.QueryAsync(connectionString, _tablesSql))
        {
            // Table names come from sys.tables and are quoted with QUOTENAME.
            var data = await SqlServer.QueryAsync(connectionString,
                $"SELECT COUNT_BIG(*), CHECKSUM_AGG(BINARY_CHECKSUM(*)) FROM {table};");
            rows.Add($"data|{table}|{data.Single()}");
        }

        if (includeTimestamps)
        {
            rows.AddRange(await SqlServer.QueryAsync(connectionString, _timestampsSql));
        }

        return rows;
    }
}
