-- Схема версии 3. Создание и недеструктивная миграция выполняются в одной транзакции.
DECLARE @lockResult int;
EXEC @lockResult = sys.sp_getapplock @Resource = N'WarehouseCourse.Schema',
    @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 15000;
IF @lockResult < 0 THROW 51000, N'Не удалось заблокировать создание схемы.', 1;

DECLARE @created bit = 0;
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
    SET @created = 1;
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.WarehouseSchemaVersion WHERE Id = 1 AND Version IN (1, 2, 3))
    THROW 51002, N'Версия базы данных несовместима с этой версией приложения.', 1;

IF EXISTS (SELECT 1 FROM dbo.WarehouseSchemaVersion WHERE Id=1 AND Version=1)
BEGIN
    -- WITH NOCHECK сохраняет старые записи с нулевой ценой/пустыми полями.
    -- SQL Server всё равно проверяет новые и изменённые записи; исправьте старые через интерфейс.
    ALTER TABLE dbo.Products WITH NOCHECK ADD CONSTRAINT CK_Products_Purchase_Positive CHECK (PurchasePrice > 0);
    ALTER TABLE dbo.Products WITH NOCHECK ADD CONSTRAINT CK_Products_Sale_Positive CHECK (SalePrice > 0);
    ALTER TABLE dbo.Warehouses WITH NOCHECK ADD CONSTRAINT CK_Warehouses_Required CHECK (
        LEN(LTRIM(RTRIM(TRANSLATE(Name, NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288), REPLICATE(N' ',24))))) > 0 AND
        LEN(LTRIM(RTRIM(TRANSLATE(Address, NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288), REPLICATE(N' ',24))))) > 0 AND
        LEN(LTRIM(RTRIM(TRANSLATE(ManagerName, NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288), REPLICATE(N' ',24))))) > 0 AND
        LEN(LTRIM(RTRIM(TRANSLATE(ManagerContact, NCHAR(9)+NCHAR(10)+NCHAR(11)+NCHAR(12)+NCHAR(13)+NCHAR(133)+NCHAR(160)+NCHAR(5760)+NCHAR(8192)+NCHAR(8193)+NCHAR(8194)+NCHAR(8195)+NCHAR(8196)+NCHAR(8197)+NCHAR(8198)+NCHAR(8199)+NCHAR(8200)+NCHAR(8201)+NCHAR(8202)+NCHAR(8232)+NCHAR(8233)+NCHAR(8239)+NCHAR(8287)+NCHAR(12288), REPLICATE(N' ',24))))) > 0);
    ALTER TABLE dbo.WarehouseUsers DROP CONSTRAINT CK_WarehouseUsers_Role;
    ALTER TABLE dbo.WarehouseUsers ADD CONSTRAINT CK_WarehouseUsers_Role CHECK (Role IN (0, 1, 2, 3));
    ALTER TABLE dbo.WarehouseUsers ADD IsActive bit NOT NULL CONSTRAINT DF_WarehouseUsers_IsActive DEFAULT 1;
    -- Учётные записи с историей не уничтожаются: удаление блокирует вход и сохраняет автора операций.
    CREATE TABLE dbo.AuditStreams (
        StreamId uniqueidentifier NOT NULL CONSTRAINT PK_AuditStreams PRIMARY KEY,
        LastWrittenEventId bigint NOT NULL CONSTRAINT DF_AuditStreams_LastWritten DEFAULT 0
    );
    CREATE TABLE dbo.AuditEvents (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditEvents PRIMARY KEY,
        StreamId uniqueidentifier NOT NULL CONSTRAINT FK_AuditEvents_Stream REFERENCES dbo.AuditStreams(StreamId),
        OccurredAtUtc datetime2(7) NOT NULL CONSTRAINT DF_AuditEvents_Time DEFAULT SYSUTCDATETIME(),
        ActorId int NULL,
        ActorLogin nvarchar(80) NOT NULL,
        ActorName nvarchar(200) NOT NULL,
        ActorRole nvarchar(80) NOT NULL,
        Action nvarchar(120) NOT NULL,
        Entity nvarchar(80) NOT NULL,
        EntityId nvarchar(80) NOT NULL,
        Details nvarchar(max) NOT NULL
    );
    CREATE INDEX IX_AuditEvents_Stream ON dbo.AuditEvents(StreamId, Id);
    UPDATE dbo.WarehouseSchemaVersion SET Version=2 WHERE Id=1;
