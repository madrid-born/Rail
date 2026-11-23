using Microsoft.EntityFrameworkCore.Migrations;

namespace Rail.Api.Migrations;

public partial class NewLines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Fold the fully applied development migrations into the new history entry.
        // A partially upgraded database must finish the old chain before switching.
        var escapedSeed = SeedExpansion.Replace("'", "''");
        migrationBuilder.Sql($"""
            DECLARE @PreviousMigrations TABLE ([Id] nvarchar(150) NOT NULL PRIMARY KEY);
            INSERT INTO @PreviousMigrations VALUES
                (N'20261123122322_LineInfrastructureStatus'),
                (N'20261123124430_MergeExpansionInterchanges'),
                (N'20261123162637_MergeEbnEBabviyehInterchange'),
                (N'20261123162956_MergeShahidSaeidiInterchange'),
                (N'20261123165820_MergeChaharbaghInterchange'),
                (N'20261123165946_MergeOlympicVillageAndMahdaviKani');

            DECLARE @AppliedCount int = (
                SELECT COUNT(*) FROM [dbo].[__EFMigrationsHistory] AS h
                JOIN @PreviousMigrations AS m ON m.[Id] = h.[MigrationId]
            );

            IF @AppliedCount = (SELECT COUNT(*) FROM @PreviousMigrations)
            BEGIN
                IF COL_LENGTH('Metro.Lines', 'InfrastructureStatus') IS NULL
                    OR OBJECT_ID('Metro.CK_Lines_Status', 'C') IS NULL
                    OR (SELECT COUNT(*) FROM [Metro].[Routes]
                        WHERE [Id] IN ('line-8', 'line-9', 'line-10', 'line-11')) <> 4
                    THROW 50006, 'The previous expansion migration history does not match the database.', 1;

                DELETE h FROM [dbo].[__EFMigrationsHistory] AS h
                JOIN @PreviousMigrations AS m ON m.[Id] = h.[MigrationId];
            END
            ELSE
            BEGIN
                IF @AppliedCount <> 0 OR COL_LENGTH('Metro.Lines', 'InfrastructureStatus') IS NOT NULL
                    THROW 50007, 'Complete the previous expansion migrations before applying NewLines.', 1;

                ALTER TABLE [Metro].[Lines] ADD [InfrastructureStatus] varchar(30) NOT NULL DEFAULT ('operational');
                -- Compile the constraint and seed after creating the line status column.
                EXEC(N'ALTER TABLE [Metro].[Lines] ADD CONSTRAINT [CK_Lines_Status]
                    CHECK ([InfrastructureStatus] IN (''operational'', ''under_construction'', ''planned'', ''temporarily_closed'', ''permanently_closed''));');
                EXEC(N'{escapedSeed}');
            END;
            """);
    }

    private const string SeedExpansion = """
        INSERT INTO [Metro].[Lines] ([Id], [NameFa], [NameEn], [Color], [InfrastructureStatus])
        VALUES (8, N'خط ۸', N'Line 8', '#c67a1a', 'under_construction'),
               (9, N'خط ۹', N'Line 9', '#b01f7c', 'under_construction'),
               (10, N'خط ۱۰', N'Line 10', '#07164f', 'under_construction'),
               (11, N'خط ۱۱', N'Line 11', '#96f13a', 'under_construction');

        INSERT INTO [Metro].[Routes] ([Id], [LineId])
        SELECT CONCAT('line-', [Id]), [Id] FROM [Metro].[Lines] WHERE [Id] IN (8, 9, 10, 11);

        DECLARE @Stops TABLE (
            [LineId] tinyint NOT NULL,
            [Sequence] smallint NOT NULL,
            [StationId] varchar(100) NULL,
            [LegacyLineId] tinyint NULL,
            [NameFa] nvarchar(200) NOT NULL,
            [NameEn] nvarchar(200) NOT NULL,
            [Latitude] decimal(10,7) NOT NULL,
            [Longitude] decimal(10,7) NOT NULL,
            PRIMARY KEY ([LineId], [Sequence])
        );

        -- A supplied station ID identifies a shared station. A null ID creates a station for this stop.
        INSERT INTO @Stops VALUES
        (8, 1, NULL, NULL, N'مسعودیه', N'Masoudiyeh', 35.6220000, 51.4960000),
        (8, 2, 'line-9-stop-3', NULL, N'میدان بسیج', N'Meydan-e Basij', 35.6325611, 51.4848044),
        (8, 3, NULL, NULL, N'شهرک شهید بروجردی', N'Shahrak-e Shahid Boroujerdi', 35.6359600, 51.4687600),
        (8, 4, 'besat', 6, N'بعثت', N'Be''sat', 35.6448382, 51.4445887),
        (8, 5, NULL, NULL, N'فدائیان اسلام', N'Fadaiyan-e Eslam', 35.6456643, 51.4341349),
        (8, 6, 'payaneh-jonoub-jonoub-terminal', 1, N'پایانه جنوب', N'Payaneh Jonoub(Jonoub Terminal)', 35.6508946, 51.4162862),
        (8, 7, NULL, NULL, N'میدان بهمن', N'Meydan-e Bahman', 35.6493400, 51.3980400),
        (8, 8, NULL, NULL, N'بوستان ولایت', N'Boostan-e Velayat', 35.6470000, 51.3820000),
        (8, 9, 'shahrak-e-shariaty', 3, N'شهرک شریعتی', N'Shahrak-e Shariaty', 35.6442308, 51.3685150),
        (8, 10, NULL, NULL, N'شهید آیت الله سعیدی', N'Shahid Ayatollah Saeidi', 35.6426100, 51.3527200),
        (8, 11, NULL, NULL, N'بهاران', N'Baharan', 35.6505584, 51.3496391),
        (8, 12, NULL, NULL, N'یافت آباد', N'Yaft Abad', 35.6615484, 51.3450983),
        (8, 13, NULL, NULL, N'میدان بوتان', N'Meydan-e Butan', 35.6680800, 51.3461800),
        (8, 14, NULL, NULL, N'آذری', N'Azari', 35.6706800, 51.3525100),
        (8, 15, NULL, NULL, N'شهید بختیاری', N'Shahid Bakhtiari', 35.6822698, 51.3542814),
        (8, 16, NULL, NULL, N'شهید هاشمی', N'Shahid Hashemi', 35.6910000, 51.3555000),
        (8, 17, NULL, NULL, N'زنجان', N'Zanjan', 35.6967000, 51.3580000),
        (8, 18, 'doctor-habibollah', 4, N'دکتر حبیب‌الله', N'Doctor Habibollah', 35.7003030, 51.3560371),
        (8, 19, 'daneshgah-e-sharif', 2, N'دانشگاه شریف', N'Daneshgah-e Sharif', 35.7057413, 51.3541472),
        (8, 20, NULL, NULL, N'ستارخان', N'Sattarkhan', 35.7214919, 51.3611798),
        (8, 21, 'shahrak-e-azmayesh', 6, N'شهرک آزمایش', N'Shahrak-e Azmayesh', 35.7316960, 51.3705445),
        (8, 22, 'boostan-e-goftegou', 7, N'بوستان گفتگو', N'Boostan-e Goftegou', 35.7319611, 51.3830310),
        (8, 23, NULL, NULL, N'امیر آباد', N'Amir Abad', 35.7330361, 51.3912572),
        (8, 24, NULL, NULL, N'کردستان', N'Kordestan', 35.7338600, 51.3982400),
        (8, 25, NULL, NULL, N'میدان سید جمال الدین اسدآبادی', N'Meydan-e Seyyed Jamaleddin Asadabadi', 35.7371800, 51.4060800),
        (8, 26, NULL, NULL, N'بوستان ساعی', N'Boostan-e Saei', 35.7389778, 51.4107475),
        (8, 27, NULL, NULL, N'میدان آرژانتین', N'Meydan-e Arjantin', 35.7397446, 51.4166522),
        (8, 28, 'mosalla-ye-imam-khomeini', 1, N'مصلی امام خمینی', N'Mosalla-ye Imam Khomeini', 35.7403353, 51.4276616),
        (8, 29, NULL, NULL, N'بوستان بهشت مادران', N'Boostan-e Behesht-e Madaran', 35.7422723, 51.4392574),
        (8, 30, NULL, NULL, N'سید خندان', N'Seyyed Khandan', 35.7416513, 51.4474477),
        (8, 31, 'shahid-sayyad-shirazi', 3, N'شهید صیاد شیرازی', N'Shahid Sayyad Shirazi', 35.7355586, 51.4583795),
        (8, 32, NULL, NULL, N'استاد حسن بنا', N'Ostad Hasan Banna', 35.7401700, 51.4674900),
        (8, 33, NULL, NULL, N'کرمان', N'Kerman', 35.7381800, 51.4799300),
        (8, 34, NULL, NULL, N'میدان رسالت', N'Meydan-e Resalat', 35.7366300, 51.4873100),
        (8, 35, 'sarsabz', 2, N'سرسبز', N'Sarsabz', 35.7357392, 51.4947995),
        (9, 1, 'shohada-ye-dowlat-abad', 6, N'شهدای دولت‌آباد', N'Shohada-ye Dowlat Abad', 35.6188159, 51.4641433),
        (9, 2, NULL, NULL, N'مشیریه', N'Moshiriyeh', 35.6196632, 51.4803262),
        (9, 3, NULL, NULL, N'میدان بسیج', N'Meydan-e Basij', 35.6325611, 51.4848044),
        (9, 4, NULL, NULL, N'شهید بقایی', N'Shahid Baghaei', 35.6483151, 51.4856196),
        (9, 5, NULL, NULL, N'شهید ده‌حقی', N'Shahid Deh Haghi', 35.6563007, 51.4884719),
        (9, 6, 'basij', 7, N'بسیج', N'Basij', 35.6665024, 51.4900285),
        (9, 7, NULL, NULL, N'ائمه اطهار', N'Aemmeh Athar', 35.6798367, 51.4801283),
        (9, 8, 'nabard', 4, N'نبرد', N'Nabard', 35.6916179, 51.4773416),
        (9, 9, NULL, NULL, N'ستاد فرماندهی نیروی هوایی', N'Setad-e Farmandehi-ye Nirou-ye Havaei', 35.7024191, 51.4790879),
        (9, 10, NULL, NULL, N'میدان امامت', N'Meydan-e Emamat', 35.7133823, 51.4899480),
        (9, 11, NULL, NULL, N'شهید ثانی', N'Shahid Sani', 35.7203256, 51.4918197),
        (9, 12, NULL, NULL, N'میدان نبوت', N'Meydan-e Nobovvat', 35.7297754, 51.4937324),
        (9, 13, 'sarsabz', 2, N'سرسبز', N'Sarsabz', 35.7357392, 51.4947995),
        (9, 14, NULL, NULL, N'فرجام', N'Farjam', 35.7435174, 51.4962105),
        (9, 15, NULL, NULL, N'شمس آباد', N'Shams Abad', 35.7517346, 51.4845057),
        (9, 16, NULL, NULL, N'میدان ملت', N'Meydan-e Mellat', 35.7547366, 51.4768864),
        (9, 17, 'shahid-zeynoddin', 3, N'شهید زین‌الدین', N'Shahid Zeynoddin', 35.7606209, 51.4658640),
        (9, 18, NULL, NULL, N'گیلان', N'Gilan', 35.7620038, 51.4599671),
        (9, 19, NULL, NULL, N'پاسداران', N'Pasdaran', 35.7569748, 51.4528570),
        (9, 20, NULL, NULL, N'مهران', N'Mehran', 35.7538585, 51.4460244),
        (9, 21, NULL, NULL, N'بانک مرکزی', N'Bank-e Markazi', 35.7563787, 51.4352830),
        (9, 22, 'shahid-haghani', 1, N'شهید حقانی', N'Shahid Haghani', 35.7567055, 51.4260913),
        (9, 23, NULL, NULL, N'میدان ونک', N'Meydan-e Vanak', 35.7575396, 51.4099521),
        (9, 24, NULL, NULL, N'شیراز', N'Shiraz', 35.7568716, 51.4003650),
        (9, 25, NULL, NULL, N'ملاصدرا', N'Mollasadra', 35.7563245, 51.3886499),
        (9, 26, 'meydan-e-sanat', 7, N'میدان صنعت', N'Meydan-e San''at', 35.7544097, 51.3679800),
        (9, 27, NULL, NULL, N'ایوانک', N'Eyvanak', 35.7549558, 51.3530260),
        (9, 28, NULL, NULL, N'پونک', N'Pounak', 35.7587908, 51.3395897),
        (9, 29, NULL, NULL, N'دانشگاه علم و فرهنگ', N'Daneshgah-e Elm-o Farhang', 35.7508941, 51.3357121),
        (9, 30, NULL, NULL, N'عدل', N'Adl', 35.7516166, 51.3272530),
        (9, 31, NULL, NULL, N'شهید مخبری', N'Shahid Mokhberi', 35.7531214, 51.3159459),
        (9, 32, 'chaharbagh', 4, N'چهارباغ', N'Chaharbagh', 35.7532399, 51.3039648),
        (9, 33, 'shahran', 6, N'شهران', N'Shahran', 35.7539673, 51.2874522),
        (9, 34, NULL, NULL, N'دهکده المپیک', N'Dehkadeh-ye Olympic', 35.7568759, 51.2657384),
        (9, 35, NULL, NULL, N'مهدوی کنی', N'Mahdavi Kani', 35.7559151, 51.2532330),
        (9, 36, NULL, NULL, N'شهرک صدرا', N'Shahrak-e Sadra', 35.7466986, 51.2543959),
        (9, 37, NULL, NULL, N'شهید همدانی', N'Shahid Hamedani', 35.7383300, 51.2514100),
        (9, 38, NULL, NULL, N'کوهک', N'Kouhak', 35.7295662, 51.2472971),
        (9, 39, 'chitgar', 5, N'چیتگر', N'Chitgar', 35.7169564, 51.2438647),
        (10, 1, NULL, NULL, N'فلکه سوم تهران پارس', N'Falakeh-ye Sevom-e Tehranpars', 35.7474100, 51.5283100),
        (10, 2, NULL, NULL, N'بوستان پلیس', N'Boostan-e Police', 35.7625800, 51.5379100),
        (10, 3, NULL, NULL, N'مجید آباد', N'Majid Abad', 35.7679100, 51.5357800),
        (10, 4, NULL, NULL, N'شهید بابایی', N'Shahid Babaei', 35.7774800, 51.5237300),
        (10, 5, NULL, NULL, N'هنگام', N'Hengam', 35.7838100, 51.5088200),
        (10, 6, NULL, NULL, N'لویزان', N'Lavizan', 35.7857200, 51.5021600),
        (10, 7, NULL, NULL, N'ازگل', N'Ozgol', 35.7856801, 51.4915456),
        (10, 8, 'nobonyad', 3, N'نوبنیاد', N'Nobonyad', 35.7903056, 51.4794353),
        (10, 9, NULL, NULL, N'اختیاریه', N'Ekhtiyariyeh', 35.7923800, 51.4652600),
        (10, 10, NULL, NULL, N'چیذر', N'Chizar', 35.7902508, 51.4545692),
        (10, 11, NULL, NULL, N'کاوه', N'Kaveh', 35.7862900, 51.4458000),
        (10, 12, 'shahid-sadr', 1, N'شهید صدر', N'Shahid Sadr', 35.7851105, 51.4360028),
        (10, 13, NULL, NULL, N'الهیه', N'Elahiyeh', 35.7854560, 51.4289908),
        (10, 14, NULL, NULL, N'پارک وی', N'Parkway', 35.7905187, 51.4164031),
        (10, 15, NULL, NULL, N'نمایشگاه بین‌المللی', N'Namayeshgah-e Beynolmelali', 35.7906417, 51.4023061),
        (10, 16, NULL, NULL, N'آتی‌ساز', N'Ati Saz', 35.7867600, 51.3888700),
        (10, 17, NULL, NULL, N'میدان فرهنگ', N'Meydan-e Farhang', 35.7840200, 51.3844300),
        (10, 18, NULL, NULL, N'سعادت‌آباد', N'Saadat Abad', 35.7817717, 51.3752466),
        (10, 19, NULL, NULL, N'شهید پاک نژاد', N'Shahid Paknejad', 35.7789000, 51.3625700),
        (10, 20, 'meydan-e-ketab', 7, N'میدان کتاب', N'Meydan-e Ketab', 35.7772938, 51.3558107),
        (10, 21, NULL, NULL, N'آیت الله هاشمی رفسنجانی', N'Ayatollah Hashemi Rafsanjani', 35.7709260, 51.3460858),
        (10, 22, NULL, NULL, N'انصار المهدی', N'Ansar al Mahdi', 35.7695800, 51.3282300),
        (10, 23, NULL, NULL, N'آبشناسان', N'Abshenasan', 35.7679400, 51.3116600),
        (10, 24, NULL, NULL, N'جنت آباد', N'Jannat Abad', 35.7638808, 51.3039809),
        (10, 25, 'shohada-ye-kan', 6, N'شهدای کن', N'Shohada-ye Kan', 35.7632069, 51.2839263),
        (10, 26, 'line-9-stop-34', NULL, N'دهکده المپیک', N'Dehkadeh-ye Olympic', 35.7568759, 51.2657384),
        (10, 27, 'line-9-stop-35', NULL, N'مهدوی کنی', N'Mahdavi Kani', 35.7559151, 51.2532330),
        (10, 28, NULL, NULL, N'میدان اتریش', N'Meydan-e Otrish', 35.7551192, 51.2423659),
        (10, 29, NULL, NULL, N'کاشان', N'Kashan', 35.7536080, 51.2282603),
        (10, 30, NULL, NULL, N'جوزانی', N'Jowzani', 35.7533812, 51.2214617),
        (10, 31, NULL, NULL, N'دریاچه چیتگر', N'Daryacheh-ye Chitgar', 35.7533812, 51.2066727),
        (10, 32, NULL, NULL, N'ایران مال', N'Iran Mall', 35.7519900, 51.1930200),
        (10, 33, NULL, NULL, N'پژوهش', N'Pazhuhesh', 35.7498216, 51.1734945),
        (10, 34, NULL, NULL, N'اردستانی', N'Ardestani', 35.7513188, 51.1559430),
        (10, 35, 'vardavard', 5, N'وردآورد', N'Vardavard', 35.7438259, 51.1506325),
        (11, 1, NULL, NULL, N'تقی آباد', N'Taghi Abad', 35.5859000, 51.4589000),
        (11, 2, 'ebn-e-babviyeh', 6, N'ابن بابویه', N'Ebn-e Babviyeh', 35.6037000, 51.4463000),
        (11, 3, NULL, NULL, N'حمزه آباد', N'Hamzeh Abad', 35.6078841, 51.4317515),
        (11, 4, 'javanmard-e-ghassab', 1, N'جوانمرد قصاب', N'Javanmard-e Ghassab', 35.6100492, 51.4194666),
        (11, 5, NULL, NULL, N'شهرک وصال', N'Shahrak-e Vesal', 35.6204100, 51.4048400),
        (11, 6, NULL, NULL, N'خانی آباد', N'Khani Abad', 35.6198566, 51.3920374),
        (11, 7, NULL, NULL, N'میدان مرکزی میوه و تره بار تهران', N'Meydan-e Markazi-ye Miveh-o Tareh Bar-e Tehran', 35.6220000, 51.3700000),
        (11, 8, NULL, NULL, N'شکوفه', N'Shokoufeh', 35.6329700, 51.3707400),
        (11, 9, 'abdol-abad', 3, N'عبدل آباد', N'Abdol Abad', 35.6383795, 51.3599365),
        (11, 10, 'line-8-stop-10', NULL, N'شهید آیت الله سعیدی', N'Shahid Ayatollah Saeidi', 35.6426100, 51.3527200),
        (11, 11, NULL, NULL, N'میدان ایثارگران', N'Meydan-e Isargaran', 35.6395197, 51.3384034),
        (11, 12, NULL, NULL, N'بیمارستان حکیم', N'Bimarestan-e Hakim', 35.6342520, 51.3220335),
        (11, 13, NULL, NULL, N'الغدیر', N'Al Ghadir', 35.6509200, 51.3109600),
        (11, 14, NULL, NULL, N'شاد آباد', N'Shad Abad', 35.6610838, 51.3033973),
        (11, 15, NULL, NULL, N'خلیج فارس', N'Khalij-e Fars', 35.6621644, 51.2632427),
        (11, 16, NULL, NULL, N'شهید متوسلیان', N'Shahid Motevaselian', 35.6773023, 51.2664503),
        (11, 17, NULL, NULL, N'شهرک پاسداران', N'Shahrak-e Pasdaran', 35.6954202, 51.2655784),
        (11, 18, NULL, NULL, N'تهرانسر', N'Tehransar', 35.7030100, 51.2562000),
        (11, 19, 'chitgar', 5, N'چیتگر', N'Chitgar', 35.7169564, 51.2438647);

        UPDATE @Stops SET [StationId] = CONCAT('line-', [LineId], '-stop-', [Sequence])
        WHERE [StationId] IS NULL;

        IF EXISTS (
            SELECT 1 FROM @Stops AS s
            WHERE EXISTS (SELECT 1 FROM [Metro].[Lines] WHERE [Id] = s.[LegacyLineId])
                AND NOT EXISTS (
                    SELECT 1 FROM [Metro].[StationLines] AS sl
                    WHERE sl.[StationId] = s.[StationId] AND sl.[LineId] = s.[LegacyLineId]
                )
        )
            THROW 50004, 'An expansion interchange does not match the expected station memberships.', 1;

        -- Existing stations keep their names and service status. Before a legacy import,
        -- insert shared station records so expansion routes can reference stable IDs.
        WITH StationRows AS (
            SELECT s.*, ROW_NUMBER() OVER (PARTITION BY [StationId] ORDER BY [LineId], [Sequence]) AS [Row]
            FROM @Stops AS s
            WHERE NOT EXISTS (SELECT 1 FROM [Metro].[Stations] WHERE [Id] = s.[StationId])
        )
        INSERT INTO [Metro].[Stations] ([Id], [NameFa], [NameEn], [Latitude], [Longitude], [InfrastructureStatus])
        SELECT [StationId], [NameFa], [NameEn], [Latitude], [Longitude], 'under_construction'
        FROM StationRows WHERE [Row] = 1;

        -- Lines 4 and 9 use the location supplied for line 9.
        UPDATE station SET [Latitude] = stop.[Latitude], [Longitude] = stop.[Longitude]
        FROM [Metro].[Stations] AS station
        JOIN @Stops AS stop ON stop.[StationId] = station.[Id]
        WHERE station.[Id] = 'chaharbagh';

        INSERT INTO [Metro].[StationLines] ([StationId], [LineId], [IsOperational])
        SELECT DISTINCT [StationId], [LineId], 0 FROM @Stops;

        INSERT INTO [Metro].[RouteStops] ([RouteId], [Sequence], [StationId], [Status])
        SELECT CONCAT('line-', [LineId]), [Sequence], [StationId], 'under_construction' FROM @Stops;

        INSERT INTO [Metro].[TrackSegments] ([SourceId], [RouteId], [FromStationId], [ToStationId], [Status])
        SELECT CONCAT('line-', a.[LineId], '-segment-', a.[Sequence]), CONCAT('line-', a.[LineId]),
            a.[StationId], b.[StationId], 'under_construction'
        FROM @Stops AS a JOIN @Stops AS b ON b.[LineId] = a.[LineId] AND b.[Sequence] = a.[Sequence] + 1;
        """;

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DECLARE @ExpansionStations TABLE ([Id] varchar(100) NOT NULL PRIMARY KEY);
            INSERT INTO @ExpansionStations
            SELECT DISTINCT [StationId] FROM [Metro].[StationLines] WHERE [LineId] IN (8, 9, 10, 11);

            DELETE FROM [Metro].[TrackSegments] WHERE [RouteId] IN ('line-8', 'line-9', 'line-10', 'line-11');
            DELETE FROM [Metro].[RouteStops] WHERE [RouteId] IN ('line-8', 'line-9', 'line-10', 'line-11');
            DELETE FROM [Metro].[Routes] WHERE [Id] IN ('line-8', 'line-9', 'line-10', 'line-11');
            DELETE FROM [Metro].[StationLines] WHERE [LineId] IN (8, 9, 10, 11);

            DELETE station FROM [Metro].[Stations] AS station
            JOIN @ExpansionStations AS expansion ON expansion.[Id] = station.[Id]
            WHERE station.[InfrastructureStatus] = 'under_construction'
                AND NOT EXISTS (SELECT 1 FROM [Metro].[StationLines] WHERE [StationId] = station.[Id]);

            UPDATE [Metro].[Stations] SET [Latitude] = 35.7506526, [Longitude] = 51.3040122
            WHERE [Id] = 'chaharbagh';

            DELETE FROM [Metro].[Lines] WHERE [Id] IN (8, 9, 10, 11);
            """);

        migrationBuilder.DropCheckConstraint("CK_Lines_Status", "Lines", "Metro");
        migrationBuilder.DropColumn("InfrastructureStatus", "Lines", "Metro");
    }
}
