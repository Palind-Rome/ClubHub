<script setup lang="ts">
import { computed, ref } from "vue";
import { Menu, Monitor, Moon, Refresh, Sunny, SwitchButton } from "@element-plus/icons-vue";
import type { NavigationGroup } from "../../navigation";
import { type ThemePreference, useTheme } from "../../composables/useTheme";
import AppNavigation from "./AppNavigation.vue";

const props = defineProps<{
  navigationGroups: NavigationGroup[];
  activeMenu: string;
  pageTitle: string;
  accountLabel: string;
  roleLabels: string[];
  healthOk: boolean;
  healthChecking: boolean;
}>();

const emit = defineEmits<{
  logout: [];
  checkHealth: [];
}>();

const drawerOpen = ref(false);
const { theme, setTheme } = useTheme();

const primaryRole = computed(() => props.roleLabels[0] ?? "暂无角色");
const extraRoleCount = computed(() => Math.max(props.roleLabels.length - 1, 0));
const themeLabel = computed(() => {
  if (theme.value === "light") return "浅色";
  if (theme.value === "dark") return "深色";
  return "跟随系统";
});

function changeTheme(command: string | number | object) {
  setTheme(command as ThemePreference);
}
</script>

<template>
  <div class="app-shell">
    <aside class="desktop-sidebar">
      <div class="brand-lockup">
        <div class="brand-mark" aria-hidden="true"><span>C</span></div>
        <div>
          <strong>ClubHub</strong>
          <small>社团协作中心</small>
        </div>
      </div>
      <AppNavigation :groups="navigationGroups" :active-menu="activeMenu" />
      <div class="sidebar-footer">
        <span class="status-dot" :class="{ online: healthOk }" />
        <span>{{ healthOk ? "服务运行正常" : "服务连接待检测" }}</span>
      </div>
    </aside>

    <div class="shell-content">
      <header class="topbar">
        <div class="topbar-context">
          <el-button
            class="mobile-menu-button"
            text
            circle
            aria-label="打开主导航"
            @click="drawerOpen = true"
          >
            <el-icon><Menu /></el-icon>
          </el-button>
          <div>
            <span class="eyebrow">ClubHub Workspace</span>
            <h1>{{ pageTitle }}</h1>
          </div>
        </div>

        <div class="topbar-actions">
          <el-button
            class="health-button"
            text
            :loading="healthChecking"
            :aria-label="healthOk ? '后端已连接，点击重新检测' : '后端未连接，点击检测'"
            @click="emit('checkHealth')"
          >
            <span class="status-dot" :class="{ online: healthOk }" />
            <span>{{ healthChecking ? "检测中" : healthOk ? "后端已连接" : "检测后端" }}</span>
            <el-icon v-if="!healthChecking"><Refresh /></el-icon>
          </el-button>

          <el-dropdown trigger="click" @command="changeTheme">
            <el-button class="theme-button" text :aria-label="`当前主题：${themeLabel}`">
              <el-icon>
                <Sunny v-if="theme === 'light'" />
                <Moon v-else-if="theme === 'dark'" />
                <Monitor v-else />
              </el-icon>
              <span>{{ themeLabel }}</span>
            </el-button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item command="system" :disabled="theme === 'system'">
                  <el-icon><Monitor /></el-icon>跟随系统
                </el-dropdown-item>
                <el-dropdown-item command="light" :disabled="theme === 'light'">
                  <el-icon><Sunny /></el-icon>浅色模式
                </el-dropdown-item>
                <el-dropdown-item command="dark" :disabled="theme === 'dark'">
                  <el-icon><Moon /></el-icon>深色模式
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>

          <el-dropdown trigger="click">
            <button class="account-button" type="button" aria-label="账号菜单">
              <span class="avatar">{{ accountLabel.slice(0, 1) }}</span>
              <span class="account-copy">
                <strong>{{ accountLabel }}</strong>
                <small
                  >{{ primaryRole
                  }}<template v-if="extraRoleCount"> +{{ extraRoleCount }}</template></small
                >
              </span>
            </button>
            <template #dropdown>
              <el-dropdown-menu>
                <el-dropdown-item disabled>{{ roleLabels.join("、") }}</el-dropdown-item>
                <el-dropdown-item divided @click="emit('logout')">
                  <el-icon><SwitchButton /></el-icon>退出登录
                </el-dropdown-item>
              </el-dropdown-menu>
            </template>
          </el-dropdown>
        </div>
      </header>

      <main class="page-canvas">
        <div class="page-content"><slot /></div>
      </main>
    </div>

    <el-drawer
      v-model="drawerOpen"
      direction="ltr"
      size="min(86vw, 300px)"
      :show-close="false"
      class="mobile-navigation-drawer"
    >
      <template #header>
        <div class="brand-lockup drawer-brand">
          <div class="brand-mark" aria-hidden="true"><span>C</span></div>
          <div><strong>ClubHub</strong><small>社团协作中心</small></div>
        </div>
      </template>
      <AppNavigation
        :groups="navigationGroups"
        :active-menu="activeMenu"
        @navigate="drawerOpen = false"
      />
    </el-drawer>
  </div>
</template>

<style scoped>
.app-shell {
  min-height: 100vh;
}

.desktop-sidebar {
  position: fixed;
  z-index: 20;
  inset: 0 auto 0 0;
  display: grid;
  grid-template-rows: auto 1fr auto;
  width: var(--club-sidebar-width);
  border-right: 1px solid var(--club-border);
  background: var(--club-surface);
  box-shadow: 12px 0 40px rgba(35, 65, 105, 0.05);
  backdrop-filter: var(--club-glass-blur);
}

