﻿IF @DB = N'101' -- Banan SD, USD, en
BEGIN
/*
Entry Type - Account Type - Center - Currency - Contract Definition - Agent
*/
		
	INSERT INTO @Accounts([Index],
		[Code], [AccountTypeId],				[ClassificationId],		[ContractDefinitionId],	[ResourceDefinitionId],	[Name],							[CurrencyId],	[CenterId],		[EntryTypeId],		[ContractId]) VALUES
	-- Assets Accounts
--	(10,N'12001',@FixturesAndFittings,			@NonCurrentAssets_AC,	NULL,					@FixturesAndFittings,	N'Fixtures and fittings',		@USD,			NULL,			@@AdditionsOtherThanThroughBusinessCombinationsPropertyPlantAndEquipment,		NULL),
	(11,N'12002',@OfficeEquipment,				@NonCurrentAssets_AC,	NULL,					@office_equipmentRD,	N'Office equipment',			@USD,			NULL,			@AdditionsOtherThanThroughBusinessCombinationsPropertyPlantAndEquipment,		NULL),
	(12,N'12003',@OfficeEquipment,				@NonCurrentAssets_AC,	NULL,					@office_equipmentRD,	N'Comp. equip. & acc.',			@USD,			NULL,			@AdditionsOtherThanThroughBusinessCombinationsPropertyPlantAndEquipment,		NULL),

--	(310,N'12011',@FixturesAndFittings,			@NonCurrentAssets_AC,	NULL,					@FixturesAndFittings,	N'Acc. Dep.- Fixtures and fittings',@USD,		NULL,			@PPEDepreciations,	NULL),
	(311,N'12012',@OfficeEquipment,				@NonCurrentAssets_AC,	NULL,					@office_equipmentRD,	N'Acc. Dep.- Office equipment',	@USD,			NULL,			@DepreciationPropertyPlantAndEquipment,	NULL),
	(312,N'12013',@OfficeEquipment,				@NonCurrentAssets_AC,	NULL,					@office_equipmentRD,	N'Acc. Dep.- Comp. equip. & acc.',@USD,			NULL,			@DepreciationPropertyPlantAndEquipment,	NULL),

	(15,N'11201',@CurrentTradeReceivables,		@Debtors_AC,			@customersCD,			NULL,					N'Trade Receivables',			NULL,			@C101_INV,		NULL,				NULL),
--	(18,N'11208',@employeeADef,					@Debtors_AC,			@CurrentTradeReceivables,NULL,					N'Employees Expenditures',		NULL,			@C101_INV,		NULL,				NULL),

	(21,N'11211',@OtherCurrentFinancialAssets,	@Debtors_AC,			@debtorsCD,				NULL,					N'Banan ET',					@USD,			@C101_INV,		NULL,				NULL),
	(22,N'11212',@OtherCurrentFinancialAssets,	@Debtors_AC,			@debtorsCD,				NULL,					N'PrimeLedgers A/R',			@USD,			@C101_INV,		NULL,				NULL),

	(23,N'11221',@OtherCurrentFinancialAssets,	@Debtors_AC,			@partnersCD,			NULL,					N'Partners Withdrawals',		@USD,			@C101_INV,		NULL,				NULL),

	(24,N'12021',@OtherCurrentFinancialAssets,	@Debtors_AC,			@employeesCD,			NULL,					N'Abu Ammar Car Loan',			@USD,			@C101_INV,		NULL,				@Abu_Ammar),
	(25,N'12022',@OtherCurrentFinancialAssets,	@Debtors_AC,			@employeesCD,			NULL,					N'M. Ali Car Loan',				@USD,			@C101_INV,		NULL,				@M_Ali),
	(26,N'12023',@OtherCurrentFinancialAssets,	@Debtors_AC,			@employeesCD,			NULL,					N'El-Amin Car Loan',			@USD,			@C101_INV,		NULL,				@el_Amin),
	(27,N'12031',@CurrentValueAddedTaxReceivables,@Debtors_AC,			NULL,					NULL,					N'VAT Input',					NULL,			@C101_INV,		NULL,				NULL),
