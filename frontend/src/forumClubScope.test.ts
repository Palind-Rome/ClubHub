import { describe, expect, it } from "vitest";
import type { Club } from "./api/models";
import { filterForumClubs, isActiveForumClub } from "./forumClubScope";

const club = (id: number, status: string | null, auditStatus: string | null): Club => ({
  id,
  name: `Club ${id}`,
  status,
  auditStatus,
  statusText: status ?? "",
  auditStatusText: auditStatus ?? "",
  createdAt: new Date("2026-01-01T00:00:00Z"),
});

describe("forum club scope", () => {
  it("keeps only approved clubs that are currently active", () => {
    const clubs = [
      club(1, "active", "approved"),
      club(2, "pending", "pending"),
      club(3, "active", "pending"),
      club(4, "inactive", "approved"),
      club(5, "rejected", "rejected"),
    ];

    expect(filterForumClubs(clubs).map((item) => item.id)).toEqual([1]);
  });

  it("normalizes status casing and surrounding whitespace", () => {
    expect(isActiveForumClub(club(1, " Active ", " APPROVED "))).toBe(true);
  });

  it("rejects missing statuses instead of exposing an unverified club", () => {
    expect(isActiveForumClub(club(1, null, "approved"))).toBe(false);
    expect(isActiveForumClub(club(2, "active", null))).toBe(false);
  });
});