.brand-lockup {
  display: flex;
  align-items: center;
  gap: var(--club-space-3);
  padding: var(--club-space-5) var(--club-space-5) var(--club-space-4);
}

.brand-lockup strong,
.brand-lockup small {
  display: block;
}

.brand-lockup strong {
  color: var(--club-text);
  font-size: 19px;
  letter-spacing: -0.02em;
}

.brand-lockup small {
  margin-top: 2px;
  color: var(--club-text-muted);
  font-size: 11px;
}

.brand-mark {
  position: relative;
  display: grid;
  width: 38px;
  height: 38px;
  flex: 0 0 auto;
  place-items: center;
  overflow: hidden;
  border-radius: 13px;
  color: #ffffff;
  background: linear-gradient(135deg, var(--club-primary), var(--club-accent));
  box-shadow: 0 8px 20px color-mix(in srgb, var(--club-primary) 24%, transparent);
}

.brand-mark::after {
  position: absolute;
  width: 20px;
  height: 20px;
  border: 2px solid rgba(255, 255, 255, 0.35);
  border-radius: 50%;
  content: "";
}

.brand-mark span {
  z-index: 1;
  font-weight: 800;
}

.sidebar-footer {
  display: flex;
  align-items: center;
  gap: var(--club-space-2);
  margin: var(--club-space-4);
  padding: var(--club-space-3) var(--club-space-4);
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  color: var(--club-text-muted);
  background: var(--club-bg-elevated);
  font-size: 12px;
}

.status-dot {
  width: 8px;
  height: 8px;
  flex: 0 0 auto;
  border-radius: 50%;
  background: var(--club-danger);
  box-shadow: 0 0 0 4px color-mix(in srgb, var(--club-danger) 12%, transparent);
}

.status-dot.online {
  background: var(--club-success);
  box-shadow: 0 0 0 4px color-mix(in srgb, var(--club-success) 12%, transparent);
}

.shell-content {
  min-width: 0;
  min-height: 100vh;
  margin-left: var(--club-sidebar-width);
}

.topbar {
  position: sticky;
  z-index: 15;
  top: 0;
  display: flex;
  min-height: var(--club-header-height);
  align-items: center;
  justify-content: space-between;
  gap: var(--club-space-4);
  padding: var(--club-space-3) var(--club-space-6);
  border-bottom: 1px solid var(--club-border);
  background: var(--club-bg-elevated);
  backdrop-filter: var(--club-glass-blur);
}

.topbar-context,
.topbar-actions,
.account-button,
.health-button,
.theme-button {
  display: flex;
  align-items: center;
}

.topbar-context {
  min-width: 0;
  gap: var(--club-space-2);
}

.eyebrow {
  display: block;
  margin-bottom: 2px;
  color: var(--club-primary);
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 0.14em;
  text-transform: uppercase;
}

.topbar h1 {
  margin: 0;
  overflow: hidden;
  color: var(--club-text);
  font-size: 20px;
  line-height: 1.2;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.topbar-actions {
  gap: var(--club-space-2);
}

.mobile-menu-button {
  display: none;
}

.health-button,
.theme-button {
  gap: var(--club-space-2);
  color: var(--club-text-secondary);
}

.account-button {
  gap: var(--club-space-2);
  min-width: 0;
  padding: 5px 8px;
  border: 0;
  border-radius: var(--club-radius-md);
  color: inherit;
  background: transparent;
  cursor: pointer;
}

.account-button:hover {
  background: var(--club-surface-hover);
}

.avatar {
  display: grid;
  width: 34px;
  height: 34px;
  flex: 0 0 auto;
  place-items: center;
  border-radius: 11px;
  color: var(--club-primary-strong);
  background: linear-gradient(135deg, var(--club-primary-soft), var(--club-accent-soft));
  font-weight: 800;
}

.account-copy {
  min-width: 0;
  max-width: 190px;
  text-align: left;
}

.account-copy strong,
.account-copy small {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.account-copy strong {
  color: var(--club-text);
  font-size: 13px;
}

.account-copy small {
  margin-top: 2px;
  color: var(--club-text-muted);
  font-size: 11px;
}

.page-canvas {
  min-width: 0;
  padding: var(--club-space-6);
  scrollbar-gutter: stable;
}

.page-content {
  width: 100%;
  max-width: var(--club-content-width);
  margin: 0 auto;
}

.drawer-brand {
  padding: 0;
}

:global(.mobile-navigation-drawer .el-drawer__body) {
  padding: 0 0 var(--club-space-4);
}

@media (max-width: 1100px) {
  .desktop-sidebar {
    display: none;
  }

  .shell-content {
    margin-left: 0;
  }

  .mobile-menu-button {
    display: inline-flex;
  }
}

@media (max-width: 760px) {
  .topbar {
    padding: var(--club-space-3) var(--club-space-4);
  }

  .topbar h1 {
    font-size: 17px;
  }

  .eyebrow,
  .health-button span:not(.status-dot),
  .theme-button span,
  .account-copy {
    display: none;
  }

  .health-button,
  .theme-button {
    padding-inline: 8px;
  }

  .page-canvas {
    padding: var(--club-space-4);
  }
}

@media (max-width: 420px) {
  .health-button {
    display: none;
  }

  .topbar-actions {
    gap: 0;
  }
}
</style>
