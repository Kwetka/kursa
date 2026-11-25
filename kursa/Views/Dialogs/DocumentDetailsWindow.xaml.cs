using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;

/// <summary>Позиции проведённого документа: что именно пришло, переместилось или реализовано.</summary>
public partial class DocumentDetailsWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly StockDocument _document;
    public DocumentDetailsWindow(WorkspaceContext context, int documentId)
    {
        InitializeComponent(); _context=context;
        _document=context.Repository.GetDocument(documentId) ?? throw new InvalidOperationException("Документ не найден.");
        var d=_document;
        Heading.Text=$"{d.KindName} № {d.NumberDisplay}";
        Title=Heading.Text;
        SubHeading.Text=$"Проведён {d.OccurredAt:dd.MM.yyyy HH:mm:ss} (время SQL Server) · {d.UserName}"
            + (d.IsLegacy ? " · перенесён из прежней версии программы (однотоварная операция)" : "");
        var details=new List<(string, string)>
        {
            ("Операция", d.KindName), ("Номер ТТН", string.IsNullOrWhiteSpace(d.Number) ? "—" : d.Number),
            ("Дата накладной", d.EffectiveDate.ToString("dd.MM.yyyy")), ("Склад-отправитель", d.FromWarehouse),
            ("Склад-получатель", d.ToWarehouse), ("Контрагент", d.CounterpartyName),
            ("Грузоотправитель", Join(d.ShipperName, d.ShipperUnp.Length>0 ? "УНП " + d.ShipperUnp : "", d.ShipperAddress)),
            ("Грузополучатель", Join(d.ConsigneeName, d.ConsigneeUnp.Length>0 ? "УНП " + d.ConsigneeUnp : "", d.ConsigneeAddress)),
            ("Заказчик перевозки", Join(d.PayerName, d.PayerUnp.Length>0 ? "УНП " + d.PayerUnp : "")), ("Основание отпуска", d.Basis),
            ("Автомобиль / прицеп", Join(d.Vehicle, d.Trailer)), ("Водитель / путевой лист", Join(d.Driver, d.WaybillNumber.Length>0 ? "п/л № " + d.WaybillNumber : "")),
            ("Пункт погрузки", d.LoadingPoint), ("Пункт разгрузки", d.UnloadingPoint),
            ("Отпуск разрешил", d.ReleaseAllowedBy), ("Сдал грузоотправитель", d.HandedOverBy), ("Комментарий", d.Comment)
        };
        var rows=(details.Count+1)/2;
        for (var i=0;i<rows;i++) DetailsGrid.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
        for (var i=0;i<details.Count;i++)
        {
            var (label,value)=details[i];
            var column=i<rows ? 0 : 3; var row=i<rows ? i : i-rows;
            var l=new TextBlock { Text=label + ":", Foreground=System.Windows.Media.Brushes.SlateGray, Margin=new Thickness(0,2,10,2) };
            var v=new TextBlock { Text=string.IsNullOrWhiteSpace(value) ? "—" : value, TextWrapping=TextWrapping.Wrap, Margin=new Thickness(0,2,0,2), FontWeight=FontWeights.SemiBold };
            Grid.SetRow(l,row); Grid.SetColumn(l,column); Grid.SetRow(v,row); Grid.SetColumn(v,column+1);
            DetailsGrid.Children.Add(l); DetailsGrid.Children.Add(v);
        }
        LinesGrid.ItemsSource=d.Lines;
        var t=d.Totals; var culture=CultureInfo.CurrentCulture;
        TotalsText.Text=$"Итого: позиций {d.Lines.Count} · количество {t.Quantity.ToString("N3",culture)} · стоимость {t.Amount.ToString("N2",culture)} BYN"
            + (d.Kind==OperationKind.Transfer ? " · без НДС" : $" · НДС {t.VatAmount.ToString("N2",culture)} · с НДС {t.AmountWithVat.ToString("N2",culture)} BYN")
            + $" · грузовых мест {t.CargoPlaces.ToString("0.###",culture)} · масса {t.CargoMassTonnes.ToString("0.000",culture)} т";
        PrintButton.Content=d.Kind==OperationKind.Receipt ? "Печать (данные ТТН поставщика)…" : "Печать ТТН…";
    }
    private static string Join(params string[] parts) => string.Join(", ", parts.Where(x=>!string.IsNullOrWhiteSpace(x)));
    private void Print_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
        new TtnPrintWindow(_document,_context.Repository.GetOrganization()) { Owner=this }.Show());
    private void History_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (LinesGrid.SelectedItem is not DocumentLine line) { Ui.Error("Выберите позицию документа."); return; }
        new ProductHistoryWindow(_context,line.ProductId,line.ProductName) { Owner=this }.Show();
    });
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
