﻿CREATE TYPE [dbo].[CustodyList] AS TABLE (
	[Index]						INT					PRIMARY KEY,
	[Id]						INT					NOT NULL DEFAULT 0,
	[Name]						NVARCHAR (255)		NOT NULL,
	[Name2]						NVARCHAR (255),
	[Name3]						NVARCHAR (255),
	[Code]						NVARCHAR (50),
	[CurrencyId]				NCHAR (3),
	[CenterId]					INT,
	--[ImageId]					NVARCHAR (50),
	[Description]				NVARCHAR (2048),
	[Description2]				NVARCHAR (2048),
	[Description3]				NVARCHAR (2048),
	[LocationJson]				NVARCHAR(MAX),
	[LocationWkb]				VARBINARY(MAX),
	[FromDate]					DATE,
	[ToDate]					DATE,
	[Decimal1]					DECIMAL (19,4),
	[Decimal2]					DECIMAL (19,4),
	[Int1]						INT,
	[Int2]						INT,
	[Lookup1Id]					INT,
	[Lookup2Id]					INT,
	[Lookup3Id]					INT,
	[Lookup4Id]					INT,
	[Text1]						NVARCHAR (50),
	[Text2]						NVARCHAR (50), 
	
	[CustodianId]				INT,
	--[AgentId]					INT,	
	--[TaxIdentificationNumber]	NVARCHAR (18),  -- China has the maximum, 18 characters
	--[JobId]						INT,
	[ExternalReference]			NVARCHAR (34),

	-- Extra Columns not in Custody.cs
	[ImageId]					NVARCHAR (50),

	INDEX IX_AgentList__Code ([Code])
);