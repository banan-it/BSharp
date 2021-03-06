﻿CREATE PROCEDURE [dal].[Definitions__Load]
AS

-- Get the version of it all
SELECT [DefinitionsVersion] FROM [dbo].[Settings];

-- Get the lookup definitions
SELECT * FROM [map].[LookupDefinitions]() WHERE [State] <> N'Hidden';
SELECT * FROM [map].[LookupDefinitionReportDefinitions]() WHERE [LookupDefinitionId] IN (SELECT [Id] FROM [map].[LookupDefinitions]() WHERE [State] <> N'Hidden') ORDER BY [Index];

-- Get the relation definitions
SELECT * FROM [map].[RelationDefinitions]() WHERE [State] <> N'Hidden';
SELECT * FROM [map].[RelationDefinitionReportDefinitions]() WHERE [RelationDefinitionId] IN (SELECT [Id] FROM [map].[RelationDefinitions]() WHERE [State] <> N'Hidden') ORDER BY [Index];

-- Get the custody definitions
SELECT * FROM [map].[CustodyDefinitions]() WHERE [State] <> N'Hidden';
SELECT * FROM [map].[CustodyDefinitionReportDefinitions]() WHERE [CustodyDefinitionId] IN (SELECT [Id] FROM [map].[CustodyDefinitions]() WHERE [State] <> N'Hidden') ORDER BY [Index];

-- Get the resource definitions
SELECT * FROM [map].[ResourceDefinitions]() WHERE [State] <> N'Hidden';
SELECT * FROM [map].[ResourceDefinitionReportDefinitions]() WHERE [ResourceDefinitionId] IN (SELECT [Id] FROM [map].[ResourceDefinitions]() WHERE [State] <> N'Hidden') ORDER BY [Index];

-- Get the report definitions
SELECT * FROM [map].[ReportDefinitions]()
SELECT * FROM [map].[ReportDefinitionParameters]() ORDER BY [Index];
SELECT * FROM [map].[ReportDefinitionSelects]() ORDER BY [Index];
SELECT * FROM [map].[ReportDefinitionRows]() ORDER BY [Index];
SELECT * FROM [map].[ReportDefinitionColumns]() ORDER BY [Index];
SELECT * FROM [map].[ReportDefinitionDimensionAttributes]() ORDER BY [Index];
SELECT * FROM [map].[ReportDefinitionMeasures]() ORDER BY [Index];

-- Get the dashboard definitions
SELECT * FROM [map].[DashboardDefinitions]() WHERE [ShowInMainMenu] = 1;
SELECT * FROM [map].[DashboardDefinitionWidgets]() WHERE [DashboardDefinitionId] IN (SELECT [Id] FROM [map].[DashboardDefinitions]() WHERE [ShowInMainMenu] = 1)

-- Get the document definitions
DECLARE @DocDefIds [dbo].[IdList];
INSERT INTO @DocDefIds ([Id]) SELECT [Id] FROM [map].[DocumentDefinitions]() WHERE [State] <> N'Hidden' OR [Code] = N'ManualJournalVoucher'

SELECT * FROM [map].[DocumentDefinitions]() WHERE [Id] IN (SELECT [Id] FROM @DocDefIds);
SELECT * FROM [dbo].[DocumentDefinitionLineDefinitions] WHERE [DocumentDefinitionId] IN (SELECT [Id] FROM @DocDefIds) ORDER BY [Index];

-- Load relevant information from Account Types
SELECT T.[Id], T.[EntryTypeParentId] FROM [map].[AccountTypes]() T 
WHERE T.[Id] IN (SELECT [ParentAccountTypeId] FROM [map].[LineDefinitionEntries]())

-- Get the line definitions
SELECT * FROM [map].[LineDefinitions]();

SELECT * FROM [map].[LineDefinitionEntries]() ORDER BY [Index];
SELECT * FROM [dbo].[LineDefinitionColumns] ORDER BY [Index];
SELECT * FROM [dbo].[LineDefinitionStateReasons] WHERE [IsActive] = 1;
SELECT * FROM [dbo].[LineDefinitionGenerateParameters] ORDER BY [Index];
	
-- Get the Custodian definitions of the line definition entries
SELECT DISTINCT LDE.[Id] AS LineDefinitionEntryId, ATC.[CustodianDefinitionId]
FROM dbo.LineDefinitionEntries LDE
JOIN dbo.AccountTypes ATP ON LDE.[ParentAccountTypeId] = ATP.[Id]
JOIN dbo.AccountTypes ATC ON (ATC.[Node].IsDescendantOf(ATP.[Node]) = 1)
WHERE ATC.[CustodianDefinitionId] IS NOT NULL

-- Get the Custody definitions of the line definition entries
SELECT [LineDefinitionEntryId], [CustodyDefinitionId] FROM [dbo].[LineDefinitionEntryCustodyDefinitions]
UNION
SELECT DISTINCT LDE.[Id] AS LineDefinitionEntryId, [CustodyDefinitionId]
FROM dbo.LineDefinitionEntries LDE
JOIN dbo.AccountTypes ATP ON LDE.[ParentAccountTypeId] = ATP.[Id]
JOIN dbo.AccountTypes ATC ON (ATC.[Node].IsDescendantOf(ATP.[Node]) = 1)
JOIN dbo.[AccountTypeCustodyDefinitions] ATCD ON ATC.[Id] = ATCD.[AccountTypeId]
WHERE LDE.[Id] NOT IN (SELECT LineDefinitionEntryId FROM [LineDefinitionEntryCustodyDefinitions])

-- Get the Participant definitions of the line definition entries
SELECT DISTINCT LDE.[Id] AS LineDefinitionEntryId, ATC.[ParticipantDefinitionId]
FROM dbo.LineDefinitionEntries LDE
JOIN dbo.AccountTypes ATP ON LDE.[ParentAccountTypeId] = ATP.[Id]
JOIN dbo.AccountTypes ATC ON (ATC.[Node].IsDescendantOf(ATP.[Node]) = 1)
WHERE ATC.[ParticipantDefinitionId] IS NOT NULL

-- Get the resource definitions of the line definition entries
SELECT [LineDefinitionEntryId], [ResourceDefinitionId] FROM [dbo].[LineDefinitionEntryResourceDefinitions]
UNION
SELECT DISTINCT LDE.[Id] AS LineDefinitionEntryId, [ResourceDefinitionId]
FROM dbo.LineDefinitionEntries LDE
JOIN dbo.AccountTypes ATP ON LDE.[ParentAccountTypeId] = ATP.[Id]
JOIN dbo.AccountTypes ATC ON (ATC.[Node].IsDescendantOf(ATP.[Node]) = 1)
JOIN dbo.AccountTypeResourceDefinitions ATCD ON ATC.[Id] = ATCD.[AccountTypeId]
WHERE LDE.[Id] NOT IN (SELECT LineDefinitionEntryId FROM [LineDefinitionEntryResourceDefinitions])

-- Get deployed markup templates
SELECT 
	[Id],
	[Name],
	[Name2],
	[Name3],
	[SupportsPrimaryLanguage],
	[SupportsSecondaryLanguage],
	[SupportsTernaryLanguage],
	[Usage],
	[Collection],
	[DefinitionId]
FROM [dbo].[MarkupTemplates] WHERE [IsDeployed] = 1; -- TODO: Only the ones for printing and reports