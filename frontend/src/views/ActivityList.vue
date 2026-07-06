<script setup lang="ts">
import { ref, onMounted } from "vue";

interface Activity {
  id: number;
  title: string;
  clubName: string;
  startTime: string;
  endTime: string;
  location: string | null;
  status: string;
  maxParticipants: number | null;
  currentParticipants: number;
}

const activities = ref<Activity[]>([]);
const loading = ref(true);

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
    activities.value = await res.json();
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="page">
    <h2>活动列表</h2>
    <div v-if="loading">加载中…</div>
    <div v-else class="grid">
      <div v-for="act in activities" :key="act.id" class="card">
        <h3>{{ act.title }}</h3>
        <div class="meta">{{ act.clubName }}</div>
        <div class="meta">{{ new Date(act.startTime).toLocaleString() }} — {{ new Date(act.endTime).toLocaleString() }}</div>
        <div v-if="act.location" class="meta">地点：{{ act.location }}</div>
        <div class="meta">
          <span class="tag">{{ statusLabel[act.status] || act.status }}</span>
          参与人数：{{ act.currentParticipants }}{{ act.maxParticipants ? ' / ' + act.maxParticipants : '' }}
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
</style>
