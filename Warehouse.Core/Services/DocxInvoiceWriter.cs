using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Warehouse.Core.Models;

namespace Warehouse.Core.Services;

/// <summary>DOCX по переданному образцу. Источник — снимок SQL, а не текущие карточки.</summary>
public static class DocxInvoiceWriter
{
    private static readonly XNamespace W="http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Rel="http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Ct="http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly CultureInfo Ru=CultureInfo.GetCultureInfo("ru-RU");
    private const int PageWidth=11106;

    public static string DefaultFileName(InvoiceDocument doc)
    {
        static string Safe(string text)
        {
            var invalid=Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*".ToCharArray()).ToHashSet();
            var value=new string(text.Where(ch=>!invalid.Contains(ch) && !char.IsControl(ch)).ToArray()).Trim().TrimEnd('.');
            return string.IsNullOrEmpty(value) ? "_" : value;
        }
        var series=string.IsNullOrWhiteSpace(doc.Series) ? "" : Safe(doc.Series)+"_";
        return $"TTN_{series}{Safe(doc.Number)}_{doc.Id}.docx";
    }

    /// <summary>Записывает сначала во временный файл; ошибка экспорта не меняет SQL-операцию.</summary>
    public static void Save(InvoiceDocument doc, string path, bool overwrite=true)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Выберите путь к файлу.");
        path=Path.GetFullPath(path);
        var directory=Path.GetDirectoryName(path) ?? throw new ArgumentException("Не удалось определить папку.");
        Directory.CreateDirectory(directory);
        var temporary=Path.Combine(directory,".ttn-"+Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            using (var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) Write(doc,file);
            File.Move(temporary,path,overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Write(InvoiceDocument d, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(d); ArgumentNullException.ThrowIfNull(stream);
        using var zip=new ZipArchive(stream,ZipArchiveMode.Create,leaveOpen:true);
        WriteXml(zip,"[Content_Types].xml",new XDocument(new XElement(Ct+"Types",
            new XElement(Ct+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Ct+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),
            new XElement(Ct+"Override",new XAttribute("PartName","/word/document.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")))));
        WriteXml(zip,"_rels/.rels",new XDocument(new XElement(Rel+"Relationships",
            new XElement(Rel+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type","http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),new XAttribute("Target","word/document.xml")))));
        WriteXml(zip,"word/document.xml",new XDocument(Document(d)));
    }

    private static void WriteXml(ZipArchive zip,string path,XDocument xml)
    {
        var entry=zip.CreateEntry(path,CompressionLevel.Optimal);
        using var target=entry.Open();
        using var writer=XmlWriter.Create(target,new XmlWriterSettings {Encoding=new System.Text.UTF8Encoding(false),Indent=false,CloseOutput=false});
        xml.Save(writer);
    }

    private static XElement Document(InvoiceDocument d)
    {
        var body=new XElement(W+"body");
        body.Add(Paragraph("Проект для проверки реквизитов. Не является защищённым бланком ТТН-1.",13,italic:true));
        body.Add(Paragraph("1-й экз. — грузополучателю    УНП    2-й экз. — грузоотправителю    3-й и 4-й экз. — перевозчику",13));
        var partyWidths=new[]{2700,2700,2700,3006};
        body.Add(Table(partyWidths,true,
            Row(partyWidths,new[]{"Грузоотправитель","Грузополучатель","Заказчик автомобильной перевозки (плательщик)","Макет по образцу ТТН-1. Проверьте требования законодательства"},13,bold:true,fill:"E7E7E7",center:true),
            Row(partyWidths,new[]{d.Sender.Unp,d.Recipient.Unp,d.Payer.Unp,""},15,bold:true,center:true)));
        body.Add(Paragraph("",6,after:65));
        var titleWidths=new[]{1800,5700,3606};
        body.Add(Table(titleWidths,true,Row(titleWidths,new[]{"Серия\n"+d.Series,"ТОВАРНО-ТРАНСПОРТНАЯ\nНАКЛАДНАЯ","№ "+d.Number},26,bold:true,fill:"D7EDF2",center:true)));
        body.Add(Paragraph(d.Date.ToString("dd.MM.yyyy",Ru),20,bold:true,before:180,after:100));
        var vehicleWidths=new[]{5200,3000,2906};
        body.Add(Table(vehicleWidths,false,Row(vehicleWidths,new[]{"Автомобиль   "+d.Vehicle,"Прицеп   "+d.Trailer,"К путевому листу № "+d.WaybillNumber},16,bottom:true)));
        body.Add(Paragraph("(марка, государственный номер)                                                        (марка, государственный номер)",11,after:130));
        body.Add(Table(new[]{PageWidth},false,Row(new[]{PageWidth},new[]{"Водитель   "+d.Driver},16,bottom:true)));
        body.Add(Paragraph("(фамилия и инициалы)",11,center:true,after:130));
        body.Add(Table(new[]{PageWidth},false,Row(new[]{PageWidth},new[]{"Заказчик автомобильной перевозки (плательщик)     "+PartyText(d.Payer)},16,bottom:true)));
        body.Add(Paragraph("(наименование, адрес)",11,center:true,after:100));
        body.Add(Table(new[]{PageWidth},false,Row(new[]{PageWidth},new[]{"Грузоотправитель     "+PartyText(d.Sender)},16,bottom:true)));
        body.Add(Paragraph("(наименование, адрес)",11,center:true,after:100));
        body.Add(Table(new[]{PageWidth},false,Row(new[]{PageWidth},new[]{"Грузополучатель     "+PartyText(d.Recipient)},16,bottom:true)));
        body.Add(Paragraph("(наименование, адрес)",11,center:true,after:130));
        var basis=string.Join(" ",new[]{d.BasisName,string.IsNullOrWhiteSpace(d.BasisNumber)?"":"№ "+d.BasisNumber,
            d.BasisDate.HasValue ? "от "+d.BasisDate.Value.ToString("dd.MM.yyyy",Ru) : ""}.Where(x=>!string.IsNullOrWhiteSpace(x)));
        var pair=new[]{5700,5406};
        body.Add(Table(pair,false,Row(pair,new[]{"Основание отпуска   "+basis,"Пункт погрузки   "+d.LoadingPoint},15,bottom:true)));
        body.Add(Paragraph("(наименование, дата и номер документа)                                                           (адрес)",11,after:95));
        body.Add(Table(new[]{PageWidth},false,Row(new[]{PageWidth},new[]{"Пункт разгрузки   "+d.UnloadingPoint},15,bottom:true)));
        body.Add(Paragraph("(адрес)",11,after:140));
        body.Add(Paragraph("I ТОВАРНЫЙ РАЗДЕЛ",20,bold:true,center:true,before:100,after:60));
        var widths=new[]{360,2900,550,690,700,850,690,790,850,690,720,1316};
        var rows=new List<XElement>
        {
            Row(widths,new[]{"№ п/п","Наименование товара","Ед. изм.","Кол-во","Цена, руб.коп.","Стоимость, руб.коп.","Ставка НДС, %","Сумма НДС, руб.коп.","Стоимость с НДС, руб.коп.","Кол-во грузовых мест","Масса груза, т","Примечание"},13,bold:true,fill:"E7E7E7",center:true,header:true),
            Row(widths,Enumerable.Range(1,12).Select(x=>x.ToString(Ru)).ToArray(),10,center:true,header:true)
        };
        for(int idx=0;idx<d.Lines.Count;idx++)
        {
            var x=d.Lines[idx];
            var notes=x.Note;
            if (x.DiscountPercent>0)
            {
                var discount=$"розн. цена = {Money(x.PriceBeforeDiscount)}; скидка = {Num(x.DiscountPercent)} %";
                notes=string.IsNullOrWhiteSpace(notes)?discount:notes+"; "+discount;
            }
            rows.Add(Row(widths,new[]{(idx+1).ToString(Ru),x.Name,x.Unit,x.Quantity.HasValue?Num(x.Quantity.Value):"—",
                Money(x.UnitPrice),Money(x.NetAmount),x.VatRate.HasValue?Num(x.VatRate.Value):"без НДС",
                x.VatAmount.HasValue?Money(x.VatAmount.Value):"—",Money(x.GrossAmount),x.CargoPlaces?.ToString(Ru)??"",
                x.MassTonnes.HasValue?Mass(x.MassTonnes.Value):"",notes},14));
        }
        var products=d.Lines.Where(x=>!x.IsService).ToList();
        var quantity=products.Count>0 && products.Select(x=>x.Unit).Distinct(StringComparer.OrdinalIgnoreCase).Count()==1
            ? Num(products.Sum(x=>x.Quantity??0m)) : "—";
        rows.Add(Row(widths,new[]{"","Итого:","X",quantity,"X",Money(d.TotalNet),"X",d.Lines.All(x=>x.VatAmount is null)?"без НДС":Money(d.TotalVat),Money(d.TotalGross),
            d.TotalCargoPlaces?.ToString(Ru)??"",d.TotalMassTonnes.HasValue?Mass(d.TotalMassTonnes.Value):"",""},14,bold:true,fill:"E7E7E7",center:true));
        body.Add(Table(widths,true,rows.ToArray()));
        body.Add(Paragraph("Валюта: BYN. Незаполненные реквизиты и правильность оформления необходимо проверить перед использованием.",11,before:80));
        body.Add(new XElement(W+"sectPr",new XElement(W+"pgSz",new XAttribute(W+"w","11906"),new XAttribute(W+"h","16838")),
            new XElement(W+"pgMar",new XAttribute(W+"top","400"),new XAttribute(W+"right","400"),new XAttribute(W+"bottom","400"),new XAttribute(W+"left","400"),new XAttribute(W+"header","200"),new XAttribute(W+"footer","200"),new XAttribute(W+"gutter","0"))));
        return new XElement(W+"document",new XAttribute(XNamespace.Xmlns+"w",W),body);
    }

    private static string PartyText(PartyDetails p) => string.Join(", ",new[]{p.Name,p.Address}.Where(s=>!string.IsNullOrWhiteSpace(s)));
    private static string Money(decimal d) => d.ToString("0.00",Ru);
    private static string Num(decimal d) => d.ToString("0.####",Ru);
    private static string Mass(decimal d) => d.ToString("0.000###",Ru);

    private static XElement Table(int[] widths,bool borders,params XElement[] rows)
    {
        var pr=new XElement(W+"tblPr",new XElement(W+"tblW",new XAttribute(W+"w",widths.Sum()),new XAttribute(W+"type","dxa")));
        if (borders) pr.Add(new XElement(W+"tblBorders",new[]{"top","left","bottom","right","insideH","insideV"}.Select(n=>
            new XElement(W+n,new XAttribute(W+"val","single"),new XAttribute(W+"sz","5"),new XAttribute(W+"color","000000")))));
        pr.Add(new XElement(W+"tblLayout",new XAttribute(W+"type","fixed")),
            new XElement(W+"tblCellMar",new XElement(W+"top",new XAttribute(W+"w","55"),new XAttribute(W+"type","dxa")),
                new XElement(W+"left",new XAttribute(W+"w","38"),new XAttribute(W+"type","dxa")),
                new XElement(W+"bottom",new XAttribute(W+"w","55"),new XAttribute(W+"type","dxa")),
                new XElement(W+"right",new XAttribute(W+"w","38"),new XAttribute(W+"type","dxa"))));
        return new XElement(W+"tbl",pr,new XElement(W+"tblGrid",widths.Select(n=>new XElement(W+"gridCol",new XAttribute(W+"w",n)))),rows);
    }

    private static XElement Row(int[] widths,string[] texts,int font,bool bold=false,string? fill=null,bool center=false,bool bottom=false,bool header=false)
    {
        if (widths.Length!=texts.Length) throw new ArgumentException("Количество ячеек не совпадает с ширинами.");
        var tr=new XElement(W+"tr");
        var props=new XElement(W+"trPr",new XElement(W+"cantSplit"));
        if (header) props.Add(new XElement(W+"tblHeader"));
        tr.Add(props);
        for(var i=0;i<widths.Length;i++)
        {
            var cellPr=new XElement(W+"tcPr",new XElement(W+"tcW",new XAttribute(W+"w",widths[i]),new XAttribute(W+"type","dxa")));
            if (bottom) cellPr.Add(new XElement(W+"tcBorders",new XElement(W+"bottom",new XAttribute(W+"val","single"),new XAttribute(W+"sz","4"))));
            if (fill!=null) cellPr.Add(new XElement(W+"shd",new XAttribute(W+"val","clear"),new XAttribute(W+"fill",fill)));
            cellPr.Add(new XElement(W+"vAlign",new XAttribute(W+"val","center")));
            var align=center || (widths.Length==12 && i!=1 && i!=11);
            tr.Add(new XElement(W+"tc",cellPr,Paragraph(texts[i],font,bold,center:align)));
        }
        return tr;
    }

    // Защита Word XML от управляющих символов и некорректных Unicode-пар в пользовательском тексте.
    private static string XmlSafe(string value)
    {
        var result=new System.Text.StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            var c=rune.Value;
            if (c is 0x9 or 0xA or 0xD || (c>=0x20 && c<=0xD7FF) || (c>=0xE000 && c<=0xFFFD) || (c>=0x10000 && c<=0x10FFFF))
                result.Append(rune.ToString());
        }
        return result.ToString();
    }

