import {
  HttpClient,
  HttpErrorResponse,
  HttpHeaders,
  HttpParams,
} from "@angular/common/http";
import { Injectable, inject } from "@angular/core";
import { Observable, catchError, throwError } from "rxjs";

export interface WorkspaceEntry {
  name: string;
  path: string;
  kind: "directory" | "file" | "link";
  size: number | null;
}

export interface WorkspaceResponse {
  path: string;
  entries: WorkspaceEntry[];
}

export interface FilePreview {
  path: string;
  text: string;
  bytesRead: number;
  truncated: boolean;
}

export interface DuplicateGroup {
  sha256: string;
  size: number;
  files: { path: string; size: number }[];
}

export interface HistoryEntry {
  id: string;
  time: string;
  kind: string;
  status: string;
  source: string;
  destination: string | null;
}

export interface OperationPreviewRequest {
  operation: "copy" | "move" | "rename" | "delete";
  source: string;
  destination: string | null;
}

export interface OperationPreview extends OperationPreviewRequest {
  isValid: boolean;
  isConflict: boolean;
  summary: string;
  error: string | null;
}

@Injectable({ providedIn: "root" })
export class FileFlowApiService {
  private readonly http = inject(HttpClient);
  private readonly clientHeaders = new HttpHeaders().set(
    "X-FileFlow-Client",
    "web",
  );

  list(path = "."): Observable<WorkspaceResponse> {
    return this.http
      .get<WorkspaceResponse>("/api/workspace", {
        headers: this.clientHeaders,
        params: new HttpParams().set("path", path),
      })
      .pipe(catchError((error: HttpErrorResponse) => this.mapError(error)));
  }

  preview(path: string): Observable<FilePreview> {
    return this.http
      .get<FilePreview>("/api/files/preview", {
        headers: this.clientHeaders,
        params: new HttpParams().set("path", path),
      })
      .pipe(catchError((error: HttpErrorResponse) => this.mapError(error)));
  }

  duplicates(path = "."): Observable<{ groups: DuplicateGroup[] }> {
    return this.http
      .get<{ groups: DuplicateGroup[] }>("/api/duplicates", {
        headers: this.clientHeaders,
        params: new HttpParams().set("path", path),
      })
      .pipe(catchError((error: HttpErrorResponse) => this.mapError(error)));
  }

  history(): Observable<HistoryEntry[]> {
    return this.http
      .get<HistoryEntry[]>("/api/history", { headers: this.clientHeaders })
      .pipe(catchError((error: HttpErrorResponse) => this.mapError(error)));
  }

  previewOperation(
    request: OperationPreviewRequest,
  ): Observable<OperationPreview> {
    return this.http
      .post<OperationPreview>("/api/operations/preview", request, {
        headers: this.clientHeaders,
      })
      .pipe(catchError((error: HttpErrorResponse) => this.mapError(error)));
  }

  private mapError(error: HttpErrorResponse): Observable<never> {
    const detail = (error.error as { detail?: unknown } | null)?.detail;
    return throwError(
      () =>
        new Error(
          typeof detail === "string" ? detail : "FileFlow API is unavailable.",
        ),
    );
  }
}
