using System.Text.Json.Serialization;

namespace QueryForge.PipelineCore;

// A single versioned contract can be authored by Easy Mode, Advanced Mode, or a future AI planner.
[JsonConverter(typeof(JsonStringEnumConverter<PipelineNodeKind>))]
public enum PipelineNodeKind { Source, Filter, Select, Aggregate }

[JsonConverter(typeof(JsonStringEnumConverter<PipelineValueType>))]
public enum PipelineValueType { Text, Decimal }

[JsonConverter(typeof(JsonStringEnumConverter<FilterComparison>))]
public enum FilterComparison { Equals, NotEquals, GreaterThan, LessThan, Contains }

[JsonConverter(typeof(JsonStringEnumConverter<AggregateFunction>))]
public enum AggregateFunction { Sum, Count }

public sealed record PipelineColumn(string Name, PipelineValueType Type);

public sealed record PipelineSource(
    string Key, string ServerApprovedFilePath, IReadOnlyList<PipelineColumn> Columns);

// The exact config used by a node depends on its kind. Compiler rejects mismatches.
public sealed record SourceNodeSettings(string DatasetKey);
public sealed record FilterNodeSettings(string Column, FilterComparison Comparison, string Value);
public sealed record SelectNodeSettings(IReadOnlyList<string> Columns);
public sealed record AggregateNodeSettings(
    string GroupBy, AggregateFunction Function, string? Measure, string OutputColumn);

public sealed record PipelineNode(
    string Id,
    PipelineNodeKind Kind,
    SourceNodeSettings? Source = null,
    FilterNodeSettings? Filter = null,
    SelectNodeSettings? Select = null,
    AggregateNodeSettings? Aggregate = null);

// This intentionally supports a linear logical graph today. Edges and topological
// ordering prepare the schema/graph contract for a future Join node.
public sealed record PipelineEdge(string From, string To);

public sealed record PipelineSpec(
    int SchemaVersion,
    IReadOnlyList<PipelineNode> Nodes,
    IReadOnlyList<PipelineEdge> Edges,
    string OutputNodeId);

public sealed record PipelineParameter(string Name, object Value);
public sealed record CompiledPipeline(
    string Sql,
    IReadOnlyList<PipelineParameter> Parameters,
    IReadOnlyList<string> OrderedNodeIds,
    IReadOnlyList<PipelineColumn> OutputSchema,
    int PreviewLimit);

public sealed class PipelineValidationException(string message) : Exception(message);
