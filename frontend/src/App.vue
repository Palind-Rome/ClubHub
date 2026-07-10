<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { useRoute, useRouter } from "vue-router";
import { type AuthResponse, clearSession, onSessionChange, readAuth } from "./authSession";

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
  if (route.path.startsWith("/awards")) return "/awards";
  if (route.path.startsWith("/learning")) return "/learning";
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

function refreshSession() {
  auth.value = readAuth();
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
      <el-menu mode="horizontal" router :default-active="activeMenu" class="nav">
        <el-menu-item index="/auth">{{ accountLabel }}</el-menu-item>
        <el-menu-item index="/clubs">我的社团</el-menu-item>
        <el-menu-item index="/club-members">成员管理</el-menu-item>
        <el-menu-item v-if="canAccessClubRegistration" index="/club-registration">
          社团注册
        </el-menu-item>
        <el-menu-item index="/recruitments">纳新</el-menu-item>
        <el-menu-item index="/awards">评奖评优</el-menu-item>
        <el-menu-item index="/activities">活动</el-menu-item>
        <el-menu-item index="/notices">通知</el-menu-item>
        <el-menu-item index="/projects">项目</el-menu-item>
        <el-menu-item v-if="canManageVenues" index="/venues">场地管理</el-menu-item>
        <el-menu-item v-if="canAccessVenueReservations" index="/venue-reservations">
          场地预约
        </el-menu-item>
        <el-menu-item index="/learning">课程</el-menu-item>
      </el-menu>
      <div class="header-aside">
        <div class="role-tags" :title="roleSummary">
          <el-tag
            v-for="(label, index) in roleLabels"
            :key="`${label}-${index}`"
            class="role-tag"
            type="success"
            size="small"
          >
            {{ label }}
          </el-tag>
        </div>
        <el-button link type="danger" @click="logout">退出</el-button>
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
  border-bottom: 1px solid var(--el-border-color-light);
  padding: 0 16px;
}
.brand {
  font-size: 18px;
  font-weight: 600;
  white-space: nowrap;
  flex-shrink: 0;
}
.nav {
  flex: 1;
  min-width: 0;
  border-bottom: none !important;
}
.header-aside {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-shrink: 0;
  margin-left: auto;
}
.role-tags {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  align-items: center;
  gap: 6px;
  width: 320px;
  max-height: 52px;
  overflow: hidden;
}
.role-tag {
  max-width: 100%;
  overflow: hidden;
  text-overflow: ellipsis;
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
</style>
