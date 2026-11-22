using System.Globalization;

namespace Warehouse.Core.Services;

public static class InputParser
{
    // Accept both common decimal separators without interpreting '.' as a group separator.
    public static decimal Number(string? input, string field)
    {
        var text = (input ?? "").Trim().Replace(" ", "").Replace("\u00A0", "");
        if (text.Contains(',') && text.Contains('.')) throw new ArgumentException($"{field}: используйте одну запятую или точку для десятичной части.");
        text = text.Replace(',', '.');
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result))
            throw new ArgumentException($"{field}: введите число, например 12,50.");
        return result;
    }

    // Dates are inclusive in the UI. An optional end time includes the entire minute.
    public static (DateTime? From, DateTime? ToExclusive) Interval(DateTime? fromDate, string? fromTime, DateTime? toDate, string? toTime)
    {
        static TimeSpan? ReadTime(string? text, string label)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!TimeOnly.TryParseExact(text.Trim(), new[] { "H:mm", "HH:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new ArgumentException($"{label}: укажите время в формате ЧЧ:ММ, например 09:30.");
            return time.ToTimeSpan();
        }
        var startTime = ReadTime(fromTime, "Начало периода");
        var endTime = ReadTime(toTime, "Конец периода");
        if (startTime.HasValue && !fromDate.HasValue || endTime.HasValue && !toDate.HasValue)
            throw new ArgumentException("Для указанного времени выберите соответствующую дату.");
        DateTime? start = fromDate?.Date.Add(startTime ?? TimeSpan.Zero);
        DateTime? end = null;
        if (toDate.HasValue)
        {
            if (toDate.Value.Date == DateTime.MaxValue.Date) throw new ArgumentException("Выберите более раннюю конечную дату.");
            end = endTime.HasValue ? toDate.Value.Date.Add(endTime.Value).AddMinutes(1) : toDate.Value.Date.AddDays(1);
        }
        Validation.Interval(start, end);
        return (start, end);
    }
}
