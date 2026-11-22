using Warehouse.Core.Data;
using Warehouse.Core.Models;

namespace kursa.Infrastructure;

public sealed class WorkspaceContext(WarehouseRepository repository, AppUser user)
{
    public WarehouseRepository Repository { get; } = repository;
    public AppUser User { get; } = user;
    public event EventHandler? DataChanged;
    public void NotifyDataChanged() => DataChanged?.Invoke(this, EventArgs.Empty);
}