    private static XElement Paragraph(string? text,int size,bool bold=false,bool italic=false,bool center=false,int before=0,int after=0)
    {
        var p=new XElement(W+"p",new XElement(W+"pPr",new XElement(W+"spacing",new XAttribute(W+"before",before),new XAttribute(W+"after",after),new XAttribute(W+"line","200"),new XAttribute(W+"lineRule","auto")),
            center ? new XElement(W+"jc",new XAttribute(W+"val","center")) : null));
        var rpr=new XElement(W+"rPr",new XElement(W+"rFonts",new XAttribute(W+"ascii","Times New Roman"),new XAttribute(W+"hAnsi","Times New Roman"),new XAttribute(W+"cs","Times New Roman")),
            bold?new XElement(W+"b"):null,italic?new XElement(W+"i"):null,new XElement(W+"sz",new XAttribute(W+"val",size)),new XElement(W+"szCs",new XAttribute(W+"val",size)));
        var parts=(text??"").Replace("\r","").Split('\n');
        for(int i=0;i<parts.Length;i++)
        {
            if (i>0) p.Add(new XElement(W+"r",new XElement(rpr),new XElement(W+"br")));
            var clean=XmlSafe(parts[i]);
            p.Add(new XElement(W+"r",new XElement(rpr),new XElement(W+"t",new XAttribute(XNamespace.Xml+"space","preserve"),clean)));
        }
        return p;
    }
}
