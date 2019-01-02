import { Location } from '@angular/common';
import { Component, EventEmitter, Input, OnDestroy, OnInit, TemplateRef, ViewChild, Output } from '@angular/core';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { NgbModal } from '@ng-bootstrap/ng-bootstrap';
import { TranslateService } from '@ngx-translate/core';
import { BehaviorSubject, Observable, of, Subject } from 'rxjs';
import { catchError, switchMap, tap } from 'rxjs/operators';
import { ApiService } from 'src/app/data/api.service';
import { ICanDeactivate } from 'src/app/data/dirty-data.guard';
import { DtoForSaveKeyBase } from 'src/app/data/dto/dto-for-save-key-base';
import { GetByIdResponse } from 'src/app/data/dto/get-by-id-response';
import { EntitiesResponse } from 'src/app/data/dto/get-response';
import { addSingleToWorkspace, addToWorkspace } from 'src/app/data/util';
import { DetailsStatus, MasterDetailsStore, WorkspaceService } from 'src/app/data/workspace.service';

@Component({
  selector: 'b-details',
  templateUrl: './details.component.html',
  styleUrls: ['./details.component.css']
})
export class DetailsComponent implements OnInit, OnDestroy, ICanDeactivate {

  @Input()
  apiEndpoint: string;

  @Input()
  collection: string;

  @Input()
  expand: string;

  @Input()
  masterCrumb: string;

  @Input()
  detailsCrumb: string;

  @Input()
  documentTemplate: TemplateRef<any>;

  @Input()
  sidebarTemplate: TemplateRef<any>;

  @Input() // popup: only the title and the document are visible
  mode: 'popup' | 'screen' = 'screen';

  @Input()
  createNew: () => DtoForSaveKeyBase = () => ({ Id: null, EntityState: 'Inserted' });

  @Input()
  public set idString(v: string) {
    if (this._idString !== v) {
      this._idString = v;
      this.fetch();
    }
  }

  public get idString() {
    return this._idString;
  }

  @Output()
  save = new EventEmitter<void>();

  @Output()
  cancel = new EventEmitter<void>();

  @ViewChild('errorModal')
  public errorModal: TemplateRef<any>;

  @ViewChild('unsavedChangesModal')
  public unsavedChangesModal: TemplateRef<any>;

  private _idString: string;
  private _editModel: DtoForSaveKeyBase;
  private notifyFetch$ = new BehaviorSubject<any>(null);
  private notifyDestruct$ = new Subject<void>();
  private localState = new MasterDetailsStore();  // Used in popup mode
  private _errorMessage: string; // in the document area itself
  private _modalErrorMessage: string; // in the modal
  private _validationErrors: { [id: string]: string[] } = {}; // on the fields
  private crud = this.api.crudFactory(this.apiEndpoint, this.notifyDestruct$); // Just for intellisense
  private _viewModelJson;

  constructor(private workspace: WorkspaceService, private api: ApiService, private location: Location, private router: Router,
    private route: ActivatedRoute, private translate: TranslateService, public modalService: NgbModal) { }

  ngOnInit() {

    this.crud = this.api.crudFactory(this.apiEndpoint, this.notifyDestruct$);

    // When the URI 'id' parameter changes in screen mode,
    // set idString which in turn fetches a new record
    if (this.mode === 'screen') {
      this.route.paramMap.subscribe((params: ParamMap) => {
        // This triggers a refresh
        this.idString = params.get('id');
      });
    }

    // When the notifyFetch$ subject fires, cancel existing backend
    // call and dispatch a new backend call
    this.notifyFetch$.pipe(
      switchMap(() => this.doFetch())
    ).subscribe();
  }

  ngOnDestroy() {
    this.notifyDestruct$.next();
  }

  private fetch() {
    this.notifyFetch$.next(null);
  }

  private doFetch(): Observable<void> {
    // clear the errors before refreshing
    this.clearErrors();

    // grab the configured state
    const s = this.state;
    if (this.isNew) {
      // IF it's create new, don't fetch anything
      this._editModel = this.createNew();
      s.detailsStatus = DetailsStatus.edit;
      return of();

    } else {
      // IF it's the last viewed item also don't do anything
      if (!!s.detailsId && s.detailsId.toString() === this.idString && s.detailsStatus === DetailsStatus.loaded) {
        // the application caches the last record that was viewed by the user
        // if the new id is equal to the Id of the last record then just display
        // that last record. This is helpful when navigating to Id after a create new
        return of();
      } else {
        // ELSE fetch the record from server
        // first show the rotator
        s.detailsStatus = DetailsStatus.loading;
        return this.crud.getById(this.idString, { expand: this.expand }).pipe(
          tap((response: GetByIdResponse) => {
            const s = this.state;
            s.detailsId = addSingleToWorkspace(response, this.workspace);

            if (this.mode === 'screen') {
              s.detailsStatus = DetailsStatus.loaded;

            } else {
              this.onEdit();
            }
          }),
          catchError((friendlyError) => {
            const s = this.state;
            s.detailsStatus = DetailsStatus.error;
            this._errorMessage = friendlyError.error;
            return of(null);
          })
        );
      }
    }
  }

