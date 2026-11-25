using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;

/// <summary>Предварительный просмотр и печать ТТН (альбомная ориентация).</summary>
public partial class TtnPrintWindow : Window
{
    private readonly StockDocument _document;
    private readonly OrganizationProfile _organization;
    public TtnPrintWindow(StockDocument document, OrganizationProfile organization)
    {
        InitializeComponent(); _document=document; _organization=organization;
        Heading.Text=$"ТТН № {document.NumberDisplay} от {document.EffectiveDate:dd.MM.yyyy} · {document.KindName}";
        Title=Heading.Text;
        var warnings=new List<string>();
        if (string.IsNullOrWhiteSpace(document.ShipperName) || string.IsNullOrWhiteSpace(document.ShipperUnp)) warnings.Add("не заполнены наименование или УНП грузоотправителя");
        if (string.IsNullOrWhiteSpace(document.ConsigneeName) || string.IsNullOrWhiteSpace(document.ConsigneeUnp)) warnings.Add("не заполнены наименование или УНП грузополучателя");
        if (string.IsNullOrWhiteSpace(document.Number)) warnings.Add("номер ТТН не указан — впишите номер бланка вручную");
        if (document.IsLegacy) warnings.Add("документ перенесён из прежней версии программы, реквизиты ТТН в нём не сохранялись");
        WarningText.Text=warnings.Count==0 ? "" : "Внимание: " + string.Join("; ",warnings) + ".";
        var preview=TtnDocumentBuilder.Build(document,organization);
        preview.PageWidth=1080;
        Viewer.Document=preview;
    }
    private void Print_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        var dialog=new PrintDialog();
        dialog.PrintTicket.PageOrientation=PageOrientation.Landscape;
        if (dialog.ShowDialog()!=true) return;
        // Для печати строится отдельный экземпляр: документ, показанный в окне, нельзя передать второму владельцу.
        var document=TtnDocumentBuilder.Build(_document,_organization);
        document.PageWidth=dialog.PrintableAreaWidth; document.PageHeight=dialog.PrintableAreaHeight;
        document.ColumnWidth=dialog.PrintableAreaWidth;
        dialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator,$"ТТН {_document.NumberDisplay}");
    });
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
