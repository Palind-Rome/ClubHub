import { describe, expect, it } from "vitest";
import {
  beijingCalendarDateTimestamp,
  beijingDateTimeTimestamp,
  beijingDateTimeToUtcIso,
  formatVenueReservationDateTime,
  venueReservationTimestamp,
} from "./beijingTime";

describe("Beijing time helpers", () => {
  it("converts a Beijing local date-time to UTC", () => {
    expect(beijingDateTimeToUtcIso("2026-07-21T08:30")).toBe("2026-07-21T00:30:00.000Z");
    expect(beijingDateTimeTimestamp("2026-07-21 08:30:15.25")).toBe(
      Date.parse("2026-07-21T00:30:15.250Z"),
    );
  });

  it("rejects values that are not local date-time inputs", () => {
    expect(Number.isNaN(beijingDateTimeTimestamp("2026-07-21"))).toBe(true);
    expect(beijingDateTimeToUtcIso("not-a-date")).toBe("not-a-date");
  });

  it("formats API timestamps consistently in Asia/Shanghai", () => {
    expect(formatVenueReservationDateTime("2026-07-21T00:30:00Z")).toBe("2026-07-21 08:30");
    expect(venueReservationTimestamp("2026-07-21T00:30:00Z")).toBe(
      Date.parse("2026-07-21T00:30:00Z"),
    );
  });

  it("uses the Beijing calendar date regardless of the machine timezone", () => {
    expect(beijingCalendarDateTimestamp(new Date("2026-07-20T16:30:00Z"))).toBe(
      Date.parse("2026-07-20T16:00:00Z"),
    );
  });
});
