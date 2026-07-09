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
const roleSummary = computed(() => {
  const roles = auth.value?.roles ?? [];
  if (roles.length === 0) return "暂无角色";
  return roles.map((role) => role.displayName || role.name).join("、");
});
const activeMenu = computed(() =>
  route.path.startsWith("/recruitments") ? "/recruitments" : route.path,
);
const canManageVenues = computed(() => {
  const permissions = auth.value?.permissions ?? [];
  return (
    permissions.includes("*") ||
    permissions.includes("venue:create") ||
    permissions.includes("venue:update") ||
    permissions.includes("venue:disable")
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
        <el-menu-item index="/clubs">社团</el-menu-item>
        <el-menu-item index="/recruitments">纳新</el-menu-item>
        <el-menu-item index="/activities">活动</el-menu-item>
        <el-menu-item index="/notices">通知</el-menu-item>
        <el-menu-item index="/projects">项目</el-menu-item>
        <el-menu-item v-if="canManageVenues" index="/venues">场地管理</el-menu-item>
        <el-menu-item index="/venue-reservations">场地预约</el-menu-item>
        <div class="session">
          <el-tag class="role-tag" type="success" size="small" :title="roleSummary">{{
            roleSummary
          }}</el-tag>
          <el-button link type="danger" @click="logout">退出</el-button>
        </div>
        <div class="health">
          <el-tag
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
      </el-menu>
    </el-header>
    <el-main>
      <router-view />
    </el-main>
  </el-container>
</template>

<style scoped>
.el-header {
  display: flex;
  align-items: center;
  border-bottom: 1px solid var(--el-border-color-light);
  padding: 0 16px;
}
.brand {
  font-size: 18px;
  font-weight: 600;
  margin-right: 16px;
  white-space: nowrap;
}
.nav {
  flex: 1;
  border-bottom: none !important;
}
.health {
  display: flex;
  align-items: center;
  cursor: pointer;
}
.session {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-left: auto;
}
.role-tag {
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
</style>
