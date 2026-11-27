using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;
public partial class ProductWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly int _id;
    public ProductWindow(WorkspaceContext context, Product? product)
    {
        InitializeComponent(); _context=context; _id=product?.Id ?? 0;
        Heading.Text=_id==0 ? "Новый товар" : "Изменить товар";
        NameBox.Text=product?.Name ?? ""; UnitBox.Text=product?.Unit ?? "шт";
        PurchaseBox.Text=Ui.FormatNumber(product?.PurchasePrice ?? 0); SaleBox.Text=Ui.FormatNumber(product?.SalePrice ?? 0);
        UnitWeightBox.Text=product?.UnitWeightKg?.ToString("0.######",System.Globalization.CultureInfo.CurrentCulture) ?? "";
        UnitsPerPlaceBox.Text=product?.UnitsPerCargoPlace?.ToString("0.###",System.Globalization.CultureInfo.CurrentCulture) ?? "";
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        _context.Repository.SaveProduct(new Product { Id=_id, Name=NameBox.Text, Unit=UnitBox.Text,
            PurchasePrice=Ui.ParseNumber(PurchaseBox.Text,"Закупочная цена"), SalePrice=Ui.ParseNumber(SaleBox.Text,"Продажная цена"), UnitWeightKg=Ui.ParseOptionalNumber(UnitWeightBox.Text,"Масса единицы"),
            UnitsPerCargoPlace=Ui.ParseOptionalNumber(UnitsPerPlaceBox.Text,"Единиц на грузовое место") }, _context.User);
        _context.NotifyDataChanged(); DialogResult=true;
    });
}
