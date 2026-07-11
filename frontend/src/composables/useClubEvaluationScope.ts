import { normalizedRoleCodeOf, roleCoversClub, type ScopedClubRole } from "./useManageableClubs";

export interface MemberGroupingScope {
  departmentName: string;
  groupName: string;
  label?: string;
}

export interface ScopedMemberLike {
  clubId: number;
  userId: number;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  isCurrent?: boolean | null;
}

export interface ScopedMembershipLike {
  clubId: number;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  memberStatus?: string | null;
}

const wholeClubMaintainRoleCodes = new Set([
  "advisor",
  "club_leader",
  "club_manager",
  "club_president",
  "president",
]);

const globalViewRoleCodes = new Set([
  "admin",
  "club_admin",
  "club_reviewer",
  "platform_admin",
  "system_admin",
  "sysadmin",
]);

const officerRoleCodes = new Set(["club_officer"]);

const cadrePositionNames = new Set([
  "干部",
  "部长",
  "副部长",
  "组长",
  "副组长",
  "干事",
  "社团干部",
  "部门负责人",
  "小组负责人",
  "officer",
  "cadre",
  "minister",
  "group leader",
]);

const departmentManagerPositionNames = new Set(["部长", "副部长", "部门负责人", "minister"]);

export function hasGlobalClubViewRole(roles: readonly ScopedClubRole[]) {
  return roles.some((role) => globalViewRoleCodes.has(normalizedRoleCodeOf(role)));
}

export function hasWholeClubMaintainRole(roles: readonly ScopedClubRole[], clubId: number) {
  return roles.some(
    (role) => roleCoversClub(role, clubId) && wholeClubMaintainRoleCodes.has(roleCodeOf(role)),
  );
}

export function hasClubOfficerRole(roles: readonly ScopedClubRole[], clubId: number) {
  return roles.some(
    (role) => roleCoversClub(role, clubId) && officerRoleCodes.has(roleCodeOf(role)),
  );
}

export function collectCadreScopesFromMembers(
  members: readonly ScopedMemberLike[],
  roles: readonly ScopedClubRole[],
  clubId: number,
  currentUserId: number,
) {
  const hasOfficerRole = hasClubOfficerRole(roles, clubId);
  const scopeMap = new Map<string, MemberGroupingScope>();

  members
    .filter(
      (member) =>
        member.clubId === clubId &&
        member.userId === currentUserId &&
        member.isCurrent === true &&
        Boolean(member.groupName?.trim()) &&
        (hasOfficerRole || isCadrePosition(member.positionName)),
    )
    .forEach((member) => {
      addScope(scopeMap, member.departmentName, member.groupName);
    });

  return Array.from(scopeMap.values());
}

export function collectCadreScopesFromMemberships(
  memberships: readonly ScopedMembershipLike[],
  roles: readonly ScopedClubRole[],
  clubId: number,
) {
  const hasOfficerRole = hasClubOfficerRole(roles, clubId);
  const scopeMap = new Map<string, MemberGroupingScope>();

  memberships
    .filter(
      (membership) =>
        membership.clubId === clubId &&
        isActiveMemberStatus(membership.memberStatus) &&
        (hasOfficerRole || isCadrePosition(membership.positionName)) &&
        (Boolean(membership.groupName?.trim()) ||
          (isDepartmentManagerPosition(membership.positionName) &&
            Boolean(membership.departmentName?.trim()))),
    )
    .forEach((membership) => {
      const groupName = isDepartmentManagerPosition(membership.positionName)
        ? ""
        : (membership.groupName ?? "");
      addScope(scopeMap, membership.departmentName, groupName);
    });

  return Array.from(scopeMap.values());
}

export function canMaintainScopedMember(
  member: Pick<ScopedMemberLike, "departmentName" | "groupName" | "isCurrent">,
  options: {
    canMaintainSelectedClub: boolean;
    canMaintainWholeClub: boolean;
    scopes: readonly MemberGroupingScope[];
  },
) {
  if (member.isCurrent !== true || !options.canMaintainSelectedClub) return false;
  if (options.canMaintainWholeClub) return true;

  return options.scopes.some((scope) =>
    groupingMatchesScope(
      member.departmentName,
      member.groupName,
      scope.departmentName,
      scope.groupName,
    ),
  );
}

export function groupingMatchesScope(
  targetDepartment: string | null | undefined,
  targetGroup: string | null | undefined,
  scopeDepartment: string | null | undefined,
  scopeGroup: string | null | undefined,
) {
  if (!scopeGroup?.trim()) {
    return (
      Boolean(scopeDepartment?.trim()) &&
      normalizeText(targetDepartment) === normalizeText(scopeDepartment)
    );
  }

  if (!targetGroup?.trim()) return false;

  const groupMatches = normalizeText(targetGroup) === normalizeText(scopeGroup);
  const departmentMatches =
    !scopeDepartment?.trim() || normalizeText(targetDepartment) === normalizeText(scopeDepartment);
  return groupMatches && departmentMatches;
}

export function isCadrePosition(positionName: string | null | undefined) {
  if (!positionName) return false;
  const trimmed = positionName.trim();
  return cadrePositionNames.has(trimmed.toLowerCase()) || cadrePositionNames.has(trimmed);
}

export function isDepartmentManagerPosition(positionName: string | null | undefined) {
  if (!positionName) return false;
  return departmentManagerPositionNames.has(positionName.trim().toLowerCase());
}

export function isActiveMemberStatus(status: string | null | undefined) {
  if (!status) return true;
  return ["active", "normal", "enabled", "在任", "正常"].includes(status.trim().toLowerCase());
}

function addScope(
  scopeMap: Map<string, MemberGroupingScope>,
  departmentName: string | null | undefined,
  groupName: string | null | undefined,
) {
  const department = departmentName?.trim() ?? "";
  const group = groupName?.trim() ?? "";
  const key = `${department}\n${group}`;
  scopeMap.set(key, {
    departmentName: department,
    groupName: group,
    label: group ? (department ? `${department} / ${group}` : group) : department,
  });
}

function roleCodeOf(role: ScopedClubRole) {
  return normalizedRoleCodeOf(role);
}

function normalizeText(value: string | null | undefined) {
  return (value ?? "").trim().toLowerCase();
}