  private clearErrors(): void {
    this._errorMessage = null;
    this._modalErrorMessage = null;
    this._validationErrors = {};
  }

  public get state(): MasterDetailsStore {
    // important to always reference the source, and not keep a local reference
    // on some occasions the source can be reset and using a local reference can cause bugs
    if (this.mode === 'popup') {

      // popups use a local store that vanishes when the popup is destroyed
      if (!this.localState) {
        this.localState = new MasterDetailsStore();
      }

      return this.localState;
    } else {
      // screen mode on the other hand use the global state
      return this.globalState;
    }
  }

  private get globalState(): MasterDetailsStore {
    if (!this.workspace.current.mdState[this.apiEndpoint]) {
      this.workspace.current.mdState[this.apiEndpoint] = new MasterDetailsStore();
    }

    return this.workspace.current.mdState[this.apiEndpoint];
  }

  public canDeactivate(): boolean | Observable<boolean> {
    if (this.isDirty) {

      // IF there are unsaved changes, prompt the user asking if they would like them discarded
      const modal = this.modalService.open(this.unsavedChangesModal);

      // capture the user's decision in a subject:
      // first action when the user presses one of the two buttons
      // second func is when the user dismisses the modal with x or ESC or clicking the background
      const decision$ = new Subject<boolean>();
      modal.result.then(
        v => { decision$.next(v); decision$.complete(); },
        _ => { decision$.next(false); decision$.complete(); }
      );

      // return the subject that will eventually emit the user's decision
      return decision$;

    } else {

      // IF there are no unsaved changes, the navigation can happily proceed
      return true;
    }
  }

  public displayModalError(errorMessage: string) {
    // shows the error message in a dismissable modal
    this._modalErrorMessage = errorMessage;
    this.modalService.open(this.errorModal);
  }

  get viewModel() {
    // view data is always directly referencing the global workspace
    // this way, un update to a record in the global workspace automatically
    // updates all places where this record is displayed... nifty
    const s = this.state;
    return !!s.detailsId ? this.workspace.current[this.collection][s.detailsId] : null;
  }

  private handleActionError(friendlyError) {
    // This handles any errors caused by actions

    if (friendlyError.status === 422) {
      const keys = Object.keys(friendlyError.error);
      keys.forEach(key => {
        // most validation error keys are expected to start with '[0].'
        // the code below removes this prefix
        let modifiedKey: string;
        const prefix = '[0].';
        if (key.startsWith(prefix)) {
          modifiedKey = key.substring(prefix.length);
        } else {
          modifiedKey = key;
        }

        this._validationErrors[modifiedKey] = friendlyError.error[key];
      });

    } else {
      this.displayModalError(friendlyError.error);
    }
  }

  ////// UI Bindings

  get errorMessage() {
    return this._errorMessage;
  }

  get modalErrorMessage() {
    return this._modalErrorMessage;
  }

  get validationErrors() {
    return this._validationErrors;
  }

  get activeModel() {
    return this.isEdit ? this._editModel : this.viewModel;
  }

  get showSpinner(): boolean {
    return this.state.detailsStatus === DetailsStatus.loading;
  }

  get showDocument(): boolean {
    return this.state.detailsStatus === DetailsStatus.loaded ||
      this.state.detailsStatus === DetailsStatus.edit;
  }

  get showSidebar(): boolean {
    return !!this.sidebarTemplate && this.showDocument;
  }

  get showRefresh(): boolean {
    return !this.isEdit;
  }

  get isNew() {
    return this.idString === 'new';
  }

  get isDirty(): boolean {
    // TODO This may cause sluggishness for large DTOs, we'll look into ways of optimizing it later
    return this.isEdit && this._viewModelJson !== JSON.stringify(this._editModel);
  }

  get isEdit(): boolean {
    return this.state.detailsStatus === DetailsStatus.edit;
  }

  get isScreenMode() {
    // the part above the main content area
    return this.mode === 'screen';
  }

  get isPopupMode() {
    // the part above the main content area
    return this.mode === 'popup';
  }

  get showViewToolbar(): boolean {
    return !this.showEditToolbar;
  }

  get showEditToolbar(): boolean {
    return this.state.detailsStatus === DetailsStatus.edit;
  }

  get showErrorMessage(): boolean {
    return this.state.detailsStatus === DetailsStatus.error;
  }

  get showDelete(): boolean {
    return true; // TODO !!this.data[this.controller].delete;
  }

  onRefresh(): void {
    const s = this.state;
    if (s.detailsStatus !== DetailsStatus.loading) {

      // clear the cached item and fetch again
      s.detailsId = null;
      this.fetch();
    }
  }

  onCreate(): void {
    this.router.navigate(['..', 'new'], { relativeTo: this.route });
  }

  get canCreate(): boolean {
    return true; // TODO !this.canUpdatePred || this.canUpdatePred();
  }

