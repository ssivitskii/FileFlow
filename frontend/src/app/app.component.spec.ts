import { provideHttpClient } from "@angular/common/http";
import {
  HttpTestingController,
  provideHttpClientTesting,
} from "@angular/common/http/testing";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { AppComponent } from "./app.component";

describe("AppComponent", () => {
  let fixture: ComponentFixture<AppComponent>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AppComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(AppComponent);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne("/api/workspace?path=.").flush({
      path: ".",
      entries: [
        { name: "first.txt", path: "first.txt", kind: "file", size: 5 },
        { name: "notes", path: "notes", kind: "directory", size: null },
      ],
    });
    http.expectOne("/api/history").flush([]);
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it("navigates directories and renders hostile preview text literally", () => {
    fixture.componentInstance.openDirectory("notes");
    http
      .expectOne("/api/workspace?path=notes")
      .flush({ path: "notes", entries: [] });
    fixture.componentInstance.openFile("first.txt");
    http.expectOne("/api/files/preview?path=first.txt").flush({
      path: "first.txt",
      text: "<img src=x onerror=alert(1)>",
      bytesRead: 28,
      truncated: false,
    });
    fixture.detectChanges();
    expect(fixture.componentInstance.path).toBe("notes");
    expect(fixture.nativeElement.querySelector("pre").textContent).toContain(
      "<img src=x",
    );
    expect(fixture.nativeElement.querySelector("pre img")).toBeNull();
  });

  it("renders an asynchronous workspace response without manual change detection", async () => {
    fixture.autoDetectChanges();
    fixture.componentInstance.openDirectory("notes");
    http.expectOne("/api/workspace?path=notes").flush({
      path: "notes",
      entries: [
        {
          name: "async.txt",
          path: "notes/async.txt",
          kind: "file",
          size: 5,
        },
      ],
    });

    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain("async.txt");
    expect(fixture.nativeElement.textContent).not.toContain(
      "Reading directory…",
    );
  });

  it("ignores a stale directory response", () => {
    fixture.componentInstance.openDirectory("notes");
    fixture.componentInstance.openDirectory(".");
    const stale = http.expectOne("/api/workspace?path=notes");
    const latest = http.expectOne("/api/workspace?path=.");
    latest.flush({ path: ".", entries: [] });
    stale.flush({
      path: "notes",
      entries: [{ name: "old", path: "notes/old", kind: "file", size: 1 }],
    });
    expect(fixture.componentInstance.path).toBe(".");
    expect(fixture.componentInstance.entries).toEqual([]);
  });

  it("discards a duplicate scan after navigation", () => {
    fixture.componentInstance.scanDuplicates();
    const stale = http.expectOne("/api/duplicates?path=.");
    fixture.componentInstance.openDirectory("notes");
    expect(stale.cancelled).toBe(true);
    http
      .expectOne("/api/workspace?path=notes")
      .flush({ path: "notes", entries: [] });
    fixture.componentInstance.scanDuplicates();
    http.expectOne("/api/duplicates?path=notes").flush({ groups: [] });
    expect(fixture.componentInstance.duplicateGroups).toEqual([]);
    expect(fixture.componentInstance.duplicateState).toBe("empty");
  });

  it("cancels a previous duplicate scan and cleans up on destroy", () => {
    fixture.componentInstance.scanDuplicates();
    const first = http.expectOne("/api/duplicates?path=.");
    fixture.componentInstance.scanDuplicates();
    expect(first.cancelled).toBe(true);
    const second = http.expectOne("/api/duplicates?path=.");
    fixture.destroy();
    expect(second.cancelled).toBe(true);
  });

  it("cancels pending workspace, preview, and history requests on destroy", () => {
    fixture.componentInstance.openDirectory("notes");
    const workspace = http.expectOne("/api/workspace?path=notes");
    fixture.componentInstance.openFile("first.txt");
    const preview = http.expectOne("/api/files/preview?path=first.txt");
    fixture.componentInstance.loadHistory();
    const history = http.expectOne("/api/history");

    fixture.destroy();

    expect(workspace.cancelled).toBe(true);
    expect(preview.cancelled).toBe(true);
    expect(history.cancelled).toBe(true);
  });

  it("cancels and invalidates operation preview when its inputs change", () => {
    fixture.componentInstance.operation = {
      operation: "copy",
      source: "first.txt",
      destination: "copy.txt",
    };
    fixture.componentInstance.previewOperation();
    const stale = http.expectOne("/api/operations/preview");
    fixture.componentInstance.operation.destination = "fresh.txt";
    fixture.componentInstance.operationChanged();
    expect(stale.cancelled).toBe(true);
    expect(fixture.componentInstance.operationResult).toBeNull();
    expect(fixture.componentInstance.operationState).toBe("idle");

    fixture.componentInstance.previewOperation();
    http.expectOne("/api/operations/preview").flush({
      operation: "copy",
      source: "first.txt",
      destination: "fresh.txt",
      isValid: true,
      isConflict: false,
      summary: "Fresh preview.",
      error: null,
    });
    expect(fixture.componentInstance.operationResult?.destination).toBe(
      "fresh.txt",
    );
  });

  it("shows duplicate, history, and operation preview results", () => {
    fixture.componentInstance.scanDuplicates();
    http.expectOne("/api/duplicates?path=.").flush({
      groups: [
        {
          sha256: "ABC",
          size: 5,
          files: [
            { path: "a", size: 5 },
            { path: "b", size: 5 },
          ],
        },
      ],
    });
    fixture.componentInstance.loadHistory();
    http.expectOne("/api/history").flush([
      {
        id: "1",
        time: "2026-09-05T00:00:00Z",
        kind: "copy",
        status: "completed",
        source: "a",
        destination: "b",
      },
    ]);
    fixture.componentInstance.operation = {
      operation: "delete",
      source: "first.txt",
      destination: null,
    };
    fixture.componentInstance.previewOperation();
    const operation = http.expectOne("/api/operations/preview");
    expect(operation.request.body).toEqual({
      operation: "delete",
      source: "first.txt",
      destination: null,
    });
    operation.flush({
      operation: "delete",
      source: "first.txt",
      destination: null,
      isValid: true,
      isConflict: false,
      summary: "Would move to recovery.",
      error: null,
    });
    expect(fixture.componentInstance.duplicateGroups).toHaveLength(1);
    expect(fixture.componentInstance.history).toHaveLength(1);
    expect(fixture.componentInstance.operationResult?.isValid).toBe(true);
  });

  it("surfaces API errors", () => {
    fixture.componentInstance.openFile("first.txt");
    http
      .expectOne("/api/files/preview?path=first.txt")
      .flush(
        { detail: "Only text is supported." },
        { status: 415, statusText: "Unsupported Media Type" },
      );
    expect(fixture.componentInstance.previewState).toBe("error");
    expect(fixture.componentInstance.previewError).toBe(
      "Only text is supported.",
    );
  });
});
