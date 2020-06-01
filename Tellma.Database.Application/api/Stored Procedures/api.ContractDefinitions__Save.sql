﻿CREATE PROCEDURE [api].[ContractDefinitions__Save]
	@Entities [ContractDefinitionList] READONLY,
	@ReturnIds BIT = 0,
	@ValidationErrorsJson NVARCHAR(MAX) OUTPUT
AS
BEGIN
SET NOCOUNT ON;
	-- Add here Code that is handled by C#
	DECLARE @ValidationErrors ValidationErrorList;
	INSERT INTO @ValidationErrors
	EXEC [bll].[ContractDefinitions_Validate__Save]
		@Entities = @Entities;

	SELECT @ValidationErrorsJson = 
	(
		SELECT *
		FROM @ValidationErrors
		FOR JSON PATH
	);

	IF @ValidationErrorsJson IS NOT NULL
		RETURN;

	EXEC [dal].[ContractDefinitions__Save]
		@Entities = @Entities,
		@ReturnIds = @ReturnIds;
END