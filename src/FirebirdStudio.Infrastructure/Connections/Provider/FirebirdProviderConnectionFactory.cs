using System.Diagnostics;
using System.Globalization;
using System.Data;
using System.Data.Common;
using FirebirdStudio.Application.Connections;
using FirebirdStudio.Core.Database;

namespace FirebirdStudio.Infrastructure.Connections.Provider;

internal sealed class FirebirdProviderConnectionFactory : IFirebirdProviderConnectionFactory
{
    public IFirebirdProviderConnection Create(string connectionString) =>
        new FirebirdProviderConnection(FirebirdDbConnectionFactory.Create(connectionString));

    private sealed class FirebirdProviderConnection(DbConnection connection) : IFirebirdProviderConnection
    {
        private DbCommand CreateCommand(string sql, DbTransaction? transaction = null)
        {
            var command = connection.CreateCommand();
            command.CommandText = sql;
            command.Transaction = transaction;
            return command;
        }

        private static void AddParameter(DbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        public Task OpenAsync(CancellationToken cancellationToken) => connection.OpenAsync(cancellationToken);

        public string ServerVersion => connection.ServerVersion;

        public Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string[] categories = ["Tables", "Views", "Procedures", "Triggers", "Indexes", "Domains"];
            IReadOnlyList<DatabaseMetadataObject> result = categories.Select(category =>
                new DatabaseMetadataObject("Category", category, null,
                    "Not loaded · select to browse", Array.Empty<DatabaseMetadataObject>())).ToArray();
            return Task.FromResult(result);
        }

        public async Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataPageAsync(
            string category, int offset, int pageSize, CancellationToken cancellationToken)
        {
            var queries = new Dictionary<string, (string Sql, string Kind)>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tables"] = ("SELECT TRIM(RDB$RELATION_NAME), 'User table' FROM RDB$RELATIONS " +
                    "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NULL", "Tables"),
                ["Views"] = ("SELECT TRIM(RDB$RELATION_NAME), 'View' FROM RDB$RELATIONS " +
                    "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NOT NULL", "Views"),
                ["Procedures"] = ("SELECT TRIM(RDB$PROCEDURE_NAME), 'Input parameters: ' || " +
                    "COALESCE(CAST(RDB$PROCEDURE_INPUTS AS VARCHAR(10)), '0') || '; output parameters: ' || " +
                    "COALESCE(CAST(RDB$PROCEDURE_OUTPUTS AS VARCHAR(10)), '0') FROM RDB$PROCEDURES " +
                    "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", "Procedures"),
                ["Triggers"] = ("SELECT TRIM(RDB$TRIGGER_NAME), 'Relation: ' || COALESCE(TRIM(RDB$RELATION_NAME), '(database)') || " +
                    "'; type: ' || COALESCE(CAST(RDB$TRIGGER_TYPE AS VARCHAR(10)), '?') || '; inactive: ' || " +
                    "COALESCE(CAST(RDB$TRIGGER_INACTIVE AS VARCHAR(1)), '?') FROM RDB$TRIGGERS " +
                    "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", "Triggers"),
                ["Indexes"] = ("SELECT TRIM(RDB$INDEX_NAME), 'Relation: ' || COALESCE(TRIM(RDB$RELATION_NAME), '(expression)') || " +
                    "'; segments: ' || COALESCE(CAST(RDB$SEGMENT_COUNT AS VARCHAR(10)), '?') || '; unique: ' || " +
                    "COALESCE(CAST(RDB$UNIQUE_FLAG AS VARCHAR(1)), '0') || '; inactive: ' || " +
                    "COALESCE(CAST(RDB$INDEX_INACTIVE AS VARCHAR(1)), '0') FROM RDB$INDICES " +
                    "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", "Indexes"),
                ["Domains"] = ("SELECT TRIM(RDB$FIELD_NAME), 'Field type: ' || " +
                    "COALESCE(CAST(RDB$FIELD_TYPE AS VARCHAR(10)), '?') || '; length: ' || " +
                    "COALESCE(CAST(RDB$FIELD_LENGTH AS VARCHAR(10)), '?') || '; scale: ' || " +
                    "COALESCE(CAST(RDB$FIELD_SCALE AS VARCHAR(10)), '0') FROM RDB$FIELDS " +
                    "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", "Domains")
            };
            if (!queries.TryGetValue(category, out var definition))
                return Array.Empty<DatabaseMetadataObject>();

