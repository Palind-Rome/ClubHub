import { describe, expect, it } from "vitest";
import { createVenueSearchIndex, formatVenueLocation, matchesVenueSearch } from "./venueSearch";

describe("venue search", () => {
  const index = createVenueSearchIndex({
    ids: [12, "V-021", 12],
    texts: ["四平路校区", "瑞安楼 101", "多媒体 教室"],
  });

  it("normalizes identifiers, case and full-width characters", () => {
    expect(matchesVenueSearch(index, "12")).toBe(true);
    expect(matchesVenueSearch(index, "ｖ－０２１")).toBe(true);
    expect(matchesVenueSearch(index, "四平路 瑞安楼101")).toBe(true);
    expect(matchesVenueSearch(index, "嘉定校区")).toBe(false);
  });

  it("treats an empty query as matching all venues", () => {
    expect(matchesVenueSearch(index, "   ")).toBe(true);
  });

  it("formats optional building and room values", () => {
    expect(formatVenueLocation("瑞安楼", "101")).toBe("瑞安楼 / 101");
    expect(formatVenueLocation("瑞安楼", null)).toBe("瑞安楼");
    expect(formatVenueLocation()).toBe("");
  });
});
