using Warehouse.Core.Models;

namespace Warehouse.Core.Services;

public static class Validation
{
    public static string Required(string? value, string field, int maxLength)
    {
        var result = (value ?? "").Trim();
        if (result.Length == 0) throw new ArgumentException($"Заполните поле «{field}».");
        if (result.Length > maxLength) throw new ArgumentException($"Поле «{field}»: не более {maxLength} символов.");
        return result;
    }
    public static string Optional(string? value, string field, int maxLength)
    {
        var result = (value ?? "").Trim();
        if (result.Length > maxLength) throw new ArgumentException($"Поле «{field}»: не более {maxLength} символов.");
        return result;
    }
    public static void Price(decimal value, string field)
    {
        if (value <= 0 || value > 1_000_000_000m || decimal.Round(value, 2) != value)
            throw new ArgumentException($"{field}: больше нуля и не больше 1 000 000 000 BYN, не более двух знаков после запятой.");
    }
    public static void Quantity(decimal value)
    {
        if (value <= 0 || value > 1_000_000_000m || decimal.Round(value, 3) != value)
            throw new ArgumentException("Количество должно быть больше нуля и не больше 1 000 000 000, не более трёх знаков после запятой.");
    }
    public static void Interval(DateTime? fromInclusive, DateTime? toExclusive)
    {
        if (fromInclusive.HasValue && toExclusive.HasValue && fromInclusive.Value >= toExclusive.Value)
            throw new ArgumentException("Начало периода должно быть раньше конца.");
    }
    public static void Password(string? password)
    {
        if (password is null || password.Length < 8 || password.Length > 200 || string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Новый пароль должен содержать от 8 до 200 символов.");
    }
    public static void Operation(OperationInput operation)
    {
        if (!Enum.IsDefined(operation.Kind) || operation.ProductId <= 0) throw new ArgumentException("Выберите товар и тип операции.");
        Quantity(operation.Quantity);
        switch (operation.Kind)
        {
            case OperationKind.Receipt when operation.FromWarehouseId is null && operation.ToWarehouseId > 0 && operation.CounterpartyId > 0:
            case OperationKind.Shipment when operation.FromWarehouseId > 0 && operation.ToWarehouseId is null && operation.CounterpartyId > 0:
            case OperationKind.Transfer when operation.FromWarehouseId > 0 && operation.ToWarehouseId > 0 && operation.FromWarehouseId != operation.ToWarehouseId && operation.CounterpartyId is null:
                break;
            default:
                throw new ArgumentException("Проверьте склады и контрагента. Для перемещения склады должны различаться.");
        }
        Optional(operation.Comment, "Комментарий", 500);
    }

    /// <summary>УНП Республики Беларусь: 9 символов (цифры или заглавные латинские буквы). Пустое значение допустимо.</summary>
    public static string Unp(string? value, string field)
    {
        var result = (value ?? "").Trim().Replace(" ", "").ToUpperInvariant();
        if (result.Length == 0) return result;
        if (result.Length != 9 || result.Any(ch => !(ch is >= '0' and <= '9' or >= 'A' and <= 'Z')))
            throw new ArgumentException($"{field}: УНП состоит из 9 символов (цифры или латинские буквы).");
        return result;
    }
    public static void VatRate(decimal value, string field = "Ставка НДС")
    {
        if (value < 0 || value > 100 || decimal.Round(value, 2) != value)
            throw new ArgumentException($"{field}: от 0 до 100 %, не более двух знаков после запятой.");
    }
    public static void Discount(decimal value)
    {
        if (value < 0 || value >= 100 || decimal.Round(value, 2) != value)
            throw new ArgumentException("Скидка: от 0 до 99,99 %, не более двух знаков после запятой.");
    }
    public static void PositiveMeasure(decimal value, string field, bool allowZero = false)
    {
        if ((allowZero ? value < 0 : value <= 0) || value > 1_000_000_000m || decimal.Round(value, 3) != value)
            throw new ArgumentException($"{field}: {(allowZero ? "не меньше нуля" : "больше нуля")}, не более 1 000 000 000, не более трёх знаков после запятой.");
    }

    /// <summary>Проверяет и нормализует документ: маршрут, позиции, длины текстовых полей.</summary>
    public static DocumentInput Document(DocumentInput document)
    {
        if (!Enum.IsDefined(document.Kind)) throw new ArgumentException("Неизвестный тип документа.");
        switch (document.Kind)
        {
            case OperationKind.Receipt when document.FromWarehouseId is null && document.ToWarehouseId > 0 && document.CounterpartyId > 0:
            case OperationKind.Shipment when document.FromWarehouseId > 0 && document.ToWarehouseId is null && document.CounterpartyId > 0:
            case OperationKind.Transfer when document.FromWarehouseId > 0 && document.ToWarehouseId > 0 && document.FromWarehouseId != document.ToWarehouseId && document.CounterpartyId is null:
                break;
            default:
                throw new ArgumentException("Проверьте склады и контрагента. Для перемещения склады должны различаться.");
        }
        if (document.Lines is null || document.Lines.Count == 0) throw new ArgumentException("Добавьте в документ хотя бы один товар.");
        if (document.Lines.Count > 500) throw new ArgumentException("В одном документе не более 500 позиций.");
        var seen = new HashSet<int>();
        var lines = new List<DocumentLineInput>();
        for (var i = 0; i < document.Lines.Count; i++)
        {
            var line = document.Lines[i] ?? throw new ArgumentException($"Позиция {i + 1}: не заполнена.");
            var prefix = $"Позиция {i + 1}";
            if (line.ProductId <= 0) throw new ArgumentException($"{prefix}: выберите товар.");
            if (!seen.Add(line.ProductId)) throw new ArgumentException($"{prefix}: товар уже есть в документе. Объедините количество в одной строке.");
            try
            {
                Quantity(line.Quantity);
                if (line.Price is decimal price) Price(price, "Цена");
                if (line.DiscountPercent is decimal discount)
                {
                    if (document.Kind != OperationKind.Shipment && discount != 0) throw new ArgumentException("Скидка указывается только при реализации.");
                    Discount(discount);
                }
                if (line.VatRate is decimal rate) VatRate(rate);
                if (line.CargoPlaces is decimal places) PositiveMeasure(places, "Количество грузовых мест", allowZero: true);
                if (line.CargoMassKg is decimal mass) PositiveMeasure(mass, "Масса груза, кг", allowZero: true);
            }
            catch (ArgumentException ex) { throw new ArgumentException($"{prefix}: {ex.Message}", ex); }
            lines.Add(line with
            {
                Note = Optional(line.Note, $"{prefix}, примечание", 500),
                DiscountPercent = line.DiscountPercent is 0m ? null : line.DiscountPercent,
                VatRate = document.Kind == OperationKind.Transfer ? null : line.VatRate
            });
        }
        if (document.DocumentDate is DateTime date && (date.Year < 2000 || date.Year > 2100))
            throw new ArgumentException("Проверьте дату накладной.");
        return document with
        {
            Lines = lines,
            Number = Optional(document.Number, "Номер ТТН", 30),
            DocumentDate = document.Kind == OperationKind.Receipt ? document.DocumentDate?.Date : null,
            Comment = Optional(document.Comment, "Комментарий", 500),
            PayerName = Optional(document.PayerName, "Заказчик перевозки (плательщик)", 300),
            PayerUnp = Unp(document.PayerUnp, "Заказчик перевозки"),
            Vehicle = Optional(document.Vehicle, "Автомобиль", 100),
            Trailer = Optional(document.Trailer, "Прицеп", 100),
            Driver = Optional(document.Driver, "Водитель", 200),
            WaybillNumber = Optional(document.WaybillNumber, "Путевой лист №", 50),
            Basis = Optional(document.Basis, "Основание отпуска", 300),
            LoadingPoint = Optional(document.LoadingPoint, "Пункт погрузки", 400),
            UnloadingPoint = Optional(document.UnloadingPoint, "Пункт разгрузки", 400),
            ReleaseAllowedBy = Optional(document.ReleaseAllowedBy, "Отпуск разрешил", 200),
            HandedOverBy = Optional(document.HandedOverBy, "Сдал грузоотправитель", 200)
        };
    }

    public static OrganizationProfile Organization(OrganizationProfile profile) => new()
    {
        Name = Required(profile.Name, "Наименование организации", 300),
        Unp = Unp(profile.Unp, "Организация") is { Length: 9 } unp ? unp : throw new ArgumentException("Укажите УНП организации (9 символов)."),
        Address = Required(profile.Address, "Адрес организации", 400),
        Phone = Optional(profile.Phone, "Телефон", 100),
        ReleaseAllowedBy = Optional(profile.ReleaseAllowedBy, "Отпуск разрешил", 200),
        DefaultVatRate = profile.DefaultVatRate is var rate && rate >= 0 && rate <= 100 && decimal.Round(rate, 2) == rate
            ? rate : throw new ArgumentException("Ставка НДС по умолчанию: от 0 до 100 %.")
    };
}
