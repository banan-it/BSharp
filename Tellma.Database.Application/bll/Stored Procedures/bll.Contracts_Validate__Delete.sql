﻿CREATE PROCEDURE [bll].[Contracts_Validate__Delete]	
	@DefinitionId INT,
	@Ids [dbo].[IndexedIdList] READONLY,
	@Top INT = 10
AS
SET NOCOUNT ON;
	DECLARE @ValidationErrors [dbo].[ValidationErrorList];

	-- Cannot delete a Contract that is used in some documents
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1], [Argument2])
    SELECT DISTINCT TOP(@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheContract0IsUsedIn12', 
		[dbo].[fn_Localize](AG.[Name], AG.[Name2], AG.[Name3]) AS ContractName,
		[dbo].[fn_Localize](DD.[TitleSingular], DD.[TitleSingular2], DD.[TitleSingular3]) AS DocumentDefinition,
		[bll].[fn_Prefix_CodeWidth_SN__Code](DD.[Prefix], DD.[CodeWidth], D.[SerialNumber]) AS [S/N]
    FROM [dbo].[Contracts] AG
	JOIN [dbo].[Entries] E ON E.[ContractId] = AG.[Id]
	JOIN [dbo].[Lines] L ON L.[Id] =  E.[LineId]
	JOIN [dbo].[Documents] D ON D.[Id] = L.[DocumentId]
	JOIN [dbo].[DocumentDefinitions] DD ON DD.[Id] = D.[DefinitionId]
	JOIN @Ids FE ON FE.[Id] = AG.[Id]
	
	-- Cannot delete a Contract that is used in some accounts
	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1])
    SELECT DISTINCT TOP(@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheContract0IsUsedInAccount1', 
		[dbo].[fn_Localize](AG.[Name], AG.[Name2], AG.[Name3]) AS ContractName,
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS AccountName
    FROM [dbo].[Contracts] AG
	JOIN [dbo].[Accounts] A ON A.[ContractId] = AG.[Id]
	JOIN @Ids FE ON FE.[Id] = AG.[Id]

	SELECT TOP(@Top) * FROM @ValidationErrors;