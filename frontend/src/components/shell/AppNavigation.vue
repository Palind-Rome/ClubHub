<script setup lang="ts">
import {
  Bell,
  Calendar,
  ChatDotRound,
  Coin,
  Collection,
  DataAnalysis,
  Files,
  Goods,
  Medal,
  OfficeBuilding,
  Opportunity,
  Postcard,
  School,
  Tickets,
  User,
  UserFilled,
} from "@element-plus/icons-vue";
import type { Component } from "vue";
import type { NavigationGroup, NavigationIcon } from "../../navigation";

defineProps<{
  groups: NavigationGroup[];
  activeMenu: string;
}>();

const emit = defineEmits<{
  navigate: [];
}>();

const iconComponents: Record<NavigationIcon, Component> = {
  account: User,
  activity: Calendar,
  award: Medal,
  budget: Coin,
  club: School,
  evaluation: DataAnalysis,
  forum: ChatDotRound,
  learning: Collection,
  material: Goods,
  member: UserFilled,
  notice: Bell,
  organization: OfficeBuilding,
  project: Files,
  recruitment: Tickets,
  registration: Postcard,
  venue: Opportunity,
};
</script>

<template>
  <nav class="app-navigation" aria-label="主导航">
    <el-menu router :default-active="activeMenu" class="navigation-menu" @select="emit('navigate')">
      <template v-for="group in groups" :key="group.label">
        <li class="navigation-group-label">{{ group.label }}</li>
        <el-menu-item v-for="item in group.items" :key="item.path" :index="item.path">
          <el-icon><component :is="iconComponents[item.icon]" /></el-icon>
          <span>{{ item.label }}</span>
        </el-menu-item>
      </template>
    </el-menu>
  </nav>
</template>

<style scoped>
.app-navigation {
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--club-border-strong) transparent;
}

.navigation-menu {
  border: 0;
  background: transparent;
}

.navigation-group-label {
  margin: var(--club-space-5) var(--club-space-4) var(--club-space-2);
  color: var(--club-text-muted);
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.12em;
  list-style: none;
  text-transform: uppercase;
}

.navigation-menu :deep(.el-menu-item) {
  height: 44px;
  margin: 3px var(--club-space-3);
  border-radius: var(--club-radius-md);
  color: var(--club-text-secondary);
  font-weight: 600;
}

.navigation-menu :deep(.el-menu-item:hover) {
  color: var(--club-primary);
  background: var(--club-surface-hover);
}

.navigation-menu :deep(.el-menu-item.is-active) {
  color: var(--club-primary-strong);
  background: linear-gradient(100deg, var(--club-primary-soft), var(--club-accent-soft));
  box-shadow: inset 3px 0 0 var(--club-primary);
}

.navigation-menu :deep(.el-icon) {
  font-size: 18px;
}
</style>
