﻿CREATE TABLE [dbo].[LineDefinitions] (
	[Id]								NVARCHAR (50) PRIMARY KEY,
	[Description]						NVARCHAR (255),
	[Description2]						NVARCHAR (255),
	[Description3]						NVARCHAR (255),
	[TitleSingular]						NVARCHAR (255) NOT NULL,
	[TitleSingular2]					NVARCHAR (255),
	[TitleSingular3]					NVARCHAR (255),
	[TitlePlural]						NVARCHAR (255) NOT NULL,
	[TitlePlural2]						NVARCHAR (255),
	[TitlePlural3]						NVARCHAR (255),
	[AgentRelationDefinitionId]			NVARCHAR (50)	REFERENCES dbo.AgentRelationDefinitions([Id]),
	[ResourceType]						NVARCHAR (255),
	[Script]							NVARCHAR (MAX) -- to store SQL code that populates the line
);