<script setup lang="ts">
withDefaults(
  defineProps<{
    title: string;
    description?: string;
    eyebrow?: string;
  }>(),
  {
    description: "",
    eyebrow: "",
  },
);
</script>

<template>
  <header class="page-header">
    <div class="page-heading">
      <span v-if="eyebrow" class="eyebrow">{{ eyebrow }}</span>
      <h2>{{ title }}</h2>
      <p v-if="description">{{ description }}</p>
      <div v-if="$slots.meta" class="page-meta"><slot name="meta" /></div>
    </div>
    <div v-if="$slots.actions" class="page-actions"><slot name="actions" /></div>
  </header>
</template>

<style scoped>
.page-header {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: var(--club-space-5);
  margin-bottom: var(--club-space-6);
}

.page-heading {
  min-width: 0;
}

.eyebrow {
  display: block;
  margin-bottom: var(--club-space-2);
  color: var(--club-accent);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}

h2 {
  margin: 0;
  color: var(--club-text);
  font-size: clamp(24px, 3vw, 34px);
  line-height: 1.2;
  letter-spacing: -0.03em;
}

p {
  max-width: 720px;
  margin: var(--club-space-2) 0 0;
  color: var(--club-text-secondary);
  font-size: 14px;
  line-height: 1.7;
}

.page-meta {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--club-space-2);
  margin-top: var(--club-space-3);
}

.page-actions {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: var(--club-space-2);
}

@media (max-width: 680px) {
  .page-header {
    align-items: stretch;
    flex-direction: column;
  }

  .page-actions {
    justify-content: flex-start;
  }
}
</style>
