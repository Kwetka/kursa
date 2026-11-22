using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Warehouse.Core.Models;

namespace kursa.Infrastructure;

public static class Ui
{
    public static void Error(string message) => MessageBox.Show(message, "Склад · ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
    public static bool Run(Action action)
    {
        try { action(); return true; }
        catch (Exception ex) { Error(ex.Message); return false; }
    }
    public static decimal ParseNumber(string? input, string field) => Warehouse.Core.Services.InputParser.Number(input, field);
    public static string FormatNumber(decimal value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    public static (DateTime? From, DateTime? ToExclusive) BuildInterval(DateTime? fromDate, string? fromTime, DateTime? toDate, string? toTime)
        => Warehouse.Core.Services.InputParser.Interval(fromDate, fromTime, toDate, toTime);
    public static DateTime? ReadDate(DatePicker picker, string field)
    {
        if (picker.SelectedDate is null && !string.IsNullOrWhiteSpace(picker.Text))
            throw new ArgumentException($"{field}: выберите корректную дату.");
        return picker.SelectedDate;
    }
    public static void SetLookup(ComboBox box, IEnumerable<LookupItem> values, int? selected = null)
    {
        box.ItemsSource = values.ToList(); box.DisplayMemberPath = nameof(LookupItem.Name); box.SelectedValuePath = nameof(LookupItem.Id);
        if (selected.HasValue) box.SelectedValue = selected.Value;
        if (box.SelectedIndex < 0) box.SelectedIndex = 0;
    }
    public static int? SelectedId(ComboBox box) => (box.SelectedItem as LookupItem)?.Id;
    public static bool Confirm(string question) => MessageBox.Show(question, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
