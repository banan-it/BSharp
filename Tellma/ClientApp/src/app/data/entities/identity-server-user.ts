import { EntityWithKey } from './base/entity-with-key';
import { WorkspaceService } from '../workspace.service';
import { TranslateService } from '@ngx-translate/core';
import { EntityDescriptor } from './base/metadata';
import { AdminSettingsForClient } from '../dto/admin-settings-for-client';
import { TimeGranularity } from './base/metadata-types';

export interface IdentityServerUser extends EntityWithKey {
    Email?: string;
    EmailConfirmed?: boolean;
    PasswordSet?: boolean;
    TwoFactorEnabled?: boolean;
    LockoutEnd?: string;
}

let _settings: AdminSettingsForClient;
let _cache: EntityDescriptor;

export function metadata_IdentityServerUser(wss: WorkspaceService, trx: TranslateService): EntityDescriptor {
    const ws = wss.admin;
    // Some global values affect the result, we check here if they have changed, otherwise we return the cached result
    if (ws.settings !== _settings) {
        _settings = ws.settings;
        _cache = {
            collection: 'IdentityServerUser',
            titleSingular: () => trx.instant('IdentityServerUser'),
            titlePlural: () => trx.instant('IdentityServerUsers'),
            select: ['Email'],
            apiEndpoint: 'identity-server-users',
            masterScreenUrl: 'identity-server-users',
            orderby: () => ['Email'],
            inactiveFilter: null,
            format: (item: IdentityServerUser) => item.Email,
            formatFromVals: (vals: any[]) => vals[0],
            isAdmin: true,
            properties: {
                Id: { datatype: 'string', control: 'text', label: () => trx.instant('Id') },
                Email: { datatype: 'string', control: 'text', label: () => trx.instant('User_Email') },
                EmailConfirmed: { datatype: 'bit', control: 'check', label: () => trx.instant('IdentityServerUser_EmailConfirmed') },
                PasswordSet: { datatype: 'bit', control: 'check', label: () => trx.instant('IdentityServerUser_PasswordSet') },
                TwoFactorEnabled: {
                    datatype: 'bit', control: 'check', label: () => trx.instant('IdentityServerUser_TwoFactorEnabled')
                },
                LockoutEnd: {
                    datatype: 'datetimeoffset',
                    control: 'datetime',
                    label: () => trx.instant('IdentityServerUser_LockoutEnd'),
                    granularity: TimeGranularity.minutes
                },
            }
        };
    }

    return _cache;
}
