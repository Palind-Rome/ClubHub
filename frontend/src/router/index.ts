import { createRouter, createWebHistory } from "vue-router";
import ClubList from "../views/ClubList.vue";
import ActivityList from "../views/ActivityList.vue";
import RecruitmentList from "../views/RecruitmentList.vue";
import AuthFlow from "../views/AuthFlow.vue";
import NoticeCenter from "../views/NoticeCenter.vue";
import ProjectList from "../views/ProjectList.vue";
import { hasCompletedSession } from "../authSession";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/auth" },
    { path: "/auth", component: AuthFlow },
    { path: "/clubs", component: ClubList },
    { path: "/recruitments", component: RecruitmentList },
    { path: "/recruitments/:recruitmentId/applications", component: RecruitmentList },
    { path: "/activities", component: ActivityList },
    { path: "/notices", component: NoticeCenter },
    { path: "/projects", component: ProjectList },
  ],
});

router.beforeEach((to) => {
  if (to.path !== "/auth" && !hasCompletedSession()) {
    return { path: "/auth", query: { redirect: to.fullPath } };
  }
});

export default router;
