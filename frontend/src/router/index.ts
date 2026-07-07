import { createRouter, createWebHistory } from "vue-router";
import ClubList from "../views/ClubList.vue";
import ActivityList from "../views/ActivityList.vue";
import AuthFlow from "../views/AuthFlow.vue";
import { hasCompletedSession } from "../authSession";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/auth" },
    { path: "/auth", component: AuthFlow },
    { path: "/clubs", component: ClubList },
    { path: "/activities", component: ActivityList },
  ],
});

router.beforeEach((to) => {
  if (to.path !== "/auth" && !hasCompletedSession()) {
    return { path: "/auth", query: { redirect: to.fullPath } };
  }
});

export default router;
