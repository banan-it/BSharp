import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { Observable, throwError, of } from 'rxjs';
import { catchError, finalize, takeUntil, tap, map } from 'rxjs/operators';
import { ActivateArguments } from './dto/activate-arguments';
import { EntityForSave } from './entities/base/entity-for-save';
import { GetArguments } from './dto/get-arguments';
import { GetByIdArguments } from './dto/get-by-id-arguments';
import { GetResponse, EntitiesResponse } from './dto/get-response';
import { MeasurementUnit } from './entities/measurement-unit';
import { TemplateArguments } from './dto/template-arguments';
import { ImportArguments } from './dto/import-arguments';
import { ImportResult } from './dto/import-result';
import { ExportArguments } from './dto/export-arguments';
import { GetByIdResponse } from './dto/get-by-id-response';
import { SaveArguments } from './dto/save-arguments';
import { appsettings } from './global-resolver.guard';
import { Agent } from './entities/agent';
import { Role } from './entities/role';
import { Settings } from './entities/settings';
import { SettingsForClient } from './dto/settings-for-client';
import { DataWithVersion } from './dto/data-with-version';
import { PermissionsForClient } from './dto/permissions-for-client';
import { SaveSettingsResponse } from './dto/save-settings-response';
import { UserSettingsForClient } from './dto/user-settings-for-client';
import { GlobalSettingsForClient } from './dto/global-settings';
import { GetEntityResponse } from './dto/get-entity-response';
import { DefinitionsForClient } from './dto/definitions-for-client';
import { Currency } from './entities/currency';
import { Lookup } from './entities/lookup';
import { Resource } from './entities/resource';
import { User } from './entities/user';
import { LegacyClassification } from './entities/legacy-classification';
import { Account } from './entities/account';
import { GetChildrenArguments } from './dto/get-children-arguments';
import { GetAggregateArguments } from './dto/get-aggregate-arguments';
import { GetAggregateResponse } from './dto/get-aggregate-response';
import { ResponsibilityCenter } from './entities/responsibility-center';
import { friendlify } from './util';
import { EntryType } from './entities/entry-type';
import { Document } from './entities/document';
import { SignArguments } from './dto/sign-arguments';
import { AssignArguments } from './dto/assign-arguments';
import { MyUserForSave } from './dto/my-user';
import { AccountType } from './entities/account-type';
import { AdminUser } from './entities/admin-user';
import { AdminSettingsForClient } from './dto/admin-settings-for-client';
import { AdminUserSettingsForClient } from './dto/admin-user-settings-for-client';
import { MyAdminUserForSave } from './dto/my-admin-user';
import { AdminPermissionsForClient } from './dto/admin-permissions-for-client';
import { CompaniesForClient } from './dto/companies-for-client';
import { IdentityServerUser } from './entities/identity-server-user';
import { ResetPasswordArgs } from './dto/reset-password-args';
import { ActionArguments } from './action-arguments';


@Injectable({
  providedIn: 'root'
})
export class ApiService {

  public showRotator = false;

  // Will abstract away standard API calls for CRUD operations
  constructor(public http: HttpClient, public trx: TranslateService) { }

  // Admin

