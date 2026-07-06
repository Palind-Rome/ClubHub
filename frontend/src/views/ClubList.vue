<script setup lang="ts">
import { ref, onMounted } from "vue";

interface Club {
  id: number;
  name: string;
  description: string | null;
  category: string | null;
  status: string | null;
}

const clubs = ref<Club[]>([]);
const loading = ref(true);
const error = ref("");

// 新增表单
const newName = ref("");
const newCategory = ref("");
const newDesc = ref("");
const creating = ref(false);

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

async function createClub() {
  if (!newName.value || !newCategory.value) return;
  creating.value = true;
  try {
    await fetch("/api/clubs", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ name: newName.value, category: newCategory.value, description: newDesc.value || null }),
    });
    newName.value = "";
    newCategory.value = "";
    newDesc.value = "";
    await loadClubs();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "创建失败";
  } finally {
    creating.value = false;
  }
}

async function deleteClub(id: number, name: string) {
  if (!confirm(`确认删除「${name}」？`)) return;
  try {
    await fetch(`/api/clubs/${id}`, { method: "DELETE" });
    await loadClubs();
  } catch (e) {
    error.value = e instanceof Error ? e.message : "删除失败";
  }
}

onMounted(loadClubs);
</script>

<template>
  <div class="page">
    <h2>社团列表</h2>

    <!-- 新增表单 -->
    <div class="form">
      <input v-model="newName" placeholder="社团名称" />
      <input v-model="newCategory" placeholder="分类（如：学术科技）" />
      <input v-model="newDesc" placeholder="简介（可选）" />
      <button @click="createClub" :disabled="creating || !newName || !newCategory">
        {{ creating ? "创建中…" : "新增社团" }}
      </button>
    </div>

    <div v-if="error" class="err">{{ error }}</div>
    <div v-if="loading">加载中…</div>
    <div v-else-if="clubs.length === 0" class="empty">暂无社团数据</div>
    <div v-else class="grid">
      <div v-for="club in clubs" :key="club.id" class="card">
        <div class="row">
          <h3>{{ club.name }}</h3>
          <button class="del" @click="deleteClub(club.id, club.name)">删除</button>
        </div>
        <div class="meta">{{ club.category ?? "未分类" }}</div>
        <p v-if="club.description">{{ club.description }}</p>
      </div>
    </div>
  </div>
</template>

<style scoped>
.page { max-width: 720px; margin: 0 auto; }
.form { display: flex; gap: 8px; margin-bottom: 16px; flex-wrap: wrap; }
.form input { flex: 1; min-width: 120px; padding: 6px 10px; border: 1px solid #ccc; border-radius: 6px; }
.form button { padding: 6px 16px; border: none; background: #1976d2; color: #fff; border-radius: 6px; cursor: pointer; }
.form button:disabled { opacity: .5; cursor: default; }
.grid { display: flex; flex-direction: column; gap: 16px; margin-top: 16px; }
.card { background: #fff; border-radius: 8px; padding: 16px; box-shadow: 0 1px 3px rgba(0,0,0,.1); }
.row { display: flex; justify-content: space-between; align-items: center; }
.row h3 { margin: 0; }
.del { border: none; background: none; color: #c62828; cursor: pointer; font-size: 13px; }
.meta { font-size: 14px; color: #666; margin-bottom: 4px; }
.err { color: #c62828; margin: 16px 0; }
.empty { color: #999; margin-top: 24px; text-align: center; }
</style>
