import { describe, expect, it } from "vitest";
import type { Activity, Notice, Project } from "./api";
import {
  buildDashboardQuickLinks,
  selectAccessibleProjects,
  selectRecentActivities,
  selectUnreadNotices,
} from "./dashboard";

function notice(id: number, isRead = false): Notice {
  return {
    id,
    publisherUserId: 1,
    noticeType: "announcement",
    title: `通知 ${id}`,
    content: "通知内容",
    targetType: "school",
    publishAt: new Date(`2026-08-${String(id).padStart(2, "0")}T08:00:00Z`),
    noticeStatus: "published",
    isRead,
  };
}

function activity(id: number, status: Activity["status"], startTime: string): Activity {
  return {
    id,
    title: `活动 ${id}`,
    clubName: "计算机协会",
    clubId: 1,
    startTime: new Date(startTime),
    endTime: new Date(startTime),
    status,
    currentParticipants: 0,
    isRegistered: false,
  };
}

function project(
  id: number,
  canViewTasks: boolean,
  projectStatus: Project["projectStatus"],
): Project {
  return {
    id,
    clubId: 1,
    projectName: `项目 ${id}`,
    startDate: new Date("2026-08-01T00:00:00Z"),
    projectStatus,
    canViewTasks,
    createdAt: new Date(`2026-08-${String(id).padStart(2, "0")}T00:00:00Z`),
  };
}

describe("Dashboard 数据选择", () => {
  it("只保留最近五条未读已发布通知", () => {
    const source = [notice(1, true), ...Array.from({ length: 7 }, (_, index) => notice(index + 2))];

    expect(selectUnreadNotices(source).map((item) => item.id)).toEqual([8, 7, 6, 5, 4]);
  });

  it("近期活动只包含进行中和未来已发布活动", () => {
    const now = new Date("2026-08-20T00:00:00Z");
    const source = [
      activity(1, "published", "2026-08-19T08:00:00Z"),
      activity(2, "draft", "2026-08-21T08:00:00Z"),
      activity(3, "published", "2026-08-22T08:00:00Z"),
      activity(4, "ongoing", "2026-08-18T08:00:00Z"),
    ];

    expect(selectRecentActivities(source, now).map((item) => item.id)).toEqual([4, 3]);
  });

  it("项目仅展示可进入工作区的记录并优先进行中状态", () => {
    const source = [
      project(1, false, "running"),
      project(2, true, "finished"),
      project(3, true, "running"),
    ];

    expect(selectAccessibleProjects(source).map((item) => item.id)).toEqual([3, 2]);
  });

  it("快捷入口沿用导航权限过滤", () => {
    const ordinaryPaths = buildDashboardQuickLinks([]).map((item) => item.path);
    const managerPaths = buildDashboardQuickLinks(["budget:view"]).map((item) => item.path);

    expect(ordinaryPaths).not.toContain("/budgets");
    expect(managerPaths).toContain("/budgets");
  });
});
