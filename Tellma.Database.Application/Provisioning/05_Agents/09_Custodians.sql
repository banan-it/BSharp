﻿	DECLARE @Custodians dbo.[AgentList];

IF @DB = N'100' -- ACME, USD, en/ar/zh
	Print N''
ELSE IF @DB = N'101' -- Banan SD, USD, en
	INSERT INTO @Custodians
	([Index],	[Name],				[UserId]) VALUES
	(0,			N'elAmin Attayyib', @amtaam),
	(1,			N'Ahmad Abdussalam', @aasalam),
	(2,			N'Bank of Khartoum', NULL);
ELSE IF @DB = N'102' -- Banan ET, ETB, en
	INSERT INTO @Custodians
	([Index], [Name]) VALUES
	(0,		N'Mohamad Akra'),
	(1,		N'Wondewsen Semaneh'),
	(2,		N'Loay Bayazid'),
	(3,		N'Abu Bakr al-Hadi')
	;
ELSE IF @DB = N'103' -- Lifan Cars, ETB, en/zh
	Print N''
ELSE IF @DB = N'104' -- Walia Steel, ETB, en/am
	INSERT INTO @Custodians
	([Index], [Name]) VALUES
	(0,		N'Cashier');
ELSE IF @DB = N'105' -- Simpex, SAR, en/ar
	Print N''

	EXEC [api].[Agents__Save]
		@DefinitionId = N'cash-custodians',
		@Entities = @Custodians,
		@ValidationErrorsJson = @ValidationErrorsJson OUTPUT;

	IF @ValidationErrorsJson IS NOT NULL 
	BEGIN
		Print 'custodies: Inserting: ' + @ValidationErrorsJson
		GOTO Err_Label;
	END;

	DECLARE @GMSafe INT = (SELECT [Id] FROM dbo.Agents WHERE Name = N'elAmin Attayyib');
	DECLARE @KSASafe INT = (SELECT [Id] FROM dbo.Agents WHERE Name = N'Ahmad Abdussalam');