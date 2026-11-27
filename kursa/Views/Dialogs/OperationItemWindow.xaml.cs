using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using Warehouse.Core.Models;
using Warehouse.Core.Services;

namespace kursa.Views.Dialogs;
public partial class OperationItemWindow : Window
{
    private readonly OperationKind _kind;
    private readonly bool _withoutVat;
    private bool _ready, _settingCargo, _cargoManuallyChanged;
    public OperationItemView? Result { get; private set; }
    public OperationItemWindow(IReadOnlyList<Product> products, OperationKind kind, OperationItemView? existing=null, bool withoutVat=false)
    {
        InitializeComponent(); _kind=kind; _withoutVat=withoutVat; VatBox.IsEnabled=!withoutVat; SalePanel.Visibility=kind==OperationKind.Shipment ? Visibility.Visible : Visibility.Collapsed;
        ProductBox.ItemsSource=products;
        ProductBox.SelectedItem=existing is null ? products.FirstOrDefault() : products.FirstOrDefault(p=>p.Id==existing.Product.Id);
        if (existing is not null)
        {
            QuantityBox.Text=existing.Input.Quantity.ToString("0.###",CultureInfo.CurrentCulture);
            PriceBox.Text=existing.Input.PriceBeforeDiscount?.ToString("0.##",CultureInfo.CurrentCulture)??"";
            DiscountBox.Text=existing.Input.DiscountPercent.ToString("0.##",CultureInfo.CurrentCulture);
            VatBox.Text=withoutVat ? "" : existing.Input.VatRate?.ToString("0.####",CultureInfo.CurrentCulture)??"";
            PlacesBox.Text=existing.Input.CargoPlaces?.ToString(CultureInfo.CurrentCulture)??"";
            MassBox.Text=existing.Input.MassTonnes?.ToString("0.######",CultureInfo.CurrentCulture)??"";
            NoteBox.Text=existing.Input.Note; _cargoManuallyChanged=true;
        }
        _ready=true;
        if (existing is null) OnProductChanged();
        else UnitText.Text="Единица измерения: "+(ProductBox.SelectedItem as Product)?.Unit;
    }
    private void Product_Changed(object sender,SelectionChangedEventArgs e) { if (_ready) OnProductChanged(); }
    private void OnProductChanged()
    {
        var product=ProductBox.SelectedItem as Product; UnitText.Text="Единица измерения: "+(product?.Unit??"—");
        PriceBox.Text=product is null ? "" : Ui.FormatNumber(product.SalePrice);
        _cargoManuallyChanged=false; CalculateCargo();
    }
    private void Quantity_Changed(object sender,TextChangedEventArgs e) { if (_ready && !_cargoManuallyChanged) CalculateCargo(); }
    private void Cargo_Changed(object sender,TextChangedEventArgs e) { if (_ready && !_settingCargo) _cargoManuallyChanged=true; }
    private void CalculateCargo_Click(object sender,RoutedEventArgs e) { _cargoManuallyChanged=false; CalculateCargo(); }
    private void CalculateCargo()
    {
        if (_kind!=OperationKind.Shipment || ProductBox.SelectedItem is not Product product) return;
        decimal quantity;
        try { quantity=Ui.ParseNumber(QuantityBox.Text,"Количество"); if (quantity<=0 || quantity>1_000_000_000m) return; }
        catch (ArgumentException) { return; }
        _settingCargo=true;
        try
        {
            PlacesBox.Text=product.UnitsPerCargoPlace is decimal units && decimal.Ceiling(quantity/units)<=1_000_000_000m
                ? decimal.Ceiling(quantity/units).ToString("0",CultureInfo.CurrentCulture) : "";
            MassBox.Text=product.UnitWeightKg is decimal weight
                ? decimal.Round(quantity*weight/1000m,6,MidpointRounding.AwayFromZero).ToString("0.######",CultureInfo.CurrentCulture) : "";
        }
        finally { _settingCargo=false; }
    }
    private void Save_Click(object sender,RoutedEventArgs e) => Ui.Run(() =>
    {
        var product=ProductBox.SelectedItem as Product ?? throw new ArgumentException("Выберите товар.");
        var quantity=Ui.ParseNumber(QuantityBox.Text,"Количество"); Warehouse.Core.Services.Validation.Quantity(quantity);
        OperationItemInput item;
        if (_kind==OperationKind.Shipment)
        {
            int? places=null;
            if (!string.IsNullOrWhiteSpace(PlacesBox.Text))
            {
                if (!int.TryParse(PlacesBox.Text.Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var n)) throw new ArgumentException("Количество грузовых мест: введите целое число.");
                places=n;
            }
            item=new OperationItemInput(product.Id,quantity,Ui.ParseNumber(PriceBox.Text,"Цена"),Ui.ParseNumber(DiscountBox.Text,"Скидка"),
                _withoutVat ? null : Ui.ParseOptionalNumber(VatBox.Text,"Ставка НДС"),places,Ui.ParseOptionalNumber(MassBox.Text,"Масса"),NoteBox.Text);
            _=InvoiceCalculator.Product(item,product,_withoutVat ? VatPriceMode.WithoutVat : VatPriceMode.Included);
        }
        else item=new OperationItemInput(product.Id,quantity);
        Result=new OperationItemView(product,item); DialogResult=true;
    });
}