  onEdit(): void {
    if (this.viewModel) {
      // clone the model (to allow for canceling changes)
      this._viewModelJson = JSON.stringify(this.viewModel);
      this._editModel = JSON.parse(this._viewModelJson);

      // show the edit view
      this.state.detailsStatus = DetailsStatus.edit;
    }
  }

  get canEdit(): boolean {
    // TODO  (!this.canUpdatePred || this.canUpdatePred()) && (this.activeModel && this.enableEditButtonPred(this.activeModel));
    return !!this.activeModel;
  }

  onSave(): void {
    if (!this.isDirty) {
      if (this.mode === 'popup') {
        // In popup mode, just notify the outside world that a save has happened
        this.save.emit();

      } else {
        // since no changes, don't save to the database
        // just go back to view mode
        this.clearErrors();
        this._editModel = null;
        this.state.detailsStatus = DetailsStatus.loaded;
      }
    } else {

      // clear any errors displayed
      this.clearErrors();

      // we need the original value for when the save API call returns
      const isNew = this.isNew;

      // TODO: some screens may wish to customize this behavior for e.g. line item DTOs
      this._editModel.EntityState = isNew ? 'Inserted' : 'Updated';

      // prepare the save observable
      this.crud.save([this._editModel], { expand: this.expand, returnEntities: true }).subscribe(
        (response: EntitiesResponse) => {

          // update the workspace with the DTO from the server
          const s = this.state;
          s.detailsId = addToWorkspace(response, this.workspace)[0];

          // IF it's a new entity add it to the global state, (not the local one one even if inside a popup)
          if (isNew) {
            this.globalState.insert([s.detailsId]);
          }

          if (this.mode === 'popup') {
            // in popup mode, just notify the outside world that a save has happened
            this.save.emit();
            this.onEdit(); // to replace the edit mode with the one from the server

          } else {
            // in screen mode always close the edit view
            s.detailsStatus = DetailsStatus.loaded;

            // remove the local copy the user was editing
            this._editModel = null;

            // IF new and in screen mode, navigate to the Id just returned
            if (this.isNew) {
              this.router.navigate(['..', s.detailsId], { relativeTo: this.route });
            }
          }
        },
        (friendlyError) => this.handleActionError(friendlyError)
      );
    }
  }

  onCancel(): void {
    if (this.mode === 'popup') {
      // in popup mode, just notify the outside world that a cancel has happened
      this.cancel.emit();
    } else {
      // in screen mode...
      // remove the edit model
      if (this.isNew) {

        // this step in order to avoid the unsaved changes modal
        this.state.detailsStatus = DetailsStatus.loaded;

        // navigate back to the last screen
        this.location.back();

      } else {
        // clear the edit model and error messages
        this._editModel = null;
        this.clearErrors();

        // ... and then close the edit form
        this.state.detailsStatus = DetailsStatus.loaded;
      }
    }
  }

  onDelete(): void {
    // Assuming the entity is not new
    const id = this.viewModel.Id;
    this.crud.delete([id]).subscribe(
      () => {
        // remove from master and total of the global state
        this.globalState.delete([id]);

        // after a successful delete navigate back to the master
        this.router.navigate(['..'], { relativeTo: this.route });
      },
      (friendlyError) => this.handleActionError(friendlyError)
    );
  }

  get canDelete(): boolean {
    // TODO && (!this.canUpdatePred || this.canUpdatePred());
    return !!this.viewModel;
  }

  onNext(): void {
    this.router.navigate(['..', this.getNextId()], { relativeTo: this.route });
  }

  get canNext(): boolean {

    return !!this.getNextId();
  }

  onPrevious(): void {
    this.router.navigate(['..', this.getPreviousId()], { relativeTo: this.route });
  }

  get canPrevious(): boolean {
    return !!this.getPreviousId();
  }

  private getNextId(): number | string {
    const s = this.state;
    const id = this.idString;

    if (!!id) {
      let index = s.masterIds.findIndex(e => e.toString() === id);
      if (index !== -1 && index !== s.masterIds.length - 1) {
        let nextIndex = index + 1;
        let nextId = s.masterIds[nextIndex];
        return nextId;
      }
    }

    return null;
  }

  private getPreviousId(): number | string {
    const s = this.state;
    const id = this.idString;

    if (!!id) {
      let index = s.masterIds.findIndex(e => e.toString() === id);
      if (index > 0) {
        let prevIndex = index - 1;
        let prevId = s.masterIds[prevIndex];
        return prevId;
      }
    }

    return null;
  }

  get total(): number {
    return this.state.total;
  }

  get order(): number {
    const s = this.state;
    const id = this.idString;

    if (!!id) {
      let index = s.masterIds.findIndex(e => e.toString() === id);
      if (index !== -1) {
        return s.skip + index + 1;
      }
    }

    return null;
  }

  public get showNextAndPrevious(): boolean {
    return !!this.order;
  }

  onDocumentDblClick() {
    if (!this.isEdit && this.canEdit) {
      this.onEdit();
    }
  }

  public get flip() {
    // this is to flip the UI icons in RTL
    return this.workspace.ws.isRtl ? 'horizontal' : null;
  }

}
