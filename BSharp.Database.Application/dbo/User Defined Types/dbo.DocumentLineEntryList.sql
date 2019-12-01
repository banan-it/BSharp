﻿CREATE TYPE [dbo].DocumentLineEntryList AS TABLE (
	[Index]						INT					PRIMARY KEY,-- IDENTITY (0,1),
	[DocumentLineIndex]			INT					NOT NULL DEFAULT 0 INDEX IX_DocumentEntryList_DocumentLineIndex ([DocumentLineIndex]),
	[DocumentIndex]				INT					NOT NULL DEFAULT 0,
	[Id]						INT					NOT NULL DEFAULT 0,
	[EntryNumber]				INT					NOT NULL DEFAULT 1,
	[Direction]					SMALLINT,
	[AccountId]					INT,
	[AccountTypeId]				NVARCHAR (50),
	[AgentDefinitionId]			NVARCHAR (50),
	[ResourceClassificationId]	INT,
	[IsCurrent]					BIT,
	[AgentId]					INT,
	[ResourceId]				INT,
	[ResponsibilityCenterId]	INT,
	[AccountDescriptorId]		NVARCHAR (10),
	[ResourceDescriptorId]		NVARCHAR (10),
	[CurrencyId]				NCHAR (3),

	[EntryClassificationId]		INT,
	--[BatchCode]					NVARCHAR (50),
	[DueDate]					DATE,
	[MonetaryValue]				MONEY				NOT NULL DEFAULT 0, -- Amount in foreign Currency 
	[Count]						DECIMAL				NOT NULL DEFAULT 0, -- CountUnit
	[Mass]						DECIMAL				NOT NULL DEFAULT 0, -- MassUnit, like LTZ bar, cement bag, etc
	[Volume]					DECIMAL				NOT NULL DEFAULT 0, -- VolumeUnit, possibly for shipping
	[Time]						DECIMAL				NOT NULL DEFAULT 0, -- ServiceTimeUnit
	[Value]						VTYPE				NOT NULL DEFAULT 0 ,-- equivalent in functional currency
	[ExternalReference]			NVARCHAR (50),
	[AdditionalReference]		NVARCHAR (50),
	[RelatedAgentId]			INT,
	[RelatedAgentName]			NVARCHAR (50),
	[RelatedAmount]				MONEY,		-- used in Tax accounts, to store the quantiy of taxable item
	[Time1]						TIME (0),	-- from time
	[Time2]						TIME (0),	-- to time
	[SortKey]					INT
);