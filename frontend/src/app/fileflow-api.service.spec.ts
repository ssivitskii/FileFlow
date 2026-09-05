import { provideHttpClient } from "@angular/common/http";
import {
  HttpTestingController,
  provideHttpClientTesting,
} from "@angular/common/http/testing";
import { TestBed } from "@angular/core/testing";
import { FileFlowApiService } from "./fileflow-api.service";

describe("FileFlowApiService", () => {
  let api: FileFlowApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(FileFlowApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it("uses root-relative query contracts", () => {
    api.list("notes").subscribe();
    const list = http.expectOne("/api/workspace?path=notes");
    expect(list.request.method).toBe("GET");
    expect(list.request.headers.get("X-FileFlow-Client")).toBe("web");
    list.flush({ path: "notes", entries: [] });

    api.preview("notes/a.txt").subscribe();
    const preview = http.expectOne("/api/files/preview?path=notes/a.txt");
    expect(preview.request.method).toBe("GET");
    expect(preview.request.headers.get("X-FileFlow-Client")).toBe("web");
    preview.flush({
      path: "notes/a.txt",
      text: "a",
      bytesRead: 1,
      truncated: false,
    });

    api.duplicates(".").subscribe();
    const duplicates = http.expectOne("/api/duplicates?path=.");
    expect(duplicates.request.method).toBe("GET");
    expect(duplicates.request.headers.get("X-FileFlow-Client")).toBe("web");
    duplicates.flush({ groups: [] });
  });

  it("posts only the preview operation contract and maps ProblemDetails", () => {
    const body = {
      operation: "rename" as const,
      source: "a.txt",
      destination: "b.txt",
    };
    let message = "";
    api
      .previewOperation(body)
      .subscribe({ error: (error: Error) => (message = error.message) });
    const request = http.expectOne("/api/operations/preview");
    expect(request.request.method).toBe("POST");
    expect(request.request.headers.get("X-FileFlow-Client")).toBe("web");
    expect(request.request.body).toEqual(body);
    request.flush(
      { detail: "Safe detail." },
      { status: 400, statusText: "Bad Request" },
    );
    expect(message).toBe("Safe detail.");
  });

  it("loads redacted history", () => {
    api.history().subscribe();
    const request = http.expectOne("/api/history");
    expect(request.request.method).toBe("GET");
    expect(request.request.headers.get("X-FileFlow-Client")).toBe("web");
    request.flush([]);
  });
});
