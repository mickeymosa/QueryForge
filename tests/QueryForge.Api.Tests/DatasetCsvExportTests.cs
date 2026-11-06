using System.Text;
using System.Text.Json;
using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class DatasetCsvExportTests
{
    [Fact]
    public void CsvExportEscapesFormulasQuotesAndCommasWithoutMutatingSource()
    {
        var row = new Dictionary<string, string>
        {
            ["order_id"] = "A,01", ["customer"] = "=HYPERLINK(\"evil\")",
            ["notes"] = "hello\nworld", ["amount"] = "10.50"
        };
        var dataset = new DatasetEntity
        {
            Id = Guid.NewGuid(), FileName = "../orders.csv", RowCount = 1,
            Sha256 = new string('a',64),
            HeadersJson = JsonSerializer.Serialize(new[] { "order_id", "customer", "notes", "amount" }),
            RowsJson = JsonSerializer.Serialize(new[] { row }),
            ReportJson = "{}"
        };
        var output = Encoding.UTF8.GetString(DatasetCsvExport.Render(dataset));
        Assert.StartsWith("\uFEFF",output);
        Assert.Contains("\"A,01\"",output);
        Assert.Contains("\"'=HYPERLINK(\"\"evil\"\")\"",output);
        Assert.Contains("\"hello\nworld\"",output);
        Assert.EndsWith("\r\n",output);
        Assert.Equal("=HYPERLINK(\"evil\")", dataset.Rows[0]["customer"]);
        Assert.Equal("orders.csv", DatasetCsvExport.FileName(dataset));
    }

    [Fact]
    public void KeepColumnsPreservesHeaderOrderAndRejectsInvalidSelections()
    {
        var source = new DatasetEntity
        {
            Id = Guid.NewGuid(), FileName = "input.csv", RowCount = 1,
            Sha256 = new string('b',64),
            HeadersJson = JsonSerializer.Serialize(new[] { "order_id", "customer", "amount" }),
            RowsJson = JsonSerializer.Serialize(new[] { new Dictionary<string,string>
            {
                ["order_id"]="A1",["customer"]="A",["amount"]="12"
            } }),
            ReportJson = "{}"
        };
        var engine = new EtlPipeline();
        var result = engine.Execute(source, new("Keep needed fields", [
            new(TransformOperation.KeepColumns, To: "amount,order_id")
        ]));
        Assert.Equal(new[] { "order_id", "amount" },result.Headers);
        Assert.Equal("12",result.Rows[0]["amount"]);
        Assert.False(result.Rows[0].ContainsKey("customer"));
        Assert.Equal(3,source.Headers.Length);
        Assert.Throws<EtlValidationException>(() => engine.Execute(source,
            new("Invalid", [new(TransformOperation.KeepColumns, To: "order_id,missing")])));
        Assert.Throws<EtlValidationException>(() => engine.Execute(source,
            new("Invalid", [new(TransformOperation.KeepColumns, To: "amount,amount")])));
    }
}
