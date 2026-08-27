import { createRouter, createWebHistory } from "vue-router";
import ClubList from "../views/ClubList.vue";
import ActivityList from "../views/ActivityList.vue";
import RecruitmentList from "../views/RecruitmentList.vue";
import EvaluationList from "../views/EvaluationList.vue";
import AwardList from "../views/AwardList.vue";
import AuthFlow from "../views/AuthFlow.vue";
import NoticeCenter from "../views/NoticeCenter.vue";
import ProjectList from "../views/ProjectList.vue";
import ProjectWorkspace from "../views/ProjectWorkspace.vue";
import VenueManage from "../views/VenueManage.vue";
import VenueReservationApply from "../views/VenueReservationApply.vue";
import LearningCenter from "../views/LearningCenter.vue";
import MaterialBorrow from "../views/MaterialBorrow.vue";
import BudgetManagement from "../views/BudgetManagement.vue";
import ForumCenter from "../views/ForumCenter.vue";
import DashboardHome from "../views/DashboardHome.vue";
import { hasCompletedSession, readAuth } from "../authSession";
import { BUDGET_ACCESS_PERMISSIONS } from "../budgetPermissions";
import { MATERIAL_ACCESS_PERMISSIONS } from "../materialPermissions";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/dashboard" },
    {
      path: "/dashboard",
      component: DashboardHome,
      meta: { title: "答辩工作台", eyebrow: "Defense Dashboard" },
    },
    {
      path: "/auth",
      component: AuthFlow,
      meta: { title: "账号与权限", eyebrow: "Account & Access" },
    },
    { path: "/clubs", component: ClubList, meta: { title: "我的社团", eyebrow: "Club Workspace" } },
    {
      path: "/club-organization",
      component: ClubList,
      props: { workspace: "organization" },
      meta: { title: "社团架构", eyebrow: "Organization Structure" },
    },
    {
      path: "/club-members",
      component: ClubList,
      props: { workspace: "members" },
      meta: { title: "成员管理", eyebrow: "Member Governance" },
    },
    {
      path: "/club-registration",
      component: ClubList,
      props: { workspace: "registration" },
      meta: { title: "社团注册", eyebrow: "Club Registration" },
    },
    {
      path: "/recruitments",
      component: RecruitmentList,
      meta: { title: "纳新管理", eyebrow: "Recruitment Workflow" },
    },
    {
      path: "/recruitments/:recruitmentId/applications",
      component: RecruitmentList,
      meta: { title: "纳新申请", eyebrow: "Recruitment Applications" },
    },
    {
      path: "/evaluations",
      component: EvaluationList,
      meta: { title: "成员考核", eyebrow: "Member Evaluation" },
    },
    {
      path: "/awards",
      component: AwardList,
      meta: { title: "评奖评优", eyebrow: "Awards Workflow" },
    },
    {
      path: "/activities",
      component: ActivityList,
      meta: { title: "活动中心", eyebrow: "Activity Operations" },
    },
    {
      path: "/notices",
      component: NoticeCenter,
      meta: { title: "通知中心", eyebrow: "Notice Center" },
    },
    {
      path: "/projects",
      component: ProjectList,
      meta: { title: "项目协作", eyebrow: "Project Collaboration" },
    },
    {
      path: "/projects/:projectId/workspace",
      component: ProjectWorkspace,
      meta: { title: "项目工作区", eyebrow: "Project Workspace" },
    },
    {
      path: "/venues",
      component: VenueManage,
      meta: { title: "场地管理", eyebrow: "Venue Management" },
    },
    {
      path: "/venue-reservations",
      component: VenueReservationApply,
      meta: { title: "场地预约", eyebrow: "Venue Reservation" },
    },
    {
      path: "/learning",
      component: LearningCenter,
      meta: { title: "学习中心", eyebrow: "Learning Center" },
    },
    {
      path: "/materials",
      component: MaterialBorrow,
      meta: { title: "物资借还", eyebrow: "Material Operations" },
    },
    {
      path: "/budgets",
      component: BudgetManagement,
      meta: { title: "经费管理", eyebrow: "Budget Workflow" },
    },
    {
      path: "/forum",
      component: ForumCenter,
      meta: { title: "讨论区", eyebrow: "Community Forum" },
    },
  ],
});

router.afterEach((to) => {
  const title = typeof to.meta.title === "string" ? to.meta.title : "ClubHub";
  document.title = title === "ClubHub" ? title : `${title} · ClubHub`;
});

router.beforeEach((to) => {
  if (to.path !== "/auth" && !hasCompletedSession()) {
    return { path: "/auth", query: { redirect: to.fullPath } };
  }

  if (to.path === "/venue-reservations") {
    const permissions = readAuth()?.permissions ?? [];
    const canAccess = ["*", "venue:reserve", "venue:review"].some((permission) =>
      permissions.includes(permission),
    );
    if (!canAccess) return { path: "/clubs" };
  }

  if (to.path === "/materials") {
    const permissions = readAuth()?.permissions ?? [];
    const canAccess =
      permissions.includes("*") ||
      MATERIAL_ACCESS_PERMISSIONS.some((permission) => permissions.includes(permission));
    if (!canAccess) return { path: "/clubs" };
  }

  if (to.path.startsWith("/budgets")) {
    const permissions = readAuth()?.permissions ?? [];
    const canAccess =
      permissions.includes("*") ||
      BUDGET_ACCESS_PERMISSIONS.some((permission) => permissions.includes(permission));
    if (!canAccess) return { path: "/clubs" };
  }
});

export default router;