  public adminUsersApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<AdminUser>('admin-users', cancellationToken$),
      deactivate: this.deactivateFactory<AdminUser>('admin-users', cancellationToken$),
      getForClient: () => {
        const url = appsettings.apiAddress + `api/admin-users/client`;
        const obs$ = this.http.get<DataWithVersion<AdminUserSettingsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
      saveForClient: (key: string, value: string) => {
        const keyParam = `key=${encodeURIComponent(key)}`;
        const valueParam = !!value ? `&value=${encodeURIComponent(value)}` : '';
        const url = appsettings.apiAddress + `api/admin-users/client?` + keyParam + valueParam;
        const obs$ = this.http.post<DataWithVersion<AdminUserSettingsForClient>>(url, null).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
      invite: (id: number | string) => {
        this.showRotator = true;
        const url = appsettings.apiAddress + `api/admin-users/invite?id=${id}`;
        const obs$ = this.http.put(url, null).pipe(
          tap(() => this.showRotator = false),
          catchError(error => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },
      getMyUser: () => {
        const url = appsettings.apiAddress + `api/admin-users/me`;
        const obs$ = this.http.get<GetByIdResponse<AdminUser>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
      saveMyUser: (entity: MyAdminUserForSave) => {
        this.showRotator = true;
        const url = appsettings.apiAddress + `api/admin-users/me`;

        const obs$ = this.http.post<GetByIdResponse<AdminUser>>(url, entity, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      }
    };
  }

  public identityServerUsersApi(cancellationToken$: Observable<void>) {
    return {
      resetPassword: (args: ResetPasswordArgs) => {
        args = args || {};
        const url = appsettings.apiAddress + `api/identity-server-users/reset-password`;
        this.showRotator = true;
        const obs$ = this.http.put<EntitiesResponse<IdentityServerUser>>(url, args, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError(error => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      }
    };
  }

  public adminSettingsApi(cancellationToken$: Observable<void>) {
    // TODO: Keep or remove?
    return {
      // get: (args: GetByIdArguments) => {
      //   args = args || {};
      //   const paramsArray: string[] = [];

      //   if (!!args.expand) {
      //     paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
      //   }

      //   const params: string = paramsArray.join('&');
      //   const url = appsettings.apiAddress + `api/admin-settings?${params}`;

      //   const obs$ = this.http.get<GetEntityResponse<AdminSettings>>(url).pipe(
      //     catchError(error => {
      //       const friendlyError = friendlify(error, this.trx);
      //       return throwError(friendlyError);
      //     }),
      //     takeUntil(cancellationToken$)
      //   );

      //   return obs$;
      // },

      getForClient: () => {
        const url = appsettings.apiAddress + `api/admin-settings/client`;
        const obs$ = this.http.get<DataWithVersion<AdminSettingsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      ping: () => {
        const url = appsettings.apiAddress + `api/admin-settings/ping`;
        const obs$ = this.http.get(url).pipe(
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      // save: (entity: AdminSettings, args: SaveArguments) => {
      //   this.showRotator = true;
      //   args = args || {};
      //   const paramsArray: string[] = [];

      //   if (!!args.expand) {
      //     paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
      //   }

      //   const params: string = paramsArray.join('&');
      //   const url = appsettings.apiAddress + `api/admin-settings?${params}`;

      //   const obs$ = this.http.post<SaveAdminSettingsResponse>(url, entity, {
      //     headers: new HttpHeaders({ 'Content-Type': 'application/json' })
      //   }).pipe(
      //     tap(() => this.showRotator = false),
      //     catchError((error) => {
      //       this.showRotator = false;
      //       const friendlyError = friendlify(error, this.trx);
      //       return throwError(friendlyError);
      //     }),
      //     takeUntil(cancellationToken$),
      //     finalize(() => this.showRotator = false)
      //   );

      //   return obs$;
      // }
    };
  }

  public adminPermissionsApi(cancellationToken$: Observable<void>) {
    return {
      getForClient: () => {
        const url = appsettings.apiAddress + `api/admin-permissions/client`;
        const obs$ = this.http.get<DataWithVersion<AdminPermissionsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
    };
  }

  // Application

  public measurementUnitsApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<MeasurementUnit>('measurement-units', cancellationToken$),
      deactivate: this.deactivateFactory<MeasurementUnit>('measurement-units', cancellationToken$)
    };
  }

  public agentsApi(definitionId: string, cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<Agent>(`agents/${definitionId}`, cancellationToken$),
      deactivate: this.deactivateFactory<Agent>(`agents/${definitionId}`, cancellationToken$)
    };
  }

  public rolesApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<Role>('roles', cancellationToken$),
      deactivate: this.deactivateFactory<Role>('roles', cancellationToken$)
    };
  }

  public accountTypesApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<AccountType>(`account-types`, cancellationToken$),
      deactivate: this.deactivateFactory<AccountType>(`account-types`, cancellationToken$)
    };
  }

  public lookupsApi(definitionId: string, cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<Lookup>(`lookups/${definitionId}`, cancellationToken$),
      deactivate: this.deactivateFactory<Lookup>(`lookups/${definitionId}`, cancellationToken$)
    };
  }

  public currenciesApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<Currency>('currencies', cancellationToken$),
      deactivate: this.deactivateFactory<Currency>('currencies', cancellationToken$)
    };
  }

