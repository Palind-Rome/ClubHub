<script setup lang="ts">
import { ref, onMounted } from "vue";

interface Club {
  id: number;
  name: string;
  description: string | null;
  category: string | null;
  status: string | null;
  foundedAt: string | null;
  createdAt: string;
}

const clubs = ref<Club[]>([]);
const loading = ref(true);
const error = ref("");

onMounted(async () => {
  try {
    const res = await fetch("/api/clubs");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    clubs.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载失败";
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="page">
    <h2>社团列表</h2>
    <div v-if="error" class="err">{{ error }}</div>
    <div v-if="loading">加载中…</div>
    <div v-else-if="clubs.length === 0" class="empty">暂无社团数据</div>
    <div v-else class="grid">
      <div v-for="club in clubs" :key="club.id" class="card">
        <h3>{{ club.name }}</h3>
        <div class="meta">
          {{ club.category ?? "未分类" }}
          <span v-if="club.status"> · {{ club.status }}</span>
        </div>
        <p v-if="club.description">{{ club.description }}</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.page { max-width: 720px; margin: 0 auto; }
.grid { display: flex; flex-direction: column; gap: 16px; margin-top: 16px; }
.card { background: #fff; border-radius: 8px; padding: 16px; box-shadow: 0 1px 3px rgba(0,0,0,.1); }
.card h3 { margin: 0 0 8px; }
.meta { font-size: 14px; color: #666; margin-bottom: 4px; }
.err { color: #c62828; margin: 16px 0; }
.empty { color: #999; margin-top: 24px; text-align: center; }
</style>
