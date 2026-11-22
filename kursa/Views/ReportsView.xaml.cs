using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using kursa.Infrastructure;
using Microsoft.Win32;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace kursa.Views;
public partial class ReportsView : UserControl
{
    private readonly WorkspaceContext _context;
    private readonly StocksView _stocks;
    private readonly TurnoverView _turnover;
    private bool _hasReport;
    private string? _reportKind;
    public ReportsView(WorkspaceContext context, StocksView stocks, TurnoverView turnover)
    {
        InitializeComponent(); _context=context; _stocks=stocks; _turnover=turnover;
        context.DataChanged += (_,_) => ClearReport("Данные изменились. Сформируйте отчёт заново.");
        stocks.ResultsChanged += (_,_) => { if (_reportKind=="stocks") ClearReport("Фильтр остатков обновлён. Сформируйте отчёт заново."); };
        turnover.ResultsInvalidated += (_,_) => { if (_reportKind=="turnover") ClearReport("Параметры расчёта изменились. Сформируйте отчёт заново."); };
    }
    private void ClearReport(string message)
    {
        _hasReport=false; _reportKind=null; ReportBox.Text=message; Chart.Model=null;
    }
    private void Stock_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        _stocks.Refresh();
        var rows=_stocks.LastRows ?? throw new InvalidOperationException("Примените изменённые фильтры на вкладке остатков и повторите формирование отчёта.");
        var report=new StringBuilder();
        report.AppendLine("ОТЧЁТ О ТЕКУЩИХ СКЛАДСКИХ ОСТАТКАХ");
        report.AppendLine($"Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm:ss} (местное время)");
        report.AppendLine(_stocks.AppliedSummary); report.AppendLine(new string('=',75));
        foreach(var row in rows)
            report.AppendLine($"{row.WarehouseName} | {row.ProductName} | {row.Quantity:N3} {row.Unit} | закупка {row.PurchaseValue:N2} BYN | продажа {row.SaleValue:N2} BYN");
        report.AppendLine(new string('-',75));
        var purchase=rows.Sum(r=>r.PurchaseValue); var sale=rows.Sum(r=>r.SaleValue);
        report.AppendLine($"Закупочная стоимость: {purchase:N2} BYN"); report.AppendLine($"Продажная стоимость:  {sale:N2} BYN");
        report.AppendLine($"Потенциальная разница: {sale-purchase:N2} BYN");
        report.AppendLine("Оценка по текущим ценам справочника. Отчёт не является фактической прибылью.");
        ReportBox.Text=report.ToString(); _hasReport=true; _reportKind="stocks";
        var groups=rows.GroupBy(r=>new {r.WarehouseId,r.WarehouseName}).Select(g=>(Label:$"{g.Key.WarehouseName} (ID {g.Key.WarehouseId})",Value:g.Sum(x=>x.PurchaseValue))).OrderByDescending(x=>x.Value).ToList();
        DrawBars("Закупочная стоимость по складам", "BYN", groups);
    });
    private void Turnover_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        var rows=_turnover.LastRows ?? throw new InvalidOperationException("Сначала выберите товары и нажмите «Применить» на вкладке оборачиваемости.");
        var report=new StringBuilder();
        report.AppendLine("ОТЧЁТ ПО ОБОРАЧИВАЕМОСТИ"); report.AppendLine($"Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm:ss} (местное время)");
        report.AppendLine(_turnover.AppliedSummary); report.AppendLine(new string('=',75));
        foreach(var row in rows)
            report.AppendLine($"{row.ProductName} | средний запас {row.AverageStock:N3} {row.Unit} | отгружено {row.Sold:N3} | коэффициент {row.CoefficientDisplay} | дней {row.DaysDisplay} | {row.Status}");
        report.AppendLine(new string('-',75));
        report.AppendLine("Средний запас взвешен по длительности между движениями. «—» означает, что показатель не определён.");
        ReportBox.Text=report.ToString(); _hasReport=true; _reportKind="turnover";
        DrawBars("Оборачиваемость выбранных товаров", "Оборотов за период", rows.Where(r=>r.Coefficient.HasValue)
            .Select(r=>(Label:r.ProductName,Value:r.Coefficient!.Value)).OrderByDescending(r=>r.Value).ToList());
    });
    private void DrawBars(string title,string axisTitle,IReadOnlyList<(string Label,decimal Value)> values)
    {
        var model=new PlotModel { Title=values.Count==0 ? title+" · нет данных" : title, TextColor=OxyColor.FromRgb(23,36,59), PlotAreaBorderColor=OxyColors.Transparent, DefaultFont="Segoe UI" };
        var category=new CategoryAxis { Position=AxisPosition.Left, IsPanEnabled=false, IsZoomEnabled=false, FontSize=11 };
        var numeric=new LinearAxis { Position=AxisPosition.Bottom, Minimum=0, MaximumPadding=0.25, Title=axisTitle, IsPanEnabled=false, IsZoomEnabled=false, MajorGridlineStyle=LineStyle.Solid, MajorGridlineColor=OxyColor.FromRgb(237,241,246) };
        var bars=new BarSeries { FillColor=OxyColor.FromRgb(23,105,210), StrokeThickness=0, LabelFormatString="{0:0.##}" };
        foreach(var item in values) { category.Labels.Add(item.Label); bars.Items.Add(new BarItem {Value=(double)item.Value}); }
        model.Axes.Add(category); model.Axes.Add(numeric); model.Series.Add(bars);
        Chart.Height=Math.Max(380,Math.Min(3000,values.Count*34+100)); Chart.Model=model;
    }
    private void Save_Click(object sender, RoutedEventArgs e) => Ui.Run(() =>
    {
        if (!_hasReport) throw new InvalidOperationException("Сначала сформируйте отчёт.");
        var dialog=new SaveFileDialog { Filter="Текстовый файл (*.txt)|*.txt",FileName=$"Report_{DateTime.Now:yyyyMMdd_HHmm}.txt", AddExtension=true };
        if (dialog.ShowDialog(Window.GetWindow(this))!=true) return;
        File.WriteAllText(dialog.FileName,ReportBox.Text,new UTF8Encoding(false));
        MessageBox.Show("Текст отчёта сохранён.","Склад",MessageBoxButton.OK,MessageBoxImage.Information);
    });
}