  public resourcesApi(definitionId: string, cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<Resource>(`resources/${definitionId}`, cancellationToken$),
      deactivate: this.deactivateFactory<Resource>(`resources/${definitionId}`, cancellationToken$)
    };
  }

  public legacyClassificationsApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<LegacyClassification>('legacy-classifications', cancellationToken$),
      deactivate: this.deactivateFactory<LegacyClassification>('legacy-classifications', cancellationToken$)
    };
  }

  public entryTypesApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<EntryType>(`entry-types`, cancellationToken$),
      deactivate: this.deactivateFactory<EntryType>(`entry-types`, cancellationToken$)
    };
  }

  public documentsApi(definitionId: string, cancellationToken$: Observable<void>) {
    return {
      assign: (ids: (string | number)[], args: AssignArguments, extras?: { [key: string]: any }) => {

        const paramsArray = this.stringifyActionArguments(args);
        this.addExtras(paramsArray, extras);

        paramsArray.push(`assigneeId=${encodeURIComponent(args.assigneeId)}`);

        if (!!args.comment) {
          paramsArray.push(`comment=${encodeURIComponent(args.comment)}`);
        }


        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/documents/${definitionId}/assign?${params}`;

        this.showRotator = true;
        const obs$ = this.http.put<EntitiesResponse<Document>>(url, ids, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError(error => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },
      sign: (ids: (string | number)[], args: SignArguments, extras?: { [key: string]: any }) => {

        const paramsArray = this.stringifyActionArguments(args);
        this.addExtras(paramsArray, extras);

        paramsArray.push(`toState=${encodeURIComponent(args.toState)}`);

        if (!!args.reasonId) {
          paramsArray.push(`reasonId=${encodeURIComponent(args.reasonId)}`);
        }

        if (!!args.reasonDetails) {
          paramsArray.push(`reasonDetails=${encodeURIComponent(args.reasonDetails)}`);
        }

        if (!!args.onBehalfOfUserId) {
          paramsArray.push(`onBehalfOfUserId=${encodeURIComponent(args.onBehalfOfUserId)}`);
        }

        if (!!args.ruleType) {
          paramsArray.push(`ruleType=${encodeURIComponent(args.ruleType)}`);
        }

        if (!!args.roleId) {
          paramsArray.push(`roleId=${encodeURIComponent(args.roleId)}`);
        }

        if (!!args.signedAt) {
          paramsArray.push(`signedAt=${encodeURIComponent(args.signedAt)}`);
        }

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/documents/${definitionId}/sign-lines?${params}`;

        this.showRotator = true;
        const obs$ = this.http.put<EntitiesResponse<Document>>(url, ids, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError(error => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },
      unsign: (ids: (string | number)[], args: ActionArguments, extras?: { [key: string]: any }) => {

        const paramsArray = this.stringifyActionArguments(args);
        this.addExtras(paramsArray, extras);

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/documents/${definitionId}/unsign-lines?${params}`;

        this.showRotator = true;
        const obs$ = this.http.put<EntitiesResponse<Document>>(url, ids, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError(error => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },
      post: this.updateStateFactory(definitionId, 'post', cancellationToken$),
      unpost: this.updateStateFactory(definitionId, 'unpost', cancellationToken$),
      cancel: this.updateStateFactory(definitionId, 'cancel', cancellationToken$),
      uncancel: this.updateStateFactory(definitionId, 'uncancel', cancellationToken$),
      getAttachment: (docId: string | number, attachmentId: string | number) => {

        const url = appsettings.apiAddress + `api/documents/${definitionId}/${docId}/attachments/${attachmentId}`;
        const obs$ = this.http.get(url, { responseType: 'blob' }).pipe(
          catchError((error) => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
        );
        return obs$;
      }
    };
  }

  public responsibilityCenterApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<ResponsibilityCenter>('responsibility-centers', cancellationToken$),
      deactivate: this.deactivateFactory<ResponsibilityCenter>('responsibility-centers', cancellationToken$)
    };
  }

  public accountsApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<Account>(`accounts`, cancellationToken$),
      deactivate: this.deactivateFactory<Account>(`accounts`, cancellationToken$)
    };
  }

  public usersApi(cancellationToken$: Observable<void>) {
    return {
      activate: this.activateFactory<User>('users', cancellationToken$),
      deactivate: this.deactivateFactory<User>('users', cancellationToken$),
      getForClient: () => {
        const url = appsettings.apiAddress + `api/users/client`;
        const obs$ = this.http.get<DataWithVersion<UserSettingsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
      saveForClient: (key: string, value: string) => {
        const keyParam = `key=${encodeURIComponent(key)}`;
        const valueParam = !!value ? `&value=${encodeURIComponent(value)}` : '';
        const url = appsettings.apiAddress + `api/users/client?` + keyParam + valueParam;
        const obs$ = this.http.post<DataWithVersion<UserSettingsForClient>>(url, null).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
      invite: (id: number | string) => {
        this.showRotator = true;
        const url = appsettings.apiAddress + `api/users/invite?id=${id}`;
        const obs$ = this.http.put(url, null).pipe(
          tap(() => this.showRotator = false),
          catchError(error => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },
      getMyUser: () => {
        const url = appsettings.apiAddress + `api/users/me`;
        const obs$ = this.http.get<GetByIdResponse<User>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
      saveMyUser: (entity: MyUserForSave) => {
        this.showRotator = true;
        const url = appsettings.apiAddress + `api/users/me`;

        const obs$ = this.http.post<GetByIdResponse<User>>(url, entity, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      }
    };
  }

  public companiesApi(cancellationToken$: Observable<void>) {
    return {
      getForClient: () => {
        const url = appsettings.apiAddress + `api/companies/client`;
        const obs$ = this.http.get<CompaniesForClient>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      }
    };
  }

  public globalSettingsApi(cancellationToken$: Observable<void>) {
    return {
      getForClient: () => {
        const url = appsettings.apiAddress + `api/global-settings/client`;
        const obs$ = this.http.get<DataWithVersion<GlobalSettingsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      ping: () => {
        const url = appsettings.apiAddress + `api/global-settings/ping`;
        const obs$ = this.http.get(url).pipe(
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
    };
  }

  public pingApi(cancellationToken$: Observable<void>) {
    return {
      ping: () => {
        const url = appsettings.apiAddress + `api/ping`;
        const obs$ = this.http.get<void>(url).pipe(
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
    };
  }

  public settingsApi(cancellationToken$: Observable<void>) {
    return {
      get: (args: GetByIdArguments) => {
        args = args || {};
        const paramsArray: string[] = [];

        if (!!args.expand) {
          paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
        }

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/settings?${params}`;

        const obs$ = this.http.get<GetEntityResponse<Settings>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      getForClient: () => {
        const url = appsettings.apiAddress + `api/settings/client`;
        const obs$ = this.http.get<DataWithVersion<SettingsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      ping: () => {
        const url = appsettings.apiAddress + `api/settings/ping`;
        const obs$ = this.http.get(url).pipe(
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      save: (entity: Settings, args: SaveArguments) => {
        this.showRotator = true;
        args = args || {};
        const paramsArray: string[] = [];

        if (!!args.expand) {
          paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
        }

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/settings?${params}`;

        const obs$ = this.http.post<SaveSettingsResponse>(url, entity, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      }
    };
  }

  public permissionsApi(cancellationToken$: Observable<void>) {
    return {
      getForClient: () => {
        const url = appsettings.apiAddress + `api/permissions/client`;
        const obs$ = this.http.get<DataWithVersion<PermissionsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
    };
  }

  public definitionsApi(cancellationToken$: Observable<void>) {
    return {
      getForClient: () => {
        const url = appsettings.apiAddress + `api/definitions/client`;
        const obs$ = this.http.get<DataWithVersion<DefinitionsForClient>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },
    };
  }

  public crudFactory<TEntity extends EntityForSave, TEntityForSave extends EntityForSave = EntityForSave>(
    endpoint: string, cancellationToken$: Observable<void>) {
    return {
      get: (args: GetArguments, extras?: { [key: string]: any }) => {
        const paramsArray = this.stringifyGetArguments(args);
        this.addExtras(paramsArray, extras);

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}?${params}`;

        const obs$ = this.http.get<GetResponse<TEntity>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      getById: (id: number | string, args: GetByIdArguments, extras?: { [key: string]: any }) => {
        args = args || {};
        const paramsArray: string[] = [];

        if (!!args.expand) {
          paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
        }

        if (!!args.select) {
          paramsArray.push(`select=${encodeURIComponent(args.select)}`);
        }

        this.addExtras(paramsArray, extras);

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}/${id}?${params}`;

        const obs$ = this.http.get<GetByIdResponse<TEntity>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      getAggregate: (args: GetAggregateArguments, extras?: { [key: string]: any }) => {
        args = args || {};
        const paramsArray: string[] = [];

        if (!!args.select) {
          paramsArray.push(`select=${encodeURIComponent(args.select)}`);
        }

        if (!!args.filter) {
          paramsArray.push(`filter=${encodeURIComponent(args.filter)}`);
        }

        this.addExtras(paramsArray, extras);

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}/aggregate?${params}`;

        const obs$ = this.http.get<GetAggregateResponse>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      getChildrenOf: (args: GetChildrenArguments, extras?: { [key: string]: any }) => {
        args = args || {};
        const paramsArray: string[] = [];

        if (!!args.expand) {
          paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
        }

        if (!!args.select) {
          paramsArray.push(`select=${encodeURIComponent(args.select)}`);
        }

        if (!!args.filter) {
          paramsArray.push(`filter=${encodeURIComponent(args.filter)}`);
        }

        paramsArray.push(`roots=${!!args.roots}`);

        if (!!args.i) {
          args.i.forEach(id => {
            paramsArray.push(`i=${encodeURIComponent(id)}`);
          });
        }

        this.addExtras(paramsArray, extras);

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}/children-of?${params}`;

        const obs$ = this.http.get<EntitiesResponse<TEntity>>(url).pipe(
          catchError(error => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$)
        );

        return obs$;
      },

      save: (entities: TEntityForSave[], args: SaveArguments, extras?: { [key: string]: any }) => {
        this.showRotator = true;
        args = args || {};
        const paramsArray: string[] = [];

        if (!!args.expand) {
          paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
        }

        if (!!args.select) {
          paramsArray.push(`select=${encodeURIComponent(args.select)}`);
        }

        paramsArray.push(`returnEntities=${!!args.returnEntities}`);

        this.addExtras(paramsArray, extras);

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}?${params}`;

        const obs$ = this.http.post<EntitiesResponse<TEntity>>(url, entities, {
          headers: new HttpHeaders({ 'Content-Type': 'application/json' })
        }).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },

      deleteId: (id: number | string) => {
        this.showRotator = true;

        const url = appsettings.apiAddress + `api/${endpoint}` + '/' + encodeURIComponent(id);
        const obs$ = this.http.delete(url).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },

      delete: (ids: (number | string)[]) => {
        this.showRotator = true;

        const url = appsettings.apiAddress + `api/${endpoint}?` + ids.map(id => `i=${encodeURIComponent(id)}`).join('&');
        const obs$ = this.http.delete(url).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },

      deleteWithDescendants: (ids: (number | string)[]) => {
        this.showRotator = true;
        const url = appsettings.apiAddress + `api/${endpoint}/with-descendants?` + ids.map(id => `i=${encodeURIComponent(id)}`).join('&');
        const obs$ = this.http.delete(url).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },

      template: (args: TemplateArguments) => {
        args = args || {};

        const paramsArray: string[] = [];

        if (!!args.format) {
          paramsArray.push(`format=${args.format}`);
        }

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}/template?${params}`;
        const obs$ = this.http.get(url, { responseType: 'blob' }).pipe(
          catchError((error) => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
        );
        return obs$;
      },

      import: (args: ImportArguments, files: any) => {
        args = args || {};

        const paramsArray: string[] = [];

        if (!!args.mode) {
          paramsArray.push(`mode=${args.mode}`);
        }

        const formData = new FormData();

        for (const file of files) {
          formData.append(file.name, file);
        }

        this.showRotator = true;
        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}/import?${params}`;
        const obs$ = this.http.post<ImportResult>(url, formData).pipe(
          tap(() => this.showRotator = false),
          catchError((error) => {
            this.showRotator = false;
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
          finalize(() => this.showRotator = false)
        );

        return obs$;
      },

      export: (args: ExportArguments) => {
        const paramsArray = this.stringifyGetArguments(args);

        if (!!args.format) {
          paramsArray.push(`format=${args.format}`);
        }

        const params: string = paramsArray.join('&');
        const url = appsettings.apiAddress + `api/${endpoint}/export?${params}`;
        const obs$ = this.http.get(url, { responseType: 'blob' }).pipe(
          catchError((error) => {
            const friendlyError = friendlify(error, this.trx);
            return throwError(friendlyError);
          }),
          takeUntil(cancellationToken$),
        );
        return obs$;
      }
    };
  }

  // We refactored this out to support the t-image component
  public getImage(endpoint: string, imageId: string, cancellationToken$: Observable<void>) {

    // Note: cache=true instructs the HTTP interceptor to not add cache-busting parameters
    const url = appsettings.apiAddress + `api/${endpoint}?imageId=${imageId}`;
    const obs$ = this.http.get(url, { responseType: 'blob', observe: 'response' }).pipe(
      map(res => {
        return { image: res.body, imageId: res.headers.get('x-image-id') };
      }),
      catchError(error => {
        const friendlyError = friendlify(error, this.trx);
        return throwError(friendlyError);
      }),
      takeUntil(cancellationToken$)
    );

    return obs$;
  }

  private updateStateFactory(definitionId: string, transition: string, cancellationToken$: Observable<void>) {
    return (ids: (string | number)[], args: ActionArguments, extras?: { [key: string]: any }) => {

      const paramsArray = this.stringifyActionArguments(args);
      this.addExtras(paramsArray, extras);

      const params: string = paramsArray.join('&');
      const url = appsettings.apiAddress + `api/documents/${definitionId}/${transition}?${params}`;

      this.showRotator = true;
      const obs$ = this.http.put<EntitiesResponse<Document>>(url, ids, {
        headers: new HttpHeaders({ 'Content-Type': 'application/json' })
      }).pipe(
        tap(() => this.showRotator = false),
        catchError(error => {
          this.showRotator = false;
          const friendlyError = friendlify(error, this.trx);
          return throwError(friendlyError);
        }),
        takeUntil(cancellationToken$),
        finalize(() => this.showRotator = false)
      );

      return obs$;
    };
  }

  private activateFactory<TDto extends EntityForSave>(endpoint: string, cancellationToken$: Observable<void>) {
    return (ids: (string | number)[], args: ActivateArguments) => {
      args = args || {};

      const paramsArray: string[] = [];

      if (!!args.returnEntities) {
        paramsArray.push(`returnEntities=${args.returnEntities}`);
      }

      if (!!args.expand) {
        paramsArray.push(`expand=${args.expand}`);
      }

      const params: string = paramsArray.join('&');
      const url = appsettings.apiAddress + `api/${endpoint}/activate?${params}`;

      this.showRotator = true;
      const obs$ = this.http.put<EntitiesResponse<TDto>>(url, ids, {
        headers: new HttpHeaders({ 'Content-Type': 'application/json' })
      }).pipe(
        tap(() => this.showRotator = false),
        catchError(error => {
          this.showRotator = false;
          const friendlyError = friendlify(error, this.trx);
          return throwError(friendlyError);
        }),
        takeUntil(cancellationToken$),
        finalize(() => this.showRotator = false)
      );

      return obs$;
    };
  }

  private deactivateFactory<TDto extends EntityForSave>(endpoint: string, cancellationToken$: Observable<void>) {
    return (ids: (string | number)[], args: ActivateArguments) => {
      args = args || {};

      const paramsArray: string[] = [];

      if (!!args.returnEntities) {
        paramsArray.push(`returnEntities=${args.returnEntities}`);
      }

      if (!!args.expand) {
        paramsArray.push(`expand=${args.expand}`);
      }

      const params: string = paramsArray.join('&');
      const url = appsettings.apiAddress + `api/${endpoint}/deactivate?${params}`;

      this.showRotator = true;
      const obs$ = this.http.put<EntitiesResponse<TDto>>(url, ids, {
        headers: new HttpHeaders({ 'Content-Type': 'application/json' })
      }).pipe(
        tap(() => this.showRotator = false),
        catchError(error => {
          this.showRotator = false;
          const friendlyError = friendlify(error, this.trx);
          return throwError(friendlyError);
        }),
        takeUntil(cancellationToken$),
        finalize(() => this.showRotator = false)
      );

      return obs$;
    };
  }

  stringifyGetArguments(args: GetArguments): string[] {
    args = args || {};
    const top = args.top || 50;
    const skip = args.skip || 0;

    const paramsArray: string[] = [
      `top=${top}`,
      `skip=${skip}`
    ];

    if (!!args.search) {
      paramsArray.push(`search=${encodeURIComponent(args.search)}`);
    }

    if (!!args.orderby) {
      paramsArray.push(`orderBy=${args.orderby}`);
      paramsArray.push(`desc=${!!args.desc}`);
    }

    if (!!args.inactive) {
      paramsArray.push(`inactive=${args.inactive}`);
    }

    if (!!args.filter) {
      paramsArray.push(`filter=${encodeURIComponent(args.filter)}`);
    }

    if (!!args.expand) {
      paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
    }

    if (!!args.select) {
      paramsArray.push(`select=${encodeURIComponent(args.select)}`);
    }

    return paramsArray;
  }

  stringifyActionArguments(args: ActionArguments): string[] {
    args = args || {};

    const paramsArray: string[] = [
    ];

    if (!!args.select) {
      paramsArray.push(`select=${encodeURIComponent(args.select)}`);
    }

    if (!!args.expand) {
      paramsArray.push(`expand=${encodeURIComponent(args.expand)}`);
    }

    if (!!args.returnEntities) {
      paramsArray.push(`returnEntities=${args.returnEntities}`);
    }

    return paramsArray;
  }

  addExtras(paramsArray: string[], extras: { [key: string]: any }) {
    if (!!extras) {
      Object.keys(extras).forEach(key => {
        const value = extras[key];
        if (value !== undefined && value !== null) {
          const valueString = value.toString();
          paramsArray.push(`${key}=${encodeURIComponent(valueString)}`);
        }
      });
    }
  }
}
