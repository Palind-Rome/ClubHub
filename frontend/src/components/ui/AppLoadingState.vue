<script setup lang="ts">
withDefaults(
  defineProps<{
    title?: string;
    rows?: number;
  }>(),
  {
    title: "正在加载",
    rows: 4,
  },
);
</script>

<template>
  <div class="loading-state" role="status" aria-live="polite" :aria-label="title">
    <div class="loading-heading">
      <span class="loading-pulse" aria-hidden="true" />
      <span>{{ title }}</span>
    </div>
    <el-skeleton animated :rows="rows" />
  </div>
</template>

<style scoped>
.loading-state {
  padding: var(--club-space-5);
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-lg);
  background: var(--club-surface);
}

.loading-heading {
  display: flex;
  align-items: center;
  gap: var(--club-space-2);
  margin-bottom: var(--club-space-4);
  color: var(--club-text-secondary);
  font-size: 13px;
  font-weight: 600;
}

.loading-pulse {
  width: 9px;
  height: 9px;
  border-radius: 50%;
  background: var(--club-primary);
  box-shadow: 0 0 0 0 color-mix(in srgb, var(--club-primary) 36%, transparent);
  animation: state-pulse 1.6s ease-out infinite;
}

@keyframes state-pulse {
  70% {
    box-shadow: 0 0 0 8px transparent;
  }
  100% {
    box-shadow: 0 0 0 0 transparent;
  }
}
</style>
