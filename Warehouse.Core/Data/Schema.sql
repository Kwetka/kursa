-- Схема версии 1. Выполняется внутри транзакции приложения, без удаления данных.
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource = N'WarehouseCourse.Schema',
    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;
IF @lockResult < 0 THROW 51000, N'Не удалось заблокировать создание схемы.', 1;

IF OBJECT_ID(N'dbo.WarehouseSchemaVersion', N'U') IS NULL
BEGIN
    IF OBJECT_ID(N'dbo.Products', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.Warehouses', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.Counterparties', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.WarehouseUsers', N'U') IS NOT NULL
       OR OBJECT_ID(N'dbo.StockBalances', N'U') IS NOT NULL OR OBJECT_ID(N'dbo.StockOperations', N'U') IS NOT NULL
        THROW 51001, N'В базе уже есть таблицы с такими именами, но версия схемы приложения отсутствует. Выберите другую пустую базу.', 1;

    CREATE TABLE dbo.WarehouseSchemaVersion (
        Id int NOT NULL CONSTRAINT PK_WarehouseSchemaVersion PRIMARY KEY CHECK (Id = 1),
        Version int NOT NULL
    );
    CREATE TABLE dbo.Products (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        Unit nvarchar(30) NOT NULL,
        PurchasePrice decimal(18,2) NOT NULL CONSTRAINT CK_Products_Purchase CHECK (PurchasePrice >= 0 AND PurchasePrice <= 1000000000),
        SalePrice decimal(18,2) NOT NULL CONSTRAINT CK_Products_Sale CHECK (SalePrice >= 0 AND SalePrice <= 1000000000)
    );
    CREATE TABLE dbo.Warehouses (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Warehouses PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        Address nvarchar(400) NOT NULL CONSTRAINT DF_Warehouses_Address DEFAULT N'',
        ManagerName nvarchar(200) NOT NULL CONSTRAINT DF_Warehouses_Manager DEFAULT N'',
        ManagerContact nvarchar(200) NOT NULL CONSTRAINT DF_Warehouses_Contact DEFAULT N''
    );
    CREATE TABLE dbo.Counterparties (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Counterparties PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        IsSupplier bit NOT NULL
    );
    CREATE TABLE dbo.WarehouseUsers (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_WarehouseUsers PRIMARY KEY,
        Login nvarchar(80) NOT NULL CONSTRAINT UQ_WarehouseUsers_Login UNIQUE,
        FullName nvarchar(200) NOT NULL,
        Role tinyint NOT NULL CONSTRAINT CK_WarehouseUsers_Role CHECK (Role IN (0, 1, 2)),
        PasswordSalt varbinary(16) NOT NULL,
        PasswordHash varbinary(32) NOT NULL,
        PasswordIterations int NOT NULL CHECK (PasswordIterations > 0)
    );
    CREATE TABLE dbo.StockBalances (
        ProductId int NOT NULL,
        WarehouseId int NOT NULL,
        Quantity decimal(18,3) NOT NULL CONSTRAINT CK_StockBalances_Quantity CHECK (Quantity >= 0 AND Quantity <= 1000000000),
        CONSTRAINT PK_StockBalances PRIMARY KEY (ProductId, WarehouseId),
        CONSTRAINT FK_StockBalances_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT FK_StockBalances_Warehouse FOREIGN KEY (WarehouseId) REFERENCES dbo.Warehouses(Id)
    );
    CREATE TABLE dbo.StockOperations (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockOperations PRIMARY KEY,
        OccurredAt datetime2(7) NOT NULL CONSTRAINT DF_StockOperations_OccurredAt DEFAULT SYSDATETIME(),
        Kind tinyint NOT NULL CONSTRAINT CK_StockOperations_Kind CHECK (Kind IN (0,1,2)),
        ProductId int NOT NULL,
        Quantity decimal(18,3) NOT NULL CONSTRAINT CK_StockOperations_Quantity CHECK (Quantity > 0 AND Quantity <= 1000000000),
        FromWarehouseId int NULL,
        ToWarehouseId int NULL,
        CounterpartyId int NULL,
        Comment nvarchar(500) NOT NULL CONSTRAINT DF_StockOperations_Comment DEFAULT N'',
        UserId int NOT NULL,
        CONSTRAINT FK_StockOperations_Product FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
        CONSTRAINT FK_StockOperations_From FOREIGN KEY (FromWarehouseId) REFERENCES dbo.Warehouses(Id),
        CONSTRAINT FK_StockOperations_To FOREIGN KEY (ToWarehouseId) REFERENCES dbo.Warehouses(Id),
        CONSTRAINT FK_StockOperations_Counterparty FOREIGN KEY (CounterpartyId) REFERENCES dbo.Counterparties(Id),
        CONSTRAINT FK_StockOperations_User FOREIGN KEY (UserId) REFERENCES dbo.WarehouseUsers(Id),
        CONSTRAINT CK_StockOperations_Routing CHECK (
            (Kind = 0 AND FromWarehouseId IS NULL AND ToWarehouseId IS NOT NULL AND CounterpartyId IS NOT NULL) OR
            (Kind = 1 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NULL AND CounterpartyId IS NOT NULL) OR
            (Kind = 2 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NOT NULL AND FromWarehouseId <> ToWarehouseId AND CounterpartyId IS NULL)
        )
    );
    CREATE INDEX IX_StockOperations_Date ON dbo.StockOperations(OccurredAt DESC, Id DESC);
    CREATE INDEX IX_StockOperations_ProductDate ON dbo.StockOperations(ProductId, OccurredAt, Id)
        INCLUDE(Kind, Quantity, FromWarehouseId, ToWarehouseId);
    CREATE INDEX IX_StockBalances_Warehouse ON dbo.StockBalances(WarehouseId, ProductId);
    INSERT INTO dbo.WarehouseSchemaVersion(Id, Version) VALUES (1, 1);
    SELECT CAST(1 AS bit);
END
ELSE
BEGIN
    IF NOT EXISTS (SELECT 1 FROM dbo.WarehouseSchemaVersion WHERE Id = 1 AND Version = 1)
        THROW 51002, N'Версия базы данных несовместима с этой версией приложения.', 1;
    SELECT CAST(0 AS bit);
END
