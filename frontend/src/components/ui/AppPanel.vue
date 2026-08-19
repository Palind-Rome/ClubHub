<script setup lang="ts">
withDefaults(
  defineProps<{
    title?: string;
    description?: string;
    padding?: "default" | "compact" | "none";
  }>(),
  {
    title: "",
    description: "",
    padding: "default",
  },
);
</script>

<template>
  <section class="panel" :class="`panel-padding-${padding}`">
    <header v-if="title || description || $slots.header || $slots.actions" class="panel-header">
      <div class="panel-heading">
        <slot name="header">
          <h3 v-if="title">{{ title }}</h3>
          <p v-if="description">{{ description }}</p>
        </slot>
      </div>
      <div v-if="$slots.actions" class="panel-actions"><slot name="actions" /></div>
    </header>
    <div class="panel-body"><slot /></div>
    <footer v-if="$slots.footer" class="panel-footer"><slot name="footer" /></footer>
  </section>
</template>

<style scoped>
.panel {
  overflow: hidden;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-lg);
  background: var(--club-surface);
  box-shadow: var(--club-shadow-sm);
  backdrop-filter: var(--club-glass-blur);
}

.panel-padding-default {
  padding: var(--club-space-6);
}

.panel-padding-compact {
  padding: var(--club-space-4);
}

.panel-padding-none {
  padding: 0;
}

.panel-padding-none .panel-header,
.panel-padding-none .panel-footer {
  padding: var(--club-space-5) var(--club-space-6);
}

.panel-padding-none .panel-footer {
  border-top: 1px solid var(--club-border);
}

.panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--club-space-4);
  margin-bottom: var(--club-space-5);
}

.panel-padding-none .panel-header {
  margin-bottom: 0;
  border-bottom: 1px solid var(--club-border);
}

.panel-heading {
  min-width: 0;
}

h3 {
  margin: 0;
  color: var(--club-text);
  font-size: 17px;
  line-height: 1.35;
}

p {
  margin: var(--club-space-1) 0 0;
  color: var(--club-text-secondary);
  font-size: 13px;
  line-height: 1.6;
}

.panel-actions {
  display: flex;
  flex: 0 0 auto;
  align-items: center;
  gap: var(--club-space-2);
}

.panel-footer {
  margin-top: var(--club-space-5);
}

.panel-padding-none .panel-footer {
  margin-top: 0;
}

@media (max-width: 600px) {
  .panel-padding-default {
    padding: var(--club-space-4);
  }

  .panel-header {
    align-items: stretch;
    flex-direction: column;
  }
}
</style>
