import type { Activity, Notice, Project } from "./api";
import { buildNavigationGroups, type NavigationItem } from "./navigation";

export const DASHBOARD_ITEM_LIMIT = 5;

export function buildDashboardQuickLinks(permissions: string[], limit = 6): NavigationItem[] {
  const groupedItems = buildNavigationGroups(permissions)
    .map((group) => group.items.filter((item) => !["/dashboard", "/auth"].includes(item.path)))
    .filter((items) => items.length > 0);
  const featuredItems = groupedItems.map((items) => items[0]);
  const remainingItems = groupedItems.flatMap((items) => items.slice(1));

  return [...featuredItems, ...remainingItems].slice(0, limit);
}

export function selectUnreadNotices(notices: Notice[], limit = DASHBOARD_ITEM_LIMIT): Notice[] {
  return notices
    .filter((notice) => !notice.isRead && notice.noticeStatus === "published")
    .sort((left, right) => timestamp(right.publishAt) - timestamp(left.publishAt))
    .slice(0, limit);
}

export function selectRecentActivities(
  activities: Activity[],
  now = new Date(),
  limit = DASHBOARD_ITEM_LIMIT,
): Activity[] {
  return activities
    .filter((activity) => {
      if (activity.status === "ongoing") return true;
      return activity.status === "published" && timestamp(activity.startTime) >= now.getTime();
    })
    .sort(
      (left, right) =>
        timestamp(left.startTime, Number.MAX_SAFE_INTEGER) -
        timestamp(right.startTime, Number.MAX_SAFE_INTEGER),
    )
    .slice(0, limit);
}

export function selectAccessibleProjects(
  projects: Project[],
  limit = DASHBOARD_ITEM_LIMIT,
): Project[] {
  const statusOrder: Record<string, number> = {
    running: 0,
    delayed: 1,
    pending: 2,
    finished: 3,
    closed: 4,
  };

  return projects
    .filter((project) => project.canViewTasks)
    .sort((left, right) => {
      const statusDifference =
        (statusOrder[left.projectStatus] ?? 99) - (statusOrder[right.projectStatus] ?? 99);
      if (statusDifference !== 0) return statusDifference;
      return timestamp(right.createdAt) - timestamp(left.createdAt);
    })
    .slice(0, limit);
}

function timestamp(value?: Date | null, fallback = 0) {
  if (!value) return fallback;
  const result = value.getTime();
  return Number.isNaN(result) ? fallback : result;
}
