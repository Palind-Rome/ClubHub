<script setup lang="ts">
import { ref, onMounted } from "vue";

interface Club {
  id: number;
  name: string;
  description: string | null;
  category: string;
  memberCount: number;
  presidentName: string;
}

const clubs = ref<Club[]>([]);
const loading = ref(true);

onMounted(async () => {
  try {
    const res = await fetch("/api/clubs");
    clubs.value = await res.json();
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="page">
    <h2>社团列表</h2>
    <div v-if="loading">加载中…</div>
    <div v-else class="grid">
      <div v-for="club in clubs" :key="club.id" class="card">
        <h3>{{ club.name }}</h3>
        <div class="meta">{{ club.category }} · {{ club.memberCount }} 人</div>
        <div class="meta">社长：{{ club.presidentName }}</div>
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
</style>
