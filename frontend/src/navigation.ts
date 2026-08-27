import { BUDGET_ACCESS_PERMISSIONS } from "./budgetPermissions";
import { MATERIAL_ACCESS_PERMISSIONS } from "./materialPermissions";

export type NavigationIcon =
  | "account"
  | "dashboard"
  | "activity"
  | "award"
  | "budget"
  | "club"
  | "evaluation"
  | "forum"
  | "learning"
  | "material"
  | "member"
  | "notice"
  | "organization"
  | "project"
  | "recruitment"
  | "registration"
  | "venue";

export interface NavigationItem {
  label: string;
  path: string;
  icon: NavigationIcon;
}

export interface NavigationGroup {
  label: string;
  items: NavigationItem[];
}

function hasAnyPermission(permissions: string[], required: readonly string[]) {
  return (
    permissions.includes("*") || required.some((permission) => permissions.includes(permission))
  );
}

export function buildNavigationGroups(permissions: string[]): NavigationGroup[] {
  const canAccessClubRegistration = hasAnyPermission(permissions, ["club:apply", "club:review"]);
  const canManageVenues = hasAnyPermission(permissions, [
    "venue:create",
    "venue:update",
    "venue:disable",
  ]);
  const canAccessVenueReservations = hasAnyPermission(permissions, [
    "venue:reserve",
    "venue:review",
  ]);
  const canAccessMaterialBorrows = hasAnyPermission(permissions, MATERIAL_ACCESS_PERMISSIONS);
  const canAccessBudgets = hasAnyPermission(permissions, BUDGET_ACCESS_PERMISSIONS);

  return [
    {
      label: "个人空间",
      items: [
        { label: "答辩工作台", path: "/dashboard", icon: "dashboard" },
        { label: "账号与权限", path: "/auth", icon: "account" },
      ],
    },
    {
      label: "社团运营",
      items: [
        { label: "我的社团", path: "/clubs", icon: "club" },
        { label: "社团架构", path: "/club-organization", icon: "organization" },
        { label: "成员管理", path: "/club-members", icon: "member" },
        ...(canAccessClubRegistration
          ? [{ label: "社团注册", path: "/club-registration", icon: "registration" as const }]
          : []),
        { label: "纳新", path: "/recruitments", icon: "recruitment" },
        { label: "成员考核", path: "/evaluations", icon: "evaluation" },
        { label: "评奖评优", path: "/awards", icon: "award" },
      ],
    },
    {
      label: "协作沟通",
      items: [
        { label: "活动", path: "/activities", icon: "activity" },
        { label: "通知", path: "/notices", icon: "notice" },
        { label: "讨论区", path: "/forum", icon: "forum" },
        { label: "项目", path: "/projects", icon: "project" },
      ],
    },
    {
      label: "资源服务",
      items: [
        ...(canAccessBudgets
          ? [{ label: "经费管理", path: "/budgets", icon: "budget" as const }]
          : []),
        ...(canManageVenues
          ? [{ label: "场地管理", path: "/venues", icon: "venue" as const }]
          : []),
        ...(canAccessVenueReservations
          ? [{ label: "场地预约", path: "/venue-reservations", icon: "venue" as const }]
          : []),
        { label: "学习中心", path: "/learning", icon: "learning" },
        ...(canAccessMaterialBorrows
          ? [{ label: "物资借还", path: "/materials", icon: "material" as const }]
          : []),
      ],
    },
  ];
}

export function resolveActiveNavigation(path: string) {
  const prefixes = [
    "/recruitments",
    "/evaluations",
    "/awards",
    "/learning",
    "/projects",
    "/budgets",
    "/forum",
  ];
  return prefixes.find((prefix) => path.startsWith(prefix)) ?? path;
}
