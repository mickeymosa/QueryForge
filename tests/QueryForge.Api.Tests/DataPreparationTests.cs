using System.Globalization;
using System.Text.Json;
using QueryForge.Api;
using Xunit;

namespace QueryForge.Api.Tests;

public sealed class DataPreparationTests
{
    [Theory]
    [InlineData("1,200.50","us","1200.5")]
    [InlineData("1.200,50","eu","1200.5")]
    [InlineData("1 200,50","eu","1200.5")]
    [InlineData("-10","us","-10")]
    [InlineData("12,34.56","us","12,34.56")]
    [InlineData("100 USD","us","100 USD")]
    public void NumericPoliciesAreExplicit(string raw,string policy,string expected) =>
        Assert.Equal(expected,DataPreparation.Normalize(raw,TransformOperation.NormalizeNumber,policy));

    [Theory]
    [InlineData("16/01/2026","unambiguous","2026-01-16")]
    [InlineData("02/03/2026","unambiguous","02/03/2026")]
    [InlineData("02/03/2026","dmy","2026-03-02")]
    [InlineData("02/03/2026","mdy","2026-02-03")]
    [InlineData("Jan 17, 2026","unambiguous","2026-01-17")]
    [InlineData("31/02/2026","dmy","31/02/2026")]
    public void DatesNeverGuessAmbiguousOrder(string raw,string policy,string expected) =>
        Assert.Equal(expected,DataPreparation.Normalize(raw,TransformOperation.NormalizeDate,policy));

    [Fact]
    public void MissingTokensAreExplicitAndCountryCodeNAIsPreserved()
    {
        Assert.Equal("",DataPreparation.Normalize("N/A",TransformOperation.NormalizeMissing,null));
        Assert.Equal("NA",DataPreparation.Normalize("NA",TransformOperation.NormalizeMissing,null));
        Assert.Equal("0",DataPreparation.Normalize("0",TransformOperation.NormalizeMissing,null));
    }

    private static DatasetEntity Build(string[] headers,IReadOnlyList<Dictionary<string,string>> rows) => new()
    {
        Id=Guid.NewGuid(), Sha256=new string('b',64), RowCount=rows.Count,
        HeadersJson=JsonSerializer.Serialize(headers), RowsJson=JsonSerializer.Serialize(rows),
        ReportJson=JsonSerializer.Serialize(new DeterministicQualityProfiler().Analyze(headers,rows))
    };

    [Fact]
    public void MessySalesReconcilesToKnownTotalsWithoutInventingMissingRevenue()
    {
        string[] headers=["order_id","order_date","product","country","revenue","currency"];
        string[][] values=[
            ["001","2026-01-15","Laptop","Poland","1,200.50","PLN"],
            ["001","2026-01-15","Laptop","Poland","1,200.50","PLN"],
            ["002","16/01/2026","Monitor","PL","800.25","PLN"],
            ["003","Jan 17, 2026","Mouse","poland","50.25","PLN"],
            ["004","02/03/2026","Laptop","Poland","300","PLN"],
            ["005","N/A","Keyboard","Poland","N/A","PLN"],
            ["006","2026-01-20","Mouse","Poland","-10","PLN"],
            ["007","2026-01-21","Monitor","Poland","100","PLN"]
        ];
        var source=Build(headers,values.Select(v=>headers.Zip(v).ToDictionary(p=>p.First,p=>p.Second)).ToList());
        var profile=DataPreparation.Profile(source);
        Assert.Equal("identifier candidate",profile.Single(p=>p.Name=="order_id").SuggestedType);
        Assert.Equal(1,profile.Single(p=>p.Name=="order_date").AmbiguousDates);
        var cleaned=new EtlPipeline().Execute(source,new("sales cleanup",[
            new(TransformOperation.NormalizeMissing,"order_date"),
            new(TransformOperation.NormalizeMissing,"revenue"),
            new(TransformOperation.NormalizeNumber,"revenue",To:"us"),
            new(TransformOperation.NormalizeDate,"order_date",To:"dmy"),
            new(TransformOperation.Replace,"country","PL","Poland"),
            new(TransformOperation.Replace,"country","poland","Poland"),
            new(TransformOperation.DropExactDuplicates)
        ]));
        Assert.Equal(7,cleaned.Rows.Count);
        Assert.Equal(8,source.Rows.Count);
        Assert.Equal("001",cleaned.Rows[0]["order_id"]);
        Assert.Equal("2026-03-02",cleaned.Rows[3]["order_date"]);
        Assert.All(cleaned.Rows,r=>Assert.Equal("Poland",r["country"]));
        var result=WorkspaceService.Analyze(Build(headers,cleaned.Rows),new("sum","revenue","product"));
        Assert.Equal(2441m,decimal.Parse(result.Total!,CultureInfo.InvariantCulture));
        Assert.Equal(1,result.InvalidMeasureRows);
        Assert.Equal(6,result.UsedRows);
        Assert.Equal("Laptop",result.Groups[0].Label);
        Assert.Equal(1500.5m,decimal.Parse(result.Groups[0].Value,CultureInfo.InvariantCulture));
        Assert.Equal(900.25m,decimal.Parse(result.Groups[1].Value,CultureInfo.InvariantCulture));
        Assert.Equal(40.25m,decimal.Parse(result.Groups[2].Value,CultureInfo.InvariantCulture));
    }
}
