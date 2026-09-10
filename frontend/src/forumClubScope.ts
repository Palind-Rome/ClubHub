import type { Club } from "./api/models";

const normalizeStatus = (value?: string | null) => value?.trim().toLowerCase() ?? "";

export function isOperationalClub<T extends Pick<Club, "status" | "auditStatus">>(
  club: T,
): boolean {
  return (
    normalizeStatus(club.status) === "active" && normalizeStatus(club.auditStatus) === "approved"
  );
}

export function filterOperationalClubs<T extends Pick<Club, "status" | "auditStatus">>(
  clubs: T[],
): T[] {
  return clubs.filter(isOperationalClub);
}

export function isActiveForumClub<T extends Pick<Club, "status" | "auditStatus">>(
  club: T,
): boolean {
  return isOperationalClub(club);
}

export function filterForumClubs(clubs: Club[]): Club[] {
  return filterOperationalClubs(clubs);
}
