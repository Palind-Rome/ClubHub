<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { useRouter } from "vue-router";
import {
  type AuthResponse,
  type AuthRole,
  clearActiveRole,
  clearSession,
  onSessionChange,
  readActiveRole,
  readAuth,
} from "./authSession";

const healthOk = ref(false);
const router = useRouter();
const auth = ref<AuthResponse | null>(null);
const activeRole = ref<AuthRole | null>(null);
let stopSessionListener: (() => void) | null = null;

const hasCompletedSession = computed(() => Boolean(auth.value && activeRole.value));
const accountLabel = computed(() => {
  const user = auth.value?.user;
  if (!user) return "账号与权限";
  return user.studentNo ? `${user.realName} / ${user.studentNo}` : user.realName;
});

function refreshSession() {
  auth.value = readAuth();
  activeRole.value = readActiveRole(auth.value);
}

async function checkHealth() {
  try {
    const res = await fetch("/api/health");
    healthOk.value = res.ok;
  } catch {
    healthOk.value = false;
  }
}

function switchRole() {
  clearActiveRole();
  refreshSession();
  router.push("/auth");
}

function logout() {
  clearSession();
  refreshSession();
  router.push("/auth");
}

onMounted(() => {
  refreshSession();
  stopSessionListener = onSessionChange(refreshSession);
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <el-container>
    <el-header v-if="hasCompletedSession">
      <div class="brand">ClubHub</div>
      <el-menu mode="horizontal" router :default-active="$route.path" class="nav">
        <el-menu-item index="/auth">{{ accountLabel }}</el-menu-item>
        <el-menu-item index="/clubs">社团</el-menu-item>
        <el-menu-item index="/activities">活动</el-menu-item>
        <div class="session">
          <el-tag type="success" size="small">{{ activeRole?.name }}</el-tag>
          <el-button link type="primary" @click="switchRole">切换角色</el-button>
          <el-button link type="danger" @click="logout">退出</el-button>
        </div>
        <div class="health">
          <el-tag :type="healthOk ? 'success' : 'danger'" size="small" @click="checkHealth">
            {{ healthOk ? "后端已连接" : "点击检测" }}
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
</style>
