using Warehouse.Core.Models;

namespace Warehouse.Core.Services;

/// <summary>Расчёт граф товарного раздела ТТН. НДС начисляется сверху на стоимость без НДС.</summary>
public static class Ttn
{
    /// <summary>Графа 6: стоимость = количество × цена, округление до копеек.</summary>
    public static decimal Amount(decimal quantity, decimal price) => decimal.Round(quantity * price, 2, MidpointRounding.AwayFromZero);

    /// <summary>Графа 8: сумма НДС = стоимость × ставка / 100, округление до копеек.</summary>
    public static decimal Vat(decimal amount, decimal ratePercent) => decimal.Round(amount * ratePercent / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>Цена со скидкой от базовой цены, округление до копеек.</summary>
    public static decimal Discounted(decimal price, decimal? discountPercent) =>
        discountPercent is decimal d and > 0 ? decimal.Round(price * (100m - d) / 100m, 2, MidpointRounding.AwayFromZero) : price;

    /// <summary>Грузовые места: количество / единиц в месте, округление вверх до целого.</summary>
    public static decimal? Places(decimal quantity, decimal? unitsPerPlace) =>
        unitsPerPlace is decimal units and > 0 ? Math.Ceiling(quantity / units) : null;

    /// <summary>Масса груза в кг по массе единицы.</summary>
    public static decimal? MassKg(decimal quantity, decimal? unitMassKg) =>
        unitMassKg is decimal mass and > 0 ? decimal.Round(quantity * mass, 3, MidpointRounding.AwayFromZero) : null;

    public static TtnTotals Totals(IEnumerable<DocumentLine> lines)
    {
        decimal quantity = 0, amount = 0, vat = 0, places = 0, massKg = 0;
        foreach (var line in lines)
        {
            quantity += line.Quantity;
            amount += line.Amount ?? 0;
            vat += line.VatAmount ?? 0;
            places += line.CargoPlaces ?? 0;
            massKg += line.CargoMassKg ?? 0;
        }
        return new TtnTotals(quantity, amount, vat, amount + vat, places, massKg / 1000m);
    }

    /// <summary>Сумма прописью для строки «Всего сумма НДС / стоимость с НДС» (белорусские рубли).</summary>
    public static string AmountInWords(decimal amount)
    {
        amount = decimal.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var rubles = (long)decimal.Truncate(amount);
        var kopecks = (int)((amount - rubles) * 100);
        var words = rubles == 0 ? "ноль" : NumberToWords(rubles);
        words = char.ToUpperInvariant(words[0]) + words[1..];
        return $"{words} {Plural(rubles, "белорусский рубль", "белорусских рубля", "белорусских рублей")} {kopecks:00} {Plural(kopecks, "копейка", "копейки", "копеек")}";
    }

    private static string Plural(long n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100;
        if (n is >= 11 and <= 19) return many;
        return (n % 10) switch { 1 => one, 2 or 3 or 4 => few, _ => many };
    }

    private static readonly string[] UnitsMale = { "", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };
    private static readonly string[] UnitsFemale = { "", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять" };
    private static readonly string[] Teens = { "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать" };
    private static readonly string[] Tens = { "", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто" };
    private static readonly string[] Hundreds = { "", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот" };

    private static string NumberToWords(long number)
    {
        var groups = new (string One, string Few, string Many, bool Female)[]
        {
            ("", "", "", false), ("тысяча", "тысячи", "тысяч", true), ("миллион", "миллиона", "миллионов", false),
            ("миллиард", "миллиарда", "миллиардов", false), ("триллион", "триллиона", "триллионов", false)
        };
        var parts = new List<string>();
        for (var g = 0; number > 0 && g < groups.Length; g++, number /= 1000)
        {
            var triple = (int)(number % 1000);
            if (triple == 0) continue;
            var words = new List<string>();
            if (triple / 100 > 0) words.Add(Hundreds[triple / 100]);
            var rest = triple % 100;
            if (rest is >= 10 and <= 19) words.Add(Teens[rest - 10]);
            else
            {
                if (rest / 10 > 0) words.Add(Tens[rest / 10]);
                if (rest % 10 > 0) words.Add((groups[g].Female ? UnitsFemale : UnitsMale)[rest % 10]);
            }
            if (g > 0) words.Add(Plural(triple, groups[g].One, groups[g].Few, groups[g].Many));
            parts.Insert(0, string.Join(" ", words));
        }
        return string.Join(" ", parts);
    }
}
