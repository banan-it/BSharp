﻿INSERT INTO @ContractDefinitions([Index], [Code], [TitleSingular], [TitlePlural], [MainMenuIcon], [MainMenuSection], [MainMenuSortKey]) VALUES
(0, N'Creditor', N'Creditor', N'Creditors', N'hands', N'Financials',100),
(1, N'Debtor', N'Debtor', N'Debtors', N'hand-holding-usd', N'Financials',105),
(2, N'Owner', N'Owner', N'Owners', N'power-off', N'Financials',110),
(3, N'Partner', N'Partner', N'Partners', N'user-tie', N'Financials',115),
(4, N'Supplier', N'Supplier', N'Suppliers', N'user-tag', N'Purchasing',120),
(5, N'Customer', N'Customer', N'Customers', N'user-shield', N'Sales',125),
(6, N'Employee', N'Employee', N'Employees', N'user-friends', N'HumanCapital',130),
(7, N'BankAccount', N'Bank Account', N'Bank Accounts', N'book', N'Cash',135),
(8, N'CashOnHandAccount', N'Cash On Hand Account', N'Cash On Hand Accounts', N'funnel-dollar', N'Cash',140),
(9, N'Warehouse', N'Warehouse', N'Warehouses', N'warehouse', N'Inventory',145),
(10, N'Shipper', N'Shipper', N'Shippers', N'ship', N'Purchasing',160);

UPDATE @ContractDefinitions
SET [AgentVisibility] = N'Optional'
WHERE [Code] IN (N'CashOnHandAccount', N'Employee', N'Supplier', N'Customer', N'Partner')

UPDATE @ContractDefinitions
SET [CenterVisibility] = N'Optional'
WHERE [Code] IN (N'CashOnHandAccount', N'Employee', N'Supplier')

UPDATE @ContractDefinitions
SET [CurrencyVisibility] = N'Required'
WHERE [Code] IN (N'BankAccount', N'Customer')

UPDATE @ContractDefinitions
SET [CenterVisibility] = N'Optional'
WHERE [Code] IN (N'Supplier')

UPDATE @ContractDefinitions
SET [CenterVisibility] = N'Required'
WHERE [Code] IN (N'CashOnHandAccount', N'BankAccount', N'Customer', N'Employee', N'Warehouse')

UPDATE @ContractDefinitions
SET [BankAccountNumberVisibility] = N'Optional'
WHERE [Code] IN (N'Employee', N'BankAccount')

UPDATE @ContractDefinitions
SET [TaxIdentificationNumberVisibility] = N'Optional'
WHERE [Code] IN (N'Employee', N'Supplier', N'Customer')

UPDATE @ContractDefinitions
SET [ImageVisibility] = N'Optional'
WHERE [Code] IN (N'Employee')

UPDATE @ContractDefinitions
SET [UserCardinality] = N'Single'
WHERE [Code] IN (N'Employee', N'Partner', N'CashOnHandAccount')

UPDATE @ContractDefinitions
SET [UserCardinality] = N'Multiple'
WHERE [Code] IN ( N'BankAccount', N'Warehouse')

UPDATE @ContractDefinitions
SET [Lookup1Visibility] = N'Optional', [Lookup1Label] = N'Account Type', [Lookup1DefinitionId] = @BankAccountTypeLKD
WHERE [Code] IN ( N'BankAccount')

UPDATE @ContractDefinitions
SET [Lookup1Visibility] = N'Optional', [Lookup1Label] = N'Market Segment', [Lookup1DefinitionId] = @MarketSegmentLKD
WHERE [Code] IN ( N'Customer')

EXEC [api].[ContractDefinitions__Save]
	@Entities = @ContractDefinitions,
	@ValidationErrorsJson = @ValidationErrorsJson OUTPUT;

IF @ValidationErrorsJson IS NOT NULL 
BEGIN
	Print 'ContractDefinitions: Inserting: ' + @ValidationErrorsJson
	GOTO Err_Label;
END;
--Declarations
DECLARE @CreditorCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Creditor');
DECLARE @DebtorCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Debtor');
DECLARE @OwnerCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Owner');
DECLARE @PartnerCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Partner');
DECLARE @SupplierCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Supplier');
DECLARE @CustomerCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Customer');
DECLARE @EmployeeCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Employee');
DECLARE @BankAccountCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'BankAccount');
DECLARE @CashOnHandAccountCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'CashOnHandAccount');
DECLARE @WarehouseCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Warehouse');
DECLARE @ShipperCD INT = (SELECT [Id] FROM dbo.ContractDefinitions WHERE [Code] = N'Shipper');