--	(28,N'11206',@TradeAndOtherCurrentReceivables,@Debtors_AC,		@TradeAndOtherCurrentReceivables,	N'Commissions',					@USD,			@C101_INV,		NULL,				NULL),
	(29,N'11231',@CurrentPrepayments,			@Debtors_AC,			@suppliersCD,			NULL,					N'Office Rent',					@SDG,			@C101_INV,		NULL,				NULL),
	(30,N'11232',@CurrentPrepayments,			@Debtors_AC,			@suppliersCD,			NULL,					N'Internet Prepayment',			@SDG,			@C101_INV,		NULL,				NULL),
	(31,N'11233',@CurrentPrepayments,			@Debtors_AC,			@suppliersCD,			NULL,					N'Car Rent Prepayment',			@SDG,			@C101_INV,		NULL,				NULL),
	(32,N'11234',@CurrentPrepayments,			@Debtors_AC,			@suppliersCD,			NULL,					N'House Rent Prepayment',		@SDG,			@C101_INV,		NULL,				NULL),
	(33,N'11235',@CurrentPrepayments,			@Debtors_AC,			@suppliersCD,			NULL,					N'Maintenance Prepayment',		@SDG,			@C101_INV,		NULL,				NULL),

	(38,N'11241',@DeferredIncomeClassifiedAsCurrent,@Debtors_AC,		@customersCD,			NULL,					N'Accrued Income',				NULL,			@C101_INV,		NULL,				NULL),

	(41,N'11111',@CashOnHand,					@BankAndCash_AC,		@vault_cash_fundsCD,	NULL,					N'GM Fund',						NULL,			@C101_INV,		NULL,				@GMSafe),
	(43,N'11112',@CashOnHand,					@BankAndCash_AC,		@petty_cash_fundsCD,	NULL,					N'Admin Fund - SDG',			@SDG,			@C101_INV,		NULL,				@AdminPettyCash),
	(44,N'11113',@CashOnHand,					@BankAndCash_AC,		@petty_cash_fundsCD,	NULL,					N'KSA Fund',					NULL,			@C101_INV,		NULL,				@KSASafe),
	(45,N'11121',@BalancesWithBanks,			@BankAndCash_AC,		@bank_accountsCD,		NULL,					N'Bank Of Khartoum - SDG',		@SDG,			@C101_INV,		NULL,				@KRTBank),

	-- Equity and Liabilities accounts
	(50,N'30001',@IssuedCapital,				@Equity_AC,				NULL,					NULL,					N'Issued Capital',				@USD,			@C101_INV,		NULL,				NULL),
	(51,N'30002',@RetainedEarnings,				@Equity_AC,				NULL,					NULL,					N'Retained Earnings',			@USD,			@C101_INV,		NULL,				NULL),
	(57,N'30091',@CashPurchaseDocumentControlExtension,@Equity_AC,		@suppliersCD,			NULL,					N'Cash Suppliers',				NULL,			@C101_INV,		NULL,				NULL),
	(58,N'30092',@CashSaleDocumentControlExtension,@Equity_AC,			@customersCD,			NULL,					N'Cash Customers',				NULL,			@C101_INV,		NULL,				NULL),

	(59,N'30099',@OtherDocumentControlExtension,@Equity_AC,				NULL,					NULL,					N'Cash Payments',				NULL,			@C101_INV,		NULL,				NULL),