END
IF EXISTS (SELECT 1 FROM dbo.WarehouseSchemaVersion WHERE Id=1 AND Version=2)
BEGIN
    -- Старые движения остаются отдельными строками без BatchId. Данные не пересчитываются.
    ALTER TABLE dbo.Products ADD UnitWeightKg decimal(18,6) NULL,
        UnitsPerCargoPlace decimal(18,3) NULL;
    EXEC(N'ALTER TABLE dbo.Products ADD CONSTRAINT CK_Products_UnitWeight CHECK (UnitWeightKg IS NULL OR (UnitWeightKg>0 AND UnitWeightKg<=1000000000))');
    EXEC(N'ALTER TABLE dbo.Products ADD CONSTRAINT CK_Products_CargoUnits CHECK (UnitsPerCargoPlace IS NULL OR (UnitsPerCargoPlace>0 AND UnitsPerCargoPlace<=1000000000))');
    ALTER TABLE dbo.Counterparties ADD Address nvarchar(400) NOT NULL CONSTRAINT DF_Counterparties_Address DEFAULT N'',
        Unp nvarchar(30) NOT NULL CONSTRAINT DF_Counterparties_Unp DEFAULT N'';
    CREATE TABLE dbo.OrganizationProfile (
        Id int NOT NULL CONSTRAINT PK_OrganizationProfile PRIMARY KEY CHECK (Id=1),
        Name nvarchar(200) NOT NULL, Address nvarchar(400) NOT NULL, Unp nvarchar(30) NOT NULL
    );
    INSERT dbo.OrganizationProfile(Id,Name,Address,Unp) VALUES(1,N'',N'',N'');
    CREATE TABLE dbo.OperationBatches (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OperationBatches PRIMARY KEY,
        OccurredAt datetime2(7) NOT NULL CONSTRAINT DF_OperationBatches_Time DEFAULT SYSDATETIME(),
        Kind tinyint NOT NULL CONSTRAINT CK_OperationBatches_Kind CHECK(Kind IN (0,1,2)),
        FromWarehouseId int NULL CONSTRAINT FK_OperationBatches_From REFERENCES dbo.Warehouses(Id),
        ToWarehouseId int NULL CONSTRAINT FK_OperationBatches_To REFERENCES dbo.Warehouses(Id),
        CounterpartyId int NULL CONSTRAINT FK_OperationBatches_Party REFERENCES dbo.Counterparties(Id),
        Comment nvarchar(500) NOT NULL, UserId int NOT NULL CONSTRAINT FK_OperationBatches_User REFERENCES dbo.WarehouseUsers(Id),
        FromWarehouseNameSnapshot nvarchar(200) NULL, ToWarehouseNameSnapshot nvarchar(200) NULL,
        CounterpartyNameSnapshot nvarchar(200) NULL, UserNameSnapshot nvarchar(200) NULL,
        CONSTRAINT CK_OperationBatches_Routing CHECK (
            (Kind=0 AND FromWarehouseId IS NULL AND ToWarehouseId IS NOT NULL AND CounterpartyId IS NOT NULL) OR
            (Kind=1 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NULL AND CounterpartyId IS NOT NULL) OR
            (Kind=2 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NOT NULL AND FromWarehouseId<>ToWarehouseId AND CounterpartyId IS NULL))
    );
    ALTER TABLE dbo.StockOperations ADD BatchId int NULL CONSTRAINT FK_StockOperations_Batch REFERENCES dbo.OperationBatches(Id),
        ProductNameSnapshot nvarchar(200) NULL, UnitSnapshot nvarchar(30) NULL;
    EXEC(N'CREATE INDEX IX_StockOperations_Batch ON dbo.StockOperations(BatchId,Id)');
    CREATE INDEX IX_OperationBatches_Date ON dbo.OperationBatches(OccurredAt DESC,Id DESC);
    CREATE TABLE dbo.InvoiceSequence (
        Id int NOT NULL CONSTRAINT PK_InvoiceSequence PRIMARY KEY CHECK (Id=1),
        NextNumber bigint NOT NULL CONSTRAINT CK_InvoiceSequence_Positive CHECK (NextNumber>0)
    );
    INSERT dbo.InvoiceSequence(Id,NextNumber) VALUES(1,1);
    CREATE TABLE dbo.Invoices (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Invoices PRIMARY KEY,
        BatchId int NOT NULL CONSTRAINT UQ_Invoices_Batch UNIQUE CONSTRAINT FK_Invoices_Batch REFERENCES dbo.OperationBatches(Id),
        Series nvarchar(30) NOT NULL, Number nvarchar(50) NOT NULL,
        DocumentDate date NOT NULL,
        Payload nvarchar(max) NOT NULL CONSTRAINT CK_Invoices_Json CHECK(ISJSON(Payload)=1),
        CONSTRAINT UQ_Invoices_SeriesNumber UNIQUE(Series,Number)
    );
    UPDATE dbo.WarehouseSchemaVersion SET Version=3 WHERE Id=1;
END
SELECT @created;
