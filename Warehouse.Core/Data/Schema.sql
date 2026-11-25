-- Схема версии 3 (документы с несколькими позициями и реквизиты ТТН).
-- Создание и недеструктивная миграция v1 → v2 → v3 выполняются в одной транзакции.
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
    -- Реквизиты своей организации (грузоотправитель в ТТН). Одна строка, заполняет администратор.
    CREATE TABLE dbo.OrganizationProfile (
        Id int NOT NULL CONSTRAINT PK_OrganizationProfile PRIMARY KEY CONSTRAINT CK_OrganizationProfile_Single CHECK (Id = 1),
        Name nvarchar(300) NOT NULL CONSTRAINT DF_OrganizationProfile_Name DEFAULT N'',
        Unp nvarchar(9) NOT NULL CONSTRAINT DF_OrganizationProfile_Unp DEFAULT N'',
        Address nvarchar(400) NOT NULL CONSTRAINT DF_OrganizationProfile_Address DEFAULT N'',
        Phone nvarchar(100) NOT NULL CONSTRAINT DF_OrganizationProfile_Phone DEFAULT N'',
        ReleaseAllowedBy nvarchar(200) NOT NULL CONSTRAINT DF_OrganizationProfile_Release DEFAULT N'',
        DefaultVatRate decimal(5,2) NOT NULL CONSTRAINT DF_OrganizationProfile_Vat DEFAULT 20
            CONSTRAINT CK_OrganizationProfile_Vat CHECK (DefaultVatRate >= 0 AND DefaultVatRate <= 100)
    );
    INSERT dbo.OrganizationProfile(Id) VALUES (1);

    -- Товар: ставка НДС (начисляется сверху на цену без НДС), грузовые места и масса для граф 10–11 ТТН.
    ALTER TABLE dbo.Products ADD
        VatRate decimal(5,2) NOT NULL CONSTRAINT DF_Products_VatRate DEFAULT 20
            CONSTRAINT CK_Products_VatRate CHECK (VatRate >= 0 AND VatRate <= 100),
        UnitsPerPlace decimal(18,3) NULL CONSTRAINT CK_Products_UnitsPerPlace CHECK (UnitsPerPlace > 0 AND UnitsPerPlace <= 1000000000),
        UnitMassKg decimal(18,3) NULL CONSTRAINT CK_Products_UnitMass CHECK (UnitMassKg > 0 AND UnitMassKg <= 1000000000);

    -- Контрагент: реквизиты для шапки ТТН.
    ALTER TABLE dbo.Counterparties ADD
        Unp nvarchar(9) NOT NULL CONSTRAINT DF_Counterparties_Unp DEFAULT N'',
        Address nvarchar(400) NOT NULL CONSTRAINT DF_Counterparties_Address DEFAULT N'',
        DeliveryAddress nvarchar(400) NOT NULL CONSTRAINT DF_Counterparties_Delivery DEFAULT N'',
        Phone nvarchar(100) NOT NULL CONSTRAINT DF_Counterparties_Phone DEFAULT N'',
        Contract nvarchar(200) NOT NULL CONSTRAINT DF_Counterparties_Contract DEFAULT N'';

    -- Документ (шапка ТТН). Реквизиты сторон сохраняются снимком на момент проведения,
    -- чтобы последующее изменение справочников не меняло уже выписанные накладные.
    CREATE TABLE dbo.StockDocuments (
        Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockDocuments PRIMARY KEY,
        Kind tinyint NOT NULL CONSTRAINT CK_StockDocuments_Kind CHECK (Kind IN (0,1,2)),
        Number nvarchar(30) NOT NULL CONSTRAINT DF_StockDocuments_Number DEFAULT N'',
        DocumentDate date NULL,
        OccurredAt datetime2(7) NOT NULL CONSTRAINT DF_StockDocuments_OccurredAt DEFAULT SYSDATETIME(),
        FromWarehouseId int NULL CONSTRAINT FK_StockDocuments_From REFERENCES dbo.Warehouses(Id),
        ToWarehouseId int NULL CONSTRAINT FK_StockDocuments_To REFERENCES dbo.Warehouses(Id),
        CounterpartyId int NULL CONSTRAINT FK_StockDocuments_Counterparty REFERENCES dbo.Counterparties(Id),
        UserId int NOT NULL CONSTRAINT FK_StockDocuments_User REFERENCES dbo.WarehouseUsers(Id),
        Comment nvarchar(500) NOT NULL CONSTRAINT DF_StockDocuments_Comment DEFAULT N'',
        ShipperName nvarchar(300) NOT NULL CONSTRAINT DF_StockDocuments_ShipperName DEFAULT N'',
        ShipperUnp nvarchar(9) NOT NULL CONSTRAINT DF_StockDocuments_ShipperUnp DEFAULT N'',
        ShipperAddress nvarchar(400) NOT NULL CONSTRAINT DF_StockDocuments_ShipperAddress DEFAULT N'',
        ConsigneeName nvarchar(300) NOT NULL CONSTRAINT DF_StockDocuments_ConsigneeName DEFAULT N'',
        ConsigneeUnp nvarchar(9) NOT NULL CONSTRAINT DF_StockDocuments_ConsigneeUnp DEFAULT N'',
        ConsigneeAddress nvarchar(400) NOT NULL CONSTRAINT DF_StockDocuments_ConsigneeAddress DEFAULT N'',
        PayerName nvarchar(300) NOT NULL CONSTRAINT DF_StockDocuments_PayerName DEFAULT N'',
        PayerUnp nvarchar(9) NOT NULL CONSTRAINT DF_StockDocuments_PayerUnp DEFAULT N'',
        Vehicle nvarchar(100) NOT NULL CONSTRAINT DF_StockDocuments_Vehicle DEFAULT N'',
        Trailer nvarchar(100) NOT NULL CONSTRAINT DF_StockDocuments_Trailer DEFAULT N'',
        Driver nvarchar(200) NOT NULL CONSTRAINT DF_StockDocuments_Driver DEFAULT N'',
        WaybillNumber nvarchar(50) NOT NULL CONSTRAINT DF_StockDocuments_Waybill DEFAULT N'',
        Basis nvarchar(300) NOT NULL CONSTRAINT DF_StockDocuments_Basis DEFAULT N'',
        LoadingPoint nvarchar(400) NOT NULL CONSTRAINT DF_StockDocuments_Loading DEFAULT N'',
        UnloadingPoint nvarchar(400) NOT NULL CONSTRAINT DF_StockDocuments_Unloading DEFAULT N'',
        ReleaseAllowedBy nvarchar(200) NOT NULL CONSTRAINT DF_StockDocuments_Release DEFAULT N'',
        HandedOverBy nvarchar(200) NOT NULL CONSTRAINT DF_StockDocuments_HandedOver DEFAULT N'',
        LegacyOperationId int NULL,
        CONSTRAINT CK_StockDocuments_Routing CHECK (
            (Kind = 0 AND FromWarehouseId IS NULL AND ToWarehouseId IS NOT NULL AND CounterpartyId IS NOT NULL) OR
            (Kind = 1 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NULL AND CounterpartyId IS NOT NULL) OR
            (Kind = 2 AND FromWarehouseId IS NOT NULL AND ToWarehouseId IS NOT NULL AND FromWarehouseId <> ToWarehouseId AND CounterpartyId IS NULL)
        )
    );
    CREATE INDEX IX_StockDocuments_Date ON dbo.StockDocuments(OccurredAt DESC, Id DESC);
    CREATE INDEX IX_StockDocuments_Number ON dbo.StockDocuments(Number, Kind);

    -- Позиция документа: цена без НДС, скидка, ставка НДС, грузовые места, масса, примечание (Comment).
    ALTER TABLE dbo.StockOperations ADD
        DocumentId int NULL,
        Price decimal(18,2) NULL CONSTRAINT CK_StockOperations_Price CHECK (Price > 0 AND Price <= 1000000000),
        DiscountPercent decimal(5,2) NULL CONSTRAINT CK_StockOperations_Discount CHECK (DiscountPercent >= 0 AND DiscountPercent < 100),
        VatRate decimal(5,2) NULL CONSTRAINT CK_StockOperations_VatRate CHECK (VatRate >= 0 AND VatRate <= 100),
        CargoPlaces decimal(18,3) NULL CONSTRAINT CK_StockOperations_Places CHECK (CargoPlaces >= 0 AND CargoPlaces <= 1000000000),
        CargoMassKg decimal(18,3) NULL CONSTRAINT CK_StockOperations_Mass CHECK (CargoMassKg >= 0 AND CargoMassKg <= 1000000000);

    -- Каждая прежняя однотоварная операция становится документом из одной позиции.
    -- Новые столбцы существующих таблиц доступны только после компиляции пакета, поэтому DML — через EXEC.
    INSERT dbo.StockDocuments(Kind, OccurredAt, FromWarehouseId, ToWarehouseId, CounterpartyId, UserId, Comment, LegacyOperationId)
        SELECT Kind, OccurredAt, FromWarehouseId, ToWarehouseId, CounterpartyId, UserId, N'', Id FROM dbo.StockOperations;
    EXEC(N'UPDATE o SET DocumentId = d.Id FROM dbo.StockOperations o JOIN dbo.StockDocuments d ON d.LegacyOperationId = o.Id;');
    EXEC(N'ALTER TABLE dbo.StockOperations ALTER COLUMN DocumentId int NOT NULL;');
    EXEC(N'ALTER TABLE dbo.StockOperations ADD CONSTRAINT FK_StockOperations_Document FOREIGN KEY (DocumentId) REFERENCES dbo.StockDocuments(Id);');
    EXEC(N'CREATE INDEX IX_StockOperations_Document ON dbo.StockOperations(DocumentId, Id);');
    UPDATE dbo.WarehouseSchemaVersion SET Version=3 WHERE Id=1;
END
SELECT @created;
