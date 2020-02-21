﻿IF @DB = N'101' -- Banan SD, USD, en
BEGIN
	DELETE FROM @Resources; DELETE FROM @ResourceUnits;
	INSERT INTO @Resources ([Index],
		[AccountTypeId],				[Name]) VALUES
	(0,	@EmployeeBenefitsExpense,		N'Basic'),
	(1, @EmployeeBenefitsExpense,		N'Labor (hourly)');

	INSERT INTO @ResourceUnits([Index], [HeaderIndex],
			[UnitId],						[Multiplier]) VALUES
	(0, 0, dbo.fn_UnitName__Id(N'wmo'),		1),
	(0, 1, dbo.fn_UnitName__Id(N'hr'),		1);;

	EXEC [api].[Resources__Save] -- N'employee-benefits'
		@DefinitionId = N'employee-benefits-expenses',
		@Entities = @Resources,
		@ResourceUnits = @ResourceUnits,
		@ValidationErrorsJson = @ValidationErrorsJson OUTPUT;

	IF @ValidationErrorsJson IS NOT NULL 
	BEGIN
		Print 'Inserting employee benefits: ' + @ValidationErrorsJson
		GOTO Err_Label;
	END;

	SELECT @BasicSalary = [Id] FROM dbo.Resources WHERE [Name] = N'Basic';
	SELECT @TransportationAllowance = [Id] FROM dbo.Resources WHERE [Name] = N'Transportation Allowance';
	SELECT @DataPackage = [Id] FROM dbo.Resources WHERE [Name] = N'Data package';
	SELECT @MealAllowance = [Id] FROM dbo.Resources WHERE [Name] = N'Meal Allowance';
	SELECT @HourlyWage = [Id] FROM dbo.Resources WHERE [Name] = N'Labor (hourly)';

	SELECT @DayOvertime = [Id] FROM dbo.Resources WHERE [Name] = N'Day Overtime';
	SELECT @NightOvertime = [Id] FROM dbo.Resources WHERE [Name] = N'Night Overtime';
	SELECT @RestOvertime = [Id] FROM dbo.Resources WHERE [Name] = N'Rest Overtime';
	SELECT @HolidayOvertime = [Id] FROM dbo.Resources WHERE [Name] = N'Holiday Overtime';
END