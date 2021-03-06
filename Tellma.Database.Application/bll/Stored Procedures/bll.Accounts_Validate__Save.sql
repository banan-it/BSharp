﻿CREATE PROCEDURE [bll].[Accounts_Validate__Save]
	@Entities [dbo].[AccountList] READONLY,
	@Top INT = 10
AS
	--=-=-=-=-=-=- [C# Validation]
	/* 
	
	 [✓] That Codes are unique within the arriving list

	*/

-- TODO: Add tests for every violation
SET NOCOUNT ON;
	DECLARE @ValidationErrors [dbo].[ValidationErrorList];

    -- Non zero Ids must exist
    INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT TOP (@Top)
		'[' + CAST([Index] AS NVARCHAR (255)) + ']',
		N'Error_TheId0WasNotFound',
		CAST([Id] As NVARCHAR (255)) AS [Id]
    FROM @Entities
    WHERE Id <> 0 AND Id NOT IN (SELECT Id from [dbo].[Accounts])

	-- Code must be unique
    INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].Code',
		N'Error_TheCode0IsUsed',
		FE.Code
	FROM @Entities FE 
	JOIN [dbo].[Accounts] BE ON FE.Code = BE.Code
	WHERE (FE.Id <> BE.Id);

	-- Code must not be duplicated in the uploaded list (Depends on SQL Collation)
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT TOP (@Top)
		'[' + CAST([Index] AS NVARCHAR (255)) + '].Code',
		N'Error_TheCode0IsDuplicated',
		[Code]
	FROM @Entities
	WHERE [Code] IN (
		SELECT [Code]
		FROM @Entities
		WHERE [Code] IS NOT NULL
		GROUP BY [Code]
		HAVING COUNT(*) > 1
	)
	
	-- Account Resource Definition must be compatible with Account Type Resource Definitions
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].ResourceDefinitionId',
		N'Error_TheField0IsIncompatible',
		N'localize:Account_ResourceDefinition'
	FROM @Entities FE
	LEFT JOIN dbo.AccountTypeResourceDefinitions ATRD ON FE.[AccountTypeId] = ATRD.[AccountTypeId] AND FE.[ResourceDefinitionId] = ATRD.[ResourceDefinitionId]
	WHERE FE.[ResourceDefinitionId] IS NOT NULL 
	AND ATRD.[ResourceDefinitionId] IS NULL;
	
	-- Account Custody Definition must be compatible with Account Type Custody Definitions
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CustodyDefinitionId',
		N'Error_TheField0IsIncompatible',
		N'localize:Account_CustodyDefinition'
	FROM @Entities FE
	LEFT JOIN dbo.AccountTypeCustodyDefinitions ATRD ON FE.[AccountTypeId] = ATRD.[AccountTypeId] AND FE.[CustodyDefinitionId] = ATRD.[CustodyDefinitionId]
	WHERE FE.[CustodyDefinitionId] IS NOT NULL
	AND ATRD.[CustodyDefinitionId] IS NULL;

	-- Center type be a business unit for All accounts except MIT, PUC, and Expense By Nature
	-- Similar Logic in bll.Documents_Validate__Save
	DECLARE @BalanceSheetRootNode HIERARCHYID = (SELECT [Node] FROM dbo.AccountTypes WHERE [Concept] = N'StatementOfFinancialPositionAbstract');
	DECLARE @ProfitLossRootNode  HIERARCHYID = (SELECT [Node] FROM dbo.AccountTypes WHERE [Concept] = N'IncomeStatementAbstract');
	DECLARE @OtherComprehensiveIncomeRootNode HIERARCHYID = (SELECT [Node] FROM dbo.AccountTypes WHERE [Concept] = N'OtherComprehensiveIncome');
	

	WITH ConstructionInProgressAccountTypes AS (
		--SELECT ATC.[Id] FROM dbo.[AccountTypes] ATC JOIN dbo.[AccountTypes] ATP ON ATC.[Node].IsDescendantOf(ATP.[Node]) = 1 WHERE ATP.[Concept] = N'ConstructionInProgress'
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'ConstructionInProgressExpendituresControl'
	), -- 
	InvestmentPropertyUnderConstructionOrDevelopmentAccountTypes AS (
		--SELECT ATC.[Id] FROM dbo.[AccountTypes] ATC JOIN dbo.[AccountTypes] ATP ON ATC.[Node].IsDescendantOf(ATP.[Node]) = 1 WHERE ATP.[Concept] = N'InvestmentPropertyUnderConstructionOrDevelopment'
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'InvestmentPropertyUnderConstructionOrDevelopmentExpendituresControl'

	), 
	WorkInProgressAccountTypes AS (
		--SELECT ATC.[Id] FROM dbo.[AccountTypes] ATC JOIN dbo.[AccountTypes] ATP ON ATC.[Node].IsDescendantOf(ATP.[Node]) = 1 WHERE ATP.[Concept] = N'WorkInProgress'
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'WorkInProgressExpendituresControl'
		
	),
	CurrentInventoriesInTransitAccountTypes AS (
		--SELECT ATC.[Id] FROM dbo.[AccountTypes] ATC JOIN dbo.[AccountTypes] ATP ON ATC.[Node].IsDescendantOf(ATP.[Node]) = 1 WHERE ATP.[Concept] = N'CurrentInventoriesInTransit'
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'CurrentInventoriesInTransitExpendituresControl'
	),
	--DirectParentAccountTypes AS (
	--	SELECT [Node] FROM dbo.[AccountTypes] WHERE [Concept] IN (N'Revenue', N'CostOfMerchandiseSold')
	--),
	DirectAccountTypes AS (
		--SELECT ATC.[Id] FROM dbo.[AccountTypes] ATC JOIN DirectParentAccountTypes ATP ON ATC.[Node].IsDescendantOf(ATP.[Node]) = 1
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'CostOfSales'
	),
	ExpendituresAccountTypes AS (
		--SELECT [Id] FROM ConstructionInProgressAccountTypes
		--UNION
		--SELECT [Id] FROM InvestmentPropertyUnderConstructionOrDevelopmentAccountTypes
		--UNION
		--SELECT [Id] FROM WorkInProgressAccountTypes
		--UNION
		--SELECT [Id] FROM CurrentInventoriesInTransitAccountTypes
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'Expenditure'
	),
	--BalanceSheetAccountTypes AS (
	--	SELECT [Id]
	--	FROM dbo.[AccountTypes]
	--	WHERE [Node].IsDescendantOf(@BalanceSheetRootNode) = 1
	--),
	--ProfitLossAccountTypes AS (
	--	SELECT [Id]
	--	FROM dbo.[AccountTypes]
	--	WHERE [Node].IsDescendantOf(@ProfitLossRootNode) = 1
	--),
	--OtherComprehensiveIncomeAccountTypes AS (
	--	SELECT [Id]
	--	FROM dbo.[AccountTypes]
	--	WHERE [Node].IsDescendantOf(@OtherComprehensiveIncomeRootNode) = 1
	--),
	BusinessUnitAccountTypes AS (
		--SELECT [Id] FROM AccountTypes
		--EXCEPT
		--SELECT [Id] FROM ExpendituresAccountTypes
		--EXCEPT
		--SELECT [Id] FROM DirectAccountTypes
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'BusinessUnit'
	),
	OtherPLAccountTypes AS (
		--SELECT [Id] FROM ProfitLossAccountTypes
		--UNION
		--SELECT [Id] FROM OtherComprehensiveIncomeAccountTypes
		--EXCEPT
		--SELECT [Id] FROM ExpendituresAccountTypes
		--EXCEPT
		--SELECT [Id] FROM DirectAccountTypes
		SELECT [Id] FROM dbo.[AccountTypes]
		WHERE [CenterType] = N'OtherPL'
	)
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsAbstract',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		NULL
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE C.[CenterType] = N'Abstract'
	UNION
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsNot1',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		N'localize:Center_CenterType_BusinessUnit'
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE FE.AccountTypeId IN (SELECT [Id] FROM BusinessUnitAccountTypes) AND C.CenterType <> N'BusinessUnit'
	UNION
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsNot1',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		N'localize:Center_CenterType_CostOfSales'
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE FE.AccountTypeId IN (SELECT [Id] FROM DirectAccountTypes) AND C.[CenterType] <> N'CostOfSales'
	UNION
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsNotLeaf',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		NULL
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE FE.AccountTypeId IN (SELECT [Id] FROM ExpendituresAccountTypes) AND C.[IsLeaf] = 0
	UNION
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsNot1',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		N'localize:Center_CenterType_ConstructionInProgressExpendituresControl'
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE FE.AccountTypeId IN (SELECT [Id] FROM ConstructionInProgressAccountTypes)  AND C.[CenterType] <> N'ConstructionInProgressExpendituresControl'
	UNION
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsNot1',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		N'localize:Center_CenterType_InvestmentPropertyUnderConstructionOrDevelopmentExpendituresControl'
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE FE.AccountTypeId IN (SELECT [Id] FROM InvestmentPropertyUnderConstructionOrDevelopmentAccountTypes)  AND C.[CenterType] <> N'InvestmentPropertyUnderConstructionOrDevelopmentExpendituresControl'
	UNION
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsNot1',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		N'localize:Center_CenterType_WorkInProgressExpendituresControl'
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE FE.AccountTypeId IN (SELECT [Id] FROM WorkInProgressAccountTypes)  AND C.[CenterType] <> N'WorkInProgressExpendituresControl'
	UNION
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CenterId',
		N'Error_Center0IsNot1',
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [CenterName],
		N'localize:Center_CenterType_CurrentInventoriesInTransitExpendituresControl'
	FROM @Entities FE
	JOIN dbo.Centers C ON FE.[CenterId] = C.[Id]
	WHERE FE.AccountTypeId IN (SELECT [Id] FROM CurrentInventoriesInTransitAccountTypes)  AND C.[CenterType] <> N'CurrentInventoriesInTransitExpendituresControl'

	-- Account/EntryTypeId must be compatible with AccountType/EntryTypeParentId
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].EntryTypeId',
		N'Error_TheField0IsIncompatible',
		N'localize:Account_EntryType'
	FROM @Entities FE
	JOIN dbo.[AccountTypes] AC ON FE.[AccountTypeId] = AC.[Id]
	JOIN dbo.[EntryTypes] ETP ON AC.[EntryTypeParentId] = ETP.[Id]
	JOIN dbo.[EntryTypes] ETC ON FE.[EntryTypeId] = ETC.[Id]
	WHERE ETC.[Node].IsDescendantOf(ETP.[Node]) = 0;

	-- Account Resource must be compatible with Account Resource definition
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].ResourceId',
		N'Error_TheField0IsIncompatible',
		N'localize:Account_Resource'
	FROM @Entities FE
	JOIN dbo.[Resources] R ON FE.[ResourceId] = R.[Id]
	WHERE (FE.[ResourceDefinitionId] IS NULL OR FE.[ResourceDefinitionId] <> R.DefinitionId);

	-- Account Custody must be compatible with Account Custody Definition
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CustodyId',
		N'Error_TheField0IsIncompatible',
		N'localize:Account_Custody'
	FROM @Entities FE
	JOIN dbo.[Custodies] C ON FE.[CustodyId] = C.[Id]
	WHERE (FE.[CustodyDefinitionId] IS NULL OR FE.[CustodyDefinitionId] <> C.DefinitionId);

	-- Account Type must be Assignable
    INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].AccountTypeId',
		N'Error_TheAccountType0IsNotAssignable',
		dbo.fn_Localize(BE.[Name], BE.[Name2], BE.[Name3]) AS AccountType
	FROM @Entities FE 
	JOIN [dbo].[AccountTypes] BE ON FE.[AccountTypeId] = BE.Id
	WHERE BE.[IsAssignable] = 0;

	-- Account Classification must be a leaf
    INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].AccountClassificationId',
		N'Error_TheAccountClassification0IsNotLeaf',
		FE.[ClassificationId]
	FROM @Entities FE 
	JOIN [dbo].[AccountClassifications] BE ON FE.[ClassificationId] = BE.Id
	WHERE BE.[IsLeaf] = 0;

	-- Account Type must be a descendant of Account Classification / Account Type Parent
    INSERT INTO @ValidationErrors([Key], [ErrorName])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].AccountTypeId',
		N'Error_TheAccountTypeIsIncompatibleWithTheClassification'
	FROM @Entities FE 
	JOIN [dbo].[AccountClassifications] AC ON FE.[ClassificationId] = AC.Id
	JOIN dbo.[AccountTypes] AAT ON FE.AccountTypeId = AAT.[Id]
	JOIN dbo.[AccountTypes] ACATP ON ACATP.Id = AC.AccountTypeParentId
	WHERE AAT.[Node].IsDescendantOf(ACATP.[Node]) = 0

	-- If Resource Id is not null, and Currency Id is not null, then Account and resource must have same currency (also added as FK constraint)
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].ResourceId',
		N'Error_TheResource0hasCurrency1whileAccountHasCurrency2',
		dbo.fn_Localize(R.[Name], R.[Name2], R.[Name3]) AS [Resource],
		dbo.fn_Localize(RC.[Name], RC.[Name2], RC.[Name3]) AS [ResourceCurrency],
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [AccountCurrency]
	FROM @Entities FE
	JOIN [dbo].[Resources] R ON R.[Id] = FE.ResourceId
	JOIN dbo.[Currencies] C ON C.[Id] = FE.[CurrencyId]
	JOIN dbo.[Currencies] RC ON RC.[Id]= R.[CurrencyId]
	WHERE (FE.[CurrencyId] <> R.[CurrencyId])

	-- If Resource Id is not null, and Center Id is not null, then Account and resource must have same Center
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].ResourceId',
		N'Error_TheResource0hasCenter1whileAccountHasCenter2',
		dbo.fn_Localize(R.[Name], R.[Name2], R.[Name3]) AS [Resource],
		dbo.fn_Localize(RC.[Name], RC.[Name2], RC.[Name3]) AS [ResourceCenter],
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [AccountCenter]
	FROM @Entities FE
	JOIN map.AccountTypes() AC ON FE.[AccountTypeId] = AC.[Id]
	JOIN [dbo].[Resources] R ON R.[Id] = FE.ResourceId
	JOIN dbo.[Centers] C ON C.[Id] = FE.[CenterId]
	JOIN dbo.[Centers] RC ON RC.[Id]= R.[CenterId]
	WHERE (FE.[CenterId] <> R.[CenterId] AND AC.[IsBusinessUnit] = 1)
	OR  (FE.[CenterId] <> R.[CostCenterId] AND AC.[IsBusinessUnit] = 0)

	-- If Custody Id is not null, and Currency Id is not null, then Account and Custody must have same currency (also added as FK constraint)
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CustodyId',
		N'Error_TheCustody0hasCurrency1whileAccountHasCurrency2',
		dbo.fn_Localize(R.[Name], R.[Name2], R.[Name3]) AS [Custody],
		dbo.fn_Localize(RC.[Name], RC.[Name2], RC.[Name3]) AS [CustodyCurrency],
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [AccountCurrency]
	FROM @Entities FE
	JOIN [dbo].[Custodies] R ON R.[Id] = FE.CustodyId
	JOIN dbo.[Currencies] C ON C.[Id] = FE.[CurrencyId]
	JOIN dbo.[Currencies] RC ON RC.[Id]= R.[CurrencyId]
	WHERE (FE.[CurrencyId] <> R.[CurrencyId])

	-- If Custody Id is not null, and Center Id is not null, then Account and Custody must have same Center
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + '].CustodyId',
		N'Error_TheCustody0hasCenter1whileAccountHasCenter2',
		dbo.fn_Localize(R.[Name], R.[Name2], R.[Name3]) AS [Custody],
		dbo.fn_Localize(RC.[Name], RC.[Name2], RC.[Name3]) AS [CustodyCenter],
		dbo.fn_Localize(C.[Name], C.[Name2], C.[Name3]) AS [AccountCenter]
	FROM @Entities FE
	JOIN map.AccountTypes() AC ON FE.[AccountTypeId] = AC.[Id]
	JOIN [dbo].[Custodies] R ON R.[Id] = FE.CustodyId
	JOIN dbo.[Centers] C ON C.[Id] = FE.[CenterId]
	JOIN dbo.[Centers] RC ON RC.[Id]= R.[CenterId]
	WHERE (FE.[CenterId] <> R.[CenterId] AND AC.[IsBusinessUnit] = 1)

	-- Trying to change the account type
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheAccount0IsUsedInDocument12',
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS Account,
		[dbo].[fn_Localize](DD.[TitleSingular], DD.[TitleSingular2], DD.[TitleSingular3]) AS DocumentDefinition,
		[bll].[fn_Prefix_CodeWidth_SN__Code](DD.[Prefix], DD.[CodeWidth], D.[SerialNumber]) AS [S/N]
	FROM @Entities FE
	JOIN [dbo].[Accounts] A ON FE.[Id] = A.[Id]
	JOIN [dbo].[Entries] E ON E.AccountId = FE.[Id]
	JOIN [dbo].[Lines] L ON L.[Id] = E.[LineId]
	JOIN [dbo].[Documents] D ON D.[Id] = L.[DocumentId]
	JOIN [dbo].[DocumentDefinitions] DD ON DD.[Id] = D.[DefinitionId]
	WHERE L.[State] >= 0
	AND FE.[AccountTypeId] <> A.[AccountTypeId]

	-- Setting the center is not allowed if the account has been used already in an entry but with different center
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2], [Argument3])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(A.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheAccount0IsUsedInDocument12WithCenter3',
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS [Account],
		[dbo].[fn_Localize](DD.[TitleSingular], DD.[TitleSingular2], DD.[TitleSingular3]) AS [DocumentDefinition],
		[bll].[fn_Prefix_CodeWidth_SN__Code](DD.[Prefix], DD.[CodeWidth], D.[SerialNumber]) AS [S/N],
		[dbo].fn_Localize(RC.[Name], RC.[Name2], RC.[Name3]) AS [Center]
	FROM @Entities A
	JOIN [dbo].[Entries] E ON E.AccountId = A.[Id]
	JOIN [dbo].[Lines] L ON L.[Id] = E.[LineId]
	JOIN [dbo].[Documents] D ON D.[Id] = L.[DocumentId]
	JOIN [dbo].[DocumentDefinitions] DD ON DD.[Id] = D.[DefinitionId]
	JOIN [dbo].[Centers] RC ON RC.Id = E.[CenterId]
	WHERE L.[State] >= 0
	AND A.[CenterId] IS NOT NULL
	AND A.[CenterId] <> E.[CenterId]

	--  Setting the custody is not allowed if the account has been used already in an entry but with different custody
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2], [Argument3])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(A.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheAccount0IsUsedInDocument12WithCustody3',
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS Account,
		[dbo].[fn_Localize](DD.[TitleSingular], DD.[TitleSingular2], DD.[TitleSingular3]) AS DocumentDefinition,
		[bll].[fn_Prefix_CodeWidth_SN__Code](DD.[Prefix], DD.[CodeWidth], D.[SerialNumber]) AS [S/N],
		dbo.fn_Localize(AG.[Name], AG.[Name2], AG.[Name3]) AS [Custody]
	FROM @Entities A
	JOIN [dbo].[Entries] E ON E.[AccountId] = A.[Id]
	JOIN [dbo].[Lines] L ON L.[Id] = E.[LineId]
	JOIN [dbo].[Documents] D ON D.[Id] = L.[DocumentId]
	JOIN [dbo].[DocumentDefinitions] DD ON DD.[Id] = D.[DefinitionId]
	JOIN [dbo].[Custodies] AG ON AG.Id = E.[CustodyId]
	WHERE L.[State] >= 0
	AND A.[CustodyId] IS NOT NULL
	AND A.[CustodyId] <> E.[CustodyId]

	-- Setting the resource is not allowed if the account has been used already in an entry but with different resource
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2], [Argument3])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(A.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheAccount0IsUsedInDocument12WithResource3',
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS Account,
		[dbo].[fn_Localize](DD.[TitleSingular], DD.[TitleSingular2], DD.[TitleSingular3]) AS DocumentDefinition,
		[bll].[fn_Prefix_CodeWidth_SN__Code](DD.[Prefix], DD.[CodeWidth], D.[SerialNumber]) AS [S/N],
		dbo.fn_Localize(R.[Name], R.[Name2], R.[Name3]) AS [Resource]
	FROM @Entities A
	JOIN [dbo].[Entries] E ON E.AccountId = A.[Id]
	JOIN dbo.[Lines] L ON L.[Id] = E.[LineId]
	JOIN dbo.Documents D ON D.[Id] = L.[DocumentId]
	JOIN dbo.DocumentDefinitions DD ON DD.[Id] = D.[DefinitionId]
	JOIN dbo.Resources R ON R.Id = E.[ResourceId]
	WHERE L.[State] >= 0
	AND A.[ResourceId] IS NOT NULL
	AND A.[ResourceId] <> E.[ResourceId]

	-- Setting the currency is not allowed if the account has been used already in an entry but with different currency 
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2], [Argument3])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(A.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheAccount0IsUsedInDocument12WithCurrency3',
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS Account,
		[dbo].[fn_Localize](DD.[TitleSingular], DD.[TitleSingular2], DD.[TitleSingular3]) AS DocumentDefinition,
		[bll].[fn_Prefix_CodeWidth_SN__Code](DD.[Prefix], DD.[CodeWidth], D.[SerialNumber]) AS [S/N],
		dbo.fn_Localize(R.[Name], R.[Name2], R.[Name3]) AS [Currency]
	FROM @Entities A
	JOIN [dbo].[Entries] E ON E.AccountId = A.[Id]
	JOIN dbo.[Lines] L ON L.[Id] = E.[LineId]
	JOIN dbo.Documents D ON D.[Id] = L.[DocumentId]
	JOIN dbo.DocumentDefinitions DD ON DD.[Id] = D.[DefinitionId]
	JOIN dbo.Currencies R ON R.Id = E.[CurrencyId]
	WHERE L.[State] >= 0
	AND A.[CurrencyId] IS NOT NULL
	AND A.[CurrencyId] <> E.[CurrencyId]

	-- Changing the entry type is not allowed if the account has been used already in an entry but with entry type that is not descendant of new onw
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2], [Argument3])
	SELECT DISTINCT TOP (@Top)
		'[' + CAST(A.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheAccount0IsUsedInDocument12HavingIncompatibleEntryType3',
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS Account,
		[dbo].[fn_Localize](DD.[TitleSingular], DD.[TitleSingular2], DD.[TitleSingular3]) AS DocumentDefinition,
		[bll].[fn_Prefix_CodeWidth_SN__Code](DD.[Prefix], DD.[CodeWidth], D.[SerialNumber]) AS [S/N],
		dbo.fn_Localize(EEC.[Name], EEC.[Name2], EEC.[Name3]) AS [EntryType]
	FROM @Entities A
	JOIN dbo.[EntryTypes] AEC ON AEC.[Id] = A.[EntryTypeId]
	JOIN [dbo].[Entries] E ON E.AccountId = A.[Id]
	JOIN dbo.[Lines] L ON L.[Id] = E.[LineId]
	JOIN dbo.Documents D ON D.[Id] = L.[DocumentId]
	JOIN dbo.DocumentDefinitions DD ON DD.[Id] = D.[DefinitionId]
	JOIN dbo.[EntryTypes] EEC ON EEC.[Id] = E.[EntryTypeId]
	WHERE L.[State] >= 0
	AND EEC.[Node].IsDescendantOf(AEC.[Node]) = 0;

	SELECT TOP (@Top) * FROM @ValidationErrors;