using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WarehousePrototype
{
    // ==================== МОДЕЛИ ДАННЫХ ====================
    public enum OperationType { Receipt, Shipment, Movement }
    public enum UserRole { Admin, Manager, Storekeeper }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Unit { get; set; } = "шт";
        public decimal PurchasePrice { get; set; }
        public decimal SalePrice { get; set; }
        public override string ToString() => $"{Id}: {Name}";
    }

    public class Warehouse
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    public class Counterparty
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsSupplier { get; set; }
        public override string ToString() => Name + (IsSupplier ? " (поставщик)" : " (покупатель)");
    }

    public class User
    {
        public string Login { get; set; }
        public string Password { get; set; }
        public string FullName { get; set; }
        public UserRole Role { get; set; }
        public override string ToString() => $"{FullName} ({Role})";
    }

    public class Stock
    {
        public int ProductId { get; set; }
        public int WarehouseId { get; set; }
        public decimal Quantity { get; set; }
    }

    public class Operation
    {
        public int Id { get; set; }
        public DateTime Date { get; set; }
        public OperationType Type { get; set; }
        public int ProductId { get; set; }
        public decimal Quantity { get; set; }
        public int? FromWarehouseId { get; set; }
        public int? ToWarehouseId { get; set; }
        public int? CounterpartyId { get; set; }
        public string Comment { get; set; }
    }

    // ==================== ГЛАВНАЯ ФОРМА ====================
    public class MainForm : Form
    {
        // Данные в памяти
        private List<Product> products = new List<Product>();
        private List<Warehouse> warehouses = new List<Warehouse>();
        private List<Counterparty> counterparties = new List<Counterparty>();
        private List<User> users = new List<User>();
        private List<Stock> stocks = new List<Stock>();
        private List<Operation> operations = new List<Operation>();

        private User currentUser;
        private int nextProductId = 1, nextWarehouseId = 1, nextCounterpartyId = 1, nextOperationId = 1;

        // Элементы управления
        private TabControl tabs;
        private DataGridView dgvProducts, dgvWarehouses, dgvCounterparties, dgvOperations, dgvStocks, dgvTurnover;
        private ComboBox cmbUserRoleFilter;
        private Label lblCurrentUser, lblStatus;

        public MainForm()
        {
            InitializeSampleData();
            InitializeUI();
            ShowLogin();
        }

        private void InitializeSampleData()
        {
            users.AddRange(new[]
            {
                new User { Login = "admin", Password = "admin", FullName = "Администратор системы", Role = UserRole.Admin },
                new User { Login = "manager", Password = "123", FullName = "Иванов А.П.", Role = UserRole.Manager },
                new User { Login = "store", Password = "123", FullName = "Петров С.И.", Role = UserRole.Storekeeper }
            });

            warehouses.AddRange(new[]
            {
                new Warehouse { Id = nextWarehouseId++, Name = "Основной склад" },
                new Warehouse { Id = nextWarehouseId++, Name = "Склад №2 (резерв)" }
            });

            products.AddRange(new[]
            {
                new Product { Id = nextProductId++, Name = "Ноутбук Dell Latitude", Unit = "шт", PurchasePrice = 45000, SalePrice = 62000 },
                new Product { Id = nextProductId++, Name = "Монитор 27\" Samsung", Unit = "шт", PurchasePrice = 18000, SalePrice = 24500 },
                new Product { Id = nextProductId++, Name = "Клавиатура механическая", Unit = "шт", PurchasePrice = 3200, SalePrice = 4500 },
                new Product { Id = nextProductId++, Name = "Мышь беспроводная", Unit = "шт", PurchasePrice = 1500, SalePrice = 2200 },
                new Product { Id = nextProductId++, Name = "Кабель HDMI 2м", Unit = "шт", PurchasePrice = 350, SalePrice = 590 }
            });

            counterparties.AddRange(new[]
            {
                new Counterparty { Id = nextCounterpartyId++, Name = "ООО «ТехноСнаб»", IsSupplier = true },
                new Counterparty { Id = nextCounterpartyId++, Name = "ИП Сидоров", IsSupplier = true },
                new Counterparty { Id = nextCounterpartyId++, Name = "ООО «ОфисПлюс»", IsSupplier = false },
                new Counterparty { Id = nextCounterpartyId++, Name = "АО «РегионТорг»", IsSupplier = false }
            });

            // Начальные остатки
            stocks.Add(new Stock { ProductId = 1, WarehouseId = 1, Quantity = 25 });
            stocks.Add(new Stock { ProductId = 2, WarehouseId = 1, Quantity = 40 });
            stocks.Add(new Stock { ProductId = 3, WarehouseId = 1, Quantity = 120 });
            stocks.Add(new Stock { ProductId = 4, WarehouseId = 1, Quantity = 85 });
            stocks.Add(new Stock { ProductId = 5, WarehouseId = 1, Quantity = 300 });
            stocks.Add(new Stock { ProductId = 1, WarehouseId = 2, Quantity = 8 });
            stocks.Add(new Stock { ProductId = 2, WarehouseId = 2, Quantity = 15 });

            // Примерные операции за последние 90 дней
            var rnd = new Random(42);
            for (int i = 0; i < 35; i++)
            {
                var p = products[rnd.Next(products.Count)];
                var type = (OperationType)rnd.Next(3);
                var qty = rnd.Next(1, 15);
                var date = DateTime.Today.AddDays(-rnd.Next(1, 90));

                if (type == OperationType.Receipt)
                {
                    operations.Add(new Operation
                    {
                        Id = nextOperationId++,
                        Date = date,
                        Type = OperationType.Receipt,
                        ProductId = p.Id,
                        Quantity = qty,
                        ToWarehouseId = 1,
                        CounterpartyId = counterparties.Where(c => c.IsSupplier).OrderBy(_ => rnd.Next()).First().Id,
                        Comment = "Поступление от поставщика"
                    });
                    ChangeStock(p.Id, 1, qty);
                }
                else if (type == OperationType.Shipment)
                {
                    var available = GetStock(p.Id, 1);
                    if (available >= qty)
                    {
                        operations.Add(new Operation
                        {
                            Id = nextOperationId++,
                            Date = date,
                            Type = OperationType.Shipment,
                            ProductId = p.Id,
                            Quantity = qty,
                            FromWarehouseId = 1,
                            CounterpartyId = counterparties.Where(c => !c.IsSupplier).OrderBy(_ => rnd.Next()).First().Id,
                            Comment = "Отгрузка покупателю"
                        });
                        ChangeStock(p.Id, 1, -qty);
                    }
                }
            }
        }

        private void InitializeUI()
        {
            Text = "Управление складскими запасами и анализ оборачиваемости — Прототип";
            Size = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);

            // Верхняя панель
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(45, 45, 48) };
            lblCurrentUser = new Label
            {
                ForeColor = Color.White,
                AutoSize = true,
                Location = new Point(15, 14),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold)
            };
            var btnLogout = new Button
            {
                Text = "Выход",
                Location = new Point(1150, 10),
                Size = new Size(90, 28),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                BackColor = Color.FromArgb(180, 50, 50)
            };
            btnLogout.Click += (s, e) => { currentUser = null; ShowLogin(); };
            topPanel.Controls.Add(lblCurrentUser);
            topPanel.Controls.Add(btnLogout);

            // Статусная строка
            lblStatus = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                BackColor = Color.FromArgb(240, 240, 240),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            tabs = new TabControl { Dock = DockStyle.Fill };

            // Вкладки
            tabs.TabPages.Add(CreateProductsTab());
            tabs.TabPages.Add(CreateWarehousesTab());
            tabs.TabPages.Add(CreateCounterpartiesTab());
            tabs.TabPages.Add(CreateOperationsTab());
            tabs.TabPages.Add(CreateStocksTab());
            tabs.TabPages.Add(CreateTurnoverTab());
            tabs.TabPages.Add(CreateReportsTab());

            Controls.Add(tabs);
            Controls.Add(topPanel);
            Controls.Add(lblStatus);
        }

        private void ShowLogin()
        {
            using (var loginForm = new Form
            {
                Text = "Авторизация",
                Size = new Size(380, 240),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                var lblInfo = new Label
                {
                    Text = "Демо-учётные записи:\nadmin / admin\nmanager / 123\nstore / 123",
                    Location = new Point(20, 15),
                    Size = new Size(320, 55)
                };
                var txtLogin = new TextBox { Location = new Point(120, 80), Width = 200, Text = "manager" };
                var txtPass = new TextBox { Location = new Point(120, 115), Width = 200, PasswordChar = '*', Text = "123" };
                var btnOk = new Button { Text = "Войти", Location = new Point(120, 155), Width = 100, DialogResult = DialogResult.OK };
                loginForm.Controls.AddRange(new Control[]
                {
                    lblInfo,
                    new Label { Text = "Логин:", Location = new Point(20, 83) },
                    txtLogin,
                    new Label { Text = "Пароль:", Location = new Point(20, 118) },
                    txtPass,
                    btnOk
                });
                loginForm.AcceptButton = btnOk;

                if (loginForm.ShowDialog() != DialogResult.OK)
                {
                    Application.Exit();
                    return;
                }

                currentUser = users.FirstOrDefault(u => u.Login == txtLogin.Text && u.Password == txtPass.Text);
                if (currentUser == null)
                {
                    MessageBox.Show("Неверный логин или пароль", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    ShowLogin();
                    return;
                }

                lblCurrentUser.Text = $"Пользователь: {currentUser.FullName}  |  Роль: {currentUser.Role}";
                ApplyRolePermissions();
                RefreshAll();
                lblStatus.Text = "Система готова к работе. Данные хранятся в оперативной памяти (прототип).";
            }
        }

        private void ApplyRolePermissions()
        {
            // Кладовщик не может редактировать справочники и анализировать оборачиваемость
            bool canEditRefs = currentUser.Role == UserRole.Admin || currentUser.Role == UserRole.Manager;
            bool canAnalyze = currentUser.Role == UserRole.Admin || currentUser.Role == UserRole.Manager;

            tabs.TabPages[0].Enabled = canEditRefs; // Товары
            tabs.TabPages[1].Enabled = canEditRefs; // Склады
            tabs.TabPages[2].Enabled = canEditRefs; // Контрагенты
            tabs.TabPages[5].Enabled = canAnalyze;  // Оборачиваемость
            tabs.TabPages[6].Enabled = canAnalyze;  // Отчёты
        }

        // ==================== ВКЛАДКА ТОВАРЫ ====================
        private TabPage CreateProductsTab()
        {
            var page = new TabPage("Номенклатура");
            dgvProducts = CreateGrid();
            dgvProducts.Columns.Add("Id", "ID");
            dgvProducts.Columns.Add("Name", "Наименование");
            dgvProducts.Columns.Add("Unit", "Ед. изм.");
            dgvProducts.Columns.Add("PurchasePrice", "Закуп. цена");
            dgvProducts.Columns.Add("SalePrice", "Прод. цена");
            dgvProducts.Columns[0].Width = 50;
            dgvProducts.Columns[1].Width = 280;

            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5) };
            var btnAdd = new Button { Text = "Добавить", Width = 100 };
            var btnEdit = new Button { Text = "Изменить", Width = 100 };
            var btnDel = new Button { Text = "Удалить", Width = 100 };
            btnAdd.Click += (s, e) => EditProduct(null);
            btnEdit.Click += (s, e) =>
            {
                if (dgvProducts.CurrentRow != null)
                    EditProduct(products.First(p => p.Id == (int)dgvProducts.CurrentRow.Cells[0].Value));
            };
            btnDel.Click += (s, e) =>
            {
                if (dgvProducts.CurrentRow == null) return;
                var id = (int)dgvProducts.CurrentRow.Cells[0].Value;
                if (MessageBox.Show("Удалить товар?", "Подтверждение", MessageBoxButtons.YesNo) == DialogResult.Yes)
                {
                    products.RemoveAll(p => p.Id == id);
                    stocks.RemoveAll(s => s.ProductId == id);
                    RefreshProducts();
                }
            };
            panel.Controls.AddRange(new Control[] { btnAdd, btnEdit, btnDel });
            page.Controls.Add(dgvProducts);
            page.Controls.Add(panel);
            return page;
        }

        private void EditProduct(Product p)
        {
            bool isNew = p == null;
            if (isNew) p = new Product { Id = nextProductId };

            using (var f = new Form { Text = isNew ? "Новый товар" : "Редактирование товара", Size = new Size(420, 280), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent })
            {
                var txtName = new TextBox { Text = p.Name ?? "", Location = new Point(140, 20), Width = 230 };
                var txtUnit = new TextBox { Text = p.Unit, Location = new Point(140, 55), Width = 100 };
                var numPurch = new NumericUpDown { Minimum = 0, Maximum = 10000000, DecimalPlaces = 2, Value = p.PurchasePrice, Location = new Point(140, 90), Width = 120 };
                var numSale = new NumericUpDown { Minimum = 0, Maximum = 10000000, DecimalPlaces = 2, Value = p.SalePrice, Location = new Point(140, 125), Width = 120 };
                var btnOk = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, Location = new Point(140, 180), Width = 110 };

                f.Controls.AddRange(new Control[]
                {
                    new Label { Text = "Наименование:", Location = new Point(20, 23) }, txtName,
                    new Label { Text = "Ед. измерения:", Location = new Point(20, 58) }, txtUnit,
                    new Label { Text = "Закупочная цена:", Location = new Point(20, 93) }, numPurch,
                    new Label { Text = "Продажная цена:", Location = new Point(20, 128) }, numSale,
                    btnOk
                });
                f.AcceptButton = btnOk;

                if (f.ShowDialog() == DialogResult.OK)
                {
                    p.Name = txtName.Text.Trim();
                    p.Unit = txtUnit.Text.Trim();
                    p.PurchasePrice = numPurch.Value;
                    p.SalePrice = numSale.Value;
                    if (isNew) { p.Id = nextProductId++; products.Add(p); }
                    RefreshProducts();
                }
            }
        }

        // ==================== ВКЛАДКА СКЛАДЫ ====================
        private TabPage CreateWarehousesTab()
        {
            var page = new TabPage("Склады");
            dgvWarehouses = CreateGrid();
            dgvWarehouses.Columns.Add("Id", "ID");
            dgvWarehouses.Columns.Add("Name", "Наименование склада");
            dgvWarehouses.Columns[1].Width = 350;

            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5) };
            var btnAdd = new Button { Text = "Добавить", Width = 100 };
            var btnEdit = new Button { Text = "Изменить", Width = 100 };
            btnAdd.Click += (s, e) =>
            {
                var name = Microsoft.VisualBasic.Interaction.InputBox("Наименование склада:", "Новый склад", "Склад №");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    warehouses.Add(new Warehouse { Id = nextWarehouseId++, Name = name.Trim() });
                    RefreshWarehouses();
                }
            };
            btnEdit.Click += (s, e) =>
            {
                if (dgvWarehouses.CurrentRow == null) return;
                var w = warehouses.First(x => x.Id == (int)dgvWarehouses.CurrentRow.Cells[0].Value);
                var name = Microsoft.VisualBasic.Interaction.InputBox("Наименование:", "Редактирование", w.Name);
                if (!string.IsNullOrWhiteSpace(name)) { w.Name = name.Trim(); RefreshWarehouses(); }
            };
            panel.Controls.AddRange(new Control[] { btnAdd, btnEdit });
            page.Controls.Add(dgvWarehouses);
            page.Controls.Add(panel);
            return page;
        }

        // ==================== ВКЛАДКА КОНТРАГЕНТЫ ====================
        private TabPage CreateCounterpartiesTab()
        {
            var page = new TabPage("Контрагенты");
            dgvCounterparties = CreateGrid();
            dgvCounterparties.Columns.Add("Id", "ID");
            dgvCounterparties.Columns.Add("Name", "Наименование");
            dgvCounterparties.Columns.Add("Type", "Тип");
            dgvCounterparties.Columns[1].Width = 300;

            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5) };
            var btnAdd = new Button { Text = "Добавить", Width = 100 };
            btnAdd.Click += (s, e) =>
            {
                using (var f = new Form { Text = "Новый контрагент", Size = new Size(380, 180), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent })
                {
                    var txt = new TextBox { Location = new Point(120, 25), Width = 210 };
                    var chk = new CheckBox { Text = "Поставщик", Location = new Point(120, 60), Checked = true };
                    var btn = new Button { Text = "Сохранить", DialogResult = DialogResult.OK, Location = new Point(120, 100) };
                    f.Controls.AddRange(new Control[] { new Label { Text = "Наименование:", Location = new Point(20, 28) }, txt, chk, btn });
                    if (f.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(txt.Text))
                    {
                        counterparties.Add(new Counterparty { Id = nextCounterpartyId++, Name = txt.Text.Trim(), IsSupplier = chk.Checked });
                        RefreshCounterparties();
                    }
                }
            };
            panel.Controls.Add(btnAdd);
            page.Controls.Add(dgvCounterparties);
            page.Controls.Add(panel);
            return page;
        }

        // ==================== ВКЛАДКА ОПЕРАЦИИ ====================
        private TabPage CreateOperationsTab()
        {
            var page = new TabPage("Складские операции");
            dgvOperations = CreateGrid();
            dgvOperations.Columns.Add("Id", "ID");
            dgvOperations.Columns.Add("Date", "Дата");
            dgvOperations.Columns.Add("Type", "Тип");
            dgvOperations.Columns.Add("Product", "Товар");
            dgvOperations.Columns.Add("Qty", "Кол-во");
            dgvOperations.Columns.Add("From", "Со склада");
            dgvOperations.Columns.Add("To", "На склад");
            dgvOperations.Columns.Add("Counterparty", "Контрагент");
            dgvOperations.Columns.Add("Comment", "Комментарий");
            dgvOperations.Columns[3].Width = 180;
            dgvOperations.Columns[8].Width = 160;

            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5) };
            var btnReceipt = new Button { Text = "Приход", Width = 100, BackColor = Color.LightGreen };
            var btnShip = new Button { Text = "Расход", Width = 100, BackColor = Color.LightCoral };
            var btnMove = new Button { Text = "Перемещение", Width = 120, BackColor = Color.LightSkyBlue };
            btnReceipt.Click += (s, e) => AddOperation(OperationType.Receipt);
            btnShip.Click += (s, e) => AddOperation(OperationType.Shipment);
            btnMove.Click += (s, e) => AddOperation(OperationType.Movement);
            panel.Controls.AddRange(new Control[] { btnReceipt, btnShip, btnMove });
            page.Controls.Add(dgvOperations);
            page.Controls.Add(panel);
            return page;
        }

        private void AddOperation(OperationType type)
        {
            using (var f = new Form { Text = type.ToString(), Size = new Size(460, 340), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent })
            {
                var cmbProduct = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 20), Width = 260 };
                cmbProduct.Items.AddRange(products.ToArray());
                if (cmbProduct.Items.Count > 0) cmbProduct.SelectedIndex = 0;

                var numQty = new NumericUpDown { Minimum = 0.01m, Maximum = 100000, DecimalPlaces = 2, Value = 1, Location = new Point(150, 55), Width = 100 };

                var cmbFrom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 90), Width = 260 };
                cmbFrom.Items.AddRange(warehouses.ToArray());
                if (cmbFrom.Items.Count > 0) cmbFrom.SelectedIndex = 0;

                var cmbTo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 125), Width = 260 };
                cmbTo.Items.AddRange(warehouses.ToArray());
                if (cmbTo.Items.Count > 1) cmbTo.SelectedIndex = 1;

                var cmbCounter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(150, 160), Width = 260 };
                var list = type == OperationType.Receipt
                    ? counterparties.Where(c => c.IsSupplier).ToArray()
                    : counterparties.Where(c => !c.IsSupplier).ToArray();
                cmbCounter.Items.AddRange(list);
                if (cmbCounter.Items.Count > 0) cmbCounter.SelectedIndex = 0;

                var txtComment = new TextBox { Location = new Point(150, 195), Width = 260 };

                // Скрытие ненужных полей
                if (type == OperationType.Receipt) { cmbFrom.Enabled = false; }
                if (type == OperationType.Shipment) { cmbTo.Enabled = false; }
                if (type == OperationType.Movement) { cmbCounter.Enabled = false; }

                var btnOk = new Button { Text = "Провести", DialogResult = DialogResult.OK, Location = new Point(150, 245), Width = 120 };

                f.Controls.AddRange(new Control[]
                {
                    new Label { Text = "Товар:", Location = new Point(20, 23) }, cmbProduct,
                    new Label { Text = "Количество:", Location = new Point(20, 58) }, numQty,
                    new Label { Text = "Со склада:", Location = new Point(20, 93) }, cmbFrom,
                    new Label { Text = "На склад:", Location = new Point(20, 128) }, cmbTo,
                    new Label { Text = "Контрагент:", Location = new Point(20, 163) }, cmbCounter,
                    new Label { Text = "Комментарий:", Location = new Point(20, 198) }, txtComment,
                    btnOk
                });

                if (f.ShowDialog() != DialogResult.OK) return;

                var product = (Product)cmbProduct.SelectedItem;
                var qty = numQty.Value;

                if (type == OperationType.Shipment || type == OperationType.Movement)
                {
                    var fromWh = (Warehouse)cmbFrom.SelectedItem;
                    if (GetStock(product.Id, fromWh.Id) < qty)
                    {
                        MessageBox.Show("Недостаточно товара на складе!", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                var op = new Operation
                {
                    Id = nextOperationId++,
                    Date = DateTime.Now,
                    Type = type,
                    ProductId = product.Id,
                    Quantity = qty,
                    Comment = txtComment.Text
                };

                if (type == OperationType.Receipt)
                {
                    op.ToWarehouseId = ((Warehouse)cmbTo.SelectedItem).Id;
                    op.CounterpartyId = ((Counterparty)cmbCounter.SelectedItem).Id;
                    ChangeStock(product.Id, op.ToWarehouseId.Value, qty);
                }
                else if (type == OperationType.Shipment)
                {
                    op.FromWarehouseId = ((Warehouse)cmbFrom.SelectedItem).Id;
                    op.CounterpartyId = ((Counterparty)cmbCounter.SelectedItem).Id;
                    ChangeStock(product.Id, op.FromWarehouseId.Value, -qty);
                }
                else // Movement
                {
                    op.FromWarehouseId = ((Warehouse)cmbFrom.SelectedItem).Id;
                    op.ToWarehouseId = ((Warehouse)cmbTo.SelectedItem).Id;
                    ChangeStock(product.Id, op.FromWarehouseId.Value, -qty);
                    ChangeStock(product.Id, op.ToWarehouseId.Value, qty);
                }

                operations.Add(op);
                RefreshOperations();
                RefreshStocks();
                lblStatus.Text = $"Операция #{op.Id} успешно проведена ({type}).";
            }
        }

        // ==================== ВКЛАДКА ОСТАТКИ ====================
        private TabPage CreateStocksTab()
        {
            var page = new TabPage("Текущие остатки");
            dgvStocks = CreateGrid();
            dgvStocks.Columns.Add("Warehouse", "Склад");
            dgvStocks.Columns.Add("Product", "Товар");
            dgvStocks.Columns.Add("Qty", "Количество");
            dgvStocks.Columns.Add("Unit", "Ед.");
            dgvStocks.Columns.Add("SumPurchase", "Сумма (закуп)");
            dgvStocks.Columns.Add("SumSale", "Сумма (прод)");
            dgvStocks.Columns[1].Width = 250;

            var btnRefresh = new Button { Text = "Обновить", Dock = DockStyle.Top, Height = 35 };
            btnRefresh.Click += (s, e) => RefreshStocks();
            page.Controls.Add(dgvStocks);
            page.Controls.Add(btnRefresh);
            return page;
        }

        // ==================== ВКЛАДКА ОБОРАЧИВАЕМОСТЬ ====================
        private TabPage CreateTurnoverTab()
        {
            var page = new TabPage("Анализ оборачиваемости");
            dgvTurnover = CreateGrid();
            dgvTurnover.Columns.Add("Product", "Товар");
            dgvTurnover.Columns.Add("AvgStock", "Средний запас");
            dgvTurnover.Columns.Add("SoldQty", "Реализовано");
            dgvTurnover.Columns.Add("Turnover", "Коэф. оборачиваемости");
            dgvTurnover.Columns.Add("Days", "Ср. срок хранения, дн.");
            dgvTurnover.Columns.Add("Status", "Статус");
            dgvTurnover.Columns[0].Width = 220;

            var panel = new Panel { Dock = DockStyle.Top, Height = 50 };
            var lblPeriod = new Label { Text = "Период анализа (дней):", Location = new Point(10, 15), AutoSize = true };
            var numDays = new NumericUpDown { Minimum = 7, Maximum = 365, Value = 90, Location = new Point(170, 12), Width = 70 };
            var btnCalc = new Button { Text = "Рассчитать", Location = new Point(260, 10), Width = 110 };
            btnCalc.Click += (s, e) => CalculateTurnover((int)numDays.Value);
            panel.Controls.AddRange(new Control[] { lblPeriod, numDays, btnCalc });

            page.Controls.Add(dgvTurnover);
            page.Controls.Add(panel);
            return page;
        }

        private void CalculateTurnover(int periodDays)
        {
            dgvTurnover.Rows.Clear();
            var fromDate = DateTime.Today.AddDays(-periodDays);

            foreach (var p in products)
            {
                // Реализованное количество (расход)
                var sold = operations
                    .Where(o => o.ProductId == p.Id && o.Type == OperationType.Shipment && o.Date >= fromDate)
                    .Sum(o => o.Quantity);

                // Средний запас (упрощённо — текущий остаток по всем складам)
                var currentStock = stocks.Where(s => s.ProductId == p.Id).Sum(s => s.Quantity);
                var avgStock = currentStock; // в реальной системе — среднее за период

                decimal turnover = avgStock > 0 ? sold / avgStock : 0;
                decimal days = turnover > 0 ? periodDays / turnover : 999;

                string status;
                Color color;
                if (turnover >= 3) { status = "Высокая"; color = Color.LightGreen; }
                else if (turnover >= 1) { status = "Нормальная"; color = Color.LightYellow; }
                else if (turnover > 0) { status = "Низкая"; color = Color.Orange; }
                else { status = "Неликвид"; color = Color.LightCoral; }

                var idx = dgvTurnover.Rows.Add(
                    p.Name,
                    avgStock.ToString("N2"),
                    sold.ToString("N2"),
                    turnover.ToString("N2"),
                    days.ToString("N1"),
                    status);
                dgvTurnover.Rows[idx].DefaultCellStyle.BackColor = color;
            }
            lblStatus.Text = $"Анализ оборачиваемости выполнен за последние {periodDays} дней.";
        }

        // ==================== ВКЛАДКА ОТЧЁТЫ ====================
        private TabPage CreateReportsTab()
        {
            var page = new TabPage("Аналитические отчёты");
            var txtReport = new TextBox
            {
                Multiline = true,
                Dock = DockStyle.Fill,
                Font = new Font("Consolas", 10f),
                ScrollBars = ScrollBars.Both,
                ReadOnly = true
            };

            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, Padding = new Padding(5) };
            var btnStockReport = new Button { Text = "Отчёт по остаткам", Width = 150 };
            var btnTurnoverReport = new Button { Text = "Отчёт по оборачиваемости", Width = 180 };
            var btnPrint = new Button { Text = "Печать / Сохранить", Width = 140 };

            btnStockReport.Click += (s, e) =>
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("ОТЧЁТ О СОСТОЯНИИ СКЛАДСКИХ ЗАПАСОВ");
                sb.AppendLine($"Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm}");
                sb.AppendLine(new string('=', 70));
                sb.AppendLine();

                decimal totalPurchase = 0, totalSale = 0;
                foreach (var wh in warehouses)
                {
                    sb.AppendLine($"СКЛАД: {wh.Name}");
                    sb.AppendLine(new string('-', 50));
                    foreach (var st in stocks.Where(s => s.WarehouseId == wh.Id && s.Quantity > 0))
                    {
                        var p = products.First(x => x.Id == st.ProductId);
                        var sumP = st.Quantity * p.PurchasePrice;
                        var sumS = st.Quantity * p.SalePrice;
                        totalPurchase += sumP;
                        totalSale += sumS;
                        sb.AppendLine($"{p.Name,-35} {st.Quantity,8:N2} {p.Unit}  |  {sumP,12:N2} руб.");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine(new string('=', 70));
                sb.AppendLine($"ИТОГО по закупочным ценам: {totalPurchase:N2} руб.");
                sb.AppendLine($"ИТОГО по продажным ценам:  {totalSale:N2} руб.");
                sb.AppendLine($"Потенциальная маржа:       {(totalSale - totalPurchase):N2} руб.");
                txtReport.Text = sb.ToString();
            };

            btnTurnoverReport.Click += (s, e) =>
            {
                CalculateTurnover(90);
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("АНАЛИТИЧЕСКИЙ ОТЧЁТ ПО ОБОРАЧИВАЕМОСТИ ТОВАРОВ");
                sb.AppendLine($"Период: последние 90 дней  |  Сформирован: {DateTime.Now:dd.MM.yyyy HH:mm}");
                sb.AppendLine(new string('=', 80));
                sb.AppendLine($"{"Товар",-35} {"Ср.запас",10} {"Реализ.",10} {"Коэф.",8} {"Дней",8} {"Статус"}");
                sb.AppendLine(new string('-', 80));
                foreach (DataGridViewRow row in dgvTurnover.Rows)
                {
                    sb.AppendLine($"{row.Cells[0].Value,-35} {row.Cells[1].Value,10} {row.Cells[2].Value,10} {row.Cells[3].Value,8} {row.Cells[4].Value,8} {row.Cells[5].Value}");
                }
                txtReport.Text = sb.ToString();
            };

            btnPrint.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(txtReport.Text)) return;
                var sfd = new SaveFileDialog { Filter = "Текстовый файл|*.txt", FileName = $"Report_{DateTime.Now:yyyyMMdd_HHmm}.txt" };
                if (sfd.ShowDialog() == DialogResult.OK)
                {
                    System.IO.File.WriteAllText(sfd.FileName, txtReport.Text, System.Text.Encoding.UTF8);
                    MessageBox.Show("Отчёт сохранён:\n" + sfd.FileName, "Успех", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };

            panel.Controls.AddRange(new Control[] { btnStockReport, btnTurnoverReport, btnPrint });
            page.Controls.Add(txtReport);
            page.Controls.Add(panel);
            return page;
        }

        // ==================== ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ====================
        private DataGridView CreateGrid()
        {
            var g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                RowHeadersVisible = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None
            };
            return g;
        }

        private decimal GetStock(int productId, int warehouseId)
        {
            var s = stocks.FirstOrDefault(x => x.ProductId == productId && x.WarehouseId == warehouseId);
            return s?.Quantity ?? 0;
        }

        private void ChangeStock(int productId, int warehouseId, decimal delta)
        {
            var s = stocks.FirstOrDefault(x => x.ProductId == productId && x.WarehouseId == warehouseId);
            if (s == null)
            {
                s = new Stock { ProductId = productId, WarehouseId = warehouseId, Quantity = 0 };
                stocks.Add(s);
            }
            s.Quantity += delta;
            if (s.Quantity < 0) s.Quantity = 0;
        }

        private void RefreshAll()
        {
            RefreshProducts();
            RefreshWarehouses();
            RefreshCounterparties();
            RefreshOperations();
            RefreshStocks();
        }

        private void RefreshProducts()
        {
            dgvProducts.Rows.Clear();
            foreach (var p in products.OrderBy(x => x.Name))
                dgvProducts.Rows.Add(p.Id, p.Name, p.Unit, p.PurchasePrice.ToString("N2"), p.SalePrice.ToString("N2"));
        }

        private void RefreshWarehouses()
        {
            dgvWarehouses.Rows.Clear();
            foreach (var w in warehouses)
                dgvWarehouses.Rows.Add(w.Id, w.Name);
        }

        private void RefreshCounterparties()
        {
            dgvCounterparties.Rows.Clear();
            foreach (var c in counterparties)
                dgvCounterparties.Rows.Add(c.Id, c.Name, c.IsSupplier ? "Поставщик" : "Покупатель");
        }

        private void RefreshOperations()
        {
            dgvOperations.Rows.Clear();
            foreach (var o in operations.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id))
            {
                var p = products.FirstOrDefault(x => x.Id == o.ProductId);
                var from = warehouses.FirstOrDefault(x => x.Id == o.FromWarehouseId)?.Name ?? "—";
                var to = warehouses.FirstOrDefault(x => x.Id == o.ToWarehouseId)?.Name ?? "—";
                var cp = counterparties.FirstOrDefault(x => x.Id == o.CounterpartyId)?.Name ?? "—";
                string typeName = o.Type switch
                {
                    OperationType.Receipt => "Приход",
                    OperationType.Shipment => "Расход",
                    _ => "Перемещение"
                };
                dgvOperations.Rows.Add(o.Id, o.Date.ToString("dd.MM.yyyy HH:mm"), typeName,
                    p?.Name, o.Quantity.ToString("N2"), from, to, cp, o.Comment);
            }
        }

        private void RefreshStocks()
        {
            dgvStocks.Rows.Clear();
            foreach (var st in stocks.Where(s => s.Quantity > 0).OrderBy(s => s.WarehouseId).ThenBy(s => s.ProductId))
            {
                var p = products.First(x => x.Id == st.ProductId);
                var w = warehouses.First(x => x.Id == st.WarehouseId);
                dgvStocks.Rows.Add(w.Name, p.Name, st.Quantity.ToString("N2"), p.Unit,
                    (st.Quantity * p.PurchasePrice).ToString("N2"),
                    (st.Quantity * p.SalePrice).ToString("N2"));
            }
        }
    }

    // ==================== ТОЧКА ВХОДА ====================
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}