            var firstRow = checked(offset + 1);
            var lastRow = checked(offset + pageSize + 1);
            var sql = definition.Sql + $" ORDER BY 1 ROWS {firstRow} TO {lastRow}";
            var result = new List<DatabaseMetadataObject>(pageSize + 1);
            await using var command = CreateCommand(sql);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var name = reader.IsDBNull(0) ? string.Empty : reader.GetString(0).Trim();
                if (name.Length == 0) continue;
                if (result.Count == pageSize)
                {
                    result.Add(new DatabaseMetadataObject(
                        "__LoadMore", $"Load next {pageSize}…", category,
                        (offset + pageSize).ToString(CultureInfo.InvariantCulture)));
                    break;
                }
                result.Add(new DatabaseMetadataObject(definition.Kind, name, null,
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim()));
            }
            return result;
        }

        public async Task<IReadOnlyList<DatabaseMetadataObject>> SearchMetadataAsync(
            string searchTerm, string? kindFilter, CancellationToken cancellationToken)
        {
            const string sql =
                "SELECT object_kind, object_name, parent_name, details FROM (" +
                "SELECT CAST('Tables' AS VARCHAR(16)) AS object_kind, TRIM(RDB$RELATION_NAME) AS object_name, " +
                "CAST(NULL AS VARCHAR(63)) AS parent_name, CAST('User table' AS VARCHAR(255)) AS details " +
                "FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NULL " +
                "UNION ALL " +
                "SELECT CAST('Views' AS VARCHAR(16)), TRIM(RDB$RELATION_NAME), CAST(NULL AS VARCHAR(63)), " +
                "CAST('View' AS VARCHAR(255)) FROM RDB$RELATIONS " +
                "WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 AND RDB$VIEW_BLR IS NOT NULL " +
                "UNION ALL " +
                "SELECT CAST('Procedures' AS VARCHAR(16)), TRIM(RDB$PROCEDURE_NAME), CAST(NULL AS VARCHAR(63)), " +
                "CAST('Stored procedure' AS VARCHAR(255)) FROM RDB$PROCEDURES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
                "UNION ALL " +
                "SELECT CAST('Triggers' AS VARCHAR(16)), TRIM(RDB$TRIGGER_NAME), CAST(NULL AS VARCHAR(63)), " +
                "CAST('Trigger' AS VARCHAR(255)) FROM RDB$TRIGGERS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
                "UNION ALL " +
                "SELECT CAST('Indexes' AS VARCHAR(16)), TRIM(RDB$INDEX_NAME), CAST(NULL AS VARCHAR(63)), " +
                "CAST('Index' AS VARCHAR(255)) FROM RDB$INDICES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
                "UNION ALL " +
                "SELECT CAST('Domains' AS VARCHAR(16)), TRIM(RDB$FIELD_NAME), CAST(NULL AS VARCHAR(63)), " +
                "CAST('Domain' AS VARCHAR(255)) FROM RDB$FIELDS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 " +
                "UNION ALL " +
                "SELECT CAST('Column' AS VARCHAR(16)), TRIM(rf.RDB$FIELD_NAME), TRIM(rf.RDB$RELATION_NAME), " +
                "CAST(CASE WHEN r.RDB$VIEW_BLR IS NULL THEN 'Column in table: ' || TRIM(r.RDB$RELATION_NAME) " +
                "ELSE 'Column in view: ' || TRIM(r.RDB$RELATION_NAME) END AS VARCHAR(255)) " +
                "FROM RDB$RELATION_FIELDS rf JOIN RDB$RELATIONS r " +
                "ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
                "WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 " +
                ") metadata_search WHERE object_name CONTAINING @searchTerm " +
                "AND (@kindFilter IS NULL OR object_kind = @kindFilter) " +
                "ORDER BY object_kind, object_name, parent_name";

            var results = new List<DatabaseMetadataObject>();
            await using var command = CreateCommand(sql);
            AddParameter(command, "@searchTerm", searchTerm);
            var kindFilterParameter = command.CreateParameter();
            kindFilterParameter.ParameterName = "@kindFilter";
            kindFilterParameter.DbType = DbType.String;
            kindFilterParameter.Size = 16;
            kindFilterParameter.Value = string.IsNullOrWhiteSpace(kindFilter) ? DBNull.Value : kindFilter.Trim();
            command.Parameters.Add(kindFilterParameter);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var kind = reader.GetString(0).Trim();
                var name = reader.GetString(1).Trim();
                var parentName = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
                var details = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim();
                results.Add(new DatabaseMetadataObject(kind, name, parentName, details));
            }

            return results;
        }

        public async Task<IReadOnlyList<DatabaseMetadataObject>> ReadMetadataObjectDetailsAsync(
            string kind, string name, CancellationToken cancellationToken)
        {
            if (string.Equals(kind, "Indexes", StringComparison.OrdinalIgnoreCase))
            {
                const string indexSql =
                    "SELECT TRIM(RDB$FIELD_NAME), COALESCE(CAST(RDB$FIELD_POSITION AS VARCHAR(10)), '?'), " +
                    "COALESCE(CAST(RDB$STATISTICS AS VARCHAR(32)), '?') " +
                    "FROM RDB$INDEX_SEGMENTS WHERE RDB$INDEX_NAME = @objectName ORDER BY RDB$FIELD_POSITION";
                var segments = new List<DatabaseMetadataObject>();
                await using var command = CreateCommand(indexSql);
                AddParameter(command, "@objectName", name);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var fieldName = reader.GetString(0).Trim();
                    segments.Add(new DatabaseMetadataObject("Index segment", fieldName, name,
                        $"Position: {reader.GetString(1).Trim()}; selectivity: {reader.GetString(2).Trim()}"));
                }
                return segments;
            }

            if (string.Equals(kind, "Domains", StringComparison.OrdinalIgnoreCase))
            {
                const string domainSql =
                    "SELECT COALESCE(CAST(f.RDB$FIELD_TYPE AS VARCHAR(10)), '?'), " +
                    "COALESCE(CAST(f.RDB$FIELD_SUB_TYPE AS VARCHAR(10)), '0'), " +
                    "COALESCE(CAST(f.RDB$FIELD_LENGTH AS VARCHAR(10)), '?'), " +
                    "COALESCE(CAST(f.RDB$FIELD_SCALE AS VARCHAR(10)), '0'), " +
                    "COALESCE(CAST(f.RDB$FIELD_PRECISION AS VARCHAR(10)), '?'), " +
                    "COALESCE(CAST(f.RDB$CHARACTER_LENGTH AS VARCHAR(10)), '?'), " +
                    "TRIM(cs.RDB$CHARACTER_SET_NAME), TRIM(co.RDB$COLLATION_NAME), " +
                    "COALESCE(CAST(f.RDB$NULL_FLAG AS VARCHAR(1)), '0'), " +
                    "f.RDB$DEFAULT_SOURCE, f.RDB$VALIDATION_SOURCE " +
                    "FROM RDB$FIELDS f " +
                    "LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
                    "LEFT JOIN RDB$COLLATIONS co ON co.RDB$COLLATION_ID = f.RDB$COLLATION_ID " +
                    "AND co.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
                    "WHERE f.RDB$FIELD_NAME = @objectName AND COALESCE(f.RDB$SYSTEM_FLAG, 0) = 0";
                await using var command = CreateCommand(domainSql);
                AddParameter(command, "@objectName", name);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    return Array.Empty<DatabaseMetadataObject>();

                var domainDetails =
                    $"Type code: {reader.GetString(0).Trim()}; subtype: {reader.GetString(1).Trim()}; " +
                    $"length: {reader.GetString(2).Trim()}; scale: {reader.GetString(3).Trim()}; " +
                    $"precision: {reader.GetString(4).Trim()}; character length: {reader.GetString(5).Trim()}; " +
                    $"character set: {(reader.IsDBNull(6) ? "" : reader.GetString(6).Trim())}; " +
                    $"collation: {(reader.IsDBNull(7) ? "" : reader.GetString(7).Trim())}; " +
                    $"{(reader.IsDBNull(8) || reader.GetString(8).Trim() != "1" ? "nullable" : "not null")}";
                var domainDetailsList = new List<DatabaseMetadataObject>
                {
                    new("Domain properties", "Properties", name, domainDetails)
                };
                for (var sourceIndex = 9; sourceIndex <= 10; sourceIndex++)
                {
                    if (reader.IsDBNull(sourceIndex))
                        continue;
                    using var sourceReader = reader.GetTextReader(sourceIndex);
                    var source = await sourceReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(source))
                        domainDetailsList.Add(new DatabaseMetadataObject(
                            sourceIndex == 9 ? "Domain default" : "Domain validation",
                            sourceIndex == 9 ? "Default" : "Validation",
                            name,
                            source.Trim()));
                }
                return domainDetailsList;
            }

            if (string.Equals(kind, "Procedures", StringComparison.OrdinalIgnoreCase))
            {
                const string duplicateSql =
                    "SELECT COUNT(*) FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @objectName " +
                    "AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0";
                await using (var command = CreateCommand(duplicateSql))
                {
                    AddParameter(command, "@objectName", name);
                    var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
                    if (count > 1) return Array.Empty<DatabaseMetadataObject>();
                }

                const string parameterSql =
                    "SELECT TRIM(pp.RDB$PARAMETER_NAME), COALESCE(CAST(pp.RDB$PARAMETER_TYPE AS VARCHAR(1)), '0'), " +
                    "COALESCE(CAST(pp.RDB$PARAMETER_NUMBER AS VARCHAR(10)), '?'), TRIM(pp.RDB$FIELD_SOURCE), " +
                    "COALESCE(CAST(f.RDB$FIELD_TYPE AS VARCHAR(10)), '?'), COALESCE(CAST(f.RDB$FIELD_SUB_TYPE AS VARCHAR(10)), '0'), " +
                    "COALESCE(CAST(f.RDB$FIELD_LENGTH AS VARCHAR(10)), '?'), COALESCE(CAST(f.RDB$FIELD_SCALE AS VARCHAR(10)), '0'), " +
                    "COALESCE(CAST(pp.RDB$NULL_FLAG AS VARCHAR(1)), '0'), pp.RDB$DEFAULT_SOURCE " +
                    "FROM RDB$PROCEDURE_PARAMETERS pp JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = pp.RDB$FIELD_SOURCE " +
                    "WHERE pp.RDB$PROCEDURE_NAME = @objectName ORDER BY pp.RDB$PARAMETER_TYPE, pp.RDB$PARAMETER_NUMBER";
                var parameters = new List<DatabaseMetadataObject>();
                await using (var command = CreateCommand(parameterSql))
                {
                    AddParameter(command, "@objectName", name);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var parameterName = reader.GetString(0).Trim();
                        var direction = reader.GetString(1).Trim() == "0" ? "Input" : "Output";
                        var details = $"{direction}; position: {reader.GetString(2).Trim()}; domain: {reader.GetString(3).Trim()}; " +
                            $"type code: {reader.GetString(4).Trim()}; subtype: {reader.GetString(5).Trim()}; " +
                            $"length: {reader.GetString(6).Trim()} byte(s); scale: {reader.GetString(7).Trim()}; " +
                            (reader.GetString(8).Trim() == "1" ? "not null" : "nullable");
                        if (!reader.IsDBNull(9))
                        {
                            using var defaultReader = reader.GetTextReader(9);
                            var defaultSource = await defaultReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(defaultSource)) details += $"; default: {defaultSource.Trim()}";
                        }
                        parameters.Add(new DatabaseMetadataObject("Procedure parameter", parameterName, name, details));
                    }
                }

                var procedureDetails = new List<DatabaseMetadataObject>();
                procedureDetails.Add(new DatabaseMetadataObject("Procedure parameter group", $"Parameters ({parameters.Count})", name,
                    "Input and output parameters ordered by direction and position", parameters));
                const string sourceSql =
                    "SELECT RDB$PROCEDURE_SOURCE FROM RDB$PROCEDURES WHERE RDB$PROCEDURE_NAME = @objectName " +
                    "AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0";
                await using (var command = CreateCommand(sourceSql))
                {
                    AddParameter(command, "@objectName", name);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull(0))
                    {
                        using var sourceReader = reader.GetTextReader(0);
                        var source = await sourceReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                        procedureDetails.Add(new DatabaseMetadataObject("Procedure source", "Source", name,
                            source.Length == 0 ? "No source text is stored for this procedure." : source));
                    }
                }
                return procedureDetails;
            }

            if (string.Equals(kind, "Triggers", StringComparison.OrdinalIgnoreCase))
            {
                const string triggerSql =
                    "SELECT RDB$TRIGGER_SOURCE FROM RDB$TRIGGERS WHERE RDB$TRIGGER_NAME = @objectName " +
                    "AND COALESCE(RDB$SYSTEM_FLAG, 0) = 0";
                await using var command = CreateCommand(triggerSql);
                AddParameter(command, "@objectName", name);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull(0))
                {
                    using var sourceReader = reader.GetTextReader(0);
                    var source = await sourceReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    return [new DatabaseMetadataObject("Trigger source", "Source", name, source)];
                }
                return Array.Empty<DatabaseMetadataObject>();
            }

            if (!string.Equals(kind, "Tables", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "Views", StringComparison.OrdinalIgnoreCase))
                return Array.Empty<DatabaseMetadataObject>();

            var columns = new List<DatabaseMetadataObject>();
            const string columnSql =
                "SELECT TRIM(rf.RDB$FIELD_NAME), TRIM(rf.RDB$FIELD_SOURCE), " +
                "COALESCE(CAST(rf.RDB$FIELD_POSITION AS VARCHAR(10)), '?'), " +
                "COALESCE(CAST(COALESCE(rf.RDB$NULL_FLAG, f.RDB$NULL_FLAG, 0) AS VARCHAR(1)), '0'), " +
                "COALESCE(CAST(f.RDB$FIELD_TYPE AS VARCHAR(10)), '?'), " +
                "COALESCE(CAST(f.RDB$FIELD_SUB_TYPE AS VARCHAR(10)), '0'), " +
                "COALESCE(CAST(f.RDB$FIELD_LENGTH AS VARCHAR(10)), '?'), " +
                "COALESCE(CAST(f.RDB$FIELD_SCALE AS VARCHAR(10)), '0'), " +
                "COALESCE(CAST(f.RDB$CHARACTER_LENGTH AS VARCHAR(10)), '?'), " +
                "COALESCE(CAST(f.RDB$FIELD_PRECISION AS VARCHAR(10)), '?'), TRIM(cs.RDB$CHARACTER_SET_NAME) " +
                "FROM RDB$RELATION_FIELDS rf JOIN RDB$FIELDS f ON f.RDB$FIELD_NAME = rf.RDB$FIELD_SOURCE " +
                "LEFT JOIN RDB$CHARACTER_SETS cs ON cs.RDB$CHARACTER_SET_ID = f.RDB$CHARACTER_SET_ID " +
                "WHERE rf.RDB$RELATION_NAME = @relationName ORDER BY rf.RDB$FIELD_POSITION";
            await using (var command = CreateCommand(columnSql))
            {
                AddParameter(command, "@relationName", name);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var fieldName = reader.GetString(0).Trim();
                    var domain = reader.GetString(1).Trim();
                    var nullable = reader.GetString(3).Trim() != "1";
                    var details = $"Domain: {domain}; type code: {reader.GetString(4).Trim()}; " +
                        $"subtype: {reader.GetString(5).Trim()}; length: {reader.GetString(6).Trim()}; " +
                        $"scale: {reader.GetString(7).Trim()}; character length: {reader.GetString(8).Trim()}; " +
                        $"precision: {reader.GetString(9).Trim()}; charset: {(reader.IsDBNull(10) ? string.Empty : reader.GetString(10).Trim())}; " +
                        $"{(nullable ? "nullable" : "not null")}; position: {reader.GetString(2).Trim()}";
                    columns.Add(new DatabaseMetadataObject("Column", fieldName, name, details));
                }
            }

            var constraints = new List<DatabaseMetadataObject>();
            const string constraintSql =
                "SELECT TRIM(rc.RDB$CONSTRAINT_NAME), TRIM(rc.RDB$CONSTRAINT_TYPE), " +
                "TRIM(rc.RDB$INDEX_NAME), TRIM(parent_rc.RDB$RELATION_NAME), " +
                "TRIM(local_segment.RDB$FIELD_NAME), TRIM(referenced_segment.RDB$FIELD_NAME), " +
                "TRIM(refc.RDB$UPDATE_RULE), TRIM(refc.RDB$DELETE_RULE) " +
                "FROM RDB$RELATION_CONSTRAINTS rc " +
                "LEFT JOIN RDB$REF_CONSTRAINTS refc ON refc.RDB$CONSTRAINT_NAME = rc.RDB$CONSTRAINT_NAME " +
                "LEFT JOIN RDB$RELATION_CONSTRAINTS parent_rc ON parent_rc.RDB$CONSTRAINT_NAME = refc.RDB$CONST_NAME_UQ " +
                "LEFT JOIN RDB$INDEX_SEGMENTS local_segment ON local_segment.RDB$INDEX_NAME = rc.RDB$INDEX_NAME " +
                "LEFT JOIN RDB$INDEX_SEGMENTS referenced_segment ON referenced_segment.RDB$INDEX_NAME = parent_rc.RDB$INDEX_NAME " +
                "AND referenced_segment.RDB$FIELD_POSITION = local_segment.RDB$FIELD_POSITION " +
                "WHERE rc.RDB$RELATION_NAME = @relationName " +
                "ORDER BY rc.RDB$CONSTRAINT_NAME, local_segment.RDB$FIELD_POSITION";
            var constraintColumns = new Dictionary<string, List<DatabaseMetadataObject>>(StringComparer.OrdinalIgnoreCase);
            var constraintDescriptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = CreateCommand(constraintSql))
            {
                AddParameter(command, "@relationName", name);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var constraintName = reader.GetString(0).Trim();
                    var type = reader.GetString(1).Trim();
                    var index = reader.IsDBNull(2) ? string.Empty : reader.GetString(2).Trim();
                    var referencedTable = reader.IsDBNull(3) ? string.Empty : reader.GetString(3).Trim();
                    var localField = reader.IsDBNull(4) ? string.Empty : reader.GetString(4).Trim();
                    var referencedField = reader.IsDBNull(5) ? string.Empty : reader.GetString(5).Trim();
                    var updateRule = reader.IsDBNull(6) ? string.Empty : reader.GetString(6).Trim();
                    var deleteRule = reader.IsDBNull(7) ? string.Empty : reader.GetString(7).Trim();
                    if (!constraintColumns.TryGetValue(constraintName, out var keyColumns))
                    {
                        keyColumns = [];
                        constraintColumns.Add(constraintName, keyColumns);
                        constraintDescriptions.Add(constraintName,
                            $"Type: {type}; index: {index};" + (referencedTable.Length > 0
                                ? $" references: {referencedTable}; ON UPDATE {updateRule}; ON DELETE {deleteRule}"
                                : string.Empty));
                    }
                    if (localField.Length > 0)
                    {
                        var columnDetails = referencedField.Length > 0
                            ? $"references: {referencedTable}.{referencedField}"
                            : string.Empty;
                        keyColumns.Add(new DatabaseMetadataObject(
                            "Constraint column", localField, name,
                            $"Constraint: {constraintName}" + (columnDetails.Length > 0 ? $"; {columnDetails}" : string.Empty)));
                    }
                }
            }
            foreach (var pair in constraintColumns)
                constraints.Add(new DatabaseMetadataObject(
                    "Constraint", pair.Key, name, constraintDescriptions[pair.Key], pair.Value));

            var result = new List<DatabaseMetadataObject>(columns.Count + 2);
            result.AddRange(columns);
            if (constraints.Count > 0)
                result.Add(new DatabaseMetadataObject("Constraint group", $"Constraints ({constraints.Count})", name,
                    "Primary keys, unique keys, foreign keys and check constraints", constraints));

            if (string.Equals(kind, "Views", StringComparison.OrdinalIgnoreCase))
            {
                const string viewSourceSql =
                    "SELECT RDB$VIEW_SOURCE FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @relationName";
                await using var command = CreateCommand(viewSourceSql);
                AddParameter(command, "@relationName", name);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false) && !reader.IsDBNull(0))
                {
                    using var sourceReader = reader.GetTextReader(0);
                    var source = await sourceReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    result.Add(new DatabaseMetadataObject("View source", "Source", name, source));
                }
            }

            return result;
        }

        public async Task<IReadOnlyList<DatabaseMetadataObject>> ReadSqlCompletionMetadataAsync(
            CancellationToken cancellationToken)
        {
            var relations = new Dictionary<string, DatabaseMetadataObject>(StringComparer.OrdinalIgnoreCase);
            const string relationSql =
                "SELECT TRIM(RDB$RELATION_NAME), CASE WHEN RDB$VIEW_BLR IS NULL THEN 'Table' ELSE 'View' END " +
                "FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY RDB$RELATION_NAME";
            await using (var command = CreateCommand(relationSql))
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var name = reader.GetString(0).Trim();
                        relations[name] = new DatabaseMetadataObject(reader.GetString(1).Trim(), name, null, string.Empty,
                            Array.Empty<DatabaseMetadataObject>());
                    }
                }

                const string fieldsSql =
                    "SELECT TRIM(rf.RDB$RELATION_NAME), TRIM(rf.RDB$FIELD_NAME) " +
                    "FROM RDB$RELATION_FIELDS rf JOIN RDB$RELATIONS r ON r.RDB$RELATION_NAME = rf.RDB$RELATION_NAME " +
                    "WHERE COALESCE(r.RDB$SYSTEM_FLAG, 0) = 0 ORDER BY rf.RDB$RELATION_NAME, rf.RDB$FIELD_POSITION";
                var fields = new Dictionary<string, List<DatabaseMetadataObject>>(StringComparer.OrdinalIgnoreCase);
                await using (var command = CreateCommand(fieldsSql))
                await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        var relation = reader.GetString(0).Trim();
                        var field = reader.GetString(1).Trim();
                        if (!fields.TryGetValue(relation, out var list)) fields[relation] = list = [];
                        list.Add(new DatabaseMetadataObject("Column", field, relation, string.Empty));
                    }
                }

                var result = new List<DatabaseMetadataObject>(relations.Count + 256);
                foreach (var pair in relations)
                {
                    IReadOnlyList<DatabaseMetadataObject> relationFields = fields.TryGetValue(pair.Key, out var relationFieldList)
                        ? relationFieldList
                        : Array.Empty<DatabaseMetadataObject>();
                    result.Add(pair.Value with { Children = relationFields });
                }

                await AddCompletionCatalogAsync(result, "Procedure",
                    "SELECT TRIM(RDB$PROCEDURE_NAME) FROM RDB$PROCEDURES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", cancellationToken);
                await AddCompletionCatalogAsync(result, "Trigger",
                    "SELECT TRIM(RDB$TRIGGER_NAME) FROM RDB$TRIGGERS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", cancellationToken);
                await AddCompletionCatalogAsync(result, "Index",
                    "SELECT TRIM(RDB$INDEX_NAME) FROM RDB$INDICES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", cancellationToken);
                await AddCompletionCatalogAsync(result, "Sequence",
                    "SELECT TRIM(RDB$GENERATOR_NAME) FROM RDB$GENERATORS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", cancellationToken);
                await AddCompletionCatalogAsync(result, "Exception",
                    "SELECT TRIM(RDB$EXCEPTION_NAME) FROM RDB$EXCEPTIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", cancellationToken);
                await AddCompletionCatalogAsync(result, "Function",
                    "SELECT TRIM(RDB$FUNCTION_NAME) FROM RDB$FUNCTIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", cancellationToken);
                await AddCompletionCatalogAsync(result, "Character set",
                    "SELECT TRIM(RDB$CHARACTER_SET_NAME) FROM RDB$CHARACTER_SETS", cancellationToken);
                await AddCompletionCatalogAsync(result, "Collation",
                    "SELECT TRIM(RDB$COLLATION_NAME) FROM RDB$COLLATIONS", cancellationToken);
                await AddCompletionCatalogAsync(result, "Role",
                    "SELECT TRIM(RDB$ROLE_NAME) FROM RDB$ROLES WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0", cancellationToken);
                return result;
            }

            public async Task<IReadOnlyList<DatabaseMetadataObject>> ReadSqlCompletionDomainsAsync(
                CancellationToken cancellationToken)
            {
                const string sql =
                    "SELECT TRIM(RDB$FIELD_NAME), COALESCE(CAST(RDB$FIELD_TYPE AS VARCHAR(10)), '?'), " +
                    "COALESCE(CAST(RDB$FIELD_LENGTH AS VARCHAR(10)), '?'), COALESCE(CAST(RDB$FIELD_SCALE AS VARCHAR(10)), '0') " +
                    "FROM RDB$FIELDS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0 ORDER BY RDB$FIELD_NAME";
                var domains = new List<DatabaseMetadataObject>();
                await using var command = CreateCommand(sql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var name = reader.GetString(0).Trim();
                    if (name.Length == 0) continue;
                    domains.Add(new DatabaseMetadataObject("Domain", name, null,
                        $"Field type: {reader.GetString(1).Trim()}; length: {reader.GetString(2).Trim()}; scale: {reader.GetString(3).Trim()}"));
                }
                return domains;
            }

            private async Task AddCompletionCatalogAsync(
                ICollection<DatabaseMetadataObject> target, string kind, string sql, CancellationToken cancellationToken)
            {
                await using var command = CreateCommand(sql);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (!reader.IsDBNull(0))
                    {
                        var name = reader.GetString(0).Trim();
                        if (name.Length > 0) target.Add(new DatabaseMetadataObject(kind, name, null, string.Empty));
                    }
                }
            }

            public async Task<DatabaseTableDataPage> ReadTableDataPageAsync(
                string tableName, int offset, int pageSize, IReadOnlyList<object?>? cursorValues,
                bool previous, CancellationToken cancellationToken)
            {
                var primaryKeyColumns = new List<string>();
                const string primaryKeySql =
                    "SELECT TRIM(s.RDB$FIELD_NAME) FROM RDB$RELATION_CONSTRAINTS rc " +
                    "JOIN RDB$INDEX_SEGMENTS s ON s.RDB$INDEX_NAME = rc.RDB$INDEX_NAME " +
                    "WHERE rc.RDB$RELATION_NAME = @relationName AND rc.RDB$CONSTRAINT_TYPE = 'PRIMARY KEY' " +
                    "ORDER BY s.RDB$FIELD_POSITION";
                await using (var command = CreateCommand(primaryKeySql))
                {
                    AddParameter(command, "@relationName", tableName);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        primaryKeyColumns.Add(reader.GetString(0).Trim());
                }

                if (cursorValues is not null && (primaryKeyColumns.Count == 0 || cursorValues.Count != primaryKeyColumns.Count))
                    throw new ArgumentException("A valid primary-key cursor is required for keyset paging.", nameof(cursorValues));

                static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
                var quotedTable = Quote(tableName);
                var hasStableOrder = primaryKeyColumns.Count > 0;
                var direction = previous ? "DESC" : "ASC";
                var orderBy = hasStableOrder
                    ? " ORDER BY " + string.Join(", ", primaryKeyColumns.Select(column => Quote(column) + " " + direction))
                    : string.Empty;

                var parameters = new List<(string Name, object Value)>();
                string whereClause = string.Empty;
                if (cursorValues is not null)
                {
                    var comparison = previous ? "<" : ">";
                    var alternatives = new List<string>(primaryKeyColumns.Count);
                    for (var index = 0; index < primaryKeyColumns.Count; index++)
                    {
                        var terms = new List<string>(index + 1);
                        for (var equalIndex = 0; equalIndex < index; equalIndex++)
                            terms.Add($"{Quote(primaryKeyColumns[equalIndex])} = @key{equalIndex}");
                        terms.Add($"{Quote(primaryKeyColumns[index])} {comparison} @key{index}");
                        alternatives.Add("(" + string.Join(" AND ", terms) + ")");
                    }
                    whereClause = " WHERE " + string.Join(" OR ", alternatives);
                    for (var index = 0; index < cursorValues.Count; index++)
                        parameters.Add(("@key" + index.ToString(CultureInfo.InvariantCulture), cursorValues[index] ?? DBNull.Value));
                }

                var firstRow = cursorValues is null ? checked(offset + 1) : 1;
                var lastRow = cursorValues is null ? checked(offset + pageSize + 1) : checked(pageSize + 1);
                var sql = $"SELECT * FROM {quotedTable}{whereClause}{orderBy} ROWS {firstRow} TO {lastRow}";
                var columns = new List<string>();
                var rows = new List<IReadOnlyList<string>>(pageSize);
                var keyRows = new List<object?[]>(pageSize);
                var hasMoreInDirection = false;
                await using (var command = CreateCommand(sql))
                {
                    foreach (var parameter in parameters)
                        AddParameter(command, parameter.Name, parameter.Value);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                    for (var index = 0; index < reader.FieldCount; index++) columns.Add(reader.GetName(index));
                    var keyOrdinals = primaryKeyColumns.Select(reader.GetOrdinal).ToArray();
                    while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (rows.Count == pageSize)
                        {
                            hasMoreInDirection = true;
                            break;
                        }

                        var values = new string[columns.Count];
                        for (var index = 0; index < columns.Count; index++)
                        {
                            var typeName = reader.GetDataTypeName(index);
                            values[index] = typeName.Contains("BLOB", StringComparison.OrdinalIgnoreCase) ? "[BLOB]" :
                                typeName.Contains("ARRAY", StringComparison.OrdinalIgnoreCase) ? "[ARRAY]" :
                                FormatSqlValue(reader.GetValue(index));
                        }
                        rows.Add(values);
                        if (hasStableOrder)
                        {
                            var keyValues = new object?[keyOrdinals.Length];
                            for (var index = 0; index < keyOrdinals.Length; index++)
                                keyValues[index] = reader.GetValue(keyOrdinals[index]);
                            keyRows.Add(keyValues);
                        }
                    }
                }

                if (previous && hasStableOrder)
                {
                    rows.Reverse();
                    keyRows.Reverse();
                }

                var hasPrevious = hasStableOrder
                    ? previous ? hasMoreInDirection : cursorValues is not null
                    : offset > 0;
                var hasNext = hasStableOrder
                    ? previous ? cursorValues is not null : hasMoreInDirection
                    : previous || hasMoreInDirection;
                IReadOnlyList<object?>? firstKeyValues = keyRows.Count > 0 ? keyRows[0] : null;
                IReadOnlyList<object?>? lastKeyValues = keyRows.Count > 0 ? keyRows[^1] : null;
                return new DatabaseTableDataPage(
                    columns, rows, offset, pageSize, hasPrevious, hasNext, hasStableOrder,
                    firstKeyValues, lastKeyValues);
            }

            public async Task<SqlExecutionResult> ExecuteSqlAsync(
                string sql, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
            {
                var stopwatch = Stopwatch.StartNew();
                await using var transaction = connection.BeginTransaction();
                try
                {
                    var columns = new List<string>();
                    var rows = new List<IReadOnlyList<string>>();
                    var sortValues = new List<IReadOnlyList<IComparable?>>();
                    var hasMoreRows = false;
                    var rowsAffected = -1;
                    var resultCharacters = 0;
                    const int maximumResultCharacters = 2_000_000;
                    await using (var command = CreateCommand(sql, transaction))
                    {
                        if (parameters is not null)
                        {
                            foreach (var parameter in parameters)
                            {
                                var parameterName = $"@{parameter.Key.TrimStart('@', ':')}";
                                AddParameter(command, parameterName, parameter.Value ?? DBNull.Value);
                            }
                        }

                        await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                    {
                        columns.Capacity = Math.Max(reader.FieldCount, 0);
                        for (var index = 0; index < reader.FieldCount; index++)
                            columns.Add(reader.GetName(index));

                        if (columns.Count > 0)
                        {
                            while (rows.Count < 500 && await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                            {
                                var row = new string[columns.Count];
                                var sortableRow = new IComparable?[columns.Count];
                                var rowTruncated = false;
                                for (var index = 0; index < columns.Count; index++)
                                {
                                    var dataType = reader.GetDataTypeName(index);
                                    var rawValue = dataType.Contains("BLOB", StringComparison.OrdinalIgnoreCase)
                                        ? "[BLOB]"
                                        : dataType.Contains("ARRAY", StringComparison.OrdinalIgnoreCase)
                                            ? "[ARRAY]"
                                            : reader.GetValue(index);
                                    var value = FormatSqlValue(rawValue);
                                    var remaining = maximumResultCharacters - resultCharacters;
                                    if (value.Length > remaining)
                                    {
                                        value = remaining > 1 ? value[..(remaining - 1)] + "…" : "…";
                                        rowTruncated = true;
                                    }

                                    row[index] = value;
                                    sortableRow[index] = rawValue switch
                                    {
                                        DBNull => null,
                                        string => value,
                                        IComparable comparable => comparable,
                                        _ => value
                                    };
                                    resultCharacters += value.Length;
                                    if (rowTruncated)
                                    {
                                        for (var remainingColumn = index + 1; remainingColumn < columns.Count; remainingColumn++)
                                            row[remainingColumn] = "[not loaded]";
                                        break;
                                    }
                                }

                                rows.Add(row);
                                sortValues.Add(sortableRow);
                                if (rowTruncated)
                                {
                                    hasMoreRows = true;
                                    break;
                                }
                            }

                            if (rows.Count == 500 && !hasMoreRows)
                                hasMoreRows = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                        }

                        rowsAffected = reader.RecordsAffected;
                    }
                }

                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                stopwatch.Stop();
                return new SqlExecutionResult(columns, rows, rowsAffected, hasMoreRows, stopwatch.Elapsed)
                {
                    SortValues = sortValues
                };
            }
            catch (Exception executionException)
            {
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        "SQL execution failed and rollback also failed; the transaction state may be uncertain.",
                        executionException,
                        rollbackException);
                }

                throw;
            }
        }

        private static string FormatSqlValue(object value)
        {
            if (value is DBNull)
                return "NULL";
            if (value.GetType().Name.Contains("Blob", StringComparison.OrdinalIgnoreCase))
                return "[BLOB]";
            if (value.GetType().Name.Contains("Array", StringComparison.OrdinalIgnoreCase))
                return "[ARRAY]";
            if (value is byte[] bytes)
            {
                var preview = Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 64)));
                return bytes.Length > 64 ? $"0x{preview}… ({bytes.Length} bytes)" : $"0x{preview}";
            }

            var text = value switch
            {
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };
            text = text.Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
            return text.Length > 4000 ? text[..4000] + "…" : text;
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
