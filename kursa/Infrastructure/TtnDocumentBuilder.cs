using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace kursa.Infrastructure;

/// <summary>
/// Печатная форма товарно-транспортной накладной (форма ТТН-1, постановление Минфина РБ от 30.06.2016 № 58):
/// шапка с УНП и реквизитами сторон, транспорт, основание отпуска, пункты погрузки/разгрузки,
/// товарный раздел (графы 1–12) с итогами и подписи.
/// </summary>
public static class TtnDocumentBuilder
{
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly Brush Line = Brushes.Black;

    public static FlowDocument Build(StockDocument d, OrganizationProfile organization)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Times New Roman"), FontSize = 10.5, PagePadding = new Thickness(36),
            ColumnWidth = double.PositiveInfinity, TextAlignment = TextAlignment.Left, Background = Brushes.White, Foreground = Brushes.Black
        };
        doc.Blocks.Add(new Paragraph(new Run("Приложение 1 к постановлению Министерства финансов Республики Беларусь 30.06.2016 № 58"))
            { FontSize = 8, TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 0, 0, 4) });

        // УНП сторон.
        var unp = Table(new[] { 1.0, 1.0, 1.0 });
        var unpRow = new TableRow();
        unpRow.Cells.Add(Cell(Labeled("УНП грузоотправителя", d.ShipperUnp), border: true));
        unpRow.Cells.Add(Cell(Labeled("УНП грузополучателя", d.ConsigneeUnp), border: true));
        unpRow.Cells.Add(Cell(Labeled("УНП заказчика автомобильной перевозки (плательщика)", d.PayerUnp), border: true));
        unp.RowGroups[0].Rows.Add(unpRow);
        doc.Blocks.Add(unp);

        var title = new Paragraph { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        title.Inlines.Add(new Bold(new Run("ТОВАРНО-ТРАНСПОРТНАЯ НАКЛАДНАЯ")) { FontSize = 14 });
        title.Inlines.Add(new Run($"   № {(string.IsNullOrWhiteSpace(d.Number) ? "________" : d.Number)}   ") { FontSize = 13 });
        title.Inlines.Add(new Run(DateText(d.EffectiveDate)) { FontSize = 12 });
        doc.Blocks.Add(title);
        doc.Blocks.Add(new Paragraph(new Run("1-й экземпляр — грузополучателю; 2-й экземпляр — грузоотправителю; 3-й, 4-й экземпляры — перевозчику"))
            { FontSize = 8, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });

        // Транспорт и стороны.
        var head = Table(new[] { 1.0, 1.0, 1.0, 1.0 });
        AddRow(head, ("Автомобиль", d.Vehicle), ("Прицеп", d.Trailer), ("К путевому листу №", d.WaybillNumber), ("Водитель", d.Driver));
        doc.Blocks.Add(head);
        doc.Blocks.Add(Field("Заказчик автомобильной перевозки (плательщик)", d.PayerName));
        doc.Blocks.Add(Field("Грузоотправитель", Join(d.ShipperName, d.ShipperAddress), "(наименование, адрес)"));
        doc.Blocks.Add(Field("Грузополучатель", Join(d.ConsigneeName, d.ConsigneeAddress), "(наименование, адрес)"));
        doc.Blocks.Add(Field("Основание отпуска", d.Basis, "(дата и номер договора или другого документа)"));
        var points = Table(new[] { 1.0, 1.0 });
        AddRow(points, ("Пункт погрузки", d.LoadingPoint), ("Пункт разгрузки", d.UnloadingPoint));
        doc.Blocks.Add(points);

        doc.Blocks.Add(new Paragraph(new Bold(new Run("I. ТОВАРНЫЙ РАЗДЕЛ"))) { TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 8, 0, 4), FontSize = 11.5 });
        doc.Blocks.Add(GoodsTable(d));

        var totals = d.Totals;
        var noVat = d.Kind == OperationKind.Transfer || d.Lines.All(x => x.VatRate is null);
        doc.Blocks.Add(Field("Всего сумма НДС", noVat ? "без НДС" : Ttn.AmountInWords(totals.VatAmount), "(прописью)"));
        doc.Blocks.Add(Field("Всего стоимость с НДС", Ttn.AmountInWords(totals.AmountWithVat), "(прописью)"));
        var cargo = Table(new[] { 1.0, 1.0 });
        AddRow(cargo, ("Всего масса груза, т", totals.CargoMassTonnes > 0 ? totals.CargoMassTonnes.ToString("0.000", Ru) : ""),
            ("Всего количество грузовых мест", totals.CargoPlaces > 0 ? totals.CargoPlaces.ToString("0.###", Ru) : ""));
        doc.Blocks.Add(cargo);

        var signs = Table(new[] { 1.0, 1.0, 1.0 });
        AddRow(signs, ("Отпуск разрешил", d.ReleaseAllowedBy), ("Сдал грузоотправитель", d.HandedOverBy), ("Товар к перевозке принял", Join(d.Driver, "")));
        AddRow(signs, ("По доверенности", ""), ("выданной", ""), ("№ пломбы", ""));
        AddRow(signs, ("Принял грузополучатель", ""), ("", ""), ("", ""));
        doc.Blocks.Add(signs);
        doc.Blocks.Add(new Paragraph(new Run("(должность, фамилия, инициалы, подпись, печать)")) { FontSize = 8, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 0) });
        if (!string.IsNullOrWhiteSpace(d.Comment))
            doc.Blocks.Add(new Paragraph(new Run("Примечание к документу: " + d.Comment)) { FontSize = 9, Margin = new Thickness(0, 6, 0, 0) });
        doc.Blocks.Add(new Paragraph(new Run($"Сформировано программой «Склад» · документ № {d.Id} · {Documents.KindName(d.Kind).ToLower(Ru)} · проведён {d.OccurredAt:dd.MM.yyyy HH:mm} · {d.UserName}"))
            { FontSize = 7.5, Foreground = Brushes.Gray, Margin = new Thickness(0, 6, 0, 0) });
        return doc;
    }

    private static Table GoodsTable(StockDocument d)
    {
        var widths = new[] { 0.45, 3.2, 0.8, 1.0, 1.0, 1.15, 0.75, 1.0, 1.2, 0.9, 0.9, 1.9 };
        var table = Table(widths);
        table.CellSpacing = 0;
        var headers = new[] { "№ п/п", "Наименование товара", "Единица измерения", "Количество", "Цена, руб. коп.", "Стоимость, руб. коп.",
            "Ставка НДС, %", "Сумма НДС, руб. коп.", "Стоимость с НДС, руб. коп.", "Количество грузовых мест", "Масса груза, т", "Примечание" };
        var group = table.RowGroups[0];
        var header = new TableRow { FontWeight = FontWeights.Bold };
        foreach (var h in headers) header.Cells.Add(Cell(new Paragraph(new Run(h)) { TextAlignment = TextAlignment.Center, FontSize = 8.5 }, border: true));
        group.Rows.Add(header);
        var numbers = new TableRow();
        for (var i = 1; i <= 12; i++) numbers.Cells.Add(Cell(new Paragraph(new Run(i.ToString())) { TextAlignment = TextAlignment.Center, FontSize = 8 }, border: true));
        group.Rows.Add(numbers);
        foreach (var line in d.Lines)
        {
            var row = new TableRow();
            Add(row, line.Number.ToString(), TextAlignment.Center);
            Add(row, line.ProductName, TextAlignment.Left);
            Add(row, line.Unit, TextAlignment.Center);
            Add(row, line.Quantity.ToString("0.###", Ru), TextAlignment.Right);
            Add(row, Money(line.Price), TextAlignment.Right);
            Add(row, Money(line.Amount), TextAlignment.Right);
            Add(row, line.VatRate is decimal r ? r.ToString("0.##", Ru) : "без НДС", TextAlignment.Center);
            Add(row, line.VatRate is null ? "—" : Money(line.VatAmount), TextAlignment.Right);
            Add(row, Money(line.AmountWithVat), TextAlignment.Right);
            Add(row, line.CargoPlaces?.ToString("0.###", Ru) ?? "", TextAlignment.Right);
            Add(row, line.CargoMassTonnes?.ToString("0.000", Ru) ?? "", TextAlignment.Right);
            Add(row, line.NoteDisplay, TextAlignment.Left);
            group.Rows.Add(row);
        }
        var t = d.Totals;
        var total = new TableRow { FontWeight = FontWeights.Bold };
        var label = Cell(new Paragraph(new Run("Итого")) { TextAlignment = TextAlignment.Right }, border: true); label.ColumnSpan = 3;
        total.Cells.Add(label);
        Add(total, t.Quantity.ToString("0.###", Ru), TextAlignment.Right);
        Add(total, "×", TextAlignment.Center);
        Add(total, Money(t.Amount), TextAlignment.Right);
        Add(total, "×", TextAlignment.Center);
        Add(total, d.Kind == OperationKind.Transfer ? "—" : Money(t.VatAmount), TextAlignment.Right);
        Add(total, Money(t.AmountWithVat), TextAlignment.Right);
        Add(total, t.CargoPlaces > 0 ? t.CargoPlaces.ToString("0.###", Ru) : "", TextAlignment.Right);
        Add(total, t.CargoMassTonnes > 0 ? t.CargoMassTonnes.ToString("0.000", Ru) : "", TextAlignment.Right);
        Add(total, "", TextAlignment.Left);
        group.Rows.Add(total);
        return table;
    }

    private static string Money(decimal? value) => value?.ToString("N2", Ru) ?? "";
    private static string Join(string a, string b) => string.Join(", ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
    private static string DateText(DateTime date)
    {
        var months = new[] { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };
        return $"«{date.Day:00}» {months[date.Month - 1]} {date.Year} г.";
    }

    private static Table Table(double[] widths)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 2, 0, 2) };
        foreach (var w in widths) table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
        table.RowGroups.Add(new TableRowGroup());
        return table;
    }

    private static TableCell Cell(Block content, bool border = false) => new(content)
    {
        BorderBrush = Line, BorderThickness = border ? new Thickness(0.5) : new Thickness(0), Padding = new Thickness(3, 1.5, 3, 1.5)
    };

    private static void Add(TableRow row, string text, TextAlignment alignment) =>
        row.Cells.Add(Cell(new Paragraph(new Run(text)) { TextAlignment = alignment, FontSize = 9.5 }, border: true));

    private static Paragraph Labeled(string label, string value)
    {
        var p = new Paragraph { Margin = new Thickness(0), FontSize = 9.5 };
        p.Inlines.Add(new Run(label + ": "));
        p.Inlines.Add(new Bold(new Run(string.IsNullOrWhiteSpace(value) ? "            " : value)) { FontSize = 11 });
        return p;
    }

    private static void AddRow(Table table, params (string Label, string Value)[] cells)
    {
        var row = new TableRow();
        foreach (var (label, value) in cells)
        {
            var p = new Paragraph { Margin = new Thickness(0) };
            if (label.Length > 0)
            {
                p.Inlines.Add(new Run(label + ": "));
                p.Inlines.Add(new Underline(new Run(string.IsNullOrWhiteSpace(value) ? "                              " : value)));
            }
            row.Cells.Add(Cell(p));
        }
        table.RowGroups[0].Rows.Add(row);
    }

    private static Paragraph Field(string label, string value, string? hint = null)
    {
        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
        p.Inlines.Add(new Run(label + ": "));
        p.Inlines.Add(new Underline(new Run(string.IsNullOrWhiteSpace(value) ? new string(' ', 80) : value)));
        if (hint is not null) p.Inlines.Add(new Run("  " + hint) { FontSize = 8, Foreground = Brushes.DimGray });
        return p;
    }
}
