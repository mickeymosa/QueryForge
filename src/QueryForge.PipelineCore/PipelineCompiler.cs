using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QueryForge.PipelineCore;

/// <summary>
/// A small, deterministic DAG-to-SQL compiler. No filesystem, database, HTTP,
/// reflection, raw SQL, arbitrary function calls or LLMs are involved here.
/// The server supplies the only approved source catalog.
/// </summary>
public sealed class PipelineCompiler
{
    public const int MaximumNodes = 24;
    public const int MaximumEdges = 32;
    public const int MaximumPreviewRows = 100;
    private static readonly Regex ValidName = new(
        "^[a-z][a-z0-9_]{0,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public CompiledPipeline Compile(
        PipelineSpec spec,
        IReadOnlyDictionary<string, PipelineSource> approvedSources,
        int previewLimit = 25)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(approvedSources);
        if (spec.SchemaVersion != 1)
            throw new PipelineValidationException("Unsupported pipeline schema version.");
        if (previewLimit is < 1 or > MaximumPreviewRows)
            throw new PipelineValidationException("Preview limit must be 1–100.");
        if (spec.Nodes is null || spec.Edges is null ||
            spec.Nodes.Count is < 1 or > MaximumNodes || spec.Edges.Count > MaximumEdges)
            throw new PipelineValidationException("Pipeline exceeds allowed graph size.");

        var nodes = new Dictionary<string, PipelineNode>(StringComparer.Ordinal);
        foreach (var node in spec.Nodes)
        {
            if (node is null || !ValidId(node.Id) || !Enum.IsDefined(node.Kind) ||
                !nodes.TryAdd(node.Id, node))
                throw new PipelineValidationException("Node IDs must be unique lowercase identifiers (max 32 chars).");

            int configs = (node.Source is null ? 0 : 1) + (node.Filter is null ? 0 : 1) +
                (node.Select is null ? 0 : 1) + (node.Aggregate is null ? 0 : 1);
            bool matching = node.Kind switch
            {
                PipelineNodeKind.Source => node.Source is not null,
                PipelineNodeKind.Filter => node.Filter is not null,
                PipelineNodeKind.Select => node.Select is not null,
                PipelineNodeKind.Aggregate => node.Aggregate is not null,
                _ => false
            };
            if (configs != 1 || !matching)
                throw new PipelineValidationException($"Node {node.Id}: exactly one matching settings object is required.");
        }
        if (string.IsNullOrWhiteSpace(spec.OutputNodeId) || !nodes.ContainsKey(spec.OutputNodeId))
            throw new PipelineValidationException("OutputNodeId must reference a node in the pipeline.");

