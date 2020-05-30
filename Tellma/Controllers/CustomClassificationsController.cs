﻿using Tellma.Controllers.Dto;
using Tellma.Controllers.Utilities;
using Tellma.Data;
using Tellma.Data.Queries;
using Tellma.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Tellma.Controllers
{
    [Route("api/" + BASE_ADDRESS)]
    [ApplicationController]
    public class CustomClassificationsController : CrudTreeControllerBase<CustomClassificationForSave, CustomClassification, int>
    {
        public const string BASE_ADDRESS = "custom-classifications";

        private readonly CustomClassificationsService _service;
        private readonly ILogger _logger;

        public CustomClassificationsController(CustomClassificationsService service, ILogger<CustomClassificationsController> logger) : base(logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpPut("activate")]
        public async Task<ActionResult<EntitiesResponse<CustomClassification>>> Activate([FromBody] List<int> ids, [FromQuery] ActivateArguments args)
        {
            return await ControllerUtilities.InvokeActionImpl(async () =>
            {
                var serverTime = DateTimeOffset.UtcNow;
                var (data, extras) = await _service.Activate(ids: ids, args);
                var response = TransformToEntitiesResponse(data, extras, serverTime, cancellation: default);
                return Ok(response);
            }, _logger);
        }

        [HttpPut("deactivate")]
        public async Task<ActionResult<EntitiesResponse<CustomClassification>>> Deprecate([FromBody] List<int> ids, [FromQuery] DeactivateArguments args)
        {
            return await ControllerUtilities.InvokeActionImpl(async () =>
            {
                var serverTime = DateTimeOffset.UtcNow;
                var (data, extras) = await _service.Deprecate(ids: ids, args);
                var response = TransformToEntitiesResponse(data, extras, serverTime, cancellation: default);
                return Ok(response);
            }, _logger);
        }

        protected override CrudTreeServiceBase<CustomClassificationForSave, CustomClassification, int> GetCrudTreeService()
        {
            return _service;
        }
    }

    public class CustomClassificationsService : CrudTreeServiceBase<CustomClassificationForSave, CustomClassification, int>
    {
        private readonly IStringLocalizer _localizer;
        private readonly ApplicationRepository _repo;

        private string View => CustomClassificationsController.BASE_ADDRESS;

        public CustomClassificationsService(IStringLocalizer<Strings> localizer, ApplicationRepository repo, IServiceProvider sp) : base(sp)
        {
            _localizer = localizer;
            _repo = repo;
        }

        protected override async Task<IEnumerable<AbstractPermission>> UserPermissions(string action, CancellationToken cancellation)
        {
            return await _repo.UserPermissions(action, View, cancellation);
        }

        protected override IRepository GetRepository()
        {
            return _repo;
        }

        protected override Query<CustomClassification> Search(Query<CustomClassification> query, GetArguments args, IEnumerable<AbstractPermission> filteredPermissions)
        {
            string search = args.Search;
            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.Replace("'", "''"); // escape quotes by repeating them

                var name = nameof(CustomClassification.Name);
                var name2 = nameof(CustomClassification.Name2);
                var name3 = nameof(CustomClassification.Name3);
                var code = nameof(CustomClassification.Code);

                var filterString = $"{name} {Ops.contains} '{search}' or {name2} {Ops.contains} '{search}' or {name3} {Ops.contains} '{search}' or {code} {Ops.contains} '{search}'";
                query = query.Filter(FilterExpression.Parse(filterString));
            }

            return query;
        }

        protected override async Task SaveValidateAsync(List<CustomClassificationForSave> entities)
        {
            // SQL validation
            int remainingErrorCount = ModelState.MaxAllowedErrors - ModelState.ErrorCount;
            var sqlErrors = await _repo.CustomClassifications_Validate__Save(entities, top: remainingErrorCount);

            // Add errors to model state
            ModelState.AddLocalizedErrors(sqlErrors, _localizer);
        }

        protected override async Task<List<int>> SaveExecuteAsync(List<CustomClassificationForSave> entities, bool returnIds)
        {
            return await _repo.CustomClassifications__Save(entities, returnIds);
        }

        protected override async Task DeleteValidateAsync(List<int> ids)
        {
            // SQL validation
            int remainingErrorCount = ModelState.MaxAllowedErrors - ModelState.ErrorCount;
            var sqlErrors = await _repo.CustomClassifications_Validate__Delete(ids, top: remainingErrorCount);

            // Add errors to model state
            ModelState.AddLocalizedErrors(sqlErrors, _localizer);
        }

        protected override async Task DeleteExecuteAsync(List<int> ids)
        {
            try
            {
                await _repo.CustomClassifications__Delete(ids);
            }
            catch (ForeignKeyViolationException)
            {
                throw new BadRequestException(_localizer["Error_CannotDelete0AlreadyInUse", _localizer["CustomClassification"]]);
            }
        }

        protected override async Task ValidateDeleteWithDescendantsAsync(List<int> ids)
        {
            // SQL validation
            int remainingErrorCount = ModelState.MaxAllowedErrors - ModelState.ErrorCount;
            var sqlErrors = await _repo.CustomClassifications_Validate__DeleteWithDescendants(ids, top: remainingErrorCount);

            // Add errors to model state
            ModelState.AddLocalizedErrors(sqlErrors, _localizer);
        }

        protected override async Task DeleteWithDescendantsAsync(List<int> ids)
        {
            try
            {
                await _repo.CustomClassifications__DeleteWithDescendants(ids);
            }
            catch (ForeignKeyViolationException)
            {
                throw new BadRequestException(_localizer["Error_CannotDelete0AlreadyInUse", _localizer["CustomClassification"]]);
            }
        }

        public Task<(List<CustomClassification>, Extras)> Activate(List<int> ids, ActionArguments args)
        {
            return SetIsDeprecated(ids, args, isDeprecated: false);
        }

        public Task<(List<CustomClassification>, Extras)> Deprecate(List<int> ids, ActionArguments args)
        {
            return SetIsDeprecated(ids, args, isDeprecated: true);
        }

        private async Task<(List<CustomClassification>, Extras)> SetIsDeprecated(List<int> ids, ActionArguments args, bool isDeprecated)
        {
            // Check user permissions
            await CheckActionPermissions("IsDeprecated", ids);

            // Execute and return
            using var trx = ControllerUtilities.CreateTransaction();
            await _repo.CustomClassifications__Deprecate(ids, isDeprecated);

            if (args.ReturnEntities ?? false)
            {
                var (data, extras) = await GetByIds(ids, args, cancellation: default);

                trx.Complete();
                return (data, extras);
            }
            else
            {
                trx.Complete();
                return (null, null);
            }
        }

    }
}
