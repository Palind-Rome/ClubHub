<script setup lang="ts">
import { ref } from "vue";

interface HealthStatus {
  status: string;
  timestamp: string;
}

const health = ref<HealthStatus | null>(null);
const loading = ref(false);
const error = ref("");

async function checkHealth() {
  loading.value = true;
  error.value = "";
  try {
    const res = await fetch("/api/health");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    health.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "请求失败";
  } finally {
    loading.value = false;
  }
}
</script>

<template>
  <header>
    <h1>ClubHub</h1>
    <nav>
      <router-link to="/clubs">社团</router-link>
      <router-link to="/activities">活动</router-link>
      <button class="health-btn" @click="checkHealth" :disabled="loading">
        {{ loading ? "…" : "health" }}
      </button>
    </nav>
  </header>

  <div v-if="health" class="banner ok">后端连接正常</div>
  <div v-if="error" class="banner error">{{ error }}</div>

  <main>
    <router-view />
  </main>
</template>

<style scoped>
header {
  background: #fff;
  border-bottom: 1px solid #e0e0e0;
  padding: 12px 24px;
  display: flex;
  align-items: center;
  gap: 24px;
}
header h1 { font-size: 20px; margin: 0; }
nav { display: flex; gap: 16px; align-items: center; }
nav a { color: #333; text-decoration: none; font-size: 15px; }
nav a:hover { color: #1976d2; }
.health-btn { font-size: 12px; padding: 2px 8px; cursor: pointer; }
.banner { text-align: center; padding: 8px; font-size: 14px; }
.ok { background: #e8f5e9; color: #2e7d32; }
.error { background: #fce4ec; color: #c62828; }
main { padding: 24px; }
</style>