--	(61,N'21101',@employeeADef,	@CurrentLiabilities_AC,@TradeAndOtherCurrentPayables,	N'Employees Payables',			NULL,			@C101_INV,		NULL,				NULL),
	(63,N'21103',@OtherCurrentFinancialLiabilities,@CurrentLiabilities_AC,@employeesCD,			NULL,					N'10% Retained Salaries',		@USD,			@C101_INV,		NULL,				NULL),
	(64,N'21201',@OtherCurrentFinancialLiabilities,@CurrentLiabilities_AC,@debtorsCD,			NULL,					N'PrimeLedgers A/P',			@USD,			@C101_INV,		NULL,				NULL),

	(65,N'21202',@TradeAndOtherCurrentPayablesToTradeSuppliers,@CurrentLiabilities_AC,@suppliersCD,NULL,				N'Trade Payables',				NULL,			@C101_INV,		NULL,				NULL),
	(66,N'21203',@AccrualsClassifiedAsCurrent,@CurrentLiabilities_AC,	@suppliersCD,			NULL,					N'Accrued Expenses',			NULL,			@C101_INV,		NULL,				NULL),
	(67,N'21299',@DeferredIncomeClassifiedAsCurrent,@CurrentLiabilities_AC,@customersCD,		NULL,					N'Unearned Revenues',			NULL,			@C101_INV,		NULL,				NULL),

	(68,N'21301',@OtherCurrentPayables,@CurrentLiabilities_AC,			@partnersCD,			NULL,					N'Dividends Payables',			@USD,			@C101_INV,		NULL,				NULL),
	(70,N'21302',@OtherCurrentFinancialLiabilities,@CurrentLiabilities_AC,@partnersCD,			NULL,					N'Borrowings from M/A',			@USD,			@C101_INV,		NULL,				@PartnerMA),
	
	(71,N'21401',@CurrentValueAddedTaxPayables,@CurrentLiabilities_AC,	NULL,					NULL,					N'VAT Output',					@SDG,			@C101_INV,		NULL,				NULL),
	(72,N'3110',@CurrentSocialSecurityPayablesExtension,@CurrentLiabilities_AC,	NULL,			NULL,					N'Employee Pensions',			@SDG,			@C101_INV,		NULL,				NULL),
	(73,N'3120',@CurrentZakatPayablesExtension,@CurrentLiabilities_AC,	NULL,					NULL,					N'Zakat',						@SDG,			@C101_INV,		NULL,				NULL),
	(75,N'21402',@CurrentEmployeeIncomeTaxPayablesExtension,@CurrentLiabilities_AC,NULL,		NULL,					N'Employees Income Tax',		@SDG,			@C101_INV,		NULL,				NULL),
	(76,N'21403',@CurrentEmployeeStampTaxPayablesExtension,	@CurrentLiabilities_AC,NULL,		NULL,					N'Employees Stamp Tax',			@SDG,			@C101_INV,		NULL,				NULL),

		-- Profit/Loss Accounts
	(91,N'40001',@RevenueFromRenderingOfServices,@Revenue_AC,			NULL,					NULL,					N'Revenues',					NULL,			NULL,			NULL,				NULL),
	(92,N'40002',@RevenueFromRenderingOfServices,@Revenue_AC,			NULL,					NULL,					N'Subscription Revenues',		NULL,			NULL,			NULL,				NULL),
	(99,N'40005',@RevenueFromRenderingOfServices,@Revenue_AC,			NULL,					NULL,					N'Rental Income - SAR',			@SAR,			@C101_FFLR,		NULL,				NULL),
	--(110,N'50050',@purchase_expenseADef,@Expenses_AC,@OtherExpenseByNature,				N'Domain Registration',			@USD,			@C101_EXEC,		@AdministrativeExpense,NULL),
	--(120,N'50060',@purchase_expenseADef,@Expenses_AC,@ServicesExpense,					N'Maintenance',					@SDG,			@C101_EXEC,		@AdministrativeExpense,NULL),
	--(125,N'50062',@purchase_expenseADef,@Expenses_AC,@OtherExpenseByNature,				N'Employee Meals',				NULL,			@C101_UNALLOC,	@ServiceExtension,	NULL),
	(130,N'50065',@CommunicationExpense,		@Expenses_AC,			NULL,					NULL,					N'Internet & Tel',				NULL,			@C101_Sys,		@ServiceExtension,	NULL),
	(140,N'50070',@UtilitiesExpense,			@Expenses_AC,			NULL,					NULL,					N'Electricity',					@SDG,			@C101_PWG,		@ServiceExtension,	NULL),
	(190,N'50100',@EmployeeBonusExtension,		@Expenses_AC,			NULL,					NULL,					N'Bonuses',						NULL,			NULL,			NULL,				NULL),
	(195,N'50120',@TerminationBenefitsExpense,	@Expenses_AC,			NULL,					NULL,					N'Termination Benefits',		@SDG,			NULL,			NULL,				NULL),
	(200,N'50401',@GainLossOnForeignExchangeExtension,@Expenses_AC,		NULL,					NULL,					N'Exchange Loss (Gain)',		@USD,			NULL,			NULL,				NULL);

