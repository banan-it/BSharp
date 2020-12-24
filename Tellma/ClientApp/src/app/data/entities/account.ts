// tslint:disable:variable-name
// tslint:disable:max-line-length
import { EntityWithKey } from './base/entity-with-key';
import { WorkspaceService, TenantWorkspace } from '../workspace.service';
import { TranslateService } from '@ngx-translate/core';
import { EntityDescriptor } from './base/metadata';
import { SettingsForClient } from '../dto/settings-for-client';

export interface AccountForSave extends EntityWithKey {
    CenterId?: number;
    Name?: string;
    Name2?: string;
    Name3?: string;
    Code?: string;
    AccountTypeId?: string;
    ClassificationId?: number;
    CustodianId?: number;
    CustodyDefinitionId?: number;
    CustodyId?: number;
    ParticipantId?: number;
    ResourceDefinitionId?: number;
    ResourceId?: number;
    CurrencyId?: string;
    EntryTypeId?: number;
}

export interface Account extends AccountForSave {
    IsActive?: boolean;
    IsBusinessUnit?: boolean;
    CreatedAt?: string;
    CreatedById?: number | string;
    ModifiedAt?: string;
    ModifiedById?: number | string;
}

const _select = ['', '2', '3'].map(pf => 'Name' + pf).concat(['Code']);
let _settings: SettingsForClient;
let _cache: EntityDescriptor;

function format(item: Account, ws: TenantWorkspace) {
    let result = ws.getMultilingualValueImmediate(item, _select[0]);
    if (!!item.Code) {
        result = `${item.Code} - ${result}`;
    }

    return result;
}

export function metadata_Account(wss: WorkspaceService, trx: TranslateService): EntityDescriptor {
    const ws = wss.currentTenant;
    // Some global values affect the result, we check here if they have changed, otherwise we return the cached result
    if (ws.settings !== _settings) {
        _settings = ws.settings;
        const entityDesc: EntityDescriptor = {
            collection: 'Account',
            titleSingular: () => trx.instant('Account'),
            titlePlural: () => trx.instant('Accounts'),
            select: _select,
            apiEndpoint: 'accounts',
            masterScreenUrl: 'accounts',
            orderby: () => ['Code'].concat(ws.isSecondaryLanguage ? [_select[1], _select[0]] : ws.isTernaryLanguage ? [_select[2], _select[0]] : [_select[0]]),
            inactiveFilter: 'IsActive eq true',
            format: (item: Account) => format(item, ws),
            properties: {
                Id: { datatype: 'integral', control: 'number', label: () => trx.instant('Id'), minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                CenterId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_Center')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                Center: { datatype: 'entity', control: 'Center', label: () => trx.instant('Account_Center'), foreignKeyName: 'CenterId' },
                Name: { datatype: 'string', control: 'text', label: () => trx.instant('Name') + ws.primaryPostfix },
                Name2: { datatype: 'string', control: 'text', label: () => trx.instant('Name') + ws.secondaryPostfix },
                Name3: { datatype: 'string', control: 'text', label: () => trx.instant('Name') + ws.ternaryPostfix },
                Code: { datatype: 'string', control: 'text', label: () => trx.instant('Code') },
                AccountTypeId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_Type')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                AccountType: { datatype: 'entity', control: 'AccountType', label: () => trx.instant('Account_Type'), foreignKeyName: 'AccountTypeId' },
                ClassificationId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_Classification')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                Classification: { datatype: 'entity', control: 'AccountClassification', label: () => trx.instant('Account_Classification'), foreignKeyName: 'ClassificationId' },
                CustodianId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_Custodian')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                Custodian: { datatype: 'entity', control: 'Relation', label: () => trx.instant('Account_Custodian'), foreignKeyName: 'CustodianId' },
                CustodyDefinitionId: {
                    datatype: 'integral',
                    control: 'choice',
                    label: () => trx.instant('Account_CustodyDefinition'),
                    choices: Object.keys(ws.definitions.Custodies).map(stringDefId => +stringDefId),
                    format: (defId: string) => ws.getMultilingualValueImmediate(ws.definitions.Custodies[defId], 'TitlePlural')
                },
                CustodyDefinition: { datatype: 'entity', control: 'CustodyDefinition', label: () => trx.instant('Account_CustodyDefinition'), foreignKeyName: 'CustodyDefinitionId' },
                CustodyId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_Custody')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                Custody: { datatype: 'entity', control: 'Custody', label: () => trx.instant('Account_Custody'), foreignKeyName: 'CustodyId' },
                ParticipantId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_Participant')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                Participant: { datatype: 'entity', control: 'Relation', label: () => trx.instant('Account_Participant'), foreignKeyName: 'ParticipantId' },
                ResourceDefinitionId: {
                    datatype: 'integral',
                    control: 'choice',
                    label: () => trx.instant('Account_ResourceDefinition'),
                    choices: Object.keys(ws.definitions.Resources).map(stringDefId => +stringDefId),
                    format: (defId: string) => ws.getMultilingualValueImmediate(ws.definitions.Resources[defId], 'TitlePlural')
                },
                ResourceDefinition: { datatype: 'entity', control: 'ResourceDefinition', label: () => trx.instant('Account_ResourceDefinition'), foreignKeyName: 'ResourceDefinitionId' },
                ResourceId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_Resource')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                Resource: { datatype: 'entity', control: 'Resource', label: () => trx.instant('Account_Resource'), foreignKeyName: 'ResourceId' },
                CurrencyId: { datatype: 'string', control: 'text', label: () => `${trx.instant('Account_Currency')} (${trx.instant('Id')})` },
                Currency: { datatype: 'entity', control: 'Currency', label: () => trx.instant('Account_Currency'), foreignKeyName: 'CurrencyId' },
                EntryTypeId: { datatype: 'integral', control: 'number', label: () => `${trx.instant('Account_EntryType')} (${trx.instant('Id')})`, minDecimalPlaces: 0, maxDecimalPlaces: 0 },
                EntryType: { datatype: 'entity', control: 'EntryType', label: () => trx.instant('Account_EntryType'), foreignKeyName: 'EntryTypeId' },

                IsActive: { datatype: 'boolean', control: 'boolean', label: () => trx.instant('IsActive') },
                CreatedAt: { datatype: 'datetimeoffset', control: 'datetime', label: () => trx.instant('CreatedAt') },
                CreatedBy: { datatype: 'entity', control: 'User', label: () => trx.instant('CreatedBy'), foreignKeyName: 'CreatedById' },
                ModifiedAt: { datatype: 'datetimeoffset', control: 'datetime', label: () => trx.instant('ModifiedAt') },
                ModifiedBy: { datatype: 'entity', control: 'User', label: () => trx.instant('ModifiedBy'), foreignKeyName: 'ModifiedById' }
            }
        };

        if (!ws.settings.SecondaryLanguageId) {
            delete entityDesc.properties.Name2;
        }

        if (!ws.settings.TernaryLanguageId) {
            delete entityDesc.properties.Name3;
        }

        _cache = entityDesc;
    }

    return _cache;
}
