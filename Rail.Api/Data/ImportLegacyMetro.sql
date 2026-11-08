-- Run in the target Rail database after applying the EF Core migration.
-- The source Metro database must be on the same SQL Server instance.
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() = N'Metro' OR DB_ID(N'Metro') IS NULL OR OBJECT_ID(N'Metro.Lines', N'U') IS NULL
    THROW 50001, 'Connect to a migrated Rail database on the same instance as the Metro source database.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF EXISTS (SELECT 1 FROM [Metro].[Lines] WITH (TABLOCKX, HOLDLOCK))
        THROW 50002, 'The target already contains network data. This import is for an empty network.', 1;

    -- Check the source relationships before dropping duplicate route, line, and branch data.
    IF EXISTS (
        SELECT 1
        FROM [Metro].[dbo].[Routes]
        WHERE [IsBranch] <> CASE WHEN NULLIF([BranchId], '') IS NULL THEN 0 ELSE 1 END
    ) OR EXISTS (
        SELECT 1
        FROM [Metro].[dbo].[Segments] AS s
        LEFT JOIN [Metro].[dbo].[Routes] AS r ON r.[RouteId] = s.[RouteId]
        WHERE r.[RouteId] IS NULL OR s.[LineId] <> r.[LineId] OR s.[BranchId] <> r.[BranchId]
    ) OR EXISTS (
        SELECT 1
        FROM [Metro].[dbo].[ScheduleGroups] AS g
        LEFT JOIN [Metro].[dbo].[Routes] AS r ON r.[RouteId] = g.[RouteId]
        WHERE r.[RouteId] IS NULL OR g.[LineId] <> r.[LineId]
    ) OR EXISTS (
        SELECT 1
        FROM [Metro].[dbo].[Trips] AS t
        LEFT JOIN [Metro].[dbo].[ScheduleGroups] AS g ON g.[ScheduleGroupId] = t.[ScheduleGroupId]
        WHERE g.[ScheduleGroupId] IS NULL OR t.[RouteId] <> g.[RouteId] OR t.[LineId] <> g.[LineId]
    )
        THROW 50003, 'The Metro source contains inconsistent route, line, or branch data.', 1;

    INSERT INTO [Metro].[Lines] ([Id], [NameFa], [NameEn], [Color])
    SELECT [LineId], [NameFa], [NameEn], [Color] FROM [Metro].[dbo].[Lines];

    INSERT INTO [Metro].[Stations] ([Id], [NameFa], [NameEn], [Latitude], [Longitude], [InfrastructureStatus])
    SELECT [StationId], [NameFa], [NameEn], [Latitude], [Longitude], [InfrastructureStatus]
    FROM [Metro].[dbo].[Stations];

    INSERT INTO [Metro].[StationLines] ([StationId], [LineId], [IsOperational])
    SELECT [StationId], [LineId], [IsOperational] FROM [Metro].[dbo].[StationLines];

    INSERT INTO [Metro].[StationAmenities] ([StationId], [Type], [IsAvailable], [IsVerified])
    SELECT s.[StationId], a.[Type], a.[IsAvailable], s.[AmenitiesVerified]
    FROM [Metro].[dbo].[Stations] AS s
    CROSS APPLY (VALUES
        (1, s.[HasWc]), (2, s.[HasElevator]), (3, s.[HasAtm]), (4, s.[HasCoffeeShop]),
        (5, s.[HasFastFood]), (6, s.[HasGroceryStore]), (7, s.[HasFreeWifi]),
        (8, s.[HasPrayerRoom]), (9, s.[HasParking]), (10, s.[HasPolice])
    ) AS a ([Type], [IsAvailable]);

    INSERT INTO [Metro].[Routes] ([Id], [LineId], [BranchCode])
    SELECT [RouteId], [LineId], NULLIF([BranchId], '') FROM [Metro].[dbo].[Routes];

    INSERT INTO [Metro].[RouteStops] ([RouteId], [Sequence], [StationId], [Status])
    SELECT [RouteId], [StopSequence], [StationId], [StopStatus] FROM [Metro].[dbo].[RouteStops];

    INSERT INTO [Metro].[TrackSegments] ([Id], [RouteId], [FromStationId], [ToStationId], [Status])
    SELECT [SegmentId], [RouteId], [FromStationId], [ToStationId], [SegmentStatus] FROM [Metro].[dbo].[Segments];

    INSERT INTO [Metro].[TransferRules] ([Id], [StationId], [FromLineId], [ToLineId], [FromRouteId], [ToRouteId], [WalkSeconds], [Confidence])
    SELECT [TransferRuleId], [StationId], [FromLineId], [ToLineId],
        NULLIF([FromRouteId], ''), NULLIF([ToRouteId], ''), [WalkSeconds], [Confidence]
    FROM [Metro].[dbo].[TransferRules];

    INSERT INTO [Metro].[ScheduleGroups] ([Id], [RouteId], [ScheduleKey], [IsBranchService], [FromTerminalId], [ToTerminalId])
    SELECT [ScheduleGroupId], [RouteId], [ScheduleKey], [IsBranch], [TerminalAStationId], [TerminalBStationId]
    FROM [Metro].[dbo].[ScheduleGroups];

    INSERT INTO [Metro].[Trips] ([Id], [ScheduleGroupId], [DayTypeCode], [DirectionStationId], [OriginStationId],
        [DestinationStationId], [IsExpress], [DepartureTime], [DepartureDayOffset], [ArrivalTime], [ArrivalDayOffset])
    SELECT [TripId], [ScheduleGroupId], [DayType], [DirectionStationId], [OriginStationId], [DestinationStationId],
        [IsExpress], [DepartureTime], [DepartureDayOffset], [ArrivalTime], [ArrivalDayOffset]
    FROM [Metro].[dbo].[Trips];

    -- Match the old departures view, which used scheduled arrival time for boarding stops.
    WITH StopTimes AS (
        SELECT [TripId], [StopSequence], [StationId], [ArrivalTime], [ServiceDayOffset],
            MAX([StopSequence]) OVER (PARTITION BY [TripId]) AS [LastSequence]
        FROM [Metro].[dbo].[StopTimes]
    )
    INSERT INTO [Metro].[StopTimes] ([TripId], [Sequence], [StationId], [ArrivalTime],
        [ArrivalDayOffset], [DepartureTime], [DepartureDayOffset])
    SELECT s.[TripId], s.[StopSequence], s.[StationId], s.[ArrivalTime], s.[ServiceDayOffset],
        CASE WHEN s.[StationId] <> t.[DestinationStationId] OR s.[StopSequence] < s.[LastSequence] THEN s.[ArrivalTime] END,
        CASE WHEN s.[StationId] <> t.[DestinationStationId] OR s.[StopSequence] < s.[LastSequence] THEN s.[ServiceDayOffset] END
    FROM StopTimes AS s
    LEFT JOIN [Metro].[Trips] AS t ON t.[Id] = s.[TripId];

    INSERT INTO [Metro].[ServiceCalendarDates] ([ServiceDate], [DayTypeCode], [IsOfficialHoliday], [Source], [VerifiedAtUtc])
    SELECT h.[ServiceDate], [Metro].[dbo].[fn_DayTypeForDate](h.[ServiceDate]), h.[IsOfficialHoliday], h.[Source],
        TODATETIMEOFFSET(h.[FetchedAtUtc], '+00:00')
    FROM [Metro].[dbo].[HolidayDates] AS h;

    INSERT INTO [Metro].[HolidayEvents] ([Id], [ServiceDate], [Title])
    SELECT [HolidayEventId], [ServiceDate], [Title] FROM [Metro].[dbo].[HolidayEvents];

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
