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

const statusType: Record<string, string> = {
  draft: "info",
  published: "success",
  ongoing: "",
  finished: "info",
  cancelled: "danger",
};

onMounted(async () => {
  loading.value = true;
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

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <el-table v-loading="loading" :data="activities" stripe empty-text="暂无活动数据">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="title" label="标题" />
      <el-table-column prop="clubName" label="主办社团" width="120" />
      <el-table-column label="时间" width="180">
        <template #default="{ row }">
          <span v-if="row.startTime">{{ new Date(row.startTime).toLocaleString() }}</span>
        </template>
      </el-table-column>
      <el-table-column prop="location" label="地点" />
      <el-table-column label="状态" width="100">
        <template #default="{ row }">
          <el-tag v-if="row.status" :type="statusType[row.status] || 'info'" size="small">
            {{ statusLabel[row.status] || row.status }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="名额" width="80">
        <template #default="{ row }">
          {{ row.maxParticipants ?? "不限" }}
        </template>
      </el-table-column>
    </el-table>
  </div>
</template>

<style scoped>
.page {
  max-width: 960px;
  margin: 0 auto;
}
h2 {
  margin-bottom: 12px;
}
</style>
