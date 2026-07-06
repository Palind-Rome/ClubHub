import { createRouter, createWebHistory } from "vue-router";
import ClubList from "../views/ClubList.vue";
import ActivityList from "../views/ActivityList.vue";

const router = createRouter({
  history: createWebHistory(),
  routes: [
    { path: "/", redirect: "/clubs" },
    { path: "/clubs", component: ClubList },
    { path: "/activities", component: ActivityList },
  ],
});

export default router;
