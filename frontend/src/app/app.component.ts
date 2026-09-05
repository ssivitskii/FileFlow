import { CommonModule } from "@angular/common";
import {
  ChangeDetectorRef,
  Component,
  DestroyRef,
  OnDestroy,
  OnInit,
  inject,
} from "@angular/core";
import { takeUntilDestroyed } from "@angular/core/rxjs-interop";
import { FormsModule } from "@angular/forms";
import { Subscription } from "rxjs";
import {
  DuplicateGroup,
  FileFlowApiService,
  FilePreview,
  HistoryEntry,
  OperationPreview,
  OperationPreviewRequest,
  WorkspaceEntry,
} from "./fileflow-api.service";

type ViewState = "idle" | "loading" | "ready" | "empty" | "error";

@Component({
  selector: "app-root",
  imports: [CommonModule, FormsModule],
  templateUrl: "./app.component.html",
})
export class AppComponent implements OnInit, OnDestroy {
  private readonly api = inject(FileFlowApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly destroyRef = inject(DestroyRef);
  private listRequest = 0;
  private previewRequest = 0;
  private duplicateRequest = 0;
  private historyRequest = 0;
  private operationRequest = 0;
  private duplicateSubscription?: Subscription;
  private operationSubscription?: Subscription;

  path = ".";
  entries: WorkspaceEntry[] = [];
  listState: ViewState = "idle";
  listError = "";
  preview: FilePreview | null = null;
  previewState: ViewState = "idle";
  previewError = "";
  duplicateGroups: DuplicateGroup[] = [];
  duplicateState: ViewState = "idle";
  duplicateError = "";
  history: HistoryEntry[] = [];
  historyState: ViewState = "idle";
  historyError = "";
  operation: OperationPreviewRequest = {
    operation: "copy",
    source: "",
    destination: "",
  };
  operationResult: OperationPreview | null = null;
  operationState: ViewState = "idle";
  operationError = "";

  get breadcrumbs(): { label: string; path: string }[] {
    if (this.path === ".") return [{ label: "Workspace", path: "." }];
    const segments = this.path.split("/");
    return [
      { label: "Workspace", path: "." },
      ...segments.map((segment, index) => ({
        label: segment,
        path: segments.slice(0, index + 1).join("/"),
      })),
    ];
  }

  ngOnInit(): void {
    this.openDirectory(".");
    this.loadHistory();
  }

  ngOnDestroy(): void {
    ++this.listRequest;
    ++this.previewRequest;
    ++this.historyRequest;
    this.cancelDuplicateScan();
    this.cancelOperationPreview();
  }

  open(entry: WorkspaceEntry): void {
    if (entry.kind === "directory") this.openDirectory(entry.path);
    if (entry.kind === "file") this.openFile(entry.path);
  }

  openDirectory(path: string): void {
    const request = ++this.listRequest;
    ++this.previewRequest;
    this.cancelDuplicateScan();
    this.listState = "loading";
    this.listError = "";
    this.preview = null;
    this.previewState = "idle";
    this.api
      .list(path)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (request !== this.listRequest) return;
          this.path = response.path;
          this.entries = response.entries;
          this.listState = response.entries.length === 0 ? "empty" : "ready";
          this.changeDetector.markForCheck();
        },
        error: (error: Error) => {
          if (request !== this.listRequest) return;
          this.listState = "error";
          this.listError = error.message;
          this.changeDetector.markForCheck();
        },
      });
  }

  openFile(path: string): void {
    const request = ++this.previewRequest;
    this.previewState = "loading";
    this.previewError = "";
    this.preview = null;
    this.operation.source = path;
    this.operationChanged();
    this.api
      .preview(path)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (preview) => {
          if (request !== this.previewRequest) return;
          this.preview = preview;
          this.previewState = "ready";
          this.changeDetector.markForCheck();
        },
        error: (error: Error) => {
          if (request !== this.previewRequest) return;
          this.previewState = "error";
          this.previewError = error.message;
          this.changeDetector.markForCheck();
        },
      });
  }

  scanDuplicates(): void {
    this.cancelDuplicateScan();
    const request = ++this.duplicateRequest;
    this.duplicateState = "loading";
    this.duplicateError = "";
    this.duplicateSubscription = this.api
      .duplicates(this.path)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ groups }) => {
          if (request !== this.duplicateRequest) return;
          this.duplicateGroups = groups;
          this.duplicateState = groups.length === 0 ? "empty" : "ready";
          this.changeDetector.markForCheck();
        },
        error: (error: Error) => {
          if (request !== this.duplicateRequest) return;
          this.duplicateState = "error";
          this.duplicateError = error.message;
          this.changeDetector.markForCheck();
        },
      });
  }

  loadHistory(): void {
    const request = ++this.historyRequest;
    this.historyState = "loading";
    this.historyError = "";
    this.api
      .history()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (history) => {
          if (request !== this.historyRequest) return;
          this.history = history;
          this.historyState = history.length === 0 ? "empty" : "ready";
          this.changeDetector.markForCheck();
        },
        error: (error: Error) => {
          if (request !== this.historyRequest) return;
          this.historyState = "error";
          this.historyError = error.message;
          this.changeDetector.markForCheck();
        },
      });
  }

  previewOperation(): void {
    if (!this.operation.source.trim()) return;
    const request: OperationPreviewRequest = {
      operation: this.operation.operation,
      source: this.operation.source.trim(),
      destination:
        this.operation.operation === "delete"
          ? null
          : this.operation.destination?.trim() || null,
    };
    this.operationSubscription?.unsubscribe();
    const requestId = ++this.operationRequest;
    const fingerprint = this.operationFingerprint();
    this.operationState = "loading";
    this.operationError = "";
    this.operationResult = null;
    this.operationSubscription = this.api
      .previewOperation(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          if (
            requestId !== this.operationRequest ||
            fingerprint !== this.operationFingerprint()
          )
            return;
          this.operationResult = result;
          this.operationState = "ready";
          this.changeDetector.markForCheck();
        },
        error: (error: Error) => {
          if (
            requestId !== this.operationRequest ||
            fingerprint !== this.operationFingerprint()
          )
            return;
          this.operationState = "error";
          this.operationError = error.message;
          this.changeDetector.markForCheck();
        },
      });
  }

  operationChanged(): void {
    this.cancelOperationPreview();
    this.operationResult = null;
    this.operationError = "";
    this.operationState = "idle";
  }

  private cancelDuplicateScan(): void {
    this.duplicateSubscription?.unsubscribe();
    this.duplicateSubscription = undefined;
    ++this.duplicateRequest;
    this.duplicateGroups = [];
    this.duplicateError = "";
    this.duplicateState = "idle";
  }

  private cancelOperationPreview(): void {
    this.operationSubscription?.unsubscribe();
    this.operationSubscription = undefined;
    ++this.operationRequest;
  }

  private operationFingerprint(): string {
    return JSON.stringify(this.operation);
  }
}
