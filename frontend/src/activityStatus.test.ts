import { describe, expect, it } from "vitest";
import { beijingStoredDateTimeTimestamp } from "./beijingTime";
import { resolveActivityDisplayStatus } from "./activityStatus";

const at = (value: string) => beijingStoredDateTimeTimestamp(value);

describe("activity display status", () => {
  it("marks a published activity as registration closed after its deadline", () => {
    expect(
      resolveActivityDisplayStatus(
        {
          status: "published",
          startTime: "2026-09-12T10:00:00",
          endTime: "2026-09-12T12:00:00",
          registrationDeadline: "2026-09-11T23:59:00",
        },
        at("2026-09-12T08:00:00"),
      ),
    ).toBe("registration_closed");
  });

  it("marks a published activity as ongoing after it starts", () => {
    expect(
      resolveActivityDisplayStatus(
        {
          status: "published",
          startTime: "2026-09-12T10:00:00",
          endTime: "2026-09-12T12:00:00",
          registrationDeadline: "2026-09-12T09:00:00",
        },
        at("2026-09-12T10:30:00"),
      ),
    ).toBe("ongoing");
  });

  it("marks a stale published row as finished after the event ends", () => {
    expect(
      resolveActivityDisplayStatus(
        {
          status: "published",
          startTime: "2026-09-12T10:00:00",
          endTime: "2026-09-12T12:00:00",
          registrationDeadline: "2026-09-12T09:00:00",
        },
        at("2026-09-12T12:00:01"),
      ),
    ).toBe("finished");
  });

  it("preserves workflow statuses that have no time based override", () => {
    expect(
      resolveActivityDisplayStatus({ status: "pending_review" }, at("2026-09-12T10:00:00")),
    ).toBe("pending_review");
    expect(resolveActivityDisplayStatus({ status: "ongoing" }, at("2026-09-12T10:00:00"))).toBe(
      "ongoing",
    );
  });
});
