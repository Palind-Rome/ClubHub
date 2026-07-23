<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { type AuthResponse, clearSession, onSessionChange, readAuth } from "./authSession";
import { BUDGET_ACCESS_PERMISSIONS } from "./budgetPermissions";
import { MATERIAL_ACCESS_PERMISSIONS } from "./materialPermissions";

const healthOk = ref(false);
const healthChecking = ref(false);
const route = useRoute();
const router = useRouter();
const auth = ref<AuthResponse | null>(null);
let stopSessionListener: (() => void) | null = null;

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
const roleSummary = computed(() => roleLabels.value.join("、"));
const activeMenu = computed(() => {
  if (route.path.startsWith("/recruitments")) return "/recruitments";
  if (route.path.startsWith("/evaluations")) return "/evaluations";
  if (route.path.startsWith("/awards")) return "/awards";
  if (route.path.startsWith("/learning")) return "/learning";
  if (route.path.startsWith("/projects")) return "/projects";
  if (route.path.startsWith("/budgets")) return "/budgets";
  return route.path;
});
const canAccessClubRegistration = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return (
    permissions.includes("*") ||
    permissions.includes("club:apply") ||
    permissions.includes("club:review")
  );
});
const canManageVenues = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return (
    permissions.includes("*") ||
    permissions.includes("venue:create") ||
    permissions.includes("venue:update") ||
    permissions.includes("venue:disable")
  );
});
const canAccessVenueReservations = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return (
    permissions.includes("*") ||
    permissions.includes("venue:reserve") ||
    permissions.includes("venue:review")
  );
});
const canAccessMaterialBorrows = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return (
    permissions.includes("*") ||
    MATERIAL_ACCESS_PERMISSIONS.some((permission) => permissions.includes(permission))
  );
});
const canAccessBudgets = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return (
    permissions.includes("*") ||
    BUDGET_ACCESS_PERMISSIONS.some((permission) => permissions.includes(permission))
  );
});

function refreshSession() {
  const nextAuth = readAuth();
  auth.value = nextAuth;

  if (!nextAuth && route.path !== "/auth") {
    router.replace({ path: "/auth", query: { redirect: route.fullPath } });
  }
}

async function checkHealth() {
  healthChecking.value = true;
  try {
    const res = await fetch("/api/health");
    healthOk.value = res.ok;
  } catch {
    healthOk.value = false;
  } finally {
    healthChecking.value = false;
  }
}

function logout() {
  clearSession();
  refreshSession();
  router.push("/auth");
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
  <el-container>
    <el-header v-if="hasSession">
      <div class="brand">ClubHub</div>
      <el-menu mode="horizontal" router :default-active="activeMenu" :ellipsis="false" class="nav">
        <el-menu-item index="/auth">{{ accountLabel }}</el-menu-item>
        <el-menu-item index="/clubs">我的社团</el-menu-item>
        <el-menu-item index="/club-organization">社团架构</el-menu-item>
        <el-menu-item index="/club-members">成员管理</el-menu-item>
        <el-menu-item v-if="canAccessClubRegistration" index="/club-registration">
          社团注册
        </el-menu-item>
        <el-menu-item index="/recruitments">纳新</el-menu-item>
        <el-menu-item index="/evaluations">成员考核</el-menu-item>
        <el-menu-item index="/awards">评奖评优</el-menu-item>
        <el-menu-item index="/activities">活动</el-menu-item>
        <el-menu-item index="/notices">通知</el-menu-item>
        <el-menu-item index="/projects">项目</el-menu-item>
        <el-menu-item v-if="canAccessBudgets" index="/budgets">经费管理</el-menu-item>
        <el-menu-item v-if="canManageVenues" index="/venues">场地管理</el-menu-item>
        <el-menu-item v-if="canAccessVenueReservations" index="/venue-reservations">
          场地预约
        </el-menu-item>
        <el-menu-item index="/learning">学习中心</el-menu-item>
        <el-menu-item v-if="canAccessMaterialBorrows" index="/materials"> 物资借还 </el-menu-item>
      </el-menu>
      <div class="header-actions">
        <div class="session">
          <div class="role-list" :title="roleSummary" aria-label="当前职务">
            <el-tag
              v-for="(role, roleIdx) in roleLabels"
              :key="`${role}-${roleIdx}`"
              class="role-tag"
              type="success"
              size="small"
              :title="role"
            >
              {{ role }}
            </el-tag>
          </div>
          <el-button class="logout-button" link type="danger" @click="logout">退出</el-button>
        </div>
        <el-tag
          class="health-tag"
          :type="healthOk ? 'success' : 'danger'"
          size="small"
          role="button"
          tabindex="0"
          :aria-label="
            healthOk ? '后端已连接，回车或空格重新检测' : '点击、回车或空格检测后端健康状态'
          "
          :aria-busy="healthChecking"
          @click="checkHealth"
          @keyup.enter="checkHealth"
          @keyup.space.prevent="checkHealth"
        >
          {{ healthChecking ? "检测中..." : healthOk ? "后端已连接" : "点击检测" }}
        </el-tag>
      </div>
    </el-header>
    <el-main class="main">
      <router-view />
    </el-main>
  </el-container>
</template>

<style scoped>
.el-header {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
  border-bottom: 1px solid var(--el-border-color-light);
  min-height: 60px;
  height: auto;
  padding: 8px 16px;
}
.brand {
  font-size: 18px;
  font-weight: 600;
  white-space: nowrap;
  flex-shrink: 0;
}
.nav {
  flex: 1 1 720px;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  min-width: min(100%, 720px);
  height: auto;
  min-height: 40px;
  overflow: visible;
  border-bottom: none !important;
}
.nav :deep(.el-menu-item),
.nav :deep(.el-sub-menu__title) {
  flex: 0 0 auto;
  height: 40px;
  line-height: 40px;
  white-space: nowrap;
}
.header-actions {
  display: flex;
  flex: 1 1 480px;
  align-items: center;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 12px;
  min-width: 0;
  margin-left: 16px;
}
.session {
  display: flex;
  flex: 0 1 auto;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
  max-width: min(100%, 680px);
}
.logout-button {
  flex: 0 0 auto;
}
.role-list {
  display: flex;
  flex: 0 1 auto;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
}
.role-tag {
  flex: 0 0 auto;
  max-width: none;
  overflow: visible;
  white-space: nowrap;
}
.role-tag :deep(.el-tag__content) {
  overflow: visible;
  white-space: nowrap;
}
.health-tag {
  width: 88px;
  justify-content: center;
  cursor: pointer;
}
.main {
  scrollbar-gutter: stable;
}

@media (max-width: 900px) {
  .el-header {
    padding: 0 12px;
  }

  .nav {
    overflow-x: auto;
    scrollbar-width: thin;
  }

  .nav :deep(.el-menu-item) {
    flex-shrink: 0;
  }

  .role-list {
    display: none;
  }
}

@media (max-width: 560px) {
  .el-header {
    gap: 8px;
  }

  .health-tag {
    display: none;
  }

  .main {
    padding: 14px;
  }
}
</style>
