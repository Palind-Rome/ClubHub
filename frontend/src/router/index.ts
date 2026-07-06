import { createRouter, createWebHistory } from "vue-router";
import ClubList from "../views/ClubList.vue";
import ActivityList from "../views/ActivityList.vue";
import AuthPanel from "../views/AuthPanel.vue";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/auth" },
    { path: "/auth", component: AuthPanel },
    { path: "/clubs", component: ClubList },
    { path: "/activities", component: ActivityList },
  ],
});

export default router;
