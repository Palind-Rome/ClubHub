<script setup lang="ts">
import { ref, onMounted } from "vue";

interface Activity {
  id: number;
  title: string;
  clubName: string;
  startTime: string | null;
  endTime: string | null;
  location: string | null;
  status: string | null;
  maxParticipants: number | null;
  createdAt: string;
}

const activities = ref<Activity[]>([]);
const loading = ref(true);
const error = ref("");

const statusLabel: Record<string, string> = {
  draft: "草稿",
  published: "报名中",
  ongoing: "进行中",
  finished: "已结束",
  cancelled: "已取消",
};

onMounted(async () => {
  try {
    const res = await fetch("/api/activities");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    activities.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载失败";
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="page">
    <h2>活动列表</h2>
    <div v-if="error" class="err">{{ error }}</div>
    <div v-if="loading">加载中…</div>
    <div v-else-if="activities.length === 0" class="empty">暂无活动数据</div>
    <div v-else class="grid">
      <div v-for="act in activities" :key="act.id" class="card">
        <h3>{{ act.title }}</h3>
        <div class="meta">{{ act.clubName }}</div>
        <div v-if="act.startTime" class="meta">
          {{ new Date(act.startTime).toLocaleString() }}
          <template v-if="act.endTime"> — {{ new Date(act.endTime).toLocaleString() }}</template>
        </div>
        <div v-if="act.location" class="meta">地点：{{ act.location }}</div>
        <div class="meta">
          <span v-if="act.status" class="tag">{{ statusLabel[act.status] || act.status }}</span>
          <span v-if="act.maxParticipants">上限 {{ act.maxParticipants }} 人</span>
        </div>
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
.tag { background: #e3f2fd; color: #1976d2; padding: 1px 8px; border-radius: 4px; font-size: 12px; margin-right: 8px; }
.err { color: #c62828; margin: 16px 0; }
.empty { color: #999; margin-top: 24px; text-align: center; }
</style>
