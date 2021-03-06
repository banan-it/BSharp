﻿using Tellma.Data.Queries;
using Tellma.Entities;
using Tellma.Services.ClientInfo;
using Tellma.Services.Identity;
using Tellma.Services.Sharding;
using Microsoft.Extensions.Localization;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using System.Threading;
using Tellma.Services.MultiTenancy;
using Tellma.Entities.Descriptors;
using Tellma.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tellma.Data
{
    /// <summary>
    /// A very thin and lightweight layer around the application database (every tenant
    /// has a dedicated application database). It's the entry point of all functionality that requires 
    /// SQL: Tables, Views, Stored Procedures etc.., it contains no logic of its own.
    /// By default it connects to the tenant Id supplied in the headers 
    /// </summary>
    public class ApplicationRepository : IDisposable, IRepository
    {
        private SqlConnection _conn;
        private UserInfo _userInfo;

        private int? _tenantId;
        private TenantInfo _tenantInfo;
        private Transaction _transactionOverride;

        #region Dependencies

        private readonly IServiceProvider _serviceProvider;

        private IShardResolver _shardResolver;
        private IShardResolver ShardResolver => _shardResolver ??= _serviceProvider.GetRequiredService<IShardResolver>();

        private IExternalUserAccessor _externalUserAccessor;
        private IExternalUserAccessor ExternalUserAccessor => _externalUserAccessor ??= _serviceProvider.GetRequiredService<IExternalUserAccessor>();

        private IClientInfoAccessor _clientInfoAccessor;
        private IClientInfoAccessor ClientInfoAccessor => _clientInfoAccessor ??= _serviceProvider.GetRequiredService<IClientInfoAccessor>();

        private IStringLocalizer _localizer;
        private IStringLocalizer Localizer => _localizer ??= _serviceProvider.GetRequiredService<IStringLocalizer<Strings>>();

        private ITenantIdAccessor _tenantIdAccessor;
        private ITenantIdAccessor TenantIdAccessor => _tenantIdAccessor ??= _serviceProvider.GetRequiredService<ITenantIdAccessor>();

        private IInstrumentationService _instrumentation;
        private IInstrumentationService Instrumentation => _instrumentation ??= _serviceProvider.GetRequiredService<IInstrumentationService>();

        private ILogger _logger;
        private ILogger Logger => _logger ??= _serviceProvider.GetRequiredService<ILogger<ApplicationRepository>>();

        #endregion

        #region Lifecycle

        public ApplicationRepository(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public void Dispose()
        {
            if (_conn != null)
            {
                _conn.Close();
                _conn.Dispose();
            }
        }

        #endregion

        #region Connection Management

        /// <summary>
        /// By default the <see cref="ApplicationRepository"/> connects to the database corresponding to 
        /// the current tenantId which is retrieved from an injected <see cref="IShardResolver"/>,
        /// this method makes it possible to conncet to a custom connection string instead, 
        /// this is useful when connecting to multiple tenants at the same time to do aggregate reporting for example
        /// </summary>
        public async Task InitConnectionAsync(int databaseId, bool setLastActive, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(InitConnectionAsync));
            if (_conn != null)
            {
                throw new InvalidOperationException("The connection is already initialized");
            }

            string connectionString = await ShardResolver.GetConnectionString(databaseId, cancellation);
            _conn = new SqlConnection(connectionString);

            // Open the SQL connection
            await _conn.OpenAsync();

            // Always call OnConnect SP as soon as you create the connection
            var externalUserId = ExternalUserAccessor.GetUserId();
            var externalEmail = ExternalUserAccessor.GetUserEmail();
            var culture = CultureInfo.CurrentUICulture.Name;
            var neutralCulture = CultureInfo.CurrentUICulture.IsNeutralCulture ? CultureInfo.CurrentUICulture.Name : CultureInfo.CurrentUICulture.Parent.Name;

            (_userInfo, _tenantInfo) = await OnConnect(externalUserId, externalEmail, culture, neutralCulture, setLastActive, cancellation);
            _tenantId = databaseId;
        }

        /// <summary>
        /// Initializes the connection if it is not already initialized
        /// </summary>
        /// <returns>The connection string that was initialized</returns>
        private async Task<SqlConnection> GetConnectionAsync(CancellationToken cancellation = default)
        {
            if (_conn == null)
            {
                int databaseId = TenantIdAccessor.GetTenantId();
                await InitConnectionAsync(databaseId, setLastActive: true, cancellation);
            }

            // Since we opened the connection once, we need to explicitly enlist it in any ambient transaction
            // every time it is requested, otherwise commands will be executed outside the boundaries of the transaction
            _conn.EnlistInTransaction(transactionOverride: _transactionOverride);
            return _conn;
        }

        /// <summary>
        /// Returns the name of the initial catalog from the active connection's connection string
        /// </summary>
        private string InitialCatalog()
        {
            if (_conn == null || _conn.ConnectionString == null)
            {
                return null;
            }

            return new SqlConnectionStringBuilder(_conn.ConnectionString).InitialCatalog;
        }

        /// <summary>
        /// Loads a <see cref="UserInfo"/> object from the database, this occurs once per <see cref="ApplicationRepository"/> 
        /// instance, subsequent calls are satisfied from a scoped cache
        /// </summary>
        public async Task<UserInfo> GetUserInfoAsync(CancellationToken cancellation)
        {
            await GetConnectionAsync(cancellation); // This automatically initializes the user info
            return _userInfo;
        }

        /// <summary>
        /// Loads a <see cref="UserInfo"/> object from the cache, or throws an exception if it's not available
        /// </summary>
        public UserInfo GetUserInfo()
        {
            return _userInfo ?? throw new InvalidOperationException("UserInfo are not initialized, call GetConnectionAsync() first or just use GetUserInfoAsync()");
        }

        /// <summary>
        /// Loads a <see cref="TenantInfo"/> object from the database, this occurs once per <see cref="ApplicationRepository"/> 
        /// instance, subsequent calls are satisfied from a scoped cache
        /// </summary>
        public async Task<TenantInfo> GetTenantInfoAsync(CancellationToken cancellation)
        {
            await GetConnectionAsync(cancellation); // This automatically initializes the tenant info
            return _tenantInfo;
        }

        public int GetTenantId()
        {
            return _tenantId ?? throw new InvalidOperationException("TenantId are not initialized, call GetConnectionAsync() firsti");
        }

        /// <summary>
        /// Loads a <see cref="TenantInfo"/> object from the cache, or throws an exception if it's not available
        /// </summary>
        public TenantInfo GetTenantInfo()
        {
            return _tenantInfo ?? throw new InvalidOperationException("TenantInfo are not initialized, call GetConnectionAsync() first or just use GetTenantInfoAsync()");
        }

        /// <summary>
        /// Enlists the repository's connection in the provided transaction such that all subsequent commands particupate in it, regardless of the ambient transaction
        /// </summary>
        /// <param name="transaction">The transaction to enlist the connection in</param>
        public void EnlistTransaction(Transaction transaction)
        {
            _transactionOverride = transaction;
        }

        #endregion

        #region Queries

        public Query<FinancialSettings> FinancialSettings => Query<FinancialSettings>();
        public Query<GeneralSettings> GeneralSettings => Query<GeneralSettings>();
        public Query<User> Users => Query<User>();
        public Query<Relation> Relations => Query<Relation>();
        public Query<Custody> Custodies => Query<Custody>();
        public Query<Resource> Resources => Query<Resource>();
        public Query<Currency> Currencies => Query<Currency>();
        public Query<ExchangeRate> ExchangeRates => Query<ExchangeRate>();

        /// <summary>
        /// Creates and returns a new <see cref="Queries.Query{T}"/>
        /// </summary>
        /// <typeparam name="T">The type of the <see cref="Queries.Query{T}"/></typeparam>
        public Query<T> Query<T>() where T : Entity
        {
            return new Query<T>(Factory);
        }

        /// <summary>
        /// Creates and returns a new <see cref="Queries.AggregateQuery{T}"/>
        /// </summary>
        /// <typeparam name="T">The root type of the <see cref="Queries.AggregateQuery{T}"/></typeparam>
        public AggregateQuery<T> AggregateQuery<T>() where T : Entity
        {
            return new AggregateQuery<T>(Factory);
        }

        /// <summary>
        /// Creates and returns a new <see cref="Queries.FactQuery{T}"/>
        /// </summary>
        /// <typeparam name="T">The root type of the <see cref="Queries.FactQuery{T}"/></typeparam>
        public FactQuery<T> FactQuery<T>() where T : Entity
        {
            return new FactQuery<T>(Factory);
        }

        private async Task<QueryArguments> Factory(CancellationToken cancellation)
        {
            var conn = await GetConnectionAsync(cancellation);
            var tenantInfo = await GetTenantInfoAsync(cancellation);
            var userInfo = await GetUserInfoAsync(cancellation);
            var userId = userInfo.UserId ?? 0;
            var userToday = ClientInfoAccessor.GetInfo().Today;

            return new QueryArguments(conn, Sources, userId, userToday, Localizer, Instrumentation, Logger);
        }

        /// <summary>
        /// Returns a function that maps every <see cref="Entity"/> type in <see cref="ApplicationRepository"/> 
        /// to the default SQL query that retrieves it + some optional parameters
        /// </summary>
        public static string Sources(Type t)
        {
            var result = t.Name switch
            {
                nameof(Account) => "[map].[Accounts]()",
                nameof(AccountClassification) => "[map].[AccountClassifications]()",
                nameof(AccountType) => "[map].[AccountTypes]()",
                nameof(AccountTypeCustodyDefinition) => "[map].[AccountTypeCustodyDefinitions]()",
                nameof(AccountTypeResourceDefinition) => "[map].[AccountTypeResourceDefinitions]()",
                nameof(Agent) => "[map].[Agents]()",
                nameof(Attachment) => "[map].[Attachments]()",
                nameof(Center) => "[map].[Centers]()",
                nameof(Currency) => "[map].[Currencies]()",
                nameof(Custody) => "[map].[Custodies]()",
                nameof(CustodyDefinition) => "[map].[CustodyDefinitions]()",
                nameof(CustodyDefinitionReportDefinition) => "[map].[CustodyDefinitionReportDefinitions]()",
                nameof(DetailsEntry) => "[map].[DetailsEntries]()",
                nameof(Document) => "[map].[Documents]()",
                nameof(DocumentAssignment) => "[map].[DocumentAssignmentsHistory]()",
                nameof(DocumentDefinition) => "[map].[DocumentDefinitions]()",
                nameof(DocumentDefinitionLineDefinition) => "[map].[DocumentDefinitionLineDefinitions]()",
                nameof(DocumentLineDefinitionEntry) => "[map].[DocumentLineDefinitionEntries]()",
                nameof(DocumentStateChange) => "[map].[DocumentStatesHistory]()",
                nameof(EmailForQuery) => "[map].[Emails]()",
                nameof(Entry) => "[map].[Entries]()",
                nameof(EntryType) => "[map].[EntryTypes]()",
                nameof(ExchangeRate) => "[map].[ExchangeRates]()",
                nameof(Entities.GeneralSettings) => "[map].[GeneralSettings]()",
                nameof(Entities.FinancialSettings) => "[map].[FinancialSettings]()",
                nameof(IfrsConcept) => "[map].[IfrsConcepts]()",
                nameof(InboxRecord) => "[map].[Inbox]()",
                nameof(Line) => "[map].[Lines]()",
                nameof(LineDefinition) => "[map].[LineDefinitions]()",
                nameof(LineDefinitionColumn) => "[map].[LineDefinitionColumns]()",
                nameof(LineDefinitionEntry) => "[map].[LineDefinitionEntries]()",
                nameof(LineDefinitionEntryCustodyDefinition) => "[map].[LineDefinitionEntryCustodyDefinitions]()",
                nameof(LineDefinitionEntryResourceDefinition) => "[map].[LineDefinitionEntryResourceDefinitions]()",
                nameof(LineDefinitionGenerateParameter) => "[map].[LineDefinitionGenerateParameters]()",
                nameof(LineDefinitionStateReason) => "[map].[LineDefinitionStateReasons]()",
                nameof(LineForQuery) => "[map].[Lines]()",
                nameof(Lookup) => "[map].[Lookups]()",
                nameof(LookupDefinition) => "[map].[LookupDefinitions]()",
                nameof(LookupDefinitionReportDefinition) => "[map].[LookupDefinitionReportDefinitions]()",
                nameof(MarkupTemplate) => "[map].[MarkupTemplates]()",
                nameof(OutboxRecord) => "[map].[Outbox]()",
                nameof(Permission) => "[dbo].[Permissions]",
                nameof(Relation) => "[map].[Relations]()",
                nameof(RelationDefinition) => "[map].[RelationDefinitions]()",
                nameof(RelationDefinitionReportDefinition) => "[map].[RelationDefinitionReportDefinitions]()",
                nameof(RelationUser) => "[map].[RelationUsers]()",
                nameof(RelationAttachment) => "[map].[RelationAttachments]()",
                nameof(ReportDefinitionColumn) => "[map].[ReportDefinitionColumns]()",
                nameof(ReportDefinition) => "[map].[ReportDefinitions]()",
                nameof(ReportDefinitionMeasure) => "[map].[ReportDefinitionMeasures]()",
                nameof(ReportDefinitionParameter) => "[map].[ReportDefinitionParameters]()",
                nameof(ReportDefinitionRow) => "[map].[ReportDefinitionRows]()",
                nameof(ReportDefinitionDimensionAttribute) => "[map].[ReportDefinitionDimensionAttributes]()",
                nameof(ReportDefinitionSelect) => "[map].[ReportDefinitionSelects]()",
                nameof(ReportDefinitionRole) => "[map].[ReportDefinitionRoles]()",
                nameof(RequiredSignature) => "[map].[DocumentsRequiredSignatures](@DocumentIds)",
                nameof(Resource) => "[map].[Resources]()",
                nameof(ResourceDefinition) => "[map].[ResourceDefinitions]()",
                nameof(ResourceDefinitionReportDefinition) => "[map].[ResourceDefinitionReportDefinitions]()",
                nameof(ResourceUnit) => "[map].[ResourceUnits]()",
                nameof(Role) => "[dbo].[Roles]",
                nameof(RoleMembership) => "[dbo].[RoleMemberships]",
                nameof(SmsMessageForQuery) => "[map].[SmsMessages]()",
                nameof(Unit) => "[map].[Units]()",
                nameof(User) => "[map].[Users]()",
                nameof(VoucherBooklet) => "[dbo].[VoucherBooklets]",
                nameof(Workflow) => "[map].[Workflows]()",
                nameof(WorkflowSignature) => "[map].[WorkflowSignatures]()",
                nameof(DashboardDefinition) => "[map].[DashboardDefinitions]()",
                nameof(DashboardDefinitionWidget) => "[map].[DashboardDefinitionWidgets]()",
                nameof(DashboardDefinitionRole) => "[map].[DashboardDefinitionRoles]()",
                _ => throw new InvalidOperationException($"The requested type '{t.Name}' is not supported in {nameof(ApplicationRepository)} queries"),
            };
            return result;
        }

        #endregion

        #region Stored Procedures

        private async Task<(UserInfo, TenantInfo)> OnConnect(string externalUserId, string userEmail, string culture, string neutralCulture, bool setLastActive, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(OnConnect));

            UserInfo userInfo = null;
            TenantInfo tenantInfo = null;

            using (SqlCommand cmd = _conn.CreateCommand()) // Use the private field _conn to avoid infinite recursion
            {
                // Parameters
                cmd.Parameters.Add("@ExternalUserId", externalUserId);
                cmd.Parameters.Add("@UserEmail", userEmail);
                cmd.Parameters.Add("@Culture", culture);
                cmd.Parameters.Add("@NeutralCulture", neutralCulture);
                cmd.Parameters.Add("@SetLastActive", setLastActive);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(OnConnect)}]";

                // Execute and Load
                using var reader = await cmd.ExecuteReaderAsync(cancellation);
                if (await reader.ReadAsync(cancellation))
                {
                    int i = 0;

                    // The user Info
                    userInfo = new UserInfo
                    {
                        UserId = reader.Int32(i++),
                        Name = reader.String(i++),
                        Name2 = reader.String(i++),
                        Name3 = reader.String(i++),
                        ExternalId = reader.String(i++),
                        Email = reader.String(i++),
                        PermissionsVersion = reader.Guid(i++)?.ToString(),
                        UserSettingsVersion = reader.Guid(i++)?.ToString(),
                    };

                    // The tenant Info
                    tenantInfo = new TenantInfo
                    {
                        ShortCompanyName = reader.String(i++),
                        ShortCompanyName2 = reader.String(i++),
                        ShortCompanyName3 = reader.String(i++),
                        DefinitionsVersion = reader.Guid(i++)?.ToString(),
                        SettingsVersion = reader.Guid(i++)?.ToString(),
                        PrimaryLanguageId = reader.String(i++),
                        PrimaryLanguageSymbol = reader.String(i++),
                        SecondaryLanguageId = reader.String(i++),
                        SecondaryLanguageSymbol = reader.String(i++),
                        TernaryLanguageId = reader.String(i++),
                        TernaryLanguageSymbol = reader.String(i++),
                        DateFormat = reader.String(i++),
                        TimeFormat = reader.String(i++),
                        TaxIdentificationNumber = reader.String(i++),
                    };
                }
                else
                {
                    throw new InvalidOperationException($"[dal].[OnConnect] did not return any data, InitialCatalog: {InitialCatalog()}, ExternalUserId: {externalUserId}, UserEmail: {userEmail}");
                }
            }

            return (userInfo, tenantInfo);
        }

        public async Task<List<InboxNotificationInfo>> InboxCounts__Load(IEnumerable<int> userIds, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(InboxCounts__Load));

            var result = new List<InboxNotificationInfo>(userIds.Count());
            if (userIds == null || !userIds.Any())
            {
                return result;
            }

            var conn = await GetConnectionAsync(cancellation);
            using (var cmd = conn.CreateCommand())
            {
                DataTable idsTable = RepositoryUtilities.DataTable(userIds.Select(id => new IdListItem { Id = id }));
                var idsTvp = new SqlParameter("@UserIds", idsTable)
                {
                    TypeName = $"[dbo].[IdList]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(idsTvp);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(InboxCounts__Load)}]";

                using var reader = await cmd.ExecuteReaderAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    string externalId = reader.GetString(i++);
                    int count = reader.GetInt32(i++);
                    int unknownCount = reader.GetInt32(i++);

                    result.Add(new InboxNotificationInfo
                    {
                        ExternalId = externalId,
                        Count = count,
                        UnknownCount = unknownCount
                    });
                }
            }

            return result;
        }

        public async Task<(Guid, User, IEnumerable<(string Key, string Value)>)> UserSettings__Load(CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(UserSettings__Load));

            Guid version;
            var user = new User();
            var customSettings = new List<(string, string)>();

            var conn = await GetConnectionAsync(cancellation);
            using (var cmd = conn.CreateCommand())
            {
                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(UserSettings__Load)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync(cancellation);
                // User Settings
                if (await reader.ReadAsync(cancellation))
                {
                    int i = 0;

                    user.Id = reader.GetInt32(i++);
                    user.Name = reader.String(i++);
                    user.Name2 = reader.String(i++);
                    user.Name3 = reader.String(i++);
                    user.ImageId = reader.String(i++);
                    user.PreferredLanguage = reader.String(i++);
                    user.PreferredCalendar = reader.String(i++);

                    version = reader.GetGuid(i++);
                }
                else
                {
                    // Developer mistake
                    throw new InvalidOperationException("No settings for client were found");
                }

                // Custom settings
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    string key = reader.GetString(0);
                    string val = reader.GetString(1);

                    customSettings.Add((key, val));
                }
            }

            return (version, user, customSettings);
        }

        public async Task<(int? singleBusinessUnitId, GeneralSettings gSettings, FinancialSettings fSettings)> Settings__Load(CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Settings__Load));

            // Returns 
            // (1) whether active leaf centers are multiple or single
            // (2) the settings with the functional currency expanded

            int? singleBusinessUnitId = null;
            GeneralSettings gSettings = new GeneralSettings();
            FinancialSettings fSettings = new FinancialSettings();

            var conn = await GetConnectionAsync(cancellation);
            using (SqlCommand cmd = conn.CreateCommand())
            {
                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Settings__Load)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync(cancellation);
                // Load the version
                if (await reader.ReadAsync(cancellation))
                {
                    singleBusinessUnitId = reader.Int32(0);
                }
                else
                {
                    // Programmer mistake
                    throw new Exception($"IsMultiResonsibilityCenter was not returned from SP {nameof(Settings__Load)}");
                }

                // Next load settings
                await reader.NextResultAsync(cancellation);

                if (await reader.ReadAsync(cancellation))
                {
                    var gProps = TypeDescriptor.Get<GeneralSettings>().SimpleProperties;
                    foreach (var prop in gProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(gSettings, propValue);
                    }

                    var fProps = TypeDescriptor.Get<FinancialSettings>().SimpleProperties;
                    foreach (var prop in fProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(fSettings, propValue);
                    }
                }
                else
                {
                    // Programmer mistake
                    throw new Exception($"Settings was not returned from SP {nameof(Settings__Load)}");
                }

                // Next load functional currency
                await reader.NextResultAsync(cancellation);

                if (await reader.ReadAsync(cancellation))
                {
                    fSettings.FunctionalCurrency = new Currency();
                    var props = TypeDescriptor.Get<Currency>().SimpleProperties;
                    foreach (var prop in props)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(fSettings.FunctionalCurrency, propValue);
                    }
                }
                else
                {
                    // Programmer mistake
                    throw new Exception($"The Functional Currency was not returned from SP {nameof(Settings__Load)}");
                }
            }

            return (singleBusinessUnitId, gSettings, fSettings);
        }

        public async Task<(Guid, IEnumerable<AbstractPermission>, List<int> reportIds, List<int> dashboardIds)> Permissions__Load(bool includeReportAndDashboardIds, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Permissions__Load));

            Guid version;
            var permissions = new List<AbstractPermission>();
            var reportIds = new List<int>();
            var dashboardIds = new List<int>();

            var conn = await GetConnectionAsync(cancellation);
            using (SqlCommand cmd = conn.CreateCommand())
            {
                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Permissions__Load)}]";

                // Parameters
                cmd.Parameters.AddWithValue("@IncludeReportAndDashboardIds", includeReportAndDashboardIds);

                // Execute
                using var reader = await cmd.ExecuteReaderAsync(cancellation);
                // Load the version
                if (await reader.ReadAsync(cancellation))
                {
                    version = reader.GetGuid(0);
                }
                else
                {
                    version = Guid.Empty;
                }

                // Load the permissions
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    permissions.Add(new AbstractPermission
                    {
                        View = reader.String(i++),
                        Action = reader.String(i++),
                        Criteria = reader.String(i++),
                        Mask = reader.String(i++)
                    });
                }

                if (includeReportAndDashboardIds)
                {
                    // Report Ids
                    await reader.NextResultAsync(cancellation);
                    while (await reader.ReadAsync(cancellation))
                    {
                        reportIds.Add(reader.GetInt32(0));
                    }

                    // Dashboard Ids
                    await reader.NextResultAsync(cancellation);
                    while (await reader.ReadAsync(cancellation))
                    {
                        dashboardIds.Add(reader.GetInt32(0));
                    }
                }
            }

            return (version, permissions, reportIds, dashboardIds);
        }

        public async Task<(Guid,
            IEnumerable<LookupDefinition>,
            IEnumerable<RelationDefinition>,
            IEnumerable<CustodyDefinition>,
            IEnumerable<ResourceDefinition>,
            IEnumerable<ReportDefinition>,
            IEnumerable<DashboardDefinition>,
            IEnumerable<DocumentDefinition>,
            IEnumerable<LineDefinition>,
            IEnumerable<MarkupTemplate>,
            Dictionary<int, List<int>>,
            Dictionary<int, List<int>>,
            Dictionary<int, List<int>>,
            Dictionary<int, List<int>>)>
            Definitions__Load(CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Definitions__Load));

            Guid version;
            var lookupDefinitions = new List<LookupDefinition>();
            var relationDefinitions = new List<RelationDefinition>();
            var custodyDefinitions = new List<CustodyDefinition>();
            var resourceDefinitions = new List<ResourceDefinition>();
            var reportDefinitions = new List<ReportDefinition>();
            var dashboardDefinitions = new List<DashboardDefinition>();
            var documentDefinitions = new List<DocumentDefinition>();
            var lineDefinitions = new List<LineDefinition>();
            var markupTemplates = new List<MarkupTemplate>();

            Dictionary<int, List<int>> entryCustodianDefs = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> entryCustodyDefs = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> entryParticipantDefs = new Dictionary<int, List<int>>();
            Dictionary<int, List<int>> entryResourceDefs = new Dictionary<int, List<int>>();

            var conn = await GetConnectionAsync(cancellation);
            using (SqlCommand cmd = conn.CreateCommand())
            {
                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Definitions__Load)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync(cancellation);
                // Load the version
                if (await reader.ReadAsync(cancellation))
                {
                    version = reader.GetGuid(0);
                }
                else
                {
                    version = Guid.Empty;
                }

                // Next load lookup definitions
                var lookupDefinitionProps = TypeDescriptor.Get<LookupDefinition>().SimpleProperties;

                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new LookupDefinition();
                    foreach (var prop in lookupDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    lookupDefinitions.Add(entity);
                }

                // LookupDefinitionReportDefinitions
                var lookupDefinitionsDic = lookupDefinitions.ToDictionary(e => e.Id);
                var lookupDefinitionReportDefinitionProps = TypeDescriptor.Get<LookupDefinitionReportDefinition>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new LookupDefinitionReportDefinition();
                    foreach (var prop in lookupDefinitionReportDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var lookupDefinition = lookupDefinitionsDic[entity.LookupDefinitionId.Value];
                    lookupDefinition.ReportDefinitions ??= new List<LookupDefinitionReportDefinition>();
                    lookupDefinition.ReportDefinitions.Add(entity);
                }

                // Next load relation definitions
                var relationDefinitionProps = TypeDescriptor.Get<RelationDefinition>().SimpleProperties;

                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new RelationDefinition();
                    foreach (var prop in relationDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    relationDefinitions.Add(entity);
                }

                // RelationDefinitionReportDefinitions
                var relationDefinitionsDic = relationDefinitions.ToDictionary(e => e.Id);
                var relationDefinitionReportDefinitionProps = TypeDescriptor.Get<RelationDefinitionReportDefinition>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new RelationDefinitionReportDefinition();
                    foreach (var prop in relationDefinitionReportDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var relationDefinition = relationDefinitionsDic[entity.RelationDefinitionId.Value];
                    relationDefinition.ReportDefinitions ??= new List<RelationDefinitionReportDefinition>();
                    relationDefinition.ReportDefinitions.Add(entity);
                }

                // Next load custody definitions
                var custodyDefinitionProps = TypeDescriptor.Get<CustodyDefinition>().SimpleProperties;

                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new CustodyDefinition();
                    foreach (var prop in custodyDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    custodyDefinitions.Add(entity);
                }

                // CustodyDefinitionReportDefinitions
                var custodyDefinitionsDic = custodyDefinitions.ToDictionary(e => e.Id);
                var custodyDefinitionReportDefinitionProps = TypeDescriptor.Get<CustodyDefinitionReportDefinition>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new CustodyDefinitionReportDefinition();
                    foreach (var prop in custodyDefinitionReportDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var custodyDefinition = custodyDefinitionsDic[entity.CustodyDefinitionId.Value];
                    custodyDefinition.ReportDefinitions ??= new List<CustodyDefinitionReportDefinition>();
                    custodyDefinition.ReportDefinitions.Add(entity);
                }

                // Next load resource definitions
                var resourceDefinitionProps = TypeDescriptor.Get<ResourceDefinition>().SimpleProperties;

                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ResourceDefinition();
                    foreach (var prop in resourceDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    resourceDefinitions.Add(entity);
                }

                // ResourceDefinitionReportDefinitions
                var resourceDefinitionsDic = resourceDefinitions.ToDictionary(e => e.Id);
                var resourceDefinitionReportDefinitionProps = TypeDescriptor.Get<ResourceDefinitionReportDefinition>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ResourceDefinitionReportDefinition();
                    foreach (var prop in resourceDefinitionReportDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var resourceDefinition = resourceDefinitionsDic[entity.ResourceDefinitionId.Value];
                    resourceDefinition.ReportDefinitions ??= new List<ResourceDefinitionReportDefinition>();
                    resourceDefinition.ReportDefinitions.Add(entity);
                }

                // Next load report definitions
                await reader.NextResultAsync(cancellation);

                var reportDefinitionsDic = new Dictionary<int, ReportDefinition>();
                var reportDefinitionProps = TypeDescriptor.Get<ReportDefinition>().SimpleProperties;
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ReportDefinition();
                    foreach (var prop in reportDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    reportDefinitionsDic[entity.Id] = entity;
                }

                // Parameters
                var reportDefinitionParameterProps = TypeDescriptor.Get<ReportDefinitionParameter>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ReportDefinitionParameter();
                    foreach (var prop in reportDefinitionParameterProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var reportDefinition = reportDefinitionsDic[entity.ReportDefinitionId.Value];
                    reportDefinition.Parameters ??= new List<ReportDefinitionParameter>();
                    reportDefinition.Parameters.Add(entity);
                }

                // Select
                var reportDefinitionSelectProps = TypeDescriptor.Get<ReportDefinitionSelect>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ReportDefinitionSelect();
                    foreach (var prop in reportDefinitionSelectProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var reportDefinition = reportDefinitionsDic[entity.ReportDefinitionId.Value];
                    reportDefinition.Select ??= new List<ReportDefinitionSelect>();
                    reportDefinition.Select.Add(entity);
                }

                // Rows
                var attributesDic = new Dictionary<int, List<ReportDefinitionDimensionAttribute>>(); // Dimension Id => Attributes list
                var reportDefinitionRowProps = TypeDescriptor.Get<ReportDefinitionRow>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ReportDefinitionRow();
                    foreach (var prop in reportDefinitionRowProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var reportDefinition = reportDefinitionsDic[entity.ReportDefinitionId.Value];
                    reportDefinition.Rows ??= new List<ReportDefinitionRow>();
                    reportDefinition.Rows.Add(entity);

                    entity.Attributes ??= new List<ReportDefinitionDimensionAttribute>();
                    attributesDic[entity.Id] = entity.Attributes;
                }

                // Columns
                var reportDefinitionColumnProps = TypeDescriptor.Get<ReportDefinitionColumn>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ReportDefinitionColumn();
                    foreach (var prop in reportDefinitionColumnProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var reportDefinition = reportDefinitionsDic[entity.ReportDefinitionId.Value];
                    reportDefinition.Columns ??= new List<ReportDefinitionColumn>();
                    reportDefinition.Columns.Add(entity);

                    entity.Attributes ??= new List<ReportDefinitionDimensionAttribute>();
                    attributesDic[entity.Id] = entity.Attributes;
                }

                // Dimension Attributes
                var reportDefinitionAttributeProps = TypeDescriptor.Get<ReportDefinitionDimensionAttribute>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ReportDefinitionDimensionAttribute();
                    foreach (var prop in reportDefinitionAttributeProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var attributesList = attributesDic[entity.ReportDefinitionDimensionId.Value];
                    attributesList.Add(entity);
                }

                // Measures
                var reportDefinitionMeasureProps = TypeDescriptor.Get<ReportDefinitionMeasure>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new ReportDefinitionMeasure();
                    foreach (var prop in reportDefinitionMeasureProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var reportDefinition = reportDefinitionsDic[entity.ReportDefinitionId.Value];
                    reportDefinition.Measures ??= new List<ReportDefinitionMeasure>();
                    reportDefinition.Measures.Add(entity);
                }

                reportDefinitions = reportDefinitionsDic.Values.ToList();

                // Dashboard Definitions
                await reader.NextResultAsync(cancellation);
                var dashboardDefinitionsDic = new Dictionary<int, DashboardDefinition>();
                var dashboardDefinitionProps = TypeDescriptor.Get<DashboardDefinition>().SimpleProperties;
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new DashboardDefinition();
                    foreach (var prop in dashboardDefinitionProps)
                    {
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    dashboardDefinitionsDic[entity.Id] = entity;
                }

                // Widgets

                var dashboardDefinitionsWidgetProps = TypeDescriptor.Get<DashboardDefinitionWidget>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new DashboardDefinitionWidget();
                    foreach (var prop in dashboardDefinitionsWidgetProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var dashboardDefinition = dashboardDefinitionsDic[entity.DashboardDefinitionId.Value];
                    dashboardDefinition.Widgets ??= new List<DashboardDefinitionWidget>();
                    dashboardDefinition.Widgets.Add(entity);
                }


                dashboardDefinitions = dashboardDefinitionsDic.Values.ToList();

                // Next load document definitions
                await reader.NextResultAsync(cancellation);

                var documentDefinitionsDic = new Dictionary<int, DocumentDefinition>();
                var documentDefinitionProps = TypeDescriptor.Get<DocumentDefinition>().SimpleProperties;
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new DocumentDefinition();
                    foreach (var prop in documentDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    documentDefinitionsDic[entity.Id] = entity;
                }

                // Document Definitions Line Definitions
                var documentDefinitionLineDefinitionProps = TypeDescriptor.Get<DocumentDefinitionLineDefinition>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new DocumentDefinitionLineDefinition();
                    foreach (var prop in documentDefinitionLineDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var documentDefinition = documentDefinitionsDic[entity.DocumentDefinitionId.Value];
                    documentDefinition.LineDefinitions ??= new List<DocumentDefinitionLineDefinition>();
                    documentDefinition.LineDefinitions.Add(entity);
                }

                documentDefinitions = documentDefinitionsDic.Values.ToList();

                // Next load account types
                var accountTypesDic = new Dictionary<int, AccountType>();
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    var entity = new AccountType
                    {
                        Id = reader.GetInt32(i++),
                        EntryTypeParentId = reader.Int32(i++),
                    };

                    accountTypesDic.Add(entity.Id, entity);
                }

                // Next load line definitions
                await reader.NextResultAsync(cancellation);

                var lineDefinitionsDic = new Dictionary<int, LineDefinition>();
                var lineDefinitionProps = TypeDescriptor.Get<LineDefinition>().SimpleProperties;
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new LineDefinition();
                    foreach (var prop in lineDefinitionProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    lineDefinitionsDic[entity.Id] = entity;
                }

                // line definition entries
                var lineDefinitionEntryProps = TypeDescriptor.Get<LineDefinitionEntry>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new LineDefinitionEntry
                    {
                        CustodyDefinitions = new List<LineDefinitionEntryCustodyDefinition>(),
                        ResourceDefinitions = new List<LineDefinitionEntryResourceDefinition>(),
                    };

                    foreach (var prop in lineDefinitionEntryProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    if (entity.ParentAccountTypeId != null)
                    {
                        entity.ParentAccountType = accountTypesDic.GetValueOrDefault(entity.ParentAccountTypeId.Value);
                    }

                    var lineDefinition = lineDefinitionsDic[entity.LineDefinitionId.Value];
                    lineDefinition.Entries ??= new List<LineDefinitionEntry>();
                    lineDefinition.Entries.Add(entity);
                }

                // line definition columns
                var lineDefinitionColumnProps = TypeDescriptor.Get<LineDefinitionColumn>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new LineDefinitionColumn();
                    foreach (var prop in lineDefinitionColumnProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var lineDefinition = lineDefinitionsDic[entity.LineDefinitionId.Value];
                    lineDefinition.Columns ??= new List<LineDefinitionColumn>();
                    lineDefinition.Columns.Add(entity);
                }

                // line definition state reason
                var lineDefinitionStateReasonProps = TypeDescriptor.Get<LineDefinitionStateReason>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new LineDefinitionStateReason();
                    foreach (var prop in lineDefinitionStateReasonProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var lineDefinition = lineDefinitionsDic[entity.LineDefinitionId.Value];
                    lineDefinition.StateReasons ??= new List<LineDefinitionStateReason>();
                    lineDefinition.StateReasons.Add(entity);
                }

                // line definition generate parameter
                var lineDefinitionGenerateParameterProps = TypeDescriptor.Get<LineDefinitionGenerateParameter>().SimpleProperties;
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    var entity = new LineDefinitionGenerateParameter();
                    foreach (var prop in lineDefinitionGenerateParameterProps)
                    {
                        // get property value
                        var propValue = reader[prop.Name];
                        propValue = propValue == DBNull.Value ? null : propValue;

                        prop.SetValue(entity, propValue);
                    }

                    var lineDefinition = lineDefinitionsDic[entity.LineDefinitionId.Value];
                    lineDefinition.GenerateParameters ??= new List<LineDefinitionGenerateParameter>();
                    lineDefinition.GenerateParameters.Add(entity);
                }

                lineDefinitions = lineDefinitionsDic.Values.ToList();

                // Custodian Definitions
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    var entryId = reader.GetInt32(i++);
                    var defId = reader.GetInt32(i++);

                    if (!entryCustodianDefs.TryGetValue(entryId, out List<int> defIds))
                    {
                        defIds = new List<int>();
                        entryCustodianDefs.Add(entryId, defIds);
                    }

                    defIds.Add(defId);
                }

                // Custody Definitions
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    var entryId = reader.GetInt32(i++);
                    var defId = reader.GetInt32(i++);

                    if (!entryCustodyDefs.TryGetValue(entryId, out List<int> defIds))
                    {
                        defIds = new List<int>();
                        entryCustodyDefs.Add(entryId, defIds);
                    }

                    defIds.Add(defId);
                }

                // Participant Definitions
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    var entryId = reader.GetInt32(i++);
                    var defId = reader.GetInt32(i++);

                    if (!entryParticipantDefs.TryGetValue(entryId, out List<int> defIds))
                    {
                        defIds = new List<int>();
                        entryParticipantDefs.Add(entryId, defIds);
                    }

                    defIds.Add(defId);
                }

                // Resource Definitions
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    var entryId = reader.GetInt32(i++);
                    var defId = reader.GetInt32(i++);

                    if (!entryResourceDefs.TryGetValue(entryId, out List<int> defIds))
                    {
                        defIds = new List<int>();
                        entryResourceDefs.Add(entryId, defIds);
                    }

                    defIds.Add(defId);
                }

                // Markup templates
                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    markupTemplates.Add(new MarkupTemplate
                    {
                        Id = reader.GetInt32(i++),
                        Name = reader.String(i++),
                        Name2 = reader.String(i++),
                        Name3 = reader.String(i++),
                        SupportsPrimaryLanguage = reader.GetBoolean(i++),
                        SupportsSecondaryLanguage = reader.GetBoolean(i++),
                        SupportsTernaryLanguage = reader.GetBoolean(i++),
                        Usage = reader.String(i++),
                        Collection = reader.String(i++),
                        DefinitionId = reader.Int32(i++),
                    });
                }
            }

            return (version, lookupDefinitions, relationDefinitions, custodyDefinitions, resourceDefinitions, reportDefinitions, dashboardDefinitions, documentDefinitions, lineDefinitions, markupTemplates, entryCustodianDefs, entryCustodyDefs, entryParticipantDefs, entryResourceDefs);
        }

        #endregion

        #region Units

        public async Task<IEnumerable<ValidationError>> Units_Validate__Save(List<UnitForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Units_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Unit)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Units_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> Units__Save(List<UnitForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Units__Save));
            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(Unit)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Units__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task Units__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Units__Activate));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Units__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> Units_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Units_Validate__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Units_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Units__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Units__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Units__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region Custodies

        private SqlParameter CustodiesTvp(List<CustodyForSave> entities)
        {
            var extraColumns = new List<ExtraColumn<CustodyForSave>> {
                    RepositoryUtilities.Column("ImageId", typeof(string), (CustodyForSave e) => e.Image == null ? "(Unchanged)" : e.EntityMetadata?.FileId)
                };

            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true, extraColumns: extraColumns);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Custody)}List]",
                SqlDbType = SqlDbType.Structured
            };

            return entitiesTvp;
        }

        public async Task Custodies__Preprocess(int definitionId, List<CustodyForSave> entities)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Custodies__Preprocess));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var entitiesTvp = CustodiesTvp(entities);

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(entitiesTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Custodies__Preprocess)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();

            var props = TypeDescriptor.Get<CustodyForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);
                var entity = entities[index];

                foreach (var prop in props)
                {
                    // get property value
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(entity, propValue);
                }
            }
        }

        public async Task<IEnumerable<ValidationError>> Custodies_Validate__Save(int definitionId, List<CustodyForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Custodies_Validate__Save));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var entitiesTvp = CustodiesTvp(entities);

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Custodies_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<(List<string> deletedImageIds, List<int> ids)> Custodies__Save(int definitionId, List<CustodyForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Custodies__Save));

            var deletedImageIds = new List<string>();
            var ids = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                var entitiesTvp = CustodiesTvp(entities);

                cmd.Parameters.Add("@DefinitionId", definitionId);
                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Custodies__Save)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    deletedImageIds.Add(reader.String(0));
                }

                if (returnIds)
                {
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        ids.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            ids.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return (deletedImageIds, sortedResult.ToList());
        }

        public async Task Custodies__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Custodies__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Custodies__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> Custodies_Validate__Delete(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Custodies_Validate__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Custodies_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Custodies__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Custodies__Activate));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Custodies__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region CustodyDefinitions

        public async Task<IEnumerable<ValidationError>> CustodyDefinitions_Validate__Save(List<CustodyDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(CustodyDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(CustodyDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
            var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(CustodyDefinitionReportDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(reportDefinitionsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(CustodyDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> CustodyDefinitions__Save(List<CustodyDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(CustodyDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(CustodyDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
                var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(CustodyDefinitionReportDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(reportDefinitionsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(CustodyDefinitions__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> CustodyDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(CustodyDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(CustodyDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task CustodyDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(CustodyDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(CustodyDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> CustodyDefinitions_Validate__UpdateState(List<int> ids, string state, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(CustodyDefinitions_Validate__UpdateState));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(CustodyDefinitions_Validate__UpdateState)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task CustodyDefinitions__UpdateState(List<int> ids, string state)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(CustodyDefinitions__UpdateState));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(CustodyDefinitions__UpdateState)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region Relations

        private SqlParameter RelationsTvp(List<RelationForSave> entities)
        {
            var extraRelationColumns = new List<ExtraColumn<RelationForSave>> {
                    RepositoryUtilities.Column("ImageId", typeof(string), (RelationForSave e) => e.Image == null ? "(Unchanged)" : e.EntityMetadata?.FileId),
                    RepositoryUtilities.Column("UpdateAttachments", typeof(bool), (RelationForSave e) => e.Attachments != null),
                };

            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true, extraColumns: extraRelationColumns);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Relation)}List]",
                SqlDbType = SqlDbType.Structured
            };

            return entitiesTvp;
        }

        private SqlParameter RelationUsersTvp(List<RelationForSave> entities)
        {
            DataTable usersTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Users);
            var usersTvp = new SqlParameter("@RelationUsers", usersTable)
            {
                TypeName = $"[dbo].[{nameof(RelationUser)}List]",
                SqlDbType = SqlDbType.Structured
            };

            return usersTvp;
        }

        private SqlParameter RelationAttachmentsTvp(List<RelationForSave> entities)
        {
            var extraAttachmentColumns = new List<ExtraColumn<RelationAttachmentForSave>> {
                    RepositoryUtilities.Column("FileId", typeof(string), (RelationAttachmentForSave e) => e.EntityMetadata?.FileId),
                    RepositoryUtilities.Column("Size", typeof(long), (RelationAttachmentForSave e) => e.EntityMetadata?.FileSize)
                };

            DataTable attachmentsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Attachments, extraColumns: extraAttachmentColumns);
            var attachmentsTvp = new SqlParameter("@Attachments", attachmentsTable)
            {
                TypeName = $"[dbo].[{nameof(RelationAttachment)}List]",
                SqlDbType = SqlDbType.Structured
            };

            return attachmentsTvp;
        }

        public async Task Relations__Preprocess(int definitionId, List<RelationForSave> entities)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Relations__Preprocess));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var entitiesTvp = RelationsTvp(entities);

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(entitiesTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Relations__Preprocess)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();

            var props = TypeDescriptor.Get<RelationForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);
                var entity = entities[index];

                foreach (var prop in props)
                {
                    // get property value
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(entity, propValue);
                }
            }
        }

        public async Task<IEnumerable<ValidationError>> Relations_Validate__Save(int definitionId, List<RelationForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Relations_Validate__Save));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var entitiesTvp = RelationsTvp(entities);
            var usersTvp = RelationUsersTvp(entities);

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(usersTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Relations_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<(List<string> deletedImageIds, List<string> deletedAttachmentIds, List<int> ids)> Relations__Save(int definitionId, List<RelationForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Relations__Save));
            var ids = new List<IndexedId>();
            var deletedImageIds = new List<string>();
            var deletedAttachmentIds = new List<string>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                var entitiesTvp = RelationsTvp(entities);
                var usersTvp = RelationUsersTvp(entities);
                var attachmentsTvp = RelationAttachmentsTvp(entities);

                cmd.Parameters.Add("@DefinitionId", definitionId);
                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(usersTvp);
                cmd.Parameters.Add(attachmentsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Relations__Save)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    deletedImageIds.Add(reader.String(0));
                }

                await reader.NextResultAsync();
                while (await reader.ReadAsync())
                {
                    deletedAttachmentIds.Add(reader.String(0));
                }

                if (returnIds)
                {
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        ids.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            ids.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return (deletedImageIds, deletedAttachmentIds, sortedResult.ToList());
        }

        public async Task Relations__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Relations__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Relations__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> Relations_Validate__Delete(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Relations_Validate__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Relations_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Relations__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Relations__Activate));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Relations__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region RelationDefinitions

        public async Task<IEnumerable<ValidationError>> RelationDefinitions_Validate__Save(List<RelationDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(RelationDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(RelationDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
            var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(RelationDefinitionReportDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(reportDefinitionsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(RelationDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> RelationDefinitions__Save(List<RelationDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(RelationDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(RelationDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
                var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(RelationDefinitionReportDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(reportDefinitionsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(RelationDefinitions__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> RelationDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(RelationDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(RelationDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task RelationDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(RelationDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(RelationDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> RelationDefinitions_Validate__UpdateState(List<int> ids, string state, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(RelationDefinitions_Validate__UpdateState));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(RelationDefinitions_Validate__UpdateState)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task RelationDefinitions__UpdateState(List<int> ids, string state)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(RelationDefinitions__UpdateState));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(RelationDefinitions__UpdateState)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region Agents

        public async Task<IEnumerable<ValidationError>> Agents_Validate__Save(List<AgentForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Agents_Validate__Save));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Agent)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Agents_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> Agents__Save(List<AgentForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Agents__Save));
            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(Agent)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Agents__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task Agents__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Agents__Activate));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Agents__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> Agents_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Agents_Validate__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Agents_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Agents__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Agents__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Agents__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region Users

        private SqlParameter UsersTvp(List<UserForSave> entities)
        {
            var extraColumns = new List<ExtraColumn<UserForSave>> {
                    RepositoryUtilities.Column("ImageId", typeof(string), (UserForSave e) => e.Image == null ? "(Unchanged)" : e.EntityMetadata?.FileId)
                };

            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true, extraColumns: extraColumns);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(User)}List]",
                SqlDbType = SqlDbType.Structured
            };

            return entitiesTvp;
        }

        public async Task Users__SaveSettings(string key, string value)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users__SaveSettings));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            cmd.Parameters.Add("Key", key);
            cmd.Parameters.Add("Value", value);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Users__SaveSettings)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task Users__SavePreferredLanguage(string preferredLanguage, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users__SavePreferredLanguage));
            var conn = await GetConnectionAsync(cancellation);
            using var cmd = conn.CreateCommand();
            // Parameters
            cmd.Parameters.Add("PreferredLanguage", preferredLanguage);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Users__SavePreferredLanguage)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync(cancellation);
        }

        public async Task Users__SavePreferredCalendar(string preferredCalendar, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users__SavePreferredCalendar));
            var conn = await GetConnectionAsync(cancellation);
            using var cmd = conn.CreateCommand();
            // Parameters
            cmd.Parameters.Add("PreferredCalendar", preferredCalendar);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Users__SavePreferredCalendar)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync(cancellation);
        }

        public async Task<IEnumerable<ValidationError>> Users_Validate__Save(List<UserForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users_Validate__Save));
            entities.ForEach(e =>
            {
                e.Roles?.ForEach(r =>
                {
                    r.UserId = e.Id;
                });
            });

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var entitiesTvp = UsersTvp(entities);

            DataTable rolesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Roles);
            var rolesTvp = new SqlParameter("@Roles", rolesTable)
            {
                TypeName = $"[dbo].[{nameof(RoleMembership)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(rolesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Users_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<(List<string> deletedImageIds, List<int> ids)> Users__Save(List<UserForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users__Save));
            entities.ForEach(e =>
            {
                e.Roles?.ForEach(r =>
                {
                    r.UserId = e.Id;
                });
            });

            var deletedImageIds = new List<string>();
            var ids = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                var entitiesTvp = UsersTvp(entities);

                DataTable rolesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Roles);
                var rolesTvp = new SqlParameter("@Roles", rolesTable)
                {
                    TypeName = $"[dbo].[{nameof(RoleMembership)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(rolesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Users__Save)}]";


                // Execute
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    deletedImageIds.Add(reader.String(0));
                }

                if (returnIds)
                {
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        ids.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            ids.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return (deletedImageIds, sortedResult.ToList());
        }

        public async Task<IEnumerable<ValidationError>> Users_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users_Validate__Delete));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Users_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<IEnumerable<string>> Users__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users__Delete));
            var deletedEmails = new List<string>(); // the result

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
                var idsTvp = new SqlParameter("@Ids", idsTable)
                {
                    TypeName = $"[dbo].[IdList]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(idsTvp);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Users__Delete)}]";

                // Execute
                try
                {
                    // Execute
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        deletedEmails.Add(reader.GetString(0));
                    }
                }
                catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
                {
                    throw new ForeignKeyViolationException();
                }
            }

            return deletedEmails;
        }

        public async Task Users__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users__Activate));
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Users__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task Users__SetExternalIdByUserId(int userId, string externalId)
        {
            // Finds the user with the given id and sets its ExternalId to the one supplied only if it's null
            using var _ = Instrumentation.Block("Repo." + nameof(Users__SetExternalIdByUserId));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            cmd.Parameters.Add("UserId", userId);
            cmd.Parameters.Add("ExternalId", externalId);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Users__SetExternalIdByUserId)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task Users__SetEmailByUserId(int userId, string externalEmail)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Users__SetEmailByUserId));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            cmd.Parameters.Add("UserId", userId);
            cmd.Parameters.Add("ExternalEmail", externalEmail);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Users__SetEmailByUserId)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region Roles

        public async Task<List<int>> Roles__Save(List<RoleForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Roles__Save));

            entities.ForEach(e =>
            {
                e.Members?.ForEach(m =>
                {
                    m.RoleId = e.Id;
                });
            });

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(Role)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable membersTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Members);
                var membersTvp = new SqlParameter("@Members", membersTable)
                {
                    TypeName = $"[dbo].[{nameof(RoleMembership)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable permissionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Permissions);
                var permissionsTvp = new SqlParameter("@Permissions", permissionsTable)
                {
                    TypeName = $"[dbo].[{nameof(Permission)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(membersTvp);
                cmd.Parameters.Add(permissionsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Roles__Save)}]";

                // Execute
                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> Roles_Validate__Save(List<RoleForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Roles_Validate__Save));

            entities.ForEach(e =>
            {
                e.Members?.ForEach(m =>
                {
                    m.RoleId = e.Id;
                });
            });

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Role)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable membersTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Members);
            var membersTvp = new SqlParameter("@Members", membersTable)
            {
                TypeName = $"[dbo].[{nameof(RoleMembership)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable permissionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Permissions);
            var permissionsTvp = new SqlParameter("@Permissions", permissionsTable)
            {
                TypeName = $"[dbo].[{nameof(Permission)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(membersTvp);
            cmd.Parameters.Add(permissionsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Roles_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Roles__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Roles__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Roles__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> Roles_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Roles_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Roles_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Roles__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Roles__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Roles__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region Blobs

        public async Task Blobs__Delete(IEnumerable<string> blobNames)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Blobs__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable namesTable = RepositoryUtilities.DataTable(blobNames.Select(id => new StringListItem { Id = id }));
            var namesTvp = new SqlParameter("@BlobNames", namesTable)
            {
                TypeName = $"[dbo].[StringList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(namesTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Blobs__Delete)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task Blobs__Save(string name, byte[] blob)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Blobs__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            cmd.Parameters.Add("@Name", name);
            cmd.Parameters.Add("@Blob", blob);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Blobs__Save)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<byte[]> Blobs__Get(string name, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Blobs__Get));

            byte[] result = null;

            var conn = await GetConnectionAsync(cancellation);
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                cmd.Parameters.Add("@Name", name);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Blobs__Get)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellation);
                if (await reader.ReadAsync(cancellation))
                {
                    result = (byte[])reader[0];
                }
            }

            return result;
        }

        #endregion

        #region GeneralSettings

        public async Task<IEnumerable<ValidationError>> GeneralSettings_Validate__Save(GeneralSettingsForSave settingsForSave, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Currencies_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var mappedProps = TypeDescriptor.Get<GeneralSettingsForSave>().SimpleProperties;
            foreach (var prop in mappedProps)
            {
                var propName = prop.Name;
                var key = $"@{propName}";
                var value = prop.GetValue(settingsForSave);

                cmd.Parameters.Add(key, value);
            }

            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(GeneralSettings_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task GeneralSettings__Save(GeneralSettingsForSave settingsForSave)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(GeneralSettings__Save));

            if (settingsForSave is null)
            {
                return;
            }

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var mappedProps = TypeDescriptor.Get<GeneralSettingsForSave>().SimpleProperties;
            foreach (var prop in mappedProps)
            {
                var propName = prop.Name;
                var key = $"@{propName}";
                var value = prop.GetValue(settingsForSave);

                cmd.Parameters.Add(key, value);
            }

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(GeneralSettings__Save)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region FinancialSettings

        public async Task<IEnumerable<ValidationError>> FinancialSettings_Validate__Save(FinancialSettingsForSave settingsForSave, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Currencies_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var mappedProps = TypeDescriptor.Get<FinancialSettingsForSave>().SimpleProperties;
            foreach (var prop in mappedProps)
            {
                var propName = prop.Name;
                var key = $"@{propName}";
                var value = prop.GetValue(settingsForSave);

                cmd.Parameters.Add(key, value);
            }

            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(FinancialSettings_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task FinancialSettings__Save(FinancialSettingsForSave settingsForSave)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(FinancialSettings__Save));

            if (settingsForSave is null)
            {
                return;
            }

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var mappedProps = TypeDescriptor.Get<FinancialSettingsForSave>().SimpleProperties;
            foreach (var prop in mappedProps)
            {
                var propName = prop.Name;
                var key = $"@{propName}";
                var value = prop.GetValue(settingsForSave);

                cmd.Parameters.Add(key, value);
            }

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(FinancialSettings__Save)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region Lookups

        public async Task<IEnumerable<ValidationError>> Lookups_Validate__Save(int definitionId, List<LookupForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lookups_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Lookup)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Lookups_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> Lookups__Save(int definitionId, List<LookupForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lookups__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(Lookup)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add("@DefinitionId", definitionId);
                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Lookups__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task Lookups__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lookups__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Lookups__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> Lookups_Validate__Delete(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lookups_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Lookups_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Lookups__Delete(int definitionId, IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lookups__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Lookups__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region Currencies

        public async Task<IEnumerable<ValidationError>> Currencies_Validate__Save(List<CurrencyForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Currencies_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Currency)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Currencies_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Currencies__Save(List<CurrencyForSave> entities)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Currencies__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Currency)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);

            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Currencies__Save)}]";

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task Currencies__Activate(List<string> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Currencies__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new StringListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[StringList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Currencies__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> Currencies_Validate__Delete(List<string> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Currencies_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new StringListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedStringList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Currencies_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Currencies__Delete(IEnumerable<string> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Currencies__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new StringListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[StringList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Currencies__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region Resources

        private SqlParameter ResourcesTvp(List<ResourceForSave> entities)
        {
            var extraColumns = new List<ExtraColumn<ResourceForSave>> {
                    RepositoryUtilities.Column("ImageId", typeof(string), (ResourceForSave e) => e.Image == null ? "(Unchanged)" : e.EntityMetadata?.FileId)
                };

            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true, extraColumns: extraColumns);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Resource)}List]",
                SqlDbType = SqlDbType.Structured
            };

            return entitiesTvp;
        }

        public async Task Resources__Preprocess(int definitionId, List<ResourceForSave> entities)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Resources__Preprocess));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var entitiesTvp = ResourcesTvp(entities);

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(entitiesTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Resources__Preprocess)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();

            var props = TypeDescriptor.Get<ResourceForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);
                var entity = entities[index];

                foreach (var prop in props)
                {
                    // get property value
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(entity, propValue);
                }
            }
        }

        public async Task<IEnumerable<ValidationError>> Resources_Validate__Save(int definitionId, List<ResourceForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Resources_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var entitiesTvp = ResourcesTvp(entities);

            DataTable unitsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Units);
            var unitsTvp = new SqlParameter("@ResourceUnits", unitsTable)
            {
                TypeName = $"[dbo].[{nameof(ResourceUnit)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(unitsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Resources_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<(List<string> deletedImageIds, List<int> ids)> Resources__Save(int definitionId, List<ResourceForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Resources__Save));

            var deletedImageIds = new List<string>();
            var ids = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                var entitiesTvp = ResourcesTvp(entities);

                DataTable unitsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Units);
                var unitsTvp = new SqlParameter("@ResourceUnits", unitsTable)
                {
                    TypeName = $"[dbo].[{nameof(ResourceUnit)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add("@DefinitionId", definitionId);
                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(unitsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Resources__Save)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    deletedImageIds.Add(reader.String(0));
                }

                if (returnIds)
                {
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        ids.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            ids.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return (deletedImageIds, sortedResult.ToList());
        }

        public async Task Resources__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Resources__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Resources__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> Resources_Validate__Delete(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Resources_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Resources_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Resources__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Resources__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Resources__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region AccountClassifications

        public async Task<IEnumerable<ValidationError>> AccountClassifications_Validate__Save(List<AccountClassificationForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountClassifications_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(AccountClassification)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(AccountClassifications_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> AccountClassifications__Save(List<AccountClassificationForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountClassifications__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(AccountClassification)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(AccountClassifications__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task AccountClassifications__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountClassifications__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(AccountClassifications__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> AccountClassifications_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountClassifications_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(AccountClassifications_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task AccountClassifications__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountClassifications__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(AccountClassifications__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> AccountClassifications_Validate__DeleteWithDescendants(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountClassifications_Validate__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(AccountClassifications_Validate__DeleteWithDescendants)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task AccountClassifications__DeleteWithDescendants(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountClassifications__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(AccountClassifications__DeleteWithDescendants)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region AccountTypes

        public async Task<IEnumerable<ValidationError>> AccountTypes_Validate__Save(List<AccountTypeForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountTypes_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(AccountType)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable resourceDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ResourceDefinitions);
            var resourceDefinitionsTvp = new SqlParameter("@AccountTypeResourceDefinitions", resourceDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(AccountTypeResourceDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable custodyDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.CustodyDefinitions);
            var custodyDefinitionsTvp = new SqlParameter("@AccountTypeCustodyDefinitions", custodyDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(AccountTypeCustodyDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(resourceDefinitionsTvp);
            cmd.Parameters.Add(custodyDefinitionsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(AccountTypes_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> AccountTypes__Save(List<AccountTypeForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountTypes__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(AccountType)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable resourceDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ResourceDefinitions);
                var resourceDefinitionsTvp = new SqlParameter("@AccountTypeResourceDefinitions", resourceDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(AccountTypeResourceDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable custodyDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.CustodyDefinitions);
                var custodyDefinitionsTvp = new SqlParameter("@AccountTypeCustodyDefinitions", custodyDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(AccountTypeCustodyDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(resourceDefinitionsTvp);
                cmd.Parameters.Add(custodyDefinitionsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(AccountTypes__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task AccountTypes__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountTypes__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(AccountTypes__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> AccountTypes_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountTypes_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(AccountTypes_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task AccountTypes__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountTypes__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(AccountTypes__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> AccountTypes_Validate__DeleteWithDescendants(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountTypes_Validate__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(AccountTypes_Validate__DeleteWithDescendants)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task AccountTypes__DeleteWithDescendants(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(AccountTypes__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(AccountTypes__DeleteWithDescendants)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region Accounts

        public async Task Accounts__Preprocess(List<AccountForSave> entities)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Accounts__Preprocess));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Account)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Accounts__Preprocess)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();
            var props = TypeDescriptor.Get<AccountForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);
                var entity = entities[index];
                foreach (var prop in props)
                {
                    // get property value
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(entity, propValue);
                }
            }
        }

        public async Task<IEnumerable<ValidationError>> Accounts_Validate__Save(List<AccountForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Accounts_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Account)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Accounts_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> Accounts__Save(List<AccountForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Accounts__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(Account)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Accounts__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task Accounts__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Accounts__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            var isActivatedParam = new SqlParameter("@IsActive", isActive);

            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Accounts__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> Accounts_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Accounts_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Accounts_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Accounts__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Accounts__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Accounts__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region Centers

        public async Task<IEnumerable<ValidationError>> Centers_Validate__Save(List<CenterForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Centers_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(Center)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Centers_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> Centers__Save(List<CenterForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Centers__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(Center)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Centers__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            if (returnIds)
            {
                var sortedResult = new int[entities.Count];
                result.ForEach(e =>
                {
                    sortedResult[e.Index] = e.Id;
                });

                return sortedResult.ToList();
            }
            else
            {
                return new List<int>();
            }
        }

        public async Task Centers__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Centers__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Centers__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> Centers_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Centers_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Centers_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Centers__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Centers__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Centers__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> Centers_Validate__DeleteWithDescendants(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Centers_Validate__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Centers_Validate__DeleteWithDescendants)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task Centers__DeleteWithDescendants(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Centers__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Centers__DeleteWithDescendants)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region EntryTypes

        public async Task<IEnumerable<ValidationError>> EntryTypes_Validate__Save(List<EntryTypeForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(EntryTypes_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(EntryType)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(EntryTypes_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> EntryTypes__Save(List<EntryTypeForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(EntryTypes__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(EntryType)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(EntryTypes__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task EntryTypes__Activate(List<int> ids, bool isActive)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(EntryTypes__Activate));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@IsActive", isActive);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(EntryTypes__Activate)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<IEnumerable<ValidationError>> EntryTypes_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(EntryTypes_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(EntryTypes_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task EntryTypes__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(EntryTypes__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(EntryTypes__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> EntryTypes_Validate__DeleteWithDescendants(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(EntryTypes_Validate__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(EntryTypes_Validate__DeleteWithDescendants)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task EntryTypes__DeleteWithDescendants(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(EntryTypes__DeleteWithDescendants));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(EntryTypes__DeleteWithDescendants)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region Documents

        public async Task Documents__Preprocess(int definitionId, List<DocumentForSave> docs)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Preprocess));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var (docsTable, lineDefinitionEntriesTable, linesTable, entriesTable, _) = RepositoryUtilities.DataTableFromDocuments(docs);

            var docsTvp = new SqlParameter("@Documents", docsTable)
            {
                TypeName = $"[dbo].[{nameof(Document)}List]",
                SqlDbType = SqlDbType.Structured
            };

            var lineDefinitionEntriesTvp = new SqlParameter("@DocumentLineDefinitionEntries", lineDefinitionEntriesTable)
            {
                TypeName = $"[dbo].[{nameof(DocumentLineDefinitionEntry)}List]",
                SqlDbType = SqlDbType.Structured
            };

            var linesTvp = new SqlParameter("@Lines", linesTable)
            {
                TypeName = $"[dbo].[{nameof(Line)}List]",
                SqlDbType = SqlDbType.Structured
            };

            var entriesTvp = new SqlParameter("@Entries", entriesTable)
            {
                TypeName = $"[dbo].[{nameof(Entry)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(docsTvp);
            cmd.Parameters.Add(lineDefinitionEntriesTvp);
            cmd.Parameters.Add(linesTvp);
            cmd.Parameters.Add(entriesTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents__Preprocess)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();

            // Documents
            var docProps = TypeDescriptor.Get<DocumentForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);

                var doc = docs[index];

                foreach (var prop in docProps)
                {
                    // get property value
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(doc, propValue);
                }
            }

            // DocumentLineDefinitionEntries
            await reader.NextResultAsync();
            var lineDefEntriesProps = TypeDescriptor.Get<DocumentLineDefinitionEntryForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);
                var docIndex = reader.GetInt32(1);

                var lineDefinitionEntry = docs[docIndex].LineDefinitionEntries[index];

                foreach (var prop in lineDefEntriesProps)
                {
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(lineDefinitionEntry, propValue);
                }
            }

            // Lines
            await reader.NextResultAsync();
            var lineProps = TypeDescriptor.Get<LineForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);
                var docIndex = reader.GetInt32(1);

                var line = docs[docIndex].Lines[index];

                foreach (var prop in lineProps)
                {
                    // get property value
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(line, propValue);
                }
            }

            // Entries         
            await reader.NextResultAsync();
            var entryProps = TypeDescriptor.Get<EntryForSave>().SimpleProperties;
            while (await reader.ReadAsync())
            {
                var index = reader.GetInt32(0);
                var lineIndex = reader.GetInt32(1);
                var docIndex = reader.GetInt32(2);

                var entry = docs[docIndex].Lines[lineIndex].Entries[index];

                foreach (var prop in entryProps)
                {
                    // get property value
                    var propValue = reader[prop.Name];
                    propValue = propValue == DBNull.Value ? null : propValue;

                    prop.SetValue(entry, propValue);
                }
            }
        }

        public async Task<IEnumerable<ValidationError>> Documents_Validate__Save(int definitionId, List<DocumentForSave> documents, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            var (docsTable, lineDefinitionEntriesTable, linesTable, entriesTable, _) = RepositoryUtilities.DataTableFromDocuments(documents);

            var docsTvp = new SqlParameter("@Documents", docsTable)
            {
                TypeName = $"[dbo].[{nameof(Document)}List]",
                SqlDbType = SqlDbType.Structured
            };

            var lineDefinitionEntriesTvp = new SqlParameter("@DocumentLineDefinitionEntries", lineDefinitionEntriesTable)
            {
                TypeName = $"[dbo].[{nameof(DocumentLineDefinitionEntry)}List]",
                SqlDbType = SqlDbType.Structured
            };

            var linesTvp = new SqlParameter("@Lines", linesTable)
            {
                TypeName = $"[dbo].[{nameof(Line)}List]",
                SqlDbType = SqlDbType.Structured
            };

            var entriesTvp = new SqlParameter("@Entries", entriesTable)
            {
                TypeName = $"[dbo].[{nameof(Entry)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(docsTvp);
            cmd.Parameters.Add(lineDefinitionEntriesTvp);
            cmd.Parameters.Add(linesTvp);
            cmd.Parameters.Add(entriesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<(List<InboxNotificationInfo> NotificationInfos, List<string> DeletedFileIds, List<int> Ids)> Documents__SaveAndRefresh(int definitionId, List<DocumentForSave> documents, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__SaveAndRefresh));

            var deletedFileIds = new List<string>();
            var notificationInfos = new List<InboxNotificationInfo>();
            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                var (docsTable, lineDefinitionEntriesTable, linesTable, entriesTable, attachmentsTable) = RepositoryUtilities.DataTableFromDocuments(documents);

                var docsTvp = new SqlParameter("@Documents", docsTable)
                {
                    TypeName = $"[dbo].[{nameof(Document)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                var lineDefinitionEntriesTvp = new SqlParameter("@DocumentLineDefinitionEntries", lineDefinitionEntriesTable)
                {
                    TypeName = $"[dbo].[{nameof(DocumentLineDefinitionEntry)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                var linesTvp = new SqlParameter("@Lines", linesTable)
                {
                    TypeName = $"[dbo].[{nameof(Line)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                var entriesTvp = new SqlParameter("@Entries", entriesTable)
                {
                    TypeName = $"[dbo].[{nameof(Entry)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                var attachmentsTvp = new SqlParameter("@Attachments", attachmentsTable)
                {
                    TypeName = $"[dbo].[{nameof(Attachment)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add("@DefinitionId", definitionId);
                cmd.Parameters.Add(docsTvp);
                cmd.Parameters.Add(lineDefinitionEntriesTvp);
                cmd.Parameters.Add(linesTvp);
                cmd.Parameters.Add(entriesTvp);
                cmd.Parameters.Add(attachmentsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Documents__SaveAndRefresh)}]";

                // Execute
                using var reader = await cmd.ExecuteReaderAsync();

                // Get the assignments notifications infos
                await RepositoryUtilities.LoadAssignmentNotificationInfos(reader, notificationInfos);

                // Get the deleted file IDs
                await reader.NextResultAsync();
                while (await reader.ReadAsync())
                {
                    deletedFileIds.Add(reader.GetString(0));
                }

                // If requested, get the document Ids too
                if (returnIds)
                {
                    await reader.NextResultAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
            }

            // Return ordered result
            var sortedResult = new int[documents.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return (notificationInfos, deletedFileIds, sortedResult.ToList());
        }

        public async Task<IEnumerable<ValidationError>> Lines_Validate__Sign(List<int> ids, int? onBehalfOfUserId, string ruleType, int? roleId, short toState, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lines_Validate__Sign));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@OnBehalfOfuserId", onBehalfOfUserId);
            cmd.Parameters.Add("@RuleType", ruleType);
            cmd.Parameters.Add("@RoleId", roleId);
            cmd.Parameters.Add("@ToState", toState);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Lines_Validate__Sign)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<IEnumerable<int>> Lines__SignAndRefresh(IEnumerable<int> ids, short toState, int? reasonId, string reasonDetails, int? onBehalfOfUserId, string ruleType, int? roleId, DateTimeOffset? signedAt, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lines__SignAndRefresh));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
                var idsTvp = new SqlParameter("@Ids", idsTable)
                {
                    TypeName = $"[dbo].[IdList]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(idsTvp);
                cmd.Parameters.Add("@ToState", toState);
                cmd.Parameters.Add("@ReasonId", reasonId);
                cmd.Parameters.Add("@ReasonDetails", reasonDetails);
                cmd.Parameters.Add("@OnBehalfOfUserId", onBehalfOfUserId);
                cmd.Parameters.Add("@RuleType", ruleType);
                cmd.Parameters.Add("@RoleId", roleId);
                cmd.Parameters.Add("@SignedAt", signedAt);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(Lines__SignAndRefresh)}]";

                // Execute                    
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    result.Add(reader.GetInt32(0));
                }
            }

            return result;
        }

        public async Task<IEnumerable<ValidationError>> LineSignatures_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LineSignatures_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(LineSignatures_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<IEnumerable<int>> LineSignatures__DeleteAndRefresh(IEnumerable<int> ids, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LineSignatures__DeleteAndRefresh));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
                var idsTvp = new SqlParameter("@Ids", idsTable)
                {
                    TypeName = $"[dbo].[IdList]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(idsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(LineSignatures__DeleteAndRefresh)}]";

                // Execute     
                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        result.Add(reader.GetInt32(0));
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            return result;
        }

        public async Task<IEnumerable<ValidationError>> Documents_Validate__Assign(IEnumerable<int> ids, int assigneeId, string comment, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents_Validate__Assign));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@AssigneeId", assigneeId);
            cmd.Parameters.Add("@Comment", comment);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents_Validate__Assign)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<(List<InboxNotificationInfo> notificationInfos, User assigneeInfo, int docSerial)> Documents__Assign(IEnumerable<int> ids, int assigneeId, string comment, bool manualAssignment)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Assign));

            List<InboxNotificationInfo> notificationInfos;
            User assigneeInfo;
            int serialNumber = 0;

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@AssigneeId", assigneeId);
            cmd.Parameters.Add("@Comment", comment);
            cmd.Parameters.Add("@ManualAssignment", manualAssignment);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Documents__Assign)}]";

            // Execute                    
            using var reader = await cmd.ExecuteReaderAsync();
            notificationInfos = await RepositoryUtilities.LoadAssignmentNotificationInfos(reader);

            if (manualAssignment)
            {
                await reader.NextResultAsync();
                if (await reader.ReadAsync())
                {
                    int i = 0;

                    assigneeInfo = new User
                    {
                        Name = reader.String(i++),
                        Name2 = reader.String(i++),
                        Name3 = reader.String(i++),
                        PreferredLanguage = reader.String(i++),
                        ContactEmail = reader.String(i++),
                        ContactMobile = reader.String(i++),
                        NormalizedContactMobile = reader.String(i++),
                        PushEndpoint = reader.String(i++),
                        PushP256dh = reader.String(i++),
                        PushAuth = reader.String(i++),
                        PreferredChannel = reader.String(i++),
                        EmailNewInboxItem = reader.Boolean(i++),
                        SmsNewInboxItem = reader.Boolean(i++),
                        PushNewInboxItem = reader.Boolean(i++),

                        EntityMetadata = new EntityMetadata {
                            { nameof(User.Name), FieldMetadata.Loaded },
                            { nameof(User.Name2), FieldMetadata.Loaded },
                            { nameof(User.Name3), FieldMetadata.Loaded },
                            { nameof(User.PreferredLanguage), FieldMetadata.Loaded },
                            { nameof(User.ContactEmail), FieldMetadata.Loaded },
                            { nameof(User.ContactMobile), FieldMetadata.Loaded },
                            { nameof(User.NormalizedContactMobile), FieldMetadata.Loaded },
                            { nameof(User.PushEndpoint), FieldMetadata.Loaded },
                            { nameof(User.PushP256dh), FieldMetadata.Loaded },
                            { nameof(User.PushAuth), FieldMetadata.Loaded },
                            { nameof(User.PreferredChannel), FieldMetadata.Loaded },
                            { nameof(User.EmailNewInboxItem), FieldMetadata.Loaded },
                            { nameof(User.SmsNewInboxItem), FieldMetadata.Loaded },
                            { nameof(User.PushNewInboxItem), FieldMetadata.Loaded },
                        }
                    };

                    serialNumber = reader.Int32(i++) ?? 0;
                }
                else
                {
                    // Just in case
                    throw new InvalidOperationException($"[Bug] Stored Procedure {nameof(Documents__Assign)} did not return assignee info.");
                }
            }
            else
            {
                assigneeInfo = null;
            }


            return (notificationInfos, assigneeInfo, serialNumber);
        }

        public async Task<(List<InboxNotificationInfo> NotificationInfos, List<string> DeletedFileIds)> Documents__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Delete));

            // Returns the new notifification counts of affected users, and the list of File Ids to be deleted
            var notificationInfos = new List<InboxNotificationInfo>();
            var deletedFileIds = new List<string>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Documents__Delete)}]";

            // Execute
            try
            {
                using var reader = await cmd.ExecuteReaderAsync();

                // Load notification infos
                await RepositoryUtilities.LoadAssignmentNotificationInfos(reader, notificationInfos);

                // Load deleted file Ids
                await reader.NextResultAsync();
                while (await reader.ReadAsync())
                {
                    deletedFileIds.Add(reader.String(0));
                }
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }

            return (notificationInfos, deletedFileIds);
        }

        public async Task<IEnumerable<ValidationError>> Documents_Validate__Delete(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        // Posting State Management

        public async Task<IEnumerable<ValidationError>> Documents_Validate__Close(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents_Validate__Close));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents_Validate__Close)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<InboxNotificationInfo>> Documents__Close(List<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Close));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Documents__Close)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();
            return await RepositoryUtilities.LoadAssignmentNotificationInfos(reader);
        }

        public async Task<IEnumerable<ValidationError>> Documents_Validate__Open(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents_Validate__Open));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents_Validate__Open)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<InboxNotificationInfo>> Documents__Open(List<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Open));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Documents__Open)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();
            return await RepositoryUtilities.LoadAssignmentNotificationInfos(reader);
        }

        public async Task<IEnumerable<ValidationError>> Documents_Validate__Cancel(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents_Validate__Cancel));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents_Validate__Cancel)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<InboxNotificationInfo>> Documents__Cancel(List<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Cancel));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Documents__Cancel)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();
            return await RepositoryUtilities.LoadAssignmentNotificationInfos(reader);
        }

        public async Task<IEnumerable<ValidationError>> Documents_Validate__Uncancel(int definitionId, List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents_Validate__Uncancel));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@DefinitionId", definitionId);
            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Documents_Validate__Uncancel)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<InboxNotificationInfo>> Documents__Uncancel(List<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Uncancel));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Documents__Uncancel)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();
            return await RepositoryUtilities.LoadAssignmentNotificationInfos(reader);
        }

        public async Task<List<InboxNotificationInfo>> Documents__Preview(int documentId, DateTimeOffset createdAt, DateTimeOffset openedAt)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Documents__Preview));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            cmd.Parameters.Add("@DocumentId", documentId);
            cmd.Parameters.Add("@CreatedAt", createdAt);
            cmd.Parameters.Add("@OpenedAt", openedAt);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Documents__Preview)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();
            return await RepositoryUtilities.LoadAssignmentNotificationInfos(reader);
        }

        #endregion

        #region Lines

        public async Task<(
            List<LineForSave> lines,
            List<Account> accounts,
            List<Custody> custodies,
            List<Resource> resources,
            List<Relation> relations,
            List<EntryType> entryTypes,
            List<Center> centers,
            List<Currency> currencies,
            List<Unit> units
            )> Lines__Generate(int lineDefId, Dictionary<string, string> args, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Lines__Generate));

            List<LineForSave> lines = new List<LineForSave>();

            // Prepare SQL command
            var conn = await GetConnectionAsync(cancellation);
            using var cmd = conn.CreateCommand();

            // Add params
            DataTable argsTable = RepositoryUtilities.DataTable(args.Select(e => new GenerateArgument { Key = e.Key, Value = e.Value }));
            var argsTvp = new SqlParameter("@GenerateArguments", argsTable)
            {
                TypeName = $"[dbo].[{nameof(GenerateArgument)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add("@LineDefinitionId", lineDefId);
            cmd.Parameters.Add(argsTvp);

            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Lines__Generate)}]";

            // Lines for save
            using var reader = await cmd.ExecuteReaderAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                lines.Add(new LineForSave
                {
                    DefinitionId = reader.Int32(i++),
                    PostingDate = reader.DateTime(i++),
                    Memo = reader.String(i++),
                    Boolean1 = reader.Boolean(i++),
                    Decimal1 = reader.Decimal(i++),
                    Text1 = reader.String(i++),

                    Entries = new List<EntryForSave>(),
                });

                int index = reader.Int32(i++) ?? throw new InvalidOperationException("Returned line [Index] was null");
                if (lines.Count != index + 1)
                {
                    throw new InvalidOperationException($"Mismatch between line index {index} and it's actual position {lines.Count - 1} in the returned result set");
                }
            }

            // Entries for save
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                var entry = new EntryForSave
                {
                    AccountId = reader.Int32(i++),
                    CurrencyId = reader.String(i++),
                    CustodianId = reader.Int32(i++),
                    CustodyId = reader.Int32(i++),
                    ParticipantId = reader.Int32(i++),
                    ResourceId = reader.Int32(i++),
                    EntryTypeId = reader.Int32(i++),
                    CenterId = reader.Int32(i++),
                    UnitId = reader.Int32(i++),
                    IsSystem = reader.Boolean(i++) ?? false,
                    Direction = reader.Int16(i++),
                    MonetaryValue = reader.Decimal(i++),
                    Quantity = reader.Decimal(i++),
                    Value = reader.Decimal(i++) ?? 0m,
                    Time1 = reader.DateTime(i++),
                    Time2 = reader.DateTime(i++),
                    ExternalReference = reader.String(i++),
                    InternalReference = reader.String(i++),
                    NotedAgentName = reader.String(i++),
                    NotedAmount = reader.Decimal(i++),
                    NotedDate = reader.DateTime(i++),
                };

                int lineIndex = reader.Int32(i++) ?? throw new InvalidOperationException("Returned entry [Index] was null");
                if (lineIndex >= lines.Count)
                {
                    throw new InvalidOperationException($"Entry's [LineIndex] = {lineIndex} is not valid, only {lines.Count} were loaded");
                }

                var line = lines[lineIndex];
                line.Entries.Add(entry);
            }

            // Account
            var list_Account = new List<Account>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_Account.Add(new Account
                {
                    Id = reader.GetInt32(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),
                    Code = reader.String(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(Account.Name), FieldMetadata.Loaded },
                        { nameof(Account.Name2), FieldMetadata.Loaded },
                        { nameof(Account.Name3), FieldMetadata.Loaded },
                        { nameof(Account.Code), FieldMetadata.Loaded },
                    }
                });
            }

            // Currency
            var list_Currency = new List<Currency>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_Currency.Add(new Currency
                {
                    Id = reader.GetString(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),
                    E = reader.Int16(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(Currency.Name), FieldMetadata.Loaded },
                        { nameof(Currency.Name2), FieldMetadata.Loaded },
                        { nameof(Currency.Name3), FieldMetadata.Loaded },
                        { nameof(Currency.E), FieldMetadata.Loaded },
                    }
                });
            }

            // Custody
            var list_Custody = new List<Custody>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_Custody.Add(new Custody
                {
                    Id = reader.GetInt32(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),
                    DefinitionId = reader.Int32(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(Custody.Name), FieldMetadata.Loaded },
                        { nameof(Custody.Name2), FieldMetadata.Loaded },
                        { nameof(Custody.Name3), FieldMetadata.Loaded },
                        { nameof(Custody.DefinitionId), FieldMetadata.Loaded },
                    }
                });
            }

            // Resource
            var list_Resource = new List<Resource>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_Resource.Add(new Resource
                {
                    Id = reader.GetInt32(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),
                    DefinitionId = reader.Int32(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(Resource.Name), FieldMetadata.Loaded },
                        { nameof(Resource.Name2), FieldMetadata.Loaded },
                        { nameof(Resource.Name3), FieldMetadata.Loaded },
                        { nameof(Resource.DefinitionId), FieldMetadata.Loaded },
                    }
                });
            }

            // Relation
            var list_Relation = new List<Relation>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_Relation.Add(new Relation
                {
                    Id = reader.GetInt32(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),
                    DefinitionId = reader.Int32(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(Relation.Name), FieldMetadata.Loaded },
                        { nameof(Relation.Name2), FieldMetadata.Loaded },
                        { nameof(Relation.Name3), FieldMetadata.Loaded },
                        { nameof(Relation.DefinitionId), FieldMetadata.Loaded },
                    }
                });
            }

            // EntryType
            var list_EntryType = new List<EntryType>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_EntryType.Add(new EntryType
                {
                    Id = reader.GetInt32(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(EntryType.Name), FieldMetadata.Loaded },
                        { nameof(EntryType.Name2), FieldMetadata.Loaded },
                        { nameof(EntryType.Name3), FieldMetadata.Loaded },
                    }
                });
            }


            // Center
            var list_Center = new List<Center>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_Center.Add(new Center
                {
                    Id = reader.GetInt32(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(Center.Name), FieldMetadata.Loaded },
                        { nameof(Center.Name2), FieldMetadata.Loaded },
                        { nameof(Center.Name3), FieldMetadata.Loaded },
                    }
                });
            }

            // Unit
            var list_Unit = new List<Unit>();
            await reader.NextResultAsync(cancellation);
            while (await reader.ReadAsync(cancellation))
            {
                int i = 0;
                list_Unit.Add(new Unit
                {
                    Id = reader.GetInt32(i++),
                    Name = reader.String(i++),
                    Name2 = reader.String(i++),
                    Name3 = reader.String(i++),

                    EntityMetadata = new EntityMetadata
                    {
                        { nameof(Unit.Name), FieldMetadata.Loaded },
                        { nameof(Unit.Name2), FieldMetadata.Loaded },
                        { nameof(Unit.Name3), FieldMetadata.Loaded },
                    }
                });
            }

            return (lines, list_Account, list_Custody, list_Resource, list_Relation, list_EntryType, list_Center, list_Currency, list_Unit);
        }

        #endregion

        #region ReportDefinitions

        public async Task<IEnumerable<ValidationError>> ReportDefinitions_Validate__Save(List<ReportDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ReportDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable parametersTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Parameters);
            var parametersTvp = new SqlParameter("@Parameters", parametersTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionParameter)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable selectTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Select);
            var selectTvp = new SqlParameter("@Select", selectTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionSelect)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable rowsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Rows);
            var rowsTvp = new SqlParameter("@Rows", rowsTable)
            {
                TypeName = $"[dbo].[ReportDefinitionDimensionList]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable columnsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Columns);
            var columnsTvp = new SqlParameter("@Columns", columnsTable)
            {
                TypeName = $"[dbo].[ReportDefinitionDimensionList]",
                SqlDbType = SqlDbType.Structured
            };

            var (rowsAttributesTable, colsAttributesTable) = RepositoryUtilities.DataTableFromReportDefinitionDimensionAttributes(entities);
            var rowsAttributesTvp = new SqlParameter("@RowsAttributes", rowsAttributesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionDimensionAttribute)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var columnsAttributesTvp = new SqlParameter("@ColumnsAttributes", colsAttributesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionDimensionAttribute)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable measuresTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Measures);
            var measuresTvp = new SqlParameter("@Measures", measuresTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionMeasure)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable rolesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Roles);
            var rolesTvp = new SqlParameter("@Roles", rolesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionRole)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(parametersTvp);
            cmd.Parameters.Add(selectTvp);
            cmd.Parameters.Add(rowsTvp);
            cmd.Parameters.Add(rowsAttributesTvp);
            cmd.Parameters.Add(columnsTvp);
            cmd.Parameters.Add(columnsAttributesTvp);
            cmd.Parameters.Add(measuresTvp);
            cmd.Parameters.Add(rolesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(ReportDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> ReportDefinitions__Save(List<ReportDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ReportDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable parametersTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Parameters);
            var parametersTvp = new SqlParameter("@Parameters", parametersTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionParameter)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable selectTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Select);
            var selectTvp = new SqlParameter("@Select", selectTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionSelect)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable rowsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Rows);
            var rowsTvp = new SqlParameter("@Rows", rowsTable)
            {
                TypeName = $"[dbo].[ReportDefinitionDimensionList]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable columnsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Columns);
            var columnsTvp = new SqlParameter("@Columns", columnsTable)
            {
                TypeName = $"[dbo].[ReportDefinitionDimensionList]",
                SqlDbType = SqlDbType.Structured
            };

            var (rowsAttributesTable, colsAttributesTable) = RepositoryUtilities.DataTableFromReportDefinitionDimensionAttributes(entities);
            var rowsAttributesTvp = new SqlParameter("@RowsAttributes", rowsAttributesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionDimensionAttribute)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var columnsAttributesTvp = new SqlParameter("@ColumnsAttributes", colsAttributesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionDimensionAttribute)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable measuresTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Measures);
            var measuresTvp = new SqlParameter("@Measures", measuresTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionMeasure)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable rolesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Roles);
            var rolesTvp = new SqlParameter("@Roles", rolesTable)
            {
                TypeName = $"[dbo].[{nameof(ReportDefinitionRole)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(parametersTvp);
            cmd.Parameters.Add(selectTvp);
            cmd.Parameters.Add(rowsTvp);
            cmd.Parameters.Add(rowsAttributesTvp);
            cmd.Parameters.Add(columnsTvp);
            cmd.Parameters.Add(columnsAttributesTvp);
            cmd.Parameters.Add(measuresTvp);
            cmd.Parameters.Add(rolesTvp);
            cmd.Parameters.Add("@ReturnIds", returnIds);

            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(ReportDefinitions__Save)}]";

            if (returnIds)
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int i = 0;
                    result.Add(new IndexedId
                    {
                        Index = reader.GetInt32(i++),
                        Id = reader.GetInt32(i++)
                    });
                }
            }
            else
            {
                await cmd.ExecuteNonQueryAsync();
            }

            // Return ordered result
            if (returnIds)
            {
                var sortedResult = new int[entities.Count];
                result.ForEach(e =>
                {
                    sortedResult[e.Index] = e.Id;
                });

                return sortedResult.ToList();
            }
            else
            {
                return new List<int>();
            }
        }

        public async Task<IEnumerable<ValidationError>> ReportDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ReportDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(ReportDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task ReportDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ReportDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(ReportDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region ExchangeRates

        public async Task<IEnumerable<ValidationError>> ExchangeRates_Validate__Save(List<ExchangeRateForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ExchangeRates_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(ExchangeRate)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(ExchangeRates_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> ExchangeRates__Save(List<ExchangeRateForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ExchangeRates__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(ExchangeRate)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(ExchangeRates__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> ExchangeRates_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ExchangeRates_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(ExchangeRates_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task ExchangeRates__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ExchangeRates__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(ExchangeRates__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<decimal?> ConvertToFunctional(DateTime date, string currencyId, decimal amount, CancellationToken cancellation)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ConvertToFunctional));

            decimal? result = null;
            var conn = await GetConnectionAsync(cancellation);
            using (var cmd = conn.CreateCommand())
            {
                // Parameters
                cmd.Parameters.Add("@Date", date);
                cmd.Parameters.Add("@CurrencyId", currencyId);
                cmd.Parameters.Add("@Amount", amount);

                // Output Parameter
                SqlParameter resultParam = new SqlParameter("@Result", SqlDbType.Decimal)
                {
                    Direction = ParameterDirection.ReturnValue
                };

                cmd.Parameters.Add(resultParam);

                // Command
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[wiz].[fn_{nameof(ConvertToFunctional)}]";

                // Execute
                await cmd.ExecuteNonQueryAsync(cancellation);
                var resultObject = cmd.Parameters["@Result"].Value;
                if (resultObject != DBNull.Value)
                {
                    result = (decimal)resultObject;
                }
            }

            return result;
        }

        #endregion

        #region Inbox

        public async Task<List<InboxNotificationInfo>> Inbox__Check(DateTimeOffset now)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(Inbox__Check));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            cmd.Parameters.Add("@Now", now);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Inbox__Check)}]";

            // Execute
            using var reader = await cmd.ExecuteReaderAsync();
            return await RepositoryUtilities.LoadAssignmentNotificationInfos(reader);
        }

        #endregion

        #region MarkupTemplates

        public async Task<IEnumerable<ValidationError>> MarkupTemplates_Validate__Save(List<MarkupTemplateForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(MarkupTemplates_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(MarkupTemplate)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(MarkupTemplates_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> MarkupTemplates__Save(List<MarkupTemplateForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(MarkupTemplates__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(MarkupTemplate)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(MarkupTemplates__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> MarkupTemplates_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(MarkupTemplates_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(MarkupTemplates_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task MarkupTemplates__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(MarkupTemplates__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(MarkupTemplates__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region ResourceDefinitions

        public async Task<IEnumerable<ValidationError>> ResourceDefinitions_Validate__Save(List<ResourceDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ResourceDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(ResourceDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
            var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(ResourceDefinitionReportDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(reportDefinitionsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(ResourceDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> ResourceDefinitions__Save(List<ResourceDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ResourceDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(ResourceDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
                var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(ResourceDefinitionReportDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(reportDefinitionsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(ResourceDefinitions__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> ResourceDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ResourceDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(ResourceDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task ResourceDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ResourceDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(ResourceDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> ResourceDefinitions_Validate__UpdateState(List<int> ids, string state, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ResourceDefinitions_Validate__UpdateState));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(ResourceDefinitions_Validate__UpdateState)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task ResourceDefinitions__UpdateState(List<int> ids, string state)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(ResourceDefinitions__UpdateState));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(ResourceDefinitions__UpdateState)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region LookupDefinitions

        public async Task<IEnumerable<ValidationError>> LookupDefinitions_Validate__Save(List<LookupDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LookupDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(LookupDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
            var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(LookupDefinitionReportDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(reportDefinitionsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(LookupDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> LookupDefinitions__Save(List<LookupDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LookupDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(LookupDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable reportDefinitionsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.ReportDefinitions);
                var reportDefinitionsTvp = new SqlParameter("@ReportDefinitions", reportDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(LookupDefinitionReportDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(reportDefinitionsTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(LookupDefinitions__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> LookupDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LookupDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(LookupDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task LookupDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LookupDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(LookupDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> LookupDefinitions_Validate__UpdateState(List<int> ids, string state, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LookupDefinitions_Validate__UpdateState));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(LookupDefinitions_Validate__UpdateState)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task LookupDefinitions__UpdateState(List<int> ids, string state)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LookupDefinitions__UpdateState));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(LookupDefinitions__UpdateState)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region LineDefinition

        public async Task<IEnumerable<ValidationError>> LineDefinitions_Validate__Save(List<LineDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LineDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Tables
            var (
                lineDefinitionsTable,
                lineDefinitionEntriesTable,
                lineDefinitionEntryCustodyDefinitionsTable,
                lineDefinitionEntryResourceDefinitionsTable,
                lineDefinitionColumnsTable,
                lineDefinitionGenerateParametersTable,
                lineDefinitionStateReasonsTable,
                workflowsTable,
                workflowSignaturesTable
                ) = RepositoryUtilities.DataTableFromLineDefinitions(entities);

            // TVPs
            var lineDefinitionsTvp = new SqlParameter("@Entities", lineDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(LineDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var lineDefinitionEntriesTvp = new SqlParameter("@LineDefinitionEntries", lineDefinitionEntriesTable)
            {
                TypeName = $"[dbo].[{nameof(LineDefinitionEntry)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var lineDefinitionEntryCustodyDefinitionsTvp = new SqlParameter("@LineDefinitionEntryCustodyDefinitions", lineDefinitionEntryCustodyDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(LineDefinitionEntryCustodyDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var lineDefinitionEntryResourceDefinitionsTvp = new SqlParameter("@LineDefinitionEntryResourceDefinitions", lineDefinitionEntryResourceDefinitionsTable)
            {
                TypeName = $"[dbo].[{nameof(LineDefinitionEntryResourceDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            var lineDefinitionColumnsTvp = new SqlParameter("@LineDefinitionColumns", lineDefinitionColumnsTable)
            {
                TypeName = $"[dbo].[{nameof(LineDefinitionColumn)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var lineDefinitionGenerateParametersTvp = new SqlParameter("@LineDefinitionGenerateParameters", lineDefinitionGenerateParametersTable)
            {
                TypeName = $"[dbo].[{nameof(LineDefinitionGenerateParameter)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var lineDefinitionStateReasonsTvp = new SqlParameter("@LineDefinitionStateReasons", lineDefinitionStateReasonsTable)
            {
                TypeName = $"[dbo].[{nameof(LineDefinitionStateReason)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var workflowsTvp = new SqlParameter("@Workflows", workflowsTable)
            {
                TypeName = $"[dbo].[{nameof(Workflow)}List]",
                SqlDbType = SqlDbType.Structured
            };
            var workflowSignaturesTvp = new SqlParameter("@WorkflowSignatures", workflowSignaturesTable)
            {
                TypeName = $"[dbo].[{nameof(WorkflowSignature)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(lineDefinitionsTvp);
            cmd.Parameters.Add(lineDefinitionEntriesTvp);
            cmd.Parameters.Add(lineDefinitionEntryCustodyDefinitionsTvp);
            cmd.Parameters.Add(lineDefinitionEntryResourceDefinitionsTvp);
            cmd.Parameters.Add(lineDefinitionColumnsTvp);
            cmd.Parameters.Add(lineDefinitionGenerateParametersTvp);
            cmd.Parameters.Add(lineDefinitionStateReasonsTvp);
            cmd.Parameters.Add(workflowsTvp);
            cmd.Parameters.Add(workflowSignaturesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(LineDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> LineDefinitions__Save(List<LineDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LineDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                // Tables
                var (
                    lineDefinitionsTable,
                    lineDefinitionEntriesTable,
                    lineDefinitionEntryCustodyDefinitionsTable,
                    lineDefinitionEntryResourceDefinitionsTable,
                    lineDefinitionColumnsTable,
                    lineDefinitionGenerateParametersTable,
                    lineDefinitionStateReasonsTable,
                    workflowsTable,
                    workflowSignaturesTable
                    ) = RepositoryUtilities.DataTableFromLineDefinitions(entities);

                // TVPs
                var lineDefinitionsTvp = new SqlParameter("@Entities", lineDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(LineDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var lineDefinitionEntriesTvp = new SqlParameter("@LineDefinitionEntries", lineDefinitionEntriesTable)
                {
                    TypeName = $"[dbo].[{nameof(LineDefinitionEntry)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var lineDefinitionEntryCustodyDefinitionsTvp = new SqlParameter("@LineDefinitionEntryCustodyDefinitions", lineDefinitionEntryCustodyDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(LineDefinitionEntryCustodyDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var lineDefinitionEntryResourceDefinitionsTvp = new SqlParameter("@LineDefinitionEntryResourceDefinitions", lineDefinitionEntryResourceDefinitionsTable)
                {
                    TypeName = $"[dbo].[{nameof(LineDefinitionEntryResourceDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var lineDefinitionColumnsTvp = new SqlParameter("@LineDefinitionColumns", lineDefinitionColumnsTable)
                {
                    TypeName = $"[dbo].[{nameof(LineDefinitionColumn)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var lineDefinitionGenerateParametersTvp = new SqlParameter("@LineDefinitionGenerateParameters", lineDefinitionGenerateParametersTable)
                {
                    TypeName = $"[dbo].[{nameof(LineDefinitionGenerateParameter)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var lineDefinitionStateReasonsTvp = new SqlParameter("@LineDefinitionStateReasons", lineDefinitionStateReasonsTable)
                {
                    TypeName = $"[dbo].[{nameof(LineDefinitionStateReason)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var workflowsTvp = new SqlParameter("@Workflows", workflowsTable)
                {
                    TypeName = $"[dbo].[{nameof(Workflow)}List]",
                    SqlDbType = SqlDbType.Structured
                };
                var workflowSignaturesTvp = new SqlParameter("@WorkflowSignatures", workflowSignaturesTable)
                {
                    TypeName = $"[dbo].[{nameof(WorkflowSignature)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(lineDefinitionsTvp);
                cmd.Parameters.Add(lineDefinitionEntriesTvp);
                cmd.Parameters.Add(lineDefinitionEntryCustodyDefinitionsTvp);
                cmd.Parameters.Add(lineDefinitionEntryResourceDefinitionsTvp);
                cmd.Parameters.Add(lineDefinitionColumnsTvp);
                cmd.Parameters.Add(lineDefinitionGenerateParametersTvp);
                cmd.Parameters.Add(lineDefinitionStateReasonsTvp);
                cmd.Parameters.Add(workflowsTvp);
                cmd.Parameters.Add(workflowSignaturesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                // Command

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(LineDefinitions__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> LineDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LineDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(LineDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task LineDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(LineDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(LineDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion

        #region DocumentDefinitions

        public async Task<IEnumerable<ValidationError>> DocumentDefinitions_Validate__Save(List<DocumentDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DocumentDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(DocumentDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable linesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.LineDefinitions);
            var linesTvp = new SqlParameter("@DocumentDefinitionLineDefinitions", linesTable)
            {
                TypeName = $"[dbo].[{nameof(DocumentDefinitionLineDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(linesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(DocumentDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> DocumentDefinitions__Save(List<DocumentDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DocumentDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using (var cmd = conn.CreateCommand())
            {
                DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
                var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
                {
                    TypeName = $"[dbo].[{nameof(DocumentDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                DataTable linesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.LineDefinitions);
                var linesTvp = new SqlParameter("@DocumentDefinitionLineDefinitions", linesTable)
                {
                    TypeName = $"[dbo].[{nameof(DocumentDefinitionLineDefinition)}List]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(entitiesTvp);
                cmd.Parameters.Add(linesTvp);
                cmd.Parameters.Add("@ReturnIds", returnIds);

                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = $"[dal].[{nameof(DocumentDefinitions__Save)}]";

                if (returnIds)
                {
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        int i = 0;
                        result.Add(new IndexedId
                        {
                            Index = reader.GetInt32(i++),
                            Id = reader.GetInt32(i++)
                        });
                    }
                }
                else
                {
                    await cmd.ExecuteNonQueryAsync();
                }
            }

            // Return ordered result
            var sortedResult = new int[entities.Count];
            result.ForEach(e =>
            {
                sortedResult[e.Index] = e.Id;
            });

            return sortedResult.ToList();
        }

        public async Task<IEnumerable<ValidationError>> DocumentDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DocumentDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(DocumentDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task DocumentDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DocumentDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(DocumentDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        public async Task<IEnumerable<ValidationError>> DocumentDefinitions_Validate__UpdateState(List<int> ids, string state, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DocumentDefinitions_Validate__UpdateState));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(DocumentDefinitions_Validate__UpdateState)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task DocumentDefinitions__UpdateState(List<int> ids, string state)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DocumentDefinitions__UpdateState));

            var result = new List<int>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@State", state);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(DocumentDefinitions__UpdateState)}]";

            // Execute
            await cmd.ExecuteNonQueryAsync();
        }

        #endregion

        #region Reconciliation

        public async Task<(
            decimal entriesBalance,
            decimal unreconciledEntriesBalance,
            decimal unreconciledExternalEntriesBalance,
            int unreconciledEntriesCount,
            int unreconciledExternalEntriesCount,
            List<EntryForReconciliation> entries,
            List<ExternalEntry>
            )> Reconciliation__Load_Unreconciled(int accountId, int custodyId, DateTime? asOfDate, int top, int skip, int topExternal, int skipExternal, CancellationToken cancellation)
        {
            using var _ = _instrumentation.Block("Repo." + nameof(Reconciliation__Load_Unreconciled));

            // Result variables
            var entries = new List<EntryForReconciliation>();
            var externalEntries = new List<ExternalEntry>();

            var conn = await GetConnectionAsync(cancellation);
            using var cmd = conn.CreateCommand();

            // Add parameters
            AddUnreconciledParamsInner(cmd, accountId, custodyId, asOfDate, top, skip, topExternal, skipExternal);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Reconciliation__Load_Unreconciled)}]";

            // Execute
            return await LoadUnreconciledInner(cmd);
        }

        public async Task<(
            int reconciledCount,
            List<Reconciliation> reconciliations
            )> Reconciliation__Load_Reconciled(int accountId, int custodyId, DateTime? fromDate, DateTime? toDate, decimal? fromAmount, decimal? toAmount, string externalReferenceContains, int top, int skip, CancellationToken cancellation)
        {
            using var _ = _instrumentation.Block("Repo." + nameof(Reconciliation__Load_Reconciled));

            // Connection
            var conn = await GetConnectionAsync(cancellation);
            using var cmd = conn.CreateCommand();

            // Add parameters
            AddReconciledParamsInner(cmd, accountId, custodyId, fromDate, toDate, fromAmount, toAmount, externalReferenceContains, top, skip);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Reconciliation__Load_Reconciled)}]";

            // Execute
            return await LoadReconciledInner(cmd, cancellation);
        }

        public async Task<IEnumerable<ValidationError>> Reconciliations_Validate__Save(int accountId, int custodyId, List<ExternalEntryForSave> externalEntriesForSave, List<ReconciliationForSave> reconciliations, int top)
        {
            using var _ = _instrumentation.Block("Repo." + nameof(Reconciliations_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            cmd.Parameters.Add("@AccountId", accountId);
            cmd.Parameters.Add("@CustodyId", custodyId);
            cmd.Parameters.Add("@Top", top);
            AddReconciliationsAndExternalEntries(cmd, externalEntriesForSave, reconciliations);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(Reconciliations_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<(
            decimal entriesBalance,
            decimal unreconciledEntriesBalance,
            decimal unreconciledExternalEntriesBalance,
            int unreconciledEntriesCount,
            int unreconciledExternalEntriesCount,
            List<EntryForReconciliation> entries,
            List<ExternalEntry> externalEntries
            )> Reconciliations__SaveAndLoad_Unreconciled(int accountId, int custodyId, List<ExternalEntryForSave> externalEntriesForSave, List<ReconciliationForSave> reconciliations, List<int> deletedExternalEntryIds, List<int> deletedReconciliationIds, DateTime? asOfDate, int top, int skip, int topExternal, int skipExternal)
        {
            using var _ = _instrumentation.Block("Repo." + nameof(Reconciliations__SaveAndLoad_Unreconciled));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Add parameters
            AddUnreconciledParamsInner(cmd, accountId, custodyId, asOfDate, top, skip, topExternal, skipExternal);
            AddReconciliationsAndExternalEntries(cmd, externalEntriesForSave, reconciliations, deletedExternalEntryIds, deletedReconciliationIds);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Reconciliations__SaveAndLoad_Unreconciled)}]";

            // Execute
            return await LoadUnreconciledInner(cmd);
        }

        public async Task<(
            int reconciledCount,
            List<Reconciliation> reconciliations
            )> Reconciliations__SaveAndLoad_Reconciled(int accountId, int custodyId, List<ExternalEntryForSave> externalEntriesForSave, List<ReconciliationForSave> reconciliations, List<int> deletedExternalEntryIds, List<int> deletedReconciliationIds, DateTime? fromDate, DateTime? toDate, decimal? fromAmount, decimal? toAmount, string externalReferenceContains, int top, int skip)
        {
            using var _ = _instrumentation.Block("Repo." + nameof(Reconciliations__SaveAndLoad_Reconciled));

            // Connection
            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Add parameters
            AddReconciledParamsInner(cmd, accountId, custodyId, fromDate, toDate, fromAmount, toAmount, externalReferenceContains, top, skip);
            AddReconciliationsAndExternalEntries(cmd, externalEntriesForSave, reconciliations, deletedExternalEntryIds, deletedReconciliationIds);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(Reconciliations__SaveAndLoad_Reconciled)}]";

            // Execute
            return await LoadReconciledInner(cmd);
        }

        #region Helpers

        private void AddReconciliationsAndExternalEntries(SqlCommand cmd, List<ExternalEntryForSave> externalEntriesForSave, List<ReconciliationForSave> reconciliations, List<int> deletedExternalEntryIds = null, List<int> deletedReconciliationIds = null)
        {
            // ExternalEntries
            DataTable externalEntriesTable = RepositoryUtilities.DataTable(externalEntriesForSave, addIndex: true);
            var externalEntriesTvp = new SqlParameter("@ExternalEntries", externalEntriesTable)
            {
                TypeName = $"[dbo].[{nameof(ExternalEntry)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(externalEntriesTvp);

            // Reconciliations
            DataTable reconciliationsTable = new DataTable();
            reconciliationsTable.Columns.Add(new DataColumn("Index", typeof(int)));
            for (int i = 0; i < reconciliations.Count; i++)
            {
                DataRow row = reconciliationsTable.NewRow();
                row["Index"] = i;
                reconciliationsTable.Rows.Add(row);
            }
            var reconciliationsTvp = new SqlParameter("@Reconciliations", reconciliationsTable)
            {
                TypeName = $"[dbo].[{nameof(Reconciliation)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(reconciliationsTvp);

            // ReconciliationEntries
            DataTable reconciliationEntriesTable = new DataTable();
            reconciliationEntriesTable.Columns.Add(new DataColumn("Index", typeof(int)));
            reconciliationEntriesTable.Columns.Add(new DataColumn("HeaderIndex", typeof(int)));
            reconciliationEntriesTable.Columns.Add(new DataColumn(nameof(ReconciliationEntryForSave.EntryId), typeof(int)));
            for (int i = 0; i < reconciliations.Count; i++)
            {
                var reconciliation = reconciliations[i];
                if (reconciliation != null && reconciliation.Entries != null)
                {
                    for (int j = 0; j < reconciliation.Entries.Count; j++)
                    {
                        var entry = reconciliation.Entries[j];
                        if (entry != null)
                        {
                            DataRow row = reconciliationEntriesTable.NewRow();
                            row["Index"] = j;
                            row["HeaderIndex"] = i;
                            row[nameof(ReconciliationEntryForSave.EntryId)] = entry.EntryId;
                            reconciliationEntriesTable.Rows.Add(row);
                        }
                    }
                }
            }
            var reconciliationEntriesTvp = new SqlParameter("@ReconciliationEntries", reconciliationEntriesTable)
            {
                TypeName = $"[dbo].[{nameof(ReconciliationEntry)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(reconciliationEntriesTvp);


            // ReconciliationExternalEntries
            DataTable reconciliationExternalEntriesTable = new DataTable();
            reconciliationExternalEntriesTable.Columns.Add(new DataColumn("Index", typeof(int)));
            reconciliationExternalEntriesTable.Columns.Add(new DataColumn("HeaderIndex", typeof(int)));
            reconciliationExternalEntriesTable.Columns.Add(new DataColumn(nameof(ReconciliationExternalEntryForSave.ExternalEntryIndex), typeof(int)));
            reconciliationExternalEntriesTable.Columns.Add(new DataColumn(nameof(ReconciliationExternalEntryForSave.ExternalEntryId), typeof(int)));
            for (int i = 0; i < reconciliations.Count; i++)
            {
                var reconciliation = reconciliations[i];
                if (reconciliation != null && reconciliation.ExternalEntries != null)
                {
                    for (int j = 0; j < reconciliation.ExternalEntries.Count; j++)
                    {
                        var exEntry = reconciliation.ExternalEntries[j];
                        if (exEntry != null)
                        {
                            DataRow row = reconciliationExternalEntriesTable.NewRow();
                            row["Index"] = j;
                            row["HeaderIndex"] = i;
                            row[nameof(ReconciliationExternalEntryForSave.ExternalEntryIndex)] = (object)exEntry.ExternalEntryIndex ?? DBNull.Value;
                            row[nameof(ReconciliationExternalEntryForSave.ExternalEntryId)] = (object)exEntry.ExternalEntryId ?? DBNull.Value;
                            reconciliationExternalEntriesTable.Rows.Add(row);
                        }
                    }
                }
            }
            var reconciliationExternalEntriesTvp = new SqlParameter("@ReconciliationExternalEntries", reconciliationExternalEntriesTable)
            {
                TypeName = $"[dbo].[{nameof(ReconciliationExternalEntry)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(reconciliationExternalEntriesTvp);

            // DeletedExternalEntryIds
            if (deletedExternalEntryIds != null) // Validate SP doesn't take this params
            {
                DataTable deletedExternalEntryIdsTable = RepositoryUtilities.DataTable(deletedExternalEntryIds.Select(e => new IdListItem { Id = e }));
                var deletedExternalEntryIdsTvp = new SqlParameter("@DeletedExternalEntryIds", deletedExternalEntryIdsTable)
                {
                    TypeName = $"[dbo].[IdList]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(deletedExternalEntryIdsTvp);
            }

            // DeletedReconciliationIds
            if (deletedReconciliationIds != null) // Validate SP doesn't take this params
            {
                DataTable deletedReconciliationIdsTable = RepositoryUtilities.DataTable(deletedReconciliationIds.Select(e => new IdListItem { Id = e }));
                var deletedReconciliationIdsTvp = new SqlParameter("@DeletedReconcilationIds", deletedReconciliationIdsTable)
                {
                    TypeName = $"[dbo].[IdList]",
                    SqlDbType = SqlDbType.Structured
                };

                cmd.Parameters.Add(deletedReconciliationIdsTvp);
            }
        }

        private void AddReconciledParamsInner(SqlCommand cmd, int accountId, int custodyId, DateTime? fromDate, DateTime? toDate, decimal? fromAmount, decimal? toAmount, string externalReferenceContains, int top, int skip)
        {
            cmd.Parameters.Add("@AccountId", accountId);
            cmd.Parameters.Add("@CustodyId", custodyId);
            cmd.Parameters.Add("@FromDate", fromDate);
            cmd.Parameters.Add("@ToDate", toDate);
            cmd.Parameters.Add("@FromAmount", fromAmount);
            cmd.Parameters.Add("@ToAmount", toAmount);
            cmd.Parameters.Add("@ExternalReferenceContains", externalReferenceContains);
            cmd.Parameters.Add("@Top", top);
            cmd.Parameters.Add("@Skip", skip);

            // Output parameters
            var reconciledCountParam = new SqlParameter("@ReconciledCount", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            // Parameters
            cmd.Parameters.Add(reconciledCountParam);
        }

        private async Task<(int reconciledCount, List<Reconciliation> reconciliations)> LoadReconciledInner(SqlCommand cmd, CancellationToken cancellation = default)
        {
            // Result variables
            var result = new List<Reconciliation>();

            using (var reader = await cmd.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    result.Add(new Reconciliation
                    {
                        Id = reader.GetInt32(i++),
                        CreatedAt = reader.GetDateTimeOffset(i++),
                        CreatedById = reader.Int32(i++),
                        Entries = new List<ReconciliationEntry>(),
                        ExternalEntries = new List<ReconciliationExternalEntry>(),
                    });
                }

                // Put the reconciliations in a dictionary for fast lookup
                var resultDic = result.ToDictionary(e => e.Id);

                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    int reconciliationId = reader.GetInt32(i++);

                    resultDic[reconciliationId].Entries.Add(new ReconciliationEntry
                    {
                        Id = reconciliationId,
                        EntryId = reader.GetInt32(i),
                        Entry = new EntryForReconciliation
                        {
                            Id = reader.GetInt32(i++),
                            PostingDate = reader.DateTime(i++),
                            Direction = reader.GetInt16(i++),
                            MonetaryValue = reader.Decimal(i++),
                            ExternalReference = reader.String(i++),
                            DocumentId = reader.GetInt32(i++),
                            DocumentDefinitionId = reader.GetInt32(i++),
                            DocumentSerialNumber = reader.GetInt32(i++),
                        }
                    });
                }


                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    int reconciliationId = reader.GetInt32(i++);

                    resultDic[reconciliationId].ExternalEntries.Add(new ReconciliationExternalEntry
                    {
                        Id = reconciliationId,
                        ExternalEntryId = reader.GetInt32(i),
                        ExternalEntry = new ExternalEntry
                        {
                            Id = reader.GetInt32(i++),
                            PostingDate = reader.DateTime(i++),
                            Direction = reader.GetInt16(i++),
                            MonetaryValue = reader.Decimal(i++),
                            ExternalReference = reader.String(i++)
                        }
                    });
                }
            }

            int reconciledCount = GetValue(cmd.Parameters["@ReconciledCount"].Value, 0);
            return (reconciledCount, result);
        }

        private void AddUnreconciledParamsInner(SqlCommand cmd, int accountId, int custodyId, DateTime? asOfDate, int top, int skip, int topExternal, int skipExternal)
        {
            // Add parameters
            cmd.Parameters.Add("@AccountId", accountId);
            cmd.Parameters.Add("@CustodyId", custodyId);
            cmd.Parameters.Add("@AsOfDate", asOfDate);
            cmd.Parameters.Add("@Top", top);
            cmd.Parameters.Add("@Skip", skip);
            cmd.Parameters.Add("@TopExternal", topExternal);
            cmd.Parameters.Add("@SkipExternal", skipExternal);

            // Output parameters
            var entriesBalanceParam = new SqlParameter("@EntriesBalance", SqlDbType.Decimal)
            {
                Direction = ParameterDirection.Output,
                Precision = 19,
                Scale = 4
            };
            var unreconciledEntriesBalanceParam = new SqlParameter("@UnreconciledEntriesBalance", SqlDbType.Decimal)
            {
                Direction = ParameterDirection.Output,
                Precision = 19,
                Scale = 4
            };
            var unreconciledExternalEntriesBalanceParam = new SqlParameter("@UnreconciledExternalEntriesBalance", SqlDbType.Decimal)
            {
                Direction = ParameterDirection.Output,
                Precision = 19,
                Scale = 4
            };
            var unreconciledEntriesCountParam = new SqlParameter("@UnreconciledEntriesCount", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };
            var unreconciledExternalEntriesCountParam = new SqlParameter("@UnreconciledExternalEntriesCount", SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            };

            // Parameters
            cmd.Parameters.Add(entriesBalanceParam);
            cmd.Parameters.Add(unreconciledEntriesBalanceParam);
            cmd.Parameters.Add(unreconciledExternalEntriesBalanceParam);
            cmd.Parameters.Add(unreconciledEntriesCountParam);
            cmd.Parameters.Add(unreconciledExternalEntriesCountParam);
        }

        private async Task<(decimal entriesBalance, decimal unreconciledEntriesBalance, decimal unreconciledExternalEntriesBalance, int unreconciledEntriesCount, int unreconciledExternalEntriesCount, List<EntryForReconciliation> entries, List<ExternalEntry>)> LoadUnreconciledInner(SqlCommand cmd, CancellationToken cancellation = default)
        {
            // Result variables
            var entries = new List<EntryForReconciliation>();
            var externalEntries = new List<ExternalEntry>();

            using (var reader = await cmd.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    entries.Add(new EntryForReconciliation
                    {
                        Id = reader.GetInt32(i++),
                        PostingDate = reader.DateTime(i++),
                        Direction = reader.GetInt16(i++),
                        MonetaryValue = reader.Decimal(i++),
                        ExternalReference = reader.String(i++),
                        DocumentId = reader.GetInt32(i++),
                        DocumentDefinitionId = reader.GetInt32(i++),
                        DocumentSerialNumber = reader.GetInt32(i++),
                        IsReconciledLater = reader.GetBoolean(i++),
                    });
                }

                await reader.NextResultAsync(cancellation);
                while (await reader.ReadAsync(cancellation))
                {
                    int i = 0;
                    externalEntries.Add(new ExternalEntry
                    {
                        Id = reader.GetInt32(i++),
                        PostingDate = reader.DateTime(i++),
                        Direction = reader.GetInt16(i++),
                        MonetaryValue = reader.Decimal(i++),
                        ExternalReference = reader.String(i++),
                        CreatedById = reader.Int32(i++),
                        CreatedAt = reader.GetDateTimeOffset(i++),
                        ModifiedById = reader.Int32(i++),
                        ModifiedAt = reader.GetDateTimeOffset(i++),
                        IsReconciledLater = reader.GetBoolean(i++),
                    });
                }
            }

            decimal entriesBalance = GetValue(cmd.Parameters["@EntriesBalance"].Value, 0m);
            decimal unreconciledEntriesBalance = GetValue(cmd.Parameters["@UnreconciledEntriesBalance"].Value, 0m);
            decimal unreconciledExternalEntriesBalance = GetValue(cmd.Parameters["@UnreconciledExternalEntriesBalance"].Value, 0m);
            int unreconciledEntriesCount = GetValue(cmd.Parameters["@UnreconciledEntriesCount"].Value, 0);
            int unreconciledExternalEntriesCount = GetValue(cmd.Parameters["@UnreconciledExternalEntriesCount"].Value, 0);

            return (entriesBalance, unreconciledEntriesBalance, unreconciledExternalEntriesBalance, unreconciledEntriesCount, unreconciledExternalEntriesCount, entries, externalEntries);
        }

        /// <summary>
        /// Utility function: if obj is <see cref="DBNull.Value"/>, returns the default value of the type, else returns cast value
        /// </summary>
        private T GetValue<T>(object obj, T defaultValue = default)
        {
            if (obj == DBNull.Value)
            {
                return defaultValue;
            }
            else
            {
                return (T)obj;
            }
        }

        #endregion

        #endregion

        #region DashboardDefinitions

        public async Task<IEnumerable<ValidationError>> DashboardDefinitions_Validate__Save(List<DashboardDefinitionForSave> entities, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DashboardDefinitions_Validate__Save));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(DashboardDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable widgetsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Widgets);
            var widgetsTvp = new SqlParameter("@Widgets", widgetsTable)
            {
                TypeName = $"[dbo].[{nameof(DashboardDefinitionWidget)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable rolesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Roles);
            var rolesTvp = new SqlParameter("@Roles", rolesTable)
            {
                TypeName = $"[dbo].[{nameof(DashboardDefinitionRole)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(widgetsTvp);
            cmd.Parameters.Add(rolesTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(DashboardDefinitions_Validate__Save)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task<List<int>> DashboardDefinitions__Save(List<DashboardDefinitionForSave> entities, bool returnIds)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DashboardDefinitions__Save));

            var result = new List<IndexedId>();

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();

            // Parameters
            DataTable entitiesTable = RepositoryUtilities.DataTable(entities, addIndex: true);
            var entitiesTvp = new SqlParameter("@Entities", entitiesTable)
            {
                TypeName = $"[dbo].[{nameof(DashboardDefinition)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable widgetsTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Widgets);
            var widgetsTvp = new SqlParameter("@Widgets", widgetsTable)
            {
                TypeName = $"[dbo].[{nameof(DashboardDefinitionWidget)}List]",
                SqlDbType = SqlDbType.Structured
            };

            DataTable rolesTable = RepositoryUtilities.DataTableWithHeaderIndex(entities, e => e.Roles);
            var rolesTvp = new SqlParameter("@Roles", rolesTable)
            {
                TypeName = $"[dbo].[{nameof(DashboardDefinitionRole)}List]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(entitiesTvp);
            cmd.Parameters.Add(widgetsTvp);
            cmd.Parameters.Add(rolesTvp);
            cmd.Parameters.Add("@ReturnIds", returnIds);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(DashboardDefinitions__Save)}]";

            if (returnIds)
            {
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int i = 0;
                    result.Add(new IndexedId
                    {
                        Index = reader.GetInt32(i++),
                        Id = reader.GetInt32(i++)
                    });
                }
            }
            else
            {
                await cmd.ExecuteNonQueryAsync();
            }

            // Return ordered result
            if (returnIds)
            {
                var sortedResult = new int[entities.Count];
                result.ForEach(e =>
                {
                    sortedResult[e.Index] = e.Id;
                });

                return sortedResult.ToList();
            }
            else
            {
                return new List<int>();
            }
        }

        public async Task<IEnumerable<ValidationError>> DashboardDefinitions_Validate__Delete(List<int> ids, int top)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DashboardDefinitions_Validate__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }), addIndex: true);
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IndexedIdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);
            cmd.Parameters.Add("@Top", top);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[bll].[{nameof(DashboardDefinitions_Validate__Delete)}]";

            // Execute
            return await RepositoryUtilities.LoadErrors(cmd);
        }

        public async Task DashboardDefinitions__Delete(IEnumerable<int> ids)
        {
            using var _ = Instrumentation.Block("Repo." + nameof(DashboardDefinitions__Delete));

            var conn = await GetConnectionAsync();
            using var cmd = conn.CreateCommand();
            // Parameters
            DataTable idsTable = RepositoryUtilities.DataTable(ids.Select(id => new IdListItem { Id = id }));
            var idsTvp = new SqlParameter("@Ids", idsTable)
            {
                TypeName = $"[dbo].[IdList]",
                SqlDbType = SqlDbType.Structured
            };

            cmd.Parameters.Add(idsTvp);

            // Command
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = $"[dal].[{nameof(DashboardDefinitions__Delete)}]";

            // Execute
            try
            {
                await cmd.ExecuteNonQueryAsync();
            }
            catch (SqlException ex) when (RepositoryUtilities.IsForeignKeyViolation(ex))
            {
                throw new ForeignKeyViolationException();
            }
        }

        #endregion
    }
}
