﻿CREATE PROCEDURE [bll].[CustomClassifications_Validate__Delete]
	@Ids [dbo].[IndexedIdList] READONLY,
	@Top INT = 10
AS
SET NOCOUNT ON;
	DECLARE @ValidationErrors [dbo].[ValidationErrorList];

	INSERT INTO @ValidationErrors([Key], [ErrorName], [Argument0], [Argument1])
	SELECT TOP (@Top)
		'[' + CAST(FE.[Index] AS NVARCHAR (255)) + ']',
		N'Error_TheAccountClassification0IsUsedInAccount1', 
		[dbo].[fn_Localize](LC.[Name], LC.[Name2], LC.[Name3]) AS CustomClassificationName,
		[dbo].[fn_Localize](A.[Name], A.[Name2], A.[Name3]) AS AccountName
    FROM [dbo].[CustomClassifications] LC
	JOIN [dbo].[Accounts] A ON A.[CustomClassificationId] = LC.Id
	JOIN @Ids FE ON FE.[Id] = LC.[Id];

	SELECT TOP(@Top) * FROM @ValidationErrors;