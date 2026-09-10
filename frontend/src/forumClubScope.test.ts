import { describe, expect, it } from "vitest";
import type { Club } from "./api/models";
import { filterForumClubs, filterOperationalClubs, isActiveForumClub } from "./forumClubScope";
import budgetManagementSource from "./views/BudgetManagement.vue?raw";
import clubListSource from "./views/ClubList.vue?raw";
import forumCenterSource from "./views/ForumCenter.vue?raw";
import materialBorrowSource from "./views/MaterialBorrow.vue?raw";
import projectListSource from "./views/ProjectList.vue?raw";
import projectMembersPanelSource from "./components/ProjectMembersPanel.vue?raw";

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
    expect(filterOperationalClubs(clubs).map((item) => item.id)).toEqual([1]);
  });

  it("normalizes status casing and surrounding whitespace", () => {
    expect(isActiveForumClub(club(1, " Active ", " APPROVED "))).toBe(true);
  });

  it("rejects missing statuses instead of exposing an unverified club", () => {
    expect(isActiveForumClub(club(1, null, "approved"))).toBe(false);
    expect(isActiveForumClub(club(2, "active", null))).toBe(false);
  });

  it("clears stale forum state when no approved active club remains", () => {
    expect(filterForumClubs([club(1, "pending", "pending")])).toEqual([]);
    expect(forumCenterSource).toContain("if (availableClubs.length === 0)");
    expect(forumCenterSource).toContain("topics.value = []");
    expect(forumCenterSource).toContain("loadError.value = null");
  });

  it("wires the same operational-club filter into project selectors", () => {
    expect(projectListSource).toContain("filterOperationalClubs");
  });

  it("keeps dissolved clubs out of operational workspaces and identities", () => {
    expect(budgetManagementSource).toContain("filterOperationalClubs");
    expect(materialBorrowSource).toContain("filterOperationalClubs");
    expect(clubListSource).toContain("filterOperationalClubs(clubData)");
    expect(clubListSource).toContain("operationalClubIds");
  });

  it("shows teacher candidates only for the mentor role", () => {
    expect(projectMembersPanelSource).toContain("isTeacherCandidate");
    expect(projectMembersPanelSource).toContain("candidates.value.filter(isTeacherCandidate)");
    expect(projectMembersPanelSource).toContain(
      "candidates.value.filter((candidate) => !isTeacherCandidate(candidate))",
    );
  });
});
