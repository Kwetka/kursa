using System.Windows;
using kursa.Infrastructure;
using Warehouse.Core.Models;

namespace kursa.Views.Dialogs;
public partial class CounterpartyWindow : Window
{
    private readonly WorkspaceContext _context;
    private readonly int _id;
    public CounterpartyWindow(WorkspaceContext context, Counterparty? party)
    {
        InitializeComponent(); _context=context; _id=party?.Id ?? 0;
        Heading.Text=_id==0 ? "Новый контрагент" : "Изменить контрагента";
        NameBox.Text=party?.Name ?? ""; TypeBox.SelectedIndex=party?.IsSupplier == false ? 1 : 0;
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        _context.Repository.SaveCounterparty(new Counterparty { Id=_id,Name=NameBox.Text,IsSupplier=TypeBox.SelectedIndex==0 },_context.User);
        _context.NotifyDataChanged(); DialogResult=true;
    });
}
