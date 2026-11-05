using System.Security.Cryptography;
using System.Text;

namespace FinanceGoals.Migrator.Tests;

/// <summary>
/// What the scripts can change, as sorted text rows. Two databases with equal snapshots have
/// the same schema, security and data, as far as these rows go:
/// <list type="bullet">
/// <item>schemas and their owners;</item>
/// <item>every user object (tables, views, procedures, functions, constraints, triggers,
/// sequences, synonyms...) by name, type, parent and explicit owner;</item>
/// <item>table columns: position, type, nullability, identity seed and increment, collation,
/// computed definition, sparse, rowguid, and default constraint;</item>
/// <item>check constraints and foreign keys (columns, referenced table, actions, disabled and
/// untrusted state);</item>
/// <item>indexes and key constraints on tables and views: kind, columns, filter, disabled;</item>
/// <item>triggers (disabled, instead-of) and a SHA-256 of every module definition, including
/// database DDL triggers;</item>
/// <item>principals, role owners, role members, and every permission with its securable,
/// column and grantor;</item>
/// <item>the journals' script names, and a row count and SHA-256 of every user table.</item>
/// </list>
/// Not covered yet: sequence and synonym details (only their names), user-defined types,
/// extended properties, partitioning, and storage options such as fill factor or compression.
/// </summary>
internal static class SchemaSnapshot
{
    // A contained database's catalog collation differs from its data collation, so every row is
    // converted to one collation before the UNION. Child objects have no explicit owner of their
    // own, so only top-level objects report one.
    private const string _structureSql = """
        SELECT CONVERT(nvarchar(max), row) COLLATE Latin1_General_100_BIN2 AS row
        FROM (
            SELECT CONCAT('schema|', s.name, '|owner=', USER_NAME(s.principal_id))
            FROM sys.schemas AS s WHERE s.schema_id < 16384
            UNION ALL
            SELECT CONCAT('object|', SCHEMA_NAME(o.schema_id), '.', o.name, '|', o.type,
                '|parent=', OBJECT_SCHEMA_NAME(o.parent_object_id), '.', OBJECT_NAME(o.parent_object_id),
                '|owner=', USER_NAME(o.principal_id))
            FROM sys.objects AS o WHERE o.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('column|', SCHEMA_NAME(t.schema_id), '.', t.name, '.', c.name,
                '|position=', ROW_NUMBER() OVER (PARTITION BY c.object_id ORDER BY c.column_id),
                '|', TYPE_NAME(c.user_type_id), '(', c.max_length, ',', c.precision, ',', c.scale, ')',
                '|null=', c.is_nullable, '|identity=', c.is_identity, ':', CONVERT(nvarchar(40), ic.seed_value), ',', CONVERT(nvarchar(40), ic.increment_value),
                '|collation=', c.collation_name, '|sparse=', c.is_sparse, '|rowguid=', c.is_rowguidcol,
                '|computed=', cc.definition, IIF(cc.is_persisted = 1, ' persisted', ''),
                '|default=', dc.name, ':', dc.definition)
            FROM sys.columns AS c
            JOIN sys.tables AS t ON t.object_id = c.object_id
            LEFT JOIN sys.identity_columns AS ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            LEFT JOIN sys.computed_columns AS cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
            LEFT JOIN sys.default_constraints AS dc ON dc.object_id = c.default_object_id
            WHERE t.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('check|', SCHEMA_NAME(ck.schema_id), '.', OBJECT_NAME(ck.parent_object_id), '|', ck.name,
                '|column=', COL_NAME(ck.parent_object_id, ck.parent_column_id), '|', ck.definition,
                '|disabled=', ck.is_disabled, '|untrusted=', ck.is_not_trusted)
            FROM sys.check_constraints AS ck WHERE ck.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('foreignkey|', SCHEMA_NAME(fk.schema_id), '.', OBJECT_NAME(fk.parent_object_id), '|', fk.name,
                '|(', (SELECT STRING_AGG(COL_NAME(fkc.parent_object_id, fkc.parent_column_id), ',') WITHIN GROUP (ORDER BY fkc.constraint_column_id)
                       FROM sys.foreign_key_columns AS fkc WHERE fkc.constraint_object_id = fk.object_id),
                ')->', OBJECT_SCHEMA_NAME(fk.referenced_object_id), '.', OBJECT_NAME(fk.referenced_object_id),
                '(', (SELECT STRING_AGG(COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id), ',') WITHIN GROUP (ORDER BY fkc.constraint_column_id)
                      FROM sys.foreign_key_columns AS fkc WHERE fkc.constraint_object_id = fk.object_id),
                ')|delete=', fk.delete_referential_action_desc, '|update=', fk.update_referential_action_desc,
                '|disabled=', fk.is_disabled, '|untrusted=', fk.is_not_trusted, '|not_for_replication=', fk.is_not_for_replication)
            FROM sys.foreign_keys AS fk WHERE fk.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('index|', SCHEMA_NAME(o.schema_id), '.', o.name, '|', i.name, '|', i.type_desc,
                '|unique=', i.is_unique, '|pk=', i.is_primary_key, '|uq=', i.is_unique_constraint, '|disabled=', i.is_disabled,
                '|', (SELECT STRING_AGG(CONCAT(c.name, IIF(ic.is_descending_key = 1, ' desc', ''), IIF(ic.is_included_column = 1, ' incl', '')), ',')
                          WITHIN GROUP (ORDER BY ic.is_included_column, ic.key_ordinal, c.name)
                      FROM sys.index_columns AS ic JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                      WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id),
                '|filter=', i.filter_definition)
            FROM sys.indexes AS i JOIN sys.objects AS o ON o.object_id = i.object_id
            WHERE o.is_ms_shipped = 0 AND o.type IN ('U', 'V') AND i.type > 0
            UNION ALL
            SELECT CONCAT('trigger|', OBJECT_SCHEMA_NAME(tr.parent_id), '.', OBJECT_NAME(tr.parent_id), '|', tr.name,
                '|class=', tr.parent_class_desc, '|disabled=', tr.is_disabled, '|instead_of=', tr.is_instead_of_trigger)
            FROM sys.triggers AS tr WHERE tr.is_ms_shipped = 0
            UNION ALL
            SELECT CONCAT('module|', COALESCE(SCHEMA_NAME(o.schema_id) + '.' + o.name, 'database.' + tr.name),
                '|', CONVERT(varchar(64), HASHBYTES('SHA2_256', m.definition), 2),
                '|ansi_nulls=', m.uses_ansi_nulls, '|quoted_identifier=', m.uses_quoted_identifier,
                '|schemabound=', m.is_schema_bound, '|execute_as=', USER_NAME(m.execute_as_principal_id))
            FROM sys.sql_modules AS m
            LEFT JOIN sys.objects AS o ON o.object_id = m.object_id
            LEFT JOIN sys.triggers AS tr ON tr.object_id = m.object_id AND tr.parent_class = 0
            WHERE COALESCE(o.is_ms_shipped, tr.is_ms_shipped) = 0
            UNION ALL
            SELECT CONCAT('principal|', p.name, '|', p.type, '|', p.authentication_type_desc,
                '|default_schema=', p.default_schema_name, '|owner=', USER_NAME(p.owning_principal_id))
            FROM sys.database_principals AS p WHERE p.principal_id > 4 AND p.is_fixed_role = 0
            UNION ALL
            SELECT CONCAT('member|', r.name, '|', m.name)
            FROM sys.database_role_members AS rm
            JOIN sys.database_principals AS r ON r.principal_id = rm.role_principal_id
            JOIN sys.database_principals AS m ON m.principal_id = rm.member_principal_id
            UNION ALL
            SELECT CONCAT('permission|', USER_NAME(dp.grantee_principal_id), '|', dp.permission_name, '|', dp.state_desc, '|',
                dp.class_desc, ':',
                CASE dp.class
                    WHEN 0 THEN ''
                    WHEN 1 THEN CONCAT(OBJECT_SCHEMA_NAME(dp.major_id), '.', OBJECT_NAME(dp.major_id),
                                       IIF(dp.minor_id > 0, '.' + COL_NAME(dp.major_id, dp.minor_id), ''))
                    WHEN 3 THEN SCHEMA_NAME(dp.major_id)
                    WHEN 4 THEN USER_NAME(dp.major_id)
                    WHEN 6 THEN (SELECT SCHEMA_NAME(ty.schema_id) + '.' + ty.name FROM sys.types AS ty WHERE ty.user_type_id = dp.major_id)
                    ELSE CONCAT(dp.major_id, '.', dp.minor_id)
                END,
                '|grantor=', USER_NAME(dp.grantor_principal_id))
            FROM sys.database_permissions AS dp
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

    // Every user table except the journals, whose Applied timestamps differ by design. The
    // journals' script names are compared above.
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
            rows.Add($"data|{table}|{await TableHashAsync(connectionString, table)}");
        }

        if (includeTimestamps)
        {
            rows.AddRange(await SqlServer.QueryAsync(connectionString, _timestampsSql));
        }

        return rows;
    }

    /// <summary>
    /// The row count and a SHA-256 over every row as JSON, sorted ordinally. JSON keeps what a
    /// checksum loses: trailing spaces, case, xml and long values, and NULL versus empty.
    /// </summary>
    private static async Task<string> TableHashAsync(string connectionString, string table)
    {
        // The table name comes from sys.tables and is quoted with QUOTENAME.
        var json = await SqlServer.QueryAsync(connectionString,
            $"SELECT (SELECT t.* FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES) FROM {table} AS t;");
        json.Sort(StringComparer.Ordinal);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', json)));
        return $"{json.Count}|{Convert.ToHexString(hash)}";
    }
}
