using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using kursa.Infrastructure;
using Warehouse.Core.Models;
using Warehouse.Core.Services;
using WarehouseEntity = Warehouse.Core.Models.Warehouse;

namespace kursa.Views.Dialogs;

/// <summary>Новый документ с несколькими товарами: приход, реализация или перемещение.</summary>
public partial class DocumentWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly OperationKind _kind;
    private readonly OrganizationProfile _organization;
    private readonly ObservableCollection<DocumentLineEditor> _lines = new();
    private Dictionary<int, decimal> _available = new();

    public DocumentWindow(WorkspaceContext context, OperationKind kind)
    {
        InitializeComponent(); _context=context; _kind=kind;
        var repository=context.Repository;
        _organization=repository.GetOrganization();
        Heading.Text=kind switch { OperationKind.Receipt=>"Приход товаров", OperationKind.Shipment=>"Реализация товаров (ТТН)", _=>"Перемещение товаров между складами" };
        SubHeading.Text=kind switch
        {
            OperationKind.Receipt=>"Оприходование по накладной поставщика. Укажите номер и дату его ТТН. Дата и время проведения назначаются SQL Server.",
            OperationKind.Shipment=>"Отпуск покупателю. Грузоотправитель — ваша организация, грузополучатель — покупатель. Дата и время проведения назначаются SQL Server.",
            _=>"Внутреннее перемещение без НДС. Стоимость — по закупочным (учётным) ценам. Дата и время проведения назначаются SQL Server."
        };
        Title=Heading.Text;

        var products=repository.GetProducts(); var warehouses=repository.GetWarehouses();
        var parties=repository.GetCounterparties().Where(x=>x.IsSupplier==(kind==OperationKind.Receipt)).ToList();
        ProductBox.ItemsSource=products; ProductBox.SelectedIndex=products.Count>0 ? 0 : -1;
        FromBox.ItemsSource=warehouses; ToBox.ItemsSource=warehouses;
        PartyBox.ItemsSource=parties;
        PartyLabel.Text=kind==OperationKind.Receipt ? "Поставщик (грузоотправитель)" : "Покупатель (грузополучатель)";
        NumberLabel.Text=kind==OperationKind.Receipt ? "Номер ТТН поставщика" : "Номер ТТН";

        FromPanel.Visibility=kind==OperationKind.Receipt ? Visibility.Collapsed : Visibility.Visible;
        ToPanel.Visibility=kind==OperationKind.Shipment ? Visibility.Collapsed : Visibility.Visible;
        PartyPanel.Visibility=kind==OperationKind.Transfer ? Visibility.Collapsed : Visibility.Visible;
        DatePanel.Visibility=kind==OperationKind.Receipt ? Visibility.Visible : Visibility.Collapsed;
        AvailableColumn.Visibility=kind==OperationKind.Receipt ? Visibility.Collapsed : Visibility.Visible;
        DiscountColumn.Visibility=kind==OperationKind.Shipment ? Visibility.Visible : Visibility.Collapsed;
        var vat=kind==OperationKind.Transfer ? Visibility.Collapsed : Visibility.Visible;
        VatRateColumn.Visibility=VatAmountColumn.Visibility=TotalColumn.Visibility=vat;
        PayerBuyerButton.Visibility=kind==OperationKind.Shipment ? Visibility.Visible : Visibility.Collapsed;
        SavePrintButton.Visibility=kind==OperationKind.Receipt ? Visibility.Collapsed : Visibility.Visible;
        TtnExpander.IsExpanded=kind==OperationKind.Shipment;

        try
        {
            var suggestions=repository.GetDocumentSuggestions();
            VehicleBox.ItemsSource=suggestions.Vehicles; TrailerBox.ItemsSource=suggestions.Trailers;
            DriverBox.ItemsSource=suggestions.Drivers; PayerBox.ItemsSource=suggestions.Payers;
        }
        catch { /* Подсказки необязательны. */ }

        if (kind!=OperationKind.Receipt)
        {
            SetAuto(ReleaseBox,_organization.ReleaseAllowedBy);
            SetAuto(HandedBox,context.User.FullName);
        }
        if (kind!=OperationKind.Receipt && !_organization.IsFilled)
        {
            OrganizationHint.Text="Реквизиты вашей организации (наименование, УНП, адрес) не заполнены — в ТТН не будет данных грузоотправителя. "
                + (context.User.CanManageOrganization ? "Заполните их: кнопка «Моя организация» в верхней панели." : "Обратитесь к администратору.");
            OrganizationHint.Visibility=Visibility.Visible;
        }

        LinesGrid.ItemsSource=_lines;
        _lines.CollectionChanged+=(_,_) => { Renumber(); UpdateTotals(); };
        FromBox.SelectedIndex=warehouses.Count>0 ? 0 : -1;
        ToBox.SelectedIndex=warehouses.Count>0 ? (kind==OperationKind.Transfer && warehouses.Count>1 ? 1 : 0) : -1;
        PartyBox.SelectedIndex=parties.Count>0 ? 0 : -1;
        if (kind==OperationKind.Transfer) { SetAuto(BasisBox,"Внутреннее перемещение"); SetAuto(PayerBox,_organization.Name); SetAuto(PayerUnpBox,_organization.Unp); }
        UpdateTotals();
        if (products.Count==0 || warehouses.Count==0 || kind!=OperationKind.Transfer && parties.Count==0)
            AvailabilityHint.Text="Если список пуст, сначала добавьте товары, склады и контрагентов в справочниках.";
    }

    /// <summary>Подставляет значение по умолчанию, если пользователь не вводил своё.</summary>
    private static void SetAuto(Control box, string? value)
    {
        value=(value ?? "").Trim();
        var current=box switch { TextBox t=>t.Text, ComboBox c=>c.Text, _=>"" };
        if (!string.IsNullOrWhiteSpace(current) && current!=box.Tag as string) return;
        if (box is TextBox text) text.Text=value; else if (box is ComboBox combo) combo.Text=value;
        box.Tag=value;
    }

    private WarehouseEntity? From => FromBox.SelectedItem as WarehouseEntity;
    private WarehouseEntity? To => ToBox.SelectedItem as WarehouseEntity;
    private Counterparty? Party => PartyBox.SelectedItem as Counterparty;

    private void FromBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_kind==OperationKind.Receipt) return;
        SetAuto(LoadingBox,From is { } w ? $"{w.Name}, {w.Address}" : "");
        LoadAvailability();
    }
    private void ToBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_kind==OperationKind.Shipment) return;
        SetAuto(UnloadingBox,To is { } w ? $"{w.Name}, {w.Address}" : "");
    }
    private void PartyBox_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Party is not { } party) return;
        SetAuto(BasisBox,party.Contract);
        if (_kind==OperationKind.Shipment)
        {
            SetAuto(UnloadingBox,string.IsNullOrWhiteSpace(party.DeliveryAddress) ? party.Address : party.DeliveryAddress);
            SetAuto(PayerBox,party.Name); SetAuto(PayerUnpBox,party.Unp);
        }
        else if (_kind==OperationKind.Receipt) SetAuto(LoadingBox,party.Address);
    }
    private void PayerBuyer_Click(object sender, RoutedEventArgs e)
    {
        if (Party is not { } party) return;
        PayerBox.Text=party.Name; PayerUnpBox.Text=party.Unp;
    }
    private void PayerOrganization_Click(object sender, RoutedEventArgs e) { PayerBox.Text=_organization.Name; PayerUnpBox.Text=_organization.Unp; }

    private void LoadAvailability() => Ui.Run(() =>
    {
        _available=From is { } w
            ? _context.Repository.GetStocks(new StockFilter { WarehouseId=w.Id, IncludeZero=true }).ToDictionary(x=>x.ProductId,x=>x.Quantity)
            : new Dictionary<int, decimal>();
        foreach (var line in _lines) line.Available=_available.GetValueOrDefault(line.Product.Id);
        AvailabilityHint.Text=From is { } source ? $"Остатки показаны по складу «{source.Name}» на момент открытия окна." : "";
    });

    private void AddLine()
    {
        if (ProductBox.SelectedItem is not Product product)
        {
            // Пользователь мог ввести название вручную — ищем точное совпадение.
            product=(ProductBox.ItemsSource as IEnumerable<Product>)?.FirstOrDefault(p=>string.Equals(p.Name,ProductBox.Text.Trim(),StringComparison.CurrentCultureIgnoreCase))
                ?? throw new ArgumentException("Выберите товар из списка.");
        }
        var quantity=Ui.ParseNumber(QuantityBox.Text,"Количество");
        Warehouse.Core.Services.Validation.Quantity(quantity);
        var existing=_lines.FirstOrDefault(x=>x.Product.Id==product.Id);
        if (existing is not null) { existing.AddQuantity(quantity); LinesGrid.ScrollIntoView(existing); }
        else
        {
            if (_lines.Count>=500) throw new ArgumentException("В одном документе не более 500 позиций.");
            var line=new DocumentLineEditor(product,_kind,quantity) { Available=_available.GetValueOrDefault(product.Id) };
            line.PropertyChanged+=Line_PropertyChanged;
            _lines.Add(line); LinesGrid.ScrollIntoView(line);
        }
        QuantityBox.Text="1"; UpdateTotals(); ProductBox.Focus();
    }
    private void AddLine_Click(object sender, RoutedEventArgs e) => Ui.Run(AddLine);
    private void QuantityBox_KeyDown(object sender, KeyEventArgs e) { if (e.Key==Key.Enter) { Ui.Run(AddLine); e.Handled=true; } }
    private void RemoveLine_Click(object sender, RoutedEventArgs e)
    {
        var line=LinesGrid.CurrentCell.Item as DocumentLineEditor ?? LinesGrid.SelectedCells.Select(c=>c.Item).OfType<DocumentLineEditor>().FirstOrDefault();
        if (line is null) { Ui.Error("Выберите строку документа."); return; }
        LinesGrid.CommitEdit(DataGridEditingUnit.Row,true);
        line.PropertyChanged-=Line_PropertyChanged; _lines.Remove(line);
    }
    private void LinesGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) => Dispatcher.BeginInvoke(UpdateTotals);
    private void Line_PropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateTotals();
    private void Renumber() { for (var i=0;i<_lines.Count;i++) _lines[i].Number=i+1; }

    private void UpdateTotals()
    {
        var culture=CultureInfo.CurrentCulture;
        decimal amount=_lines.Sum(x=>x.Amount ?? 0m), vat=_lines.Sum(x=>x.VatAmount ?? 0m);
        decimal places=_lines.Sum(x=>x.Places ?? 0m), mass=_lines.Sum(x=>x.MassKg ?? 0m);
        var text=$"Позиций: {_lines.Count} · стоимость без НДС: {amount.ToString("N2",culture)} BYN";
        if (_kind!=OperationKind.Transfer) text+=$" · НДС: {vat.ToString("N2",culture)} · с НДС: {(amount+vat).ToString("N2",culture)} BYN";
        text+=$" · грузовых мест: {places.ToString("0.###",culture)} · масса: {(mass/1000m).ToString("0.###",culture)} т";
        var shortLines=_lines.Count(x=>x.IsShort);
        if (shortLines>0) text+=$" · не хватает остатка: {shortLines} поз.";
        TotalsText.Text=text;
    }

    private DocumentInput BuildInput()
    {
        LinesGrid.CommitEdit(DataGridEditingUnit.Row,true);
        if (_lines.Count==0) throw new ArgumentException("Добавьте в документ хотя бы один товар.");
        int? from=_kind==OperationKind.Receipt ? null : From?.Id ?? throw new ArgumentException("Выберите склад-отправитель.");
        int? to=_kind==OperationKind.Shipment ? null : To?.Id ?? throw new ArgumentException("Выберите склад-получатель.");
        int? party=_kind==OperationKind.Transfer ? null : Party?.Id ?? throw new ArgumentException(_kind==OperationKind.Receipt ? "Выберите поставщика." : "Выберите покупателя.");
        return new DocumentInput(_kind,from,to,party,_lines.Select(x=>x.ToInput()).ToList())
        {
            Number=NumberBox.Text, DocumentDate=_kind==OperationKind.Receipt ? Ui.ReadDate(DocumentDatePicker,"Дата накладной") : null,
            Comment=CommentBox.Text, PayerName=PayerBox.Text, PayerUnp=PayerUnpBox.Text, Vehicle=VehicleBox.Text, Trailer=TrailerBox.Text,
            Driver=DriverBox.Text, WaybillNumber=WaybillBox.Text, Basis=BasisBox.Text, LoadingPoint=LoadingBox.Text, UnloadingPoint=UnloadingBox.Text,
            ReleaseAllowedBy=ReleaseBox.Text, HandedOverBy=HandedBox.Text
        };
    }

    private void Save(bool print) => Ui.Run(() =>
    {
        var input=BuildInput();
        var shortLines=_lines.Where(x=>x.IsShort).Select(x=>x.ProductName).ToList();
        if (shortLines.Count>0 && !Ui.Confirm($"По остаткам на момент открытия окна не хватает: {string.Join(", ",shortLines)}.\nВсё равно попробовать провести? Сервер проверит актуальные остатки."))
            return;
        var id=_context.Repository.CreateDocument(input,_context.User);
        _context.NotifyDataChanged();
        var document=_context.Repository.GetDocument(id);
        if (print && document is not null) new TtnPrintWindow(document,_context.Repository.GetOrganization()) { Owner=Owner }.Show();
        else MessageBox.Show($"{Documents.KindName(_kind)} № {document?.NumberDisplay ?? id.ToString()} проведён(а): позиций {_lines.Count}.","Склад",MessageBoxButton.OK,MessageBoxImage.Information);
        DialogResult=true;
    });
    private void Save_Click(object sender, RoutedEventArgs e) => Save(false);
    private void SavePrint_Click(object sender, RoutedEventArgs e) => Save(true);
}