/*
-- 5: Direct, Cost of sales
-- 6: Indirect, Production, 7:service
-- 8:Distribution
-- 9:Admin. Nature: 2 digits, Center: 2 digits, Varieties: 1

	(101,N'90510',	@ProfessionalFeesExpense,		N'Acc. & Legal Services - USD',	@USD,			@C101_EXEC,		@AdministrativeExpense,	NULL),
	(102,N'90511',	@ProfessionalFeesExpense,		N'Acc. & Legal Services - SDG',	@SDG,			@C101_EXEC,		@AdministrativeExpense,	NULL),

	(110,N'X0611',	@TransportationExpense,			N'Transportation',			NULL,			NULL,			NULL,					NULL),
	(115,N'90710',	@BankAndSimilarCharges,			N'Banking Services',		NULL,			@C101_EXEC,		@AdministrativeExpense,	NULL),
	(120,N'X0811',	@TravelExpense,					N'Visa & Travel',			NULL,			NULL,			NULL,					NULL),
	(135,N'50511',	@ServicesExpense,				N'Cloud Hosting',			@USD,			NULL,			@CostOfSales,			NULL),
	
	(140,N'A1000',	@UtilitiesExpense,				N'Utilities',				@SDG,			@C101_UNALLOC,	@OtherExpenseByFunction,NULL),
	(145,N'A9900',	@ServicesExpense,				N'Office Rental',			@SDG,			@C101_UNALLOC,	@OtherExpenseByFunction,NULL),

	(150,N'81120',	@AdvertisingExpense,			N'Marketing Service - SDG',		NULL,			@C101_Sales,	@DistributionCosts,		NULL),
	(151,N'81120',	@AdvertisingExpense,			N'Marketing Service - SDG',		NULL,			@C101_Sales,	@DistributionCosts,		NULL),
	
	(165,N'X05113',	@ServicesExpense,				N'Medical',					NULL,			NULL,			NULL,					NULL),

	(170,N'92310',	@WagesAndSalaries,		N'Salaries - Exec Office Equip.',	@USD,			@C101_EXEC,		@AdministrativeExpense,	NULL),
	(171,N'82320',	@WagesAndSalaries,		N'Salaries - Sales Equip.',			@USD,			@C101_Sales,	@DistributionCosts,		NULL),
	(172,N'72330',	@WagesAndSalaries,		N'Salaries - Sys Admin Equip.',		@USD,			@C101_Sys,		@ServiceExtension,		NULL),
	(173,N'52340',	@WagesAndSalaries,		N'Salaries - B10/HCM Equip.',		@USD,			@C101_B10,		@CostOfSales,			NULL),
	(174,N'52350',	@WagesAndSalaries,		N'Salaries - BSmart Equip.',		@USD,			@C101_BSmart,	@CostOfSales,			NULL),
	(175,N'52360',	@WagesAndSalaries,		N'Salaries - Campus Equip.',		@USD,			@C101_Campus,	@CostOfSales,			NULL),
	(176,N'52370',	@WagesAndSalaries,		N'Salaries - Tellma Equip.',		@USD,			@C101_Tellma,	@CostOfSales,			NULL),
	(177,N'52380',	@WagesAndSalaries,		N'Salaries - Floor Rental Equip.',	@USD,			@C101_FFLR,		@CostOfSales,			NULL),

	(178,N'X1201',	@EmployeeBenefitsExpense,		N'Zakat & Eid',				@USD,			NULL,			NULL,					NULL),

	(180,N'50550',	@ProfessionalFeesExpense,		N'Salaries - B10 Contractors',@USD,			@C101_B10,		@CostOfSales,			NULL),
	(185,N'X1500',	@SocialSecurityContributions,	N'Employee Pension Contribution',@SDG,		NULL,			NULL,					NULL),

	(200,N'X99002',	@OtherExpenseByNature,			N'Stationery & Grocery',	NULL,			NULL,			NULL,					NULL),

	(205,N'99910',	@OtherExpenseByNature,			N'Gov fees',				@SDG,			@C101_EXEC,		@AdministrativeExpense,	NULL),

	(210,N'89920',	@OtherExpenseByNature,			N'Presentation tools',		NULL,			@C101_Sales,	@DistributionCosts,		NULL),
	(215,N'X99003',	@OtherExpenseByNature,			N'Education & Certifications',NULL,			NULL,			NULL,					NULL),
	(220,N'X99004',	@OtherExpenseByNature,			N'Consumables',				NULL,			NULL,			NULL,					NULL),
	(225,N'X99005',	@OtherExpenseByNature,			N'Tender Fees',				NULL,			NULL,			NULL,					NULL),
	(230,N'X99006',	@OtherExpenseByNature,			N'Office Furniture',		NULL,			NULL,			NULL,					NULL),
	(235,N'X99007',	@OtherExpenseByNature,			N'Other Expenses',			NULL,			NULL,			NULL,					NULL),

	(241,N'92311',	@DepreciationExpense,	N'Dep. Exp. - Exec Office Equip.',	@USD,			@C101_EXEC,		@AdministrativeExpense,	NULL),
	(242,N'82321',	@DepreciationExpense,	N'Dep. Exp. - Sales Equip.',		@USD,			@C101_Sales,	@DistributionCosts,		NULL),
	(243,N'72331',	@DepreciationExpense,	N'Dep. Exp. - Sys Admin Equip.',	@USD,			@C101_Sys,		@ServiceExtension,		NULL),
	(244,N'52341',	@DepreciationExpense,	N'Dep. Exp. - B10/HCM Equip.',		@USD,			@C101_B10,		@CostOfSales,			NULL),
	(245,N'52351',	@DepreciationExpense,	N'Dep. Exp. - BSmart Equip.',		@USD,			@C101_BSmart,	@CostOfSales,			NULL),
	(246,N'52361',	@DepreciationExpense,	N'Dep. Exp. - Campus Equip.',		@USD,			@C101_Campus,	@CostOfSales,			NULL),
	(247,N'52371',	@DepreciationExpense,	N'Dep. Exp. - Tellma Equip.',		@USD,			@C101_Tellma,	@CostOfSales,			NULL),
	(248,N'52381',	@DepreciationExpense,	N'Dep. Exp. - Floor Rental Equip.',	@USD,			@C101_FFLR,		@CostOfSales,			NULL),

	(250,N'B01',	@GainLossOnDisposalOfPropertyPlantAndEquipment,
														N'Gain (loss) on disposal',	@USD,			@C101_INV,			NULL,					NULL);

-- 5: Direct, Cost of sales
-- 6: Indirect, Production, 7:service
-- 8:Distribution
-- 9:Admin. Nature: 2 digits, Center: 2 digits, Varieties: 1

	-- Expenses
	(69,	@ProfessionalFeesExpense,	N'Accounting Services',	NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(83,	@ProfessionalFeesExpense,	N'Legal Services',		NULL,			@C101_INV,					NULL,			NULL,	NULL),

	(62,	@TransportationExpense,		N'Transportation',		NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(82,	@BankAndSimilarCharges,		N'Banking Services',	NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(80,	@TravelExpense,				N'Travel',				NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(81,	@TravelExpense,				N'Visa',				NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(60,	@UtilitiesExpense,			N'Cloud Hosting',		NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(72,	@UtilitiesExpense,			N'Office Rental',		NULL,			@C101_INV,					NULL,			NULL,	NULL),

	(64,	@AdvertisingExpense,		N'Marketing Service',	NULL,			@C101_INV,					@DistributionCosts,NULL,	NULL),

	(65,@WagesAndSalaries,		N'Salaries',			NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(79,@WagesAndSalaries,		N'Zakat & Eid',			NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(66,@WagesAndSalaries,		N'Contractors Salaries',			@C101_INV,					NULL,			NULL,	NULL),
	(70,@SocialSecurityContributions,N'Employee Pension Contribution',@C101_INV,					NULL,			NULL,	NULL),
	(88,@EmployeeBenefitsExpense,	N'Allowances & Bonuses',			@C101_INV,					NULL,			NULL,	NULL),
	
	
	(71,@OtherExpenseByNature,	N'Stationery & Grocery',			@C101_INV,					NULL,			NULL,	NULL),

	(74,@OtherExpenseByNature,	N'Gov fees',			NULL,			@C101_INV,					NULL,			NULL,	NULL),

	(77,@OtherExpenseByNature,	N'Maintenance',			NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(78,@OtherExpenseByNature,	N'Medical',				NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(84,@OtherExpenseByNature,	N'Presentation tools',	NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(85,@OtherExpenseByNature,	N'Education & Certifications',		@C101_INV,					NULL,			NULL,	NULL),
	(86,@OtherExpenseByNature,	N'Consumables',			NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(87,@OtherExpenseByNature,	N'Tender Fees',			NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(89,@OtherExpenseByNature,	N'Office Furniture',	NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(90,@OtherExpenseByNature,	N'Other Expenses',		NULL,			@C101_INV,					NULL,			NULL,	NULL),

	(98,@OtherExpenseByNature,	N'Depreciation',		NULL,			@C101_INV,					NULL,			NULL,	NULL),
	(99,@OtherExpenseByNature,	N'Gain (loss) on disposal',	NULL,		@C101_INV,					NULL,			NULL,	NULL);
*/

	EXEC [api].[Accounts__Save]
		@Entities = @Accounts,
		@ValidationErrorsJson = @ValidationErrorsJson OUTPUT;

	IF @ValidationErrorsJson IS NOT NULL 
	BEGIN
		Print 'Inserting Accounts: ' + @ValidationErrorsJson
		GOTO Err_Label;
	END;

	DECLARE @1GMFund INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'GM Fund');
	DECLARE @1AdminPC INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Admin Fund - SDG');
	DECLARE @1KSAFund INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'KSA Fund');
	DECLARE @1BOK INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Bank Of Khartoum - SDG');
	DECLARE @1Meals INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Employee Meals');
	DECLARE @1Education INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Education & Certifications');
	DECLARE @1MAPayable INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Borrowings from M/A');
	DECLARE @1DomainRegistration INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Domain Registration');
	DECLARE @1Maintenance INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Maintenance');
	DECLARE @1Electricity INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Electricity');
	DECLARE @1Internet INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Internet & Tel');
	DECLARE @1EITax INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Employees Income Tax');
	DECLARE @1EStax INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Employees Stamp Tax');
	DECLARE @1AR INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Trade Receivables');
	DECLARE @1Revenues INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Revenues');
	DECLARE @1SubscriptionRevenues INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Subscription Revenues');
	DECLARE @1RentalIncome INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Rental Income - SAR');


	DECLARE @1DocumentControl INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Document Control');
	DECLARE @1VATInput INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'VAT Input');
	DECLARE @1VATOutput INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'VAT Output');

	DECLARE @1RetainedSalaries INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'10% Retained Salaries');
	DECLARE @1Bonuses INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Bonuses');
	DECLARE @1Termination INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Termination Benefits');
	DECLARE @1CashSuppliers INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Cash Suppliers');
	DECLARE @1CashCustomers INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Cash Customers');
	DECLARE @1ExchangeGainLoss INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Exchange Loss (Gain)');
	DECLARE @1ExchangeVariance INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Exchange Variance');
	DECLARE @1TradeReceivables INT = (SELECT [Id] FROM dbo.Accounts WHERE [Name] = N'Trade Receivables');
END