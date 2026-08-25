import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  attachIdempotencyKey,
  finishIdempotencyAttempt,
  resetIdempotencyAttemptsForTests,
} from "./apiClient";

describe("idempotency request middleware", () => {
  beforeEach(() => {
    resetIdempotencyAttemptsForTests();
    let sequence = 0;
    vi.stubGlobal("crypto", {
      randomUUID: () => `00000000-0000-4000-8000-${String(++sequence).padStart(12, "0")}`,
    });
  });

  it("reuses the same key for the same in-flight request fingerprint", () => {
    const first = attachIdempotencyKey(
      "/api/v1/projects/1/tasks/2/deliverable",
      requestInit('{"title":"first"}'),
    );
    const second = attachIdempotencyKey(
      "/api/v1/projects/1/tasks/2/deliverable",
      requestInit('{"title":"first"}'),
    );

    expect(key(first)).toBeTruthy();
    expect(key(second)).toBe(key(first));
  });

  it.each([409, 500, 503])("retains the key after retryable status %s", (status) => {
    const first = attachIdempotencyKey(
      "/api/v1/material-borrows/3/return",
      requestInit('{"condition":"good"}'),
    );
    finishIdempotencyAttempt(first, new Response(null, { status }));
    const retry = attachIdempotencyKey(
      "/api/v1/material-borrows/3/return",
      requestInit('{"condition":"good"}'),
    );

    expect(key(retry)).toBe(key(first));
  });

  it("clears the key after a completed non-retryable response", () => {
    const first = attachIdempotencyKey(
      "/api/v1/activities/4/registrations",
      requestInit('{"remark":"join"}'),
    );
    finishIdempotencyAttempt(first, new Response(null, { status: 201 }));
    const nextSubmission = attachIdempotencyKey(
      "/api/v1/activities/4/registrations",
      requestInit('{"remark":"join"}'),
    );

    expect(key(nextSubmission)).not.toBe(key(first));
  });
});

function requestInit(body: string): RequestInit {
  return { method: "POST", body };
}

function key(init: RequestInit) {
  return new Headers(init.headers).get("Idempotency-Key");
}
