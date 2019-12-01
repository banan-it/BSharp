﻿CREATE PROCEDURE [dal].[ResourceClassifications__DeleteWithDescendants]
	@Ids [IdList] READONLY
AS
	IF NOT EXISTS(SELECT * FROM @Ids) RETURN;

	-- Delete the entites and their children
	WITH EntitiesWithDescendants
	AS (
		SELECT T2.[Id]
		FROM [dbo].[ResourceClassifications] T1
		JOIN [dbo].[ResourceClassifications] T2
		ON T2.[Node].IsDescendantOf(T1.[Node]) = 1
		WHERE T1.[Id] IN (SELECT [Id] FROM @Ids)
	)
	DELETE FROM [dbo].[ResourceClassifications]
	WHERE [Id] IN (SELECT [Id] FROM EntitiesWithDescendants);
