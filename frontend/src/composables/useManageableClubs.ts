export interface ScopedClubRole {
  code?: string | null;
  roleCode?: string | null;
  clubId?: number | null;
  clubIds?: number[] | null;
  permissions?: string[] | null;
}

export function collectManageableClubIds(
  roles: readonly ScopedClubRole[],
  permission: string | readonly string[],
): Set<number> {
  const clubIds = new Set<number>();
  const permissions = Array.isArray(permission) ? permission : [permission];

  roles
    .filter((role) =>
      role.permissions?.some(
        (rolePermission) => rolePermission === "*" || permissions.includes(rolePermission),
      ),
    )
    .forEach((role) => {
      const ids = role.clubIds?.length ? role.clubIds : role.clubId != null ? [role.clubId] : [];
      ids.forEach((clubId) => clubIds.add(clubId));
    });

  return clubIds;
}

export function roleCoversClub(role: ScopedClubRole, clubId: number) {
  return role.clubId === clubId || Boolean(role.clubIds?.includes(clubId));
}

export function hasScopedRole(
  roles: readonly ScopedClubRole[],
  clubId: number,
  roleCodes: Iterable<string>,
) {
  const normalizedRoleCodes = new Set([...roleCodes].map((roleCode) => roleCode.toLowerCase()));

  return roles.some(
    (role) => roleCoversClub(role, clubId) && normalizedRoleCodes.has(roleCodeOf(role)),
  );
}

function roleCodeOf(role: ScopedClubRole) {
  return (role.roleCode ?? role.code ?? "").toLowerCase();
}
