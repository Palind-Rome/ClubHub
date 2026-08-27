<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import zhCn from "element-plus/es/locale/lang/zh-cn";
import { ElMessage } from "element-plus";
import { type AuthResponse, clearSession, onSessionChange, readAuth } from "./authSession";
import { apiClient } from "./apiClient";
import { buildNavigationGroups, resolveActiveNavigation } from "./navigation";
import { initializeTheme } from "./composables/useTheme";
import AppShell from "./components/shell/AppShell.vue";

const healthOk = ref(false);
const healthChecking = ref(false);
const route = useRoute();
const router = useRouter();
const auth = ref<AuthResponse | null>(null);
const sessionResolved = ref(false);
let stopSessionListener: (() => void) | null = null;

initializeTheme();

const hasSession = computed(() => Boolean(auth.value));
const accountLabel = computed(() => {
  const user = auth.value?.user;
  if (!user) return "账号与权限";
  return user.studentNo ? `${user.realName} / ${user.studentNo}` : user.realName;
});
const roleLabels = computed(() => {
  const roles = auth.value?.roles ?? [];
  if (roles.length === 0) return ["暂无角色"];
  return roles.map((role) => role.displayName || role.name);
});
const navigationGroups = computed(() => buildNavigationGroups(auth.value?.permissions ?? []));
const activeMenu = computed(() => resolveActiveNavigation(route.path));
const pageTitle = computed(() => String(route.meta.title ?? "ClubHub"));
const pageEyebrow = computed(() => String(route.meta.eyebrow ?? "ClubHub"));

function refreshSession() {
  const nextAuth = readAuth();
  auth.value = nextAuth;

  if (!nextAuth && route.path !== "/auth") {
    void router.replace({ path: "/auth", query: { redirect: route.fullPath } }).finally(() => {
      sessionResolved.value = true;
    });
    return;
  }

  sessionResolved.value = true;
}

async function checkHealth() {
  healthChecking.value = true;
  try {
    const response = await fetch("/api/v1/health");
    healthOk.value = response.ok;
  } catch {
    healthOk.value = false;
  } finally {
    healthChecking.value = false;
  }
}

async function logout() {
  try {
    await apiClient.logoutCurrentSession();
    clearSession();
    refreshSession();
    await router.push("/auth");
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "注销失败，请稍后重试");
  }
}

onMounted(() => {
  refreshSession();
  checkHealth();
  stopSessionListener = onSessionChange(refreshSession);
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <el-config-provider :locale="zhCn">
    <AppShell
      v-if="sessionResolved && hasSession"
      :navigation-groups="navigationGroups"
      :active-menu="activeMenu"
      :page-title="pageTitle"
      :page-eyebrow="pageEyebrow"
      :account-label="accountLabel"
      :role-labels="roleLabels"
      :health-ok="healthOk"
      :health-checking="healthChecking"
      @logout="logout"
      @check-health="checkHealth"
    >
      <router-view v-slot="{ Component, route: currentRoute }">
        <transition name="page" mode="out-in">
          <component :is="Component" :key="currentRoute.fullPath" />
        </transition>
      </router-view>
    </AppShell>

    <router-view v-else-if="sessionResolved && route.path === '/auth'" />
  </el-config-provider>
</template>

<style>
.page-enter-active,
.page-leave-active {
  transition:
    opacity 160ms ease,
    transform 180ms ease;
}

.page-enter-from {
  opacity: 0;
  transform: translateY(8px);
}

.page-leave-to {
  opacity: 0;
  transform: translateY(-4px);
}
</style>
