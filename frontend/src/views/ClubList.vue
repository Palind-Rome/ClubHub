<script setup lang="ts">
import { ref, onMounted } from "vue";
import { ElMessageBox } from "element-plus";

interface Club {
  id: number;
  name: string;
  description: string | null;
  category: string | null;
}

const clubs = ref<Club[]>([]);
const loading = ref(true);
const error = ref("");

const dialogVisible = ref(false);
const formName = ref("");
const formCategory = ref("");
const formDesc = ref("");
const saving = ref(false);

async function loadClubs() {
  loading.value = true;
  error.value = "";
  try {
    const res = await fetch("/api/clubs");
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    clubs.value = await res.json();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "加载失败";
  } finally {
    loading.value = false;
  }
}

function openCreate() {
  formName.value = "";
  formCategory.value = "";
  formDesc.value = "";
  dialogVisible.value = true;
}

async function createClub() {
  if (!formName.value || !formCategory.value) return;
  saving.value = true;
  try {
    await fetch("/api/clubs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        name: formName.value,
        category: formCategory.value,
        description: formDesc.value || null,
      }),
    });
    dialogVisible.value = false;
    await loadClubs();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "创建失败";
  } finally {
    saving.value = false;
  }
}

async function deleteClub(id: number, name: string) {
  try {
    await ElMessageBox.confirm(`确认删除「${name}」？`, "删除社团", { type: "warning" });
    await fetch(`/api/clubs/${id}`, { method: "DELETE" });
    await loadClubs();
  } catch {
    /* 用户取消 */
  }
}

onMounted(loadClubs);
</script>

<template>
  <div class="page">
    <div class="toolbar">
      <h2>社团列表</h2>
      <el-button type="primary" @click="openCreate">新增社团</el-button>
    </div>

    <el-alert v-if="error" :title="error" type="error" show-icon closable @close="error = ''" />

    <el-table v-loading="loading" :data="clubs" stripe empty-text="暂无社团数据">
      <el-table-column prop="id" label="ID" width="60" />
      <el-table-column prop="name" label="名称" />
      <el-table-column prop="category" label="分类" />
      <el-table-column prop="description" label="简介" show-overflow-tooltip />
      <el-table-column label="操作" width="80">
        <template #default="{ row }">
          <el-button type="danger" size="small" text @click="deleteClub(row.id, row.name)"
            >删除</el-button
          >
        </template>
      </el-table-column>
    </el-table>

    <!-- 新增对话框 -->
    <el-dialog v-model="dialogVisible" title="新增社团" width="420px">
      <el-form label-position="top">
        <el-form-item label="名称" required>
          <el-input v-model="formName" placeholder="社团名称" />
        </el-form-item>
        <el-form-item label="分类" required>
          <el-input v-model="formCategory" placeholder="如：学术科技" />
        </el-form-item>
        <el-form-item label="简介">
          <el-input v-model="formDesc" type="textarea" placeholder="可选" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="saving" @click="createClub">确定</el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.page {
  max-width: 860px;
  margin: 0 auto;
}
.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}
.toolbar h2 {
  margin: 0;
}
</style>
