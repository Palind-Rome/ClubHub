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
import { hasCompletedSession, readAuth } from "../authSession";
import { MATERIAL_ACCESS_PERMISSIONS } from "../materialPermissions";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/auth" },
    { path: "/auth", component: AuthFlow },
    { path: "/clubs", component: ClubList },
    { path: "/club-organization", component: ClubList, props: { workspace: "organization" } },
    { path: "/club-members", component: ClubList, props: { workspace: "members" } },
    { path: "/club-registration", component: ClubList, props: { workspace: "registration" } },
    { path: "/recruitments", component: RecruitmentList },
    { path: "/recruitments/:recruitmentId/applications", component: RecruitmentList },
    { path: "/evaluations", component: EvaluationList },
    { path: "/awards", component: AwardList },
    { path: "/activities", component: ActivityList },
    { path: "/notices", component: NoticeCenter },
    { path: "/projects", component: ProjectList },
    { path: "/projects/:projectId/workspace", component: ProjectWorkspace },
    { path: "/venues", component: VenueManage },
    { path: "/venue-reservations", component: VenueReservationApply },
    { path: "/learning", component: LearningCenter },
    { path: "/materials", component: MaterialBorrow },
  ],
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
});

export default router;
