import type { Club } from "./api/models";

const normalizeStatus = (value?: string | null) => value?.trim().toLowerCase() ?? "";

export function isOperationalClub(club: Pick<Club, "status" | "auditStatus">): boolean {
  return (
    normalizeStatus(club.status) === "active" && normalizeStatus(club.auditStatus) === "approved"
  );
}

export function filterOperationalClubs(clubs: Club[]): Club[] {
  return clubs.filter(isOperationalClub);
}

export function isActiveForumClub(club: Pick<Club, "status" | "auditStatus">): boolean {
  return isOperationalClub(club);
}

export function filterForumClubs(clubs: Club[]): Club[] {
  return filterOperationalClubs(clubs);
}
