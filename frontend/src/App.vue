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
  health.value = null;
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
  <div class="container">
    <h1>ClubHub</h1>
    <p>高校社团运营与协同管理平台</p>

    <button @click="checkHealth" :disabled="loading">
      {{ loading ? "检查中…" : "检查后端连接" }}
    </button>

    <div v-if="health" class="result ok">
      <p>状态：{{ health.status }}</p>
      <p>时间：{{ new Date(health.timestamp).toLocaleString() }}</p>
    </div>
    <div v-if="error" class="result error">
      <p>连接失败：{{ error }}</p>
    </div>
  </div>
</template>

<style scoped>
.container {
  max-width: 480px;
  margin: 80px auto;
  text-align: center;
  font-family: system-ui, sans-serif;
}
button {
  padding: 10px 24px;
  font-size: 16px;
  cursor: pointer;
}
.result {
  margin-top: 24px;
  padding: 16px;
  border-radius: 8px;
}
.ok {
  background: #e8f5e9;
  color: #2e7d32;
}
.error {
  background: #fce4ec;
  color: #c62828;
}
</style>
