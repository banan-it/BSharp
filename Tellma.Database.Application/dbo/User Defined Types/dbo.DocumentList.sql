﻿CREATE TYPE [dbo].[DocumentList] AS TABLE (
	[Index]							INT				PRIMARY KEY,-- IDENTITY (0,1),
	[Id]							INT				NOT NULL DEFAULT 0,
	[SerialNumber]					INT,
	[PostingDate]					DATE,
	[Clearance]						TINYINT			NOT NULL DEFAULT 0,
	[DocumentLookup1Id]				INT, -- e.g., cash machine serial in the case of a sale
	[DocumentLookup2Id]				INT,
	[DocumentLookup3Id]				INT,
	[DocumentText1]					NVARCHAR (255),
	[DocumentText2]					NVARCHAR (255),
	[Memo]							NVARCHAR (255),	
	[MemoIsCommon]					BIT				DEFAULT 1,
	[DebitAgentId]					INT,
	[DebitAgentIsCommon]			BIT				NOT NULL DEFAULT 0,
	[CreditAgentId]					INT,
	[CreditAgentIsCommon]			BIT				NOT NULL DEFAULT 0,
	[NotedAgentId]					INT,
	[NotedAgentIsCommon]			BIT				NOT NULL DEFAULT 0,
	[InvestmentCenterId]			INT,
	[InvestmentCenterIsCommon]		BIT				NOT NULL DEFAULT 1,
	[Time1]							DATETIME2 (2),
	[Time1IsCommon]					BIT				NOT NULL DEFAULT 0,
	[Time2]							DATETIME2 (2),
	[Time2IsCommon]					BIT				NOT NULL DEFAULT 0,
	[Quantity]						DECIMAL (19,4)	NULL,
	[QuantityIsCommon]				BIT				NOT NULL DEFAULT 0,
	[UnitId]						INT,
	[UnitIsCommon]					BIT				NOT NULL DEFAULT 0,
	[CurrencyId]					NCHAR (3), 
	[CurrencyIsCommon]				BIT				NOT NULL DEFAULT 0
);