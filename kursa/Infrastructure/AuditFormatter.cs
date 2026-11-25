using System.Text;
using System.Text.Json;
using Warehouse.Core.Models;

namespace kursa.Infrastructure;

public static class AuditFormatter
{
    private static readonly Dictionary<string,string> Names=new()
    {
        ["Before"]="Было",["After"]="Стало",["Id"]="ID",["Name"]="Наименование",["Unit"]="Единица измерения",
        ["PurchasePrice"]="Закупочная цена, BYN",["SalePrice"]="Продажная цена, BYN",["Address"]="Адрес",
        ["ManagerName"]="Заведующий",["ManagerContact"]="Контакт",["IsSupplier"]="Поставщик",["Login"]="Логин",
        ["FullName"]="ФИО",["Role"]="Роль",["IsActive"]="Учётная запись активна",["PasswordChanged"]="Пароль изменён",
        ["Operation"]="Операция",["Kind"]="Тип",["ProductId"]="ID товара",["Quantity"]="Количество",
        ["FromWarehouseId"]="ID исходного склада",["ToWarehouseId"]="ID склада назначения",["CounterpartyId"]="ID контрагента",
        ["Comment"]="Комментарий",["SourceBefore"]="Остаток на исходном складе до",["SourceAfter"]="Остаток на исходном складе после",
        ["DestinationBefore"]="Остаток на складе назначения до",["DestinationAfter"]="Остаток на складе назначения после",
        ["Number"]="Номер документа",["FromWarehouse"]="Склад-отправитель",["ToWarehouse"]="Склад-получатель",["OccurredAt"]="Время проведения",
        ["Unp"]="УНП",["DeliveryAddress"]="Адрес разгрузки",["Phone"]="Телефон",["Contract"]="Договор (основание отпуска)",
        ["VatRate"]="Ставка НДС, %",["UnitsPerPlace"]="Единиц в грузовом месте",["UnitMassKg"]="Масса единицы, кг",
        ["ReleaseAllowedBy"]="Отпуск разрешил",["DefaultVatRate"]="Ставка НДС по умолчанию, %"
    };
    public static string Details(string json)
    {
        try
        {
            using var document=JsonDocument.Parse(json); var text=new StringBuilder();
            Append(text,document.RootElement,0,null);
            return text.Length==0 ? "Дополнительных данных нет." : text.ToString().TrimEnd();
        }
        catch (JsonException) { return "Не удалось разобрать подробности SQL-события: " + json; }
    }
    private static void Append(StringBuilder text,JsonElement value,int level,string? property)
    {
        if (value.ValueKind==JsonValueKind.Object)
        {
            foreach(var child in value.EnumerateObject())
            {
                if (child.Name is "RoleName" or "TypeName") continue;
                text.Append(' ',level*2).Append(Names.GetValueOrDefault(child.Name,child.Name)).Append(": ");
                if (child.Value.ValueKind==JsonValueKind.Object) { text.AppendLine(); Append(text,child.Value,level+1,child.Name); }
                else { Append(text,child.Value,level+1,child.Name); text.AppendLine(); }
            }
            return;
        }
        if (value.ValueKind==JsonValueKind.Null) { text.Append("—"); return; }
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) { text.Append(value.GetBoolean() ? "да" : "нет"); return; }
        if (value.ValueKind==JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            if (property=="Role" && Enum.IsDefined((UserRole)number)) { text.Append(UserRoles.Name((UserRole)number)); return; }
            if (property=="Kind" && Enum.IsDefined((OperationKind)number)) { text.Append(number switch {0=>"Приход",1=>"Реализация",2=>"Перемещение",_=>number.ToString()}); return; }
        }
        if (value.ValueKind==JsonValueKind.String && property=="OccurredAt" && value.TryGetDateTime(out var time)) { text.Append(time.ToString("dd.MM.yyyy HH:mm:ss")); return; }
        text.Append(value.ValueKind==JsonValueKind.String ? value.GetString() : value.GetRawText());
    }
}
