﻿/*
Using Defined Account Classifications, we need the following account types

Cash flow statement:
--------------------
- Non cash asset types: current (operating), non-current (investing)
- liability types: current (operating), non-current (financing)
- equity types (financing)

Balance Sheet
-------------
-- Assets, Equity and Liabilities + One virtual line = Net P/L As of today, to balance it all

Income Statement
----------------
-- P/L + Design the classification so that it mimics the desired view

Aging Payable & Receivable
--------------------------
Account Type = Payable or receivable, then run algorithm on Accounts (extended)

Other Period Statements
-----------------------
Fix the classification (e.g., inventory, Fixed Assets, etc) and show per account (extended):
Opening, Debit, Credit, Closing
*/

INSERT INTO dbo.AccountTypes 
([Id],					[AgentRelationDefinitionList],	[ResourceTypeList]) VALUES
(N'NonFinancialAsset',	N'storage-custodies',			N'Inventories,PropertyPlantAndEquipment'),-- smart, refers to resources on hand
(N'Receivable',			N'customers',					N'Cash'),-- smart: for postpaid customers, Dr. for sales invoice, Cr. for payment
(N'AccruedIncome',		N'customers',					N'Cash'),-- smart: Dr. for G/S issue, Cr. for sales invoice
(N'Cash',				N'banks,cashiers',				N'Cash'), -- smart: for CPV, also for Cash flow statement, built on Account Classification
(N'OtherAsset',			NULL,							N'Cash'), -- GL
---
(N'Capital',			N'owners',						N'Shares'),  -- smart: for retained earnings, and to calculate ROI
(N'OtherEquity',		NULL,							N'Cash'), -- G/L
--
(N'Payable',			N'employees,suppliers',			N'Cash'),-- smart: for postpaid suppliers, Cr. For purchase invoice, Dr. for payment
(N'Accruals',			N'suppliers',					N'Cash'),  -- smart: for purchase invoice, Cr. For G/S receipt, Dr. for purchase invoice
(N'OtherLiability',		N'customers,owners',			N'Cash'), -- G/L
--
(N'OperatingRevenue',	N'cost-units',					N'FinishedGoods,Merchandise'), -- smart, for sales,
(N'OperatingExpense',	N'cost-units,cost-centers',		N'ExpenseByNature'), -- smart, for purchases, depreciation, SIV
(N'OtherProfitLoss',	N'cost-centers',				N'Cash'); -- G/L

IF @DebugAccountTypes = 1
	SELECT * FROM dbo.AccountTypes;