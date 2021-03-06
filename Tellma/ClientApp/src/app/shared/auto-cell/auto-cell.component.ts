import { Component, OnInit, Input, ChangeDetectionStrategy, ChangeDetectorRef, OnDestroy, SimpleChanges, OnChanges } from '@angular/core';
import {
  metadata,
  ChoicePropDescriptor,
  NumberPropDescriptor,
  EntityDescriptor, PropDescriptor,
  PropVisualDescriptor
} from '~/app/data/entities/base/metadata';
import { WorkspaceService } from '~/app/data/workspace.service';
import { TranslateService } from '@ngx-translate/core';
import { Subscription } from 'rxjs';
import { isSpecified } from '~/app/data/util';
import { datetimeFormat, dateFormat } from '../date-format/date-time-format';
import { formatSerial } from '~/app/data/entities/document';
import { Entity } from '~/app/data/entities/base/entity';
import { formatPercent } from '@angular/common';
import { accountingFormat } from '../accounting/accounting-format';

@Component({
  selector: 't-auto-cell',
  templateUrl: './auto-cell.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class AutoCellComponent implements OnInit, OnChanges, OnDestroy {

  // This component automatically displays the property value from its metadata

  @Input()
  collection: string;

  @Input()
  definitionId: number;

  @Input()
  path: string;

  @Input()
  entity: any;

  @Input()
  propDescriptor: PropDescriptor; // When set it (1) ignores collection, definition and path (2) assumes the entity is the immediate value

  @Input()
  entityDescriptor: EntityDescriptor;

  _subscription: Subscription;

  // The method 'recompute' efficiently populates all the following
  // values once, it is run once at the beginning and every time the
  // input changes or the workspace changes
  _entityDescriptor: EntityDescriptor;
  _propDescriptor: PropDescriptor;
  _metavalue: -1 | 0 | 1 | 2;
  _value: any;
  _control: string;
  _isEntity: boolean;

  // Constructor and lifecycle hooks
  constructor(private workspace: WorkspaceService, private translate: TranslateService, private cdr: ChangeDetectorRef) { }

  ngOnInit() {
    this._subscription = this.workspace.stateChanged$.subscribe({
      next: () => {
        this.recompute();
        this.cdr.markForCheck();
      }
    });
  }

  ngOnChanges(_: SimpleChanges) {
    this.recompute();
  }

  ngOnDestroy() {
    if (!!this._subscription) {
      this._subscription.unsubscribe();
    }
  }

  // For computing values and definitions

  private metadataFactory(collection: string) {
    const factory = metadata[collection]; // metadata factory for User
    if (!factory) {
      throw new Error(`The collection ${collection} does not exist`);
    }

    return factory;
  }

  private recompute() {

    // clear previous values
    this._entityDescriptor = null;
    this._propDescriptor = null;
    this._metavalue = 2;
    this._value = null;
    this._control = null;
    this._isEntity = false;

    try {

      this._value = this.entity;

      if (!!this.propDescriptor) {
        // The parent of the component did all the heavy lifting and supplied these values
        this._propDescriptor = this.propDescriptor;
        this._entityDescriptor = this.entityDescriptor;
        this._metavalue = 2;
        this._control = this._propDescriptor.control;
        this._isEntity = this._propDescriptor.datatype === 'entity';

      } else {
        if (!this.collection) {
          throw new Error(`The collection is not specified`);
        }

        const pathArray = (this.path || '').split('.').map(e => e.trim()).filter(e => !!e);
        this._entityDescriptor = this.metadataFactory(this.collection)(this.workspace, this.translate, this.definitionId);

        if (pathArray.length === 0) {
          this._propDescriptor = null;
          this._metavalue = 2;
          this._control = this.collection;
          this._isEntity = true;

        } else {
          let currentCollection = this.collection;
          let currentDefinition = this.definitionId;

          for (let i = 0; i < pathArray.length; i++) {
            const step = pathArray[i];

            this._propDescriptor = this._entityDescriptor.properties[step];
            if (!this._propDescriptor) {
              throw new Error(`'${step}' does not exist on '${currentCollection}', definition:'${currentDefinition}'`);

            } else {

              // always set the control
              this._control = this._propDescriptor.control;
              this._isEntity = this._propDescriptor.datatype === 'entity';

              if (this._propDescriptor.datatype === 'entity') {

                currentCollection = this._propDescriptor.control;
                currentDefinition = this._propDescriptor.definitionId;
                this._entityDescriptor = this.metadataFactory(currentCollection)(this.workspace, this.translate, currentDefinition);

                if (!!this._value && !!this._value.EntityMetadata) {
                  this._metavalue = step === 'Id' ? 2 : this._value.EntityMetadata[step] || 0;

                  const fkValue = this._value[this._propDescriptor.foreignKeyName];
                  this._value = this.workspace.current[currentCollection][fkValue];
                } else {
                  break; // null entity along the path
                }
              } else {
                // only allowed at the last step
                if (i !== pathArray.length - 1) {
                  throw new Error(`'${step}' is not a navigation property on '${currentCollection}', definition:'${currentDefinition}'`);
                }

                // set the property and control at the end
                if (this._value && this._value.EntityMetadata) {
                  this._metavalue = step === 'Id' ? 2 : this._value.EntityMetadata[step] || 0;
                  this._value = this._value[step];
                } else {
                  break; // null entity
                }
              }
            }

            if (this._metavalue !== 2) {
              // The remaining fields don't matter
              break;
            }
          }
        }
      }
    } catch (ex) {

      this._entityDescriptor = null;
      this._propDescriptor = null;
      this._metavalue = -1;
      this._value = ex.message;
      this._control = 'error';
      this._isEntity = false;
    }
  }

  // UI Binding

  get isEntity(): boolean {
    return this._isEntity;
  }

  get control(): string {
    return this._control;
  }

  get metavalue(): -1 | 0 | 1 | 2 { // -1=Error, 0=Not Loaded, 1=Restricted, 2=Loaded
    return this._metavalue;
  }

  get errorMessage(): string {
    return this._metavalue === -1 ? this._value : '';
  }

  get displayValue(): string {
    return displayScalarValue(this._value, this._propDescriptor, this.workspace, this.translate);
  }

  get stateColor(): string {
    const prop = this._propDescriptor as ChoicePropDescriptor;
    const value = this._value;
    return (!!prop && !!prop.color ? prop.color(value) : null) || 'transparent';
  }

  get hasColor(): boolean {
    const prop = this._propDescriptor as ChoicePropDescriptor;
    return !!prop.color;
  }

  get alignment(): string {
    if ((this._propDescriptor as NumberPropDescriptor).isRightAligned) {
      return 'right';
    }
  }

  get navigationValue(): any {
    // "this._value" should return the entity itself
    return displayEntity(this._value, this._entityDescriptor);
  }
}



/**
 * Returns a string representation of the value based on the property descriptor.
 * IMPORTANT: Does not support navigation property descriptors, use displayEntity instead
 * @param value The value to represent as a string
 * @param prop The property descriptor used to format the value as a string
 */
export function displayScalarValue(value: any, prop: PropVisualDescriptor, ws: WorkspaceService, trx: TranslateService): string {
  switch (prop.control) {
    case 'null': {
      return '';
    }
    case 'text': {
      return value;
    }
    case 'number': {
      if (value === undefined || value === null) {
        return '';
      }
      const digitsInfo = `1.${prop.minDecimalPlaces}-${prop.maxDecimalPlaces}`;
      let result = accountingFormat(value, digitsInfo);

      if (prop.noSeparator) {
        result = result.replace(/,/g, '');
      }

      return result;
    }
    case 'percent': {
      if (value === undefined || value === null) {
        return '';
      }
      const digitsInfo = `1.${prop.minDecimalPlaces}-${prop.maxDecimalPlaces}`;
      let result = isSpecified(value) ? formatPercent(value, 'en-GB', digitsInfo) : '';

      if (prop.noSeparator) {
        result = result.replace(/,/g, '');
      }

      return result;
    }
    case 'date': {
      if (value === undefined || value === null) {
        return '';
      }

      return dateFormat(value, ws, trx, prop.calendar, prop.granularity);
    }
    case 'datetime': {
      if (value === undefined || value === null) {
        return '';
      }

      return datetimeFormat(value, ws, trx, prop.calendar, prop.granularity);
    }
    case 'check': {
      return !!prop && !!prop.format ? prop.format(value) : value === true ? trx.instant('Yes') : value === false ? trx.instant('No') : '';
    }
    case 'choice': {
      return !!prop && !!prop.format ? prop.format(value) : '';
    }
    case 'serial': {
      if (value === undefined || value === null) {
        return '';
      }
      return !!prop ? formatSerial(value, prop.prefix, prop.codeWidth) : (value + '');
    }
    case 'unsupported': {
      return trx.instant('NotSupported');
    }
    default:
      return (value === undefined || value === null) ? '' : value + '';
    // throw new Error(`calling "displayValue" on a property of an unknown control ${prop.control}`);
  }
}

/**
 * Returns a string representation of the entity based on the entity descriptor.
 * @param entity The entity to represent as a string
 * @param entityDesc The entity descriptor used to format the entity as a string
 */
export function displayEntity(entity: Entity, entityDesc: EntityDescriptor) {
  return !!entityDesc.format ? (!!entity ? entityDesc.format(entity) : '') : '(Format function missing)';
}
