﻿CREATE PROCEDURE [api].[Accounts__Save]
	@Entities [dbo].[AccountList] READONLY,
	@ReturnIds BIT = 0,
	@ValidationErrorsJson NVARCHAR(MAX) OUTPUT
AS
BEGIN
SET NOCOUNT ON;
	DECLARE @ProcessedAccounts [dbo].[AccountList];

	INSERT INTO @ProcessedAccounts
	EXEC bll.[Accounts__Preprocess]
		@Entities = @Entities;

	-- Add here Code that is handled by C#
	DECLARE @ValidationErrors ValidationErrorList;
	INSERT INTO @ValidationErrors	
	EXEC [bll].[Accounts_Validate__Save]
		@Entities = @ProcessedAccounts;

	SELECT @ValidationErrorsJson = 
	(
		SELECT *
		FROM @ValidationErrors
		FOR JSON PATH
	);
	
	IF @ValidationErrorsJson IS NOT NULL
		RETURN;

	EXEC [dal].[Accounts__Save]
		@Entities = @ProcessedAccounts,
		@ReturnIds = @ReturnIds;
END;