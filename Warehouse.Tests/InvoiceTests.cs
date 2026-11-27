using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Warehouse.Core.Models;
using Warehouse.Core.Services;
using Xunit;

namespace Warehouse.Tests;

public sealed class InvoiceTests
{
    private static readonly Product Product=new() {Id=4,Name="Товар & деталь",Unit="шт",PurchasePrice=1,SalePrice=120};

    [Fact]
    public void IncludedAndExcludedVatAreDifferentAndRoundedPerLine()
    {
        var item=new OperationItemInput(Product.Id,2,120m,0,20m);
        var included=InvoiceCalculator.Product(item,Product,VatPriceMode.Included);
        Assert.Equal(240m,included.GrossAmount); Assert.Equal(40m,included.VatAmount); Assert.Equal(200m,included.NetAmount);
        var excluded=InvoiceCalculator.Product(item,Product,VatPriceMode.Excluded);
        Assert.Equal(240m,excluded.NetAmount); Assert.Equal(48m,excluded.VatAmount); Assert.Equal(288m,excluded.GrossAmount);
    }

    [Fact]
    public void NoVatAndZeroRateRemainDistinctAndDiscountDoesNotMakePriceZero()
    {
        var noTax=InvoiceCalculator.Product(new(Product.Id,1,10,10,null),Product,VatPriceMode.Included);
        var zeroTax=InvoiceCalculator.Product(new(Product.Id,1,10,10,0),Product,VatPriceMode.Included);
        Assert.Null(noTax.VatRate); Assert.Null(noTax.VatAmount); Assert.Equal(9m,noTax.GrossAmount);
        Assert.Equal(0m,zeroTax.VatRate); Assert.Equal(0m,zeroTax.VatAmount);
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.Product(new(Product.Id,1,0.01m,99.99m,20),Product,VatPriceMode.Included));
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.Product(new(Product.Id,1,10,100,20),Product,VatPriceMode.Included));
    }

    [Fact]
    public void WithoutVatModeDisallowsRatesAndDoesNotCreateTax()
    {
        var item=new OperationItemInput(Product.Id,2,10,0,null);
        var line=InvoiceCalculator.Product(item,Product,VatPriceMode.WithoutVat);
        Assert.Equal(20m,line.NetAmount); Assert.Equal(20m,line.GrossAmount); Assert.Null(line.VatRate); Assert.Null(line.VatAmount);
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.Product(item with {VatRate=20},Product,VatPriceMode.WithoutVat));
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.Transport(10,0,VatPriceMode.WithoutVat));
    }

    [Fact]
    public void AutomaticFilesNeverOverwritePriorDocuments()
    {
        var directory=Path.Combine(Path.GetTempPath(),"invoice-unit-"+Guid.NewGuid().ToString("N"));
        var path=Path.Combine(directory,"one.docx");
        try
        {
            var doc=new InvoiceDocument(); DocxInvoiceWriter.Save(doc,path,overwrite:false); var original=File.ReadAllBytes(path);
            Assert.Throws<IOException>(()=>DocxInvoiceWriter.Save(doc,path,overwrite:false));
            Assert.Equal(original,File.ReadAllBytes(path)); Assert.Single(Directory.GetFiles(directory));
            DocxInvoiceWriter.Save(doc,path); Assert.True(File.Exists(path));
        }
        finally { if(Directory.Exists(directory)) Directory.Delete(directory,true); }
    }

    [Fact]
    public void FractionalVatRatesAndCargoAreValidated()
    {
        var l=InvoiceCalculator.Product(new(Product.Id,20,7.83m,0,16.3587m,20,0.5m),Product,VatPriceMode.Excluded);
        Assert.Equal(16.3587m,l.VatRate); Assert.Equal(25.62m,l.VatAmount);
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.VatRate(16.35871m));
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.VatRate(-1));
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.OptionalCargo(-1,0));
        Assert.Throws<ArgumentException>(()=>InvoiceCalculator.OptionalCargo(1,0.0000001m));
    }

    [Fact]
    public void MissingCargoIsNotMistakenForACompleteTotal()
    {
        var one=new InvoiceLine {Name="Один",CargoPlaces=3,MassTonnes=1};
        var two=new InvoiceLine {Name="Два",CargoPlaces=null,MassTonnes=null};
        var d=new InvoiceDocument {Lines=[one,two,InvoiceCalculator.Transport(10,null,VatPriceMode.Included)]};
        Assert.Null(d.TotalCargoPlaces); Assert.Null(d.TotalMassTonnes);
        two.CargoPlaces=4; two.MassTonnes=0.5m;
        Assert.Equal(7L,d.TotalCargoPlaces); Assert.Equal(1.5m,d.TotalMassTonnes);
    }

    [Fact]
    public void DocxMeetsOpenXmlSchemaAndMarksNoVatTotals()
    {
        var item=InvoiceCalculator.Product(new(Product.Id,2,10,0,null),Product,VatPriceMode.WithoutVat);
        var invoice=new InvoiceDocument {Series="",Number="5",Date=new DateTime(2026,1,1),PriceMode=VatPriceMode.WithoutVat,Lines=[item],
            Sender=new PartyDetails {Name="Тест\u0001\uFFFE\uD800 🛒"}};
        Assert.Equal("TTN_5_0.docx",DocxInvoiceWriter.DefaultFileName(invoice));
        using var stream=new MemoryStream(); DocxInvoiceWriter.Write(invoice,stream); stream.Position=0;
        using var document=WordprocessingDocument.Open(stream,false);
        Assert.Empty(new OpenXmlValidator().Validate(document));
        var text=document.MainDocumentPart!.Document.InnerText;
        Assert.True(text.Split("без НДС").Length>=3,"both the item and total identify no VAT");
        Assert.Contains("Тест",text); Assert.Contains("🛒",text); Assert.False(text.Contains('\u0001')); Assert.False(text.Contains('\uFFFE'));
    }

    [Fact]
    public void DocxContainsPersistedValuesAndValidWordPackage()
    {
        var line=InvoiceCalculator.Product(new(Product.Id,2,120,10,20,1,0.02m,"хрупкое & важное"),Product,VatPriceMode.Included);
        var d=new InvoiceDocument {Id=42,BatchId=5,Date=new DateTime(2026,11,22),Series="ВН",Number="18",
            Sender=new PartyDetails {Name="Отправитель & Ко",Address="Минск",Unp="123"},
            Recipient=new PartyDetails {Name="Покупатель",Address="Брест"},Payer=new PartyDetails {Name="Плательщик"},
            Vehicle="МАЗ 1234",Driver="Иванов И.И.",LoadingPoint="Минск",UnloadingPoint="Брест",Lines=[line]};
        // Реквизиты должны оставаться читаемыми и после сериализации, как при сохранении в SQL.
        d=JsonSerializer.Deserialize<InvoiceDocument>(JsonSerializer.Serialize(d))!;
        using var ms=new MemoryStream(); DocxInvoiceWriter.Write(d,ms); ms.Position=0;
        using var zip=new ZipArchive(ms,ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("[Content_Types].xml")); Assert.NotNull(zip.GetEntry("_rels/.rels"));
        using var file=zip.GetEntry("word/document.xml")!.Open(); var xml=XDocument.Load(file);
        var all=string.Join(" ",xml.Descendants().Where(x=>x.Name.LocalName=="t").Select(x=>x.Value));
        foreach(var text in new[]{"ТОВАРНО-ТРАНСПОРТНАЯ","НАКЛАДНАЯ","ВН","18","Отправитель & Ко","Покупатель","МАЗ 1234","Товар & деталь","хрупкое & важное","Итого:"}) Assert.Contains(text,all);
        Assert.True(xml.Descendants().Count(x=>x.Name.LocalName=="tblHeader")>=2);
        Assert.Equal("TTN_ВН_18_42.docx",DocxInvoiceWriter.DefaultFileName(d));
    }
}
