<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ElMessage } from "element-plus";
import { readAuth } from "../authSession";

interface LearningItem {
  id: number;
  clubId: number;
  title: string;
  itemType?: string | null;
  teacherUserId?: number | null;
  startAt?: string | null;
  endAt?: string | null;
  capacity?: number | null;
  itemStatus?: string | null;
  currentEnrollments?: number;
  currentUserRecordStatus?: string | null;
}

const loading = ref(false);
const learningItems = ref<LearningItem[]>([]);
const statusFilter = ref("all");

const filteredItems = computed(() => {
  if (statusFilter.value === "all") return learningItems.value;
  return learningItems.value.filter((item) => item.itemStatus === statusFilter.value);
});

function statusLabel(status?: string | null) {
  switch (status) {
    case "published":
      return "报名中";
    case "closed":
      return "已关闭";
    case "finished":
      return "已结束";
    default:
      return "草稿";
  }
}

async function loadLearningItems() {
  loading.value = true;
  try {
    const currentUserId = readAuth()?.user?.id;
    const query = currentUserId ? `?currentUserId=${currentUserId}` : "";
    const response = await fetch(`/api/learning/items${query}`);
    if (!response.ok) throw new Error(await response.text());
    learningItems.value = (await response.json()) as LearningItem[];
  } catch (error) {
    learningItems.value = [];
    ElMessage.warning(error instanceof Error ? error.message : "课程列表暂不可用");
  } finally {
    loading.value = false;
  }
}

onMounted(loadLearningItems);
</script>

<template>
  <section class="learning-page">
    <div class="page-header">
      <div>
        <h1>培训课程</h1>
        <p>发布课程、报名课程，并跟踪学习进度与完成情况。</p>
      </div>
      <el-segmented
        v-model="statusFilter"
        :options="[
          { label: '全部', value: 'all' },
          { label: '报名中', value: 'published' },
          { label: '已关闭', value: 'closed' },
          { label: '已结束', value: 'finished' },
        ]"
      />
    </div>

    <el-table v-loading="loading" :data="filteredItems" empty-text="暂无课程数据">
      <el-table-column prop="title" label="课程名称" min-width="180" />
      <el-table-column prop="itemType" label="类型" width="120" />
      <el-table-column label="课程时间" min-width="220">
        <template #default="{ row }">
          {{ row.startAt || "未设置" }}<span v-if="row.endAt"> 至 {{ row.endAt }}</span>
        </template>
      </el-table-column>
      <el-table-column label="容量" width="120" align="center">
        <template #default="{ row }">
          {{ row.currentEnrollments ?? 0 }} / {{ row.capacity ?? "不限" }}
        </template>
      </el-table-column>
      <el-table-column label="状态" width="120" align="center">
        <template #default="{ row }">
          <el-tag>{{ statusLabel(row.itemStatus) }}</el-tag>
        </template>
      </el-table-column>
      <el-table-column label="我的学习" width="140" align="center">
        <template #default="{ row }">
          {{ row.currentUserRecordStatus || "未报名" }}
        </template>
      </el-table-column>
    </el-table>
  </section>
</template>

<style scoped>
.learning-page {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 16px;
}

.page-header h1 {
  margin: 0 0 6px;
  font-size: 24px;
}

.page-header p {
  margin: 0;
  color: var(--el-text-color-secondary);
}
</style>
