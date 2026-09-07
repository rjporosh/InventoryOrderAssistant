/* -------------------------------------------------------------------------
   Inventory & Order Assistant - schema + seed + read-only login
   Run once against a SQL Server instance:  sqlcmd -S localhost -i Database/schema.sql
   ------------------------------------------------------------------------- */

IF DB_ID('InventoryDb') IS NULL
    CREATE DATABASE InventoryDb;
GO

USE InventoryDb;
GO

/* ---- Tables ---------------------------------------------------------------- */
DROP TABLE IF EXISTS dbo.OrderItems;
DROP TABLE IF EXISTS dbo.Orders;
DROP TABLE IF EXISTS dbo.Products;
DROP TABLE IF EXISTS dbo.Customers;
GO

CREATE TABLE dbo.Customers (
    CustomerId  INT IDENTITY PRIMARY KEY,
    Name        NVARCHAR(120) NOT NULL,
    Email       NVARCHAR(200) NOT NULL,
    CreatedAt   DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE dbo.Products (
    ProductId    INT IDENTITY PRIMARY KEY,
    Name         NVARCHAR(160) NOT NULL,
    Sku          NVARCHAR(40)  NOT NULL UNIQUE,
    UnitPrice    DECIMAL(10,2) NOT NULL,
    UnitsInStock INT           NOT NULL
);

CREATE TABLE dbo.Orders (
    OrderId    INT IDENTITY PRIMARY KEY,
    CustomerId INT           NOT NULL REFERENCES dbo.Customers(CustomerId),
    OrderDate  DATETIME2     NOT NULL DEFAULT SYSUTCDATETIME(),
    Status     NVARCHAR(20)  NOT NULL DEFAULT 'Completed'
);

CREATE TABLE dbo.OrderItems (
    OrderItemId INT IDENTITY PRIMARY KEY,
    OrderId     INT           NOT NULL REFERENCES dbo.Orders(OrderId),
    ProductId   INT           NOT NULL REFERENCES dbo.Products(ProductId),
    Quantity    INT           NOT NULL,
    UnitPrice   DECIMAL(10,2) NOT NULL
);
GO

/* ---- Seed data ----------------------------------------------------------- */
INSERT dbo.Customers (Name, Email) VALUES
    ('Rafi Ahmed',    'rafi@example.com'),
    ('Nadia Khan',    'nadia@example.com'),
    ('Tanvir Hasan',  'tanvir@example.com'),
    ('Priya Das',     'priya@example.com');

INSERT dbo.Products (Name, Sku, UnitPrice, UnitsInStock) VALUES
    ('USB-C Cable 1m',      'CBL-USBC-1M', 4.50,  6),
    ('Wireless Mouse',      'MOU-WL-01',   12.00, 40),
    ('Mechanical Keyboard', 'KEY-MECH-87', 65.00, 3),
    ('Laptop Stand',        'STD-LAP-01',  22.00, 9),
    ('27in Monitor',        'MON-27-4K',   240.00, 15);

/* Orders: a few recent, a few old, so the sample questions have real answers. */
INSERT dbo.Orders (CustomerId, OrderDate, Status) VALUES
    (1, DATEADD(day,  -2, SYSUTCDATETIME()), 'Completed'),
    (1, DATEADD(day,  -5, SYSUTCDATETIME()), 'Completed'),
    (2, DATEADD(day,  -3, SYSUTCDATETIME()), 'Completed'),
    (3, DATEADD(day, -40, SYSUTCDATETIME()), 'Completed'),
    (1, DATEADD(day, -80, SYSUTCDATETIME()), 'Completed');

INSERT dbo.OrderItems (OrderId, ProductId, Quantity, UnitPrice) VALUES
    (1, 2, 1, 12.00), (1, 1, 3, 4.50),
    (2, 3, 1, 65.00),
    (3, 5, 2, 240.00),
    (4, 4, 1, 22.00),
    (5, 2, 2, 12.00);
GO

/* ---- Read-only login used by the app ----------------------------------- */
IF SUSER_ID('inventory_reader') IS NULL
    CREATE LOGIN inventory_reader WITH PASSWORD = 'Reader_Passw0rd!';
GO

USE InventoryDb;
GO
IF USER_ID('inventory_reader') IS NULL
    CREATE USER inventory_reader FOR LOGIN inventory_reader;
ALTER ROLE db_datareader ADD MEMBER inventory_reader;   -- SELECT only, no writes/DDL
DENY EXECUTE TO inventory_reader;
GO
