<script setup lang="ts">
import { ref } from "vue";

const healthOk = ref(false);

async function checkHealth() {
  try {
    const res = await fetch("/api/health");
    healthOk.value = res.ok;
  } catch {
    healthOk.value = false;
  }
}
</script>

<template>
  <el-container>
    <el-header>
      <div class="brand">ClubHub</div>
      <el-menu mode="horizontal" router :default-active="$route.path" class="nav">
        <el-menu-item index="/clubs">社团</el-menu-item>
        <el-menu-item index="/activities">活动</el-menu-item>
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
  margin-left: auto;
  cursor: pointer;
}
</style>