        var indegrees = nodes.Keys.ToDictionary(k => k, _ => 0, StringComparer.Ordinal);
        var outgoing = nodes.Keys.ToDictionary(k => k, _ => new List<string>(), StringComparer.Ordinal);
        var inbound = nodes.Keys.ToDictionary(k => k, _ => new List<string>(), StringComparer.Ordinal);
        var edges = new HashSet<(string From, string To)>();
        foreach (var edge in spec.Edges)
        {
            if (edge is null || !nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To) ||
                edge.From == edge.To || !edges.Add((edge.From, edge.To)))
                throw new PipelineValidationException("Edges must be unique, non-self-referencing and connect existing nodes.");
            indegrees[edge.To]++;
            outgoing[edge.From].Add(edge.To);
            inbound[edge.To].Add(edge.From);
        }

        // Kahn's algorithm: deterministic lexicographic ordering for independent nodes.
        var ready = new SortedSet<string>(
            indegrees.Where(p => p.Value == 0).Select(p => p.Key), StringComparer.Ordinal);
        var order = new List<string>(nodes.Count);
        while (ready.Count != 0)
        {
            string id = ready.Min!;
            ready.Remove(id);
            order.Add(id);
            foreach (var to in outgoing[id])
            {
                indegrees[to]--;
                if (indegrees[to] == 0) ready.Add(to);
            }
        }
        if (order.Count != nodes.Count)
            throw new PipelineValidationException("Pipeline contains a directed cycle.");

        // Intentional v0.6 scope: one input for transform nodes, no joins/branches yet.
        // Reject rather than silently compiling only part of a graph.
        foreach (string id in order)
        {
            int count = inbound[id].Count;
            if (nodes[id].Kind == PipelineNodeKind.Source ? count != 0 : count != 1)
                throw new PipelineValidationException($"Node {id}: invalid input count for this node kind.");
        }
        var ancestors = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (!ancestors.Add(id)) return;
            foreach (var parent in inbound[id]) Visit(parent);
        }
        Visit(spec.OutputNodeId);
        if (ancestors.Count != nodes.Count)
            throw new PipelineValidationException("All nodes must contribute to the selected output. Disconnected nodes are not allowed.");

        var schemas = new Dictionary<string, IReadOnlyList<PipelineColumn>>(StringComparer.Ordinal);
        var clauses = new List<string>();
        var parameters = new List<PipelineParameter>();
        foreach (string id in order)
        {
            var node = nodes[id];
            string cte = CteName(id);
            string query;
            IReadOnlyList<PipelineColumn> resultSchema;
            if (node.Kind == PipelineNodeKind.Source)
            {
                var sourceKey = node.Source!.DatasetKey;
                if (string.IsNullOrWhiteSpace(sourceKey) ||
                    !approvedSources.TryGetValue(sourceKey, out var source))
                    throw new PipelineValidationException($"Node {id}: dataset source is not approved.");
                if (source.Columns is null || source.Columns.Count == 0 ||
                    !Path.IsPathFullyQualified(source.ServerApprovedFilePath) ||
                    !source.Columns.All(c => c is not null && !string.IsNullOrWhiteSpace(c.Name) &&
                        Enum.IsDefined(c.Type)) ||
                    source.Columns.Select(c => c.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != source.Columns.Count)
                    throw new PipelineValidationException($"Node {id}: approved source schema is invalid.");
                resultSchema = source.Columns.ToArray();
                string projection = string.Join(", ", resultSchema.Select(c =>
                    c.Type == PipelineValueType.Decimal
                        ? $"TRY_CAST({Ident(c.Name)} AS DECIMAL(18,4)) AS {Ident(c.Name)}"
                        : $"CAST({Ident(c.Name)} AS VARCHAR) AS {Ident(c.Name)}"));
                // The path comes only from the server-controlled allowlist, never the request.
                query = $"SELECT {projection} FROM read_csv({Literal(source.ServerApprovedFilePath)}, header=true, all_varchar=true)";
            }
            else
            {
                string input = inbound[id][0];
                string parent = CteName(input);
                var columns = schemas[input];
                switch (node.Kind)
                {
                    case PipelineNodeKind.Filter:
                    {
                        var settings = node.Filter!;
                        var column = Find(columns, settings.Column, id);
                        if (!Enum.IsDefined(settings.Comparison) ||
                            settings.Value is null || settings.Value.Length > 256)
                            throw new PipelineValidationException($"Node {id}: invalid filter comparison/value.");
                        string name = "p" + parameters.Count;
                        string predicate;
                        if (settings.Comparison == FilterComparison.Contains)
                        {
                            if (column.Type != PipelineValueType.Text)
                                throw new PipelineValidationException($"Node {id}: Contains requires a text column.");
                            predicate = $"contains({Ident(column.Name)}, ${name})";
                            parameters.Add(new(name, settings.Value));
                        }
                        else
                        {
                            string op = settings.Comparison switch
                            {
                                FilterComparison.Equals => "=",
                                FilterComparison.NotEquals => "<>",
                                FilterComparison.GreaterThan => ">",
                                FilterComparison.LessThan => "<",
                                _ => throw new PipelineValidationException($"Node {id}: unsupported filter operator.")
                            };
                            if (column.Type == PipelineValueType.Text &&
                                settings.Comparison is FilterComparison.GreaterThan or FilterComparison.LessThan)
                                throw new PipelineValidationException($"Node {id}: ordered comparisons require a decimal column.");
                            object value = column.Type switch
                            {
                                PipelineValueType.Decimal when decimal.TryParse(settings.Value,
                                    NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                                    CultureInfo.InvariantCulture, out var number) => number,
                                PipelineValueType.Text => settings.Value,
                                _ => throw new PipelineValidationException($"Node {id}: filter value does not match column type.")
                            };
                            parameters.Add(new(name, value));
                            predicate = $"{Ident(column.Name)} {op} ${name}";
                        }
                        resultSchema = columns;
                        query = $"SELECT * FROM {parent} WHERE {predicate}";
                        break;
                    }
                    case PipelineNodeKind.Select:
                    {
                        var selected = node.Select!.Columns;
                        if (selected is null || selected.Count is < 1 or > 80 ||
                            selected.Distinct(StringComparer.Ordinal).Count() != selected.Count)
                            throw new PipelineValidationException($"Node {id}: select at least one unique column.");
                        resultSchema = selected.Select(col => Find(columns, col, id)).ToArray();
                        query = $"SELECT {string.Join(", ", resultSchema.Select(c => Ident(c.Name)))} FROM {parent}";
                        break;
                    }
                    case PipelineNodeKind.Aggregate:
                    {
                        var settings = node.Aggregate!;
                        var group = Find(columns, settings.GroupBy, id);
                        if (!ValidId(settings.OutputColumn) ||
                            columns.Any(c => c.Name.Equals(settings.OutputColumn, StringComparison.OrdinalIgnoreCase)) ||
                            !Enum.IsDefined(settings.Function))
                            throw new PipelineValidationException($"Node {id}: output column name/function is invalid.");
                        string expr;
                        if (settings.Function == AggregateFunction.Sum)
                        {
                            var measure = Find(columns, settings.Measure, id);
                            if (measure.Type != PipelineValueType.Decimal)
                                throw new PipelineValidationException($"Node {id}: SUM requires a numeric measure.");
                            // Conservative demo policy: never aggregate amounts across currencies
                            // when the input schema includes a currency field.
                            if (measure.Name.Equals("amount", StringComparison.OrdinalIgnoreCase) &&
                                columns.Any(c => c.Name.Equals("currency", StringComparison.OrdinalIgnoreCase)) &&
                                !group.Name.Equals("currency", StringComparison.OrdinalIgnoreCase))
                                throw new PipelineValidationException(
                                    $"Node {id}: amounts must be grouped by currency to avoid mixed-unit totals.");
                            expr = $"SUM({Ident(measure.Name)})";
                        }
                        else
                            expr = "COUNT(*)";
                        query = $"SELECT {Ident(group.Name)}, {expr} AS {Ident(settings.OutputColumn)} FROM {parent} GROUP BY {Ident(group.Name)}";
                        resultSchema = [group, new(settings.OutputColumn, PipelineValueType.Decimal)];
                        break;
                    }
                    default:
                        throw new PipelineValidationException($"Node {id}: unsupported node kind.");
                }
            }
            schemas[id] = resultSchema;
            clauses.Add($"{cte} AS ({query})");
        }

        // The backend, not the client, owns LIMIT and the entire executable SQL.
        string sql = "WITH " + string.Join(",\n", clauses) +
            $"\nSELECT * FROM {CteName(spec.OutputNodeId)} ORDER BY 1 LIMIT {previewLimit}";
        return new(sql, parameters, order, schemas[spec.OutputNodeId], previewLimit);
    }

    private static bool ValidId(string? id) => id is not null && ValidName.IsMatch(id);
    private static string CteName(string id) => Ident("qf_" + id);
    private static string Ident(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    private static string Literal(string text) => "'" + text.Replace("'", "''") + "'";
    private static PipelineColumn Find(IReadOnlyList<PipelineColumn> schema, string? name, string node)
    {
        var found = schema.FirstOrDefault(c => c.Name == name);
        return found ?? throw new PipelineValidationException(
            $"Node {node}: column '{name}' is not in the input schema.");
    }
}
