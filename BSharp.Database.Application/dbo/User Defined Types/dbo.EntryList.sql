﻿CREATE TYPE [dbo].EntryList AS TABLE (
	[Index]						INT					PRIMARY KEY,-- IDENTITY (0,1),
	[LineIndex]					INT					NOT NULL DEFAULT 0 INDEX IX_EntryList_LineIndex ([LineIndex]),
	[DocumentIndex]				INT					NOT NULL DEFAULT 0,
	[Id]						INT					NOT NULL DEFAULT 0,
	[EntryNumber]				INT					NOT NULL DEFAULT 1,
	[Direction]					SMALLINT,
	[AccountId]					INT,
	[ContractType]				NVARCHAR (50) CHECK ( [ContractType] IN (
										N'OnHand',
--										N'OnDemand', -- for all practical purposes, this is the same as OnHand
										N'InTransit',
										N'Receivable',--/PrepaidExpense
										N'Deposit',
										N'Loan',
										N'AccruedIncome',
										N'Equity',
										N'AccruedExpense',
										N'Payable',--/UnearnedRevenue
										N'Retention',
										N'Borrowing',
										N'Revenue',
										N'Expense'
									)),
	[AgentDefinitionId]			NVARCHAR (50),
	[ResourceClassificationId]	INT,
	[IsCurrent]					BIT,
	[AgentId]					INT,
	[ResourceId]				INT,
	[ResponsibilityCenterId]	INT,
	[AccountIdentifier]			NVARCHAR (10),
	[ResourceIdentifier]		NVARCHAR (10),
	[CurrencyId]				NCHAR (3),

	[EntryClassificationId]		INT,
	--[BatchCode]					NVARCHAR (50),
	[DueDate]					DATE,
	[MonetaryValue]				DECIMAL (19,4)		NOT NULL DEFAULT 0, -- Amount in foreign Currency 
	[Count]						DECIMAL (19,4)		NOT NULL DEFAULT 0, -- CountUnit
	[Mass]						DECIMAL (19,4)		NOT NULL DEFAULT 0, -- MassUnit, like LTZ bar, cement bag, etc
	[Volume]					DECIMAL (19,4)		NOT NULL DEFAULT 0, -- VolumeUnit, possibly for shipping
	[Time]						DECIMAL (19,4)		NOT NULL DEFAULT 0, -- ServiceTimeUnit
	[Value]						DECIMAL (19,4)		NOT NULL DEFAULT 0 ,-- equivalent in functional currency
	[ExternalReference]			NVARCHAR (50),
	[AdditionalReference]		NVARCHAR (50),
	[RelatedAgentId]			INT,
	[RelatedAgentName]			NVARCHAR (50),
	[RelatedAmount]				DECIMAL (19,4), 	-- used in Tax accounts, to store the quantiy of taxable item
	[RelatedDate]				DATE,
	[Time1]						TIME (0),	-- from time
	[Time2]						TIME (0),	-- to time
	[SortKey]					INT
);