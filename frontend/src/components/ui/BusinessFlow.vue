<script setup lang="ts">
defineProps<{
  title: string;
  description: string;
  steps: readonly string[];
}>();
</script>

<template>
  <article class="business-flow">
    <div class="flow-copy">
      <h3>{{ title }}</h3>
      <p>{{ description }}</p>
    </div>
    <ol>
      <li v-for="(step, index) in steps" :key="step">
        <span>{{ index + 1 }}</span>
        <strong>{{ step }}</strong>
      </li>
    </ol>
  </article>
</template>

<style scoped>
.business-flow {
  display: grid;
  gap: var(--club-space-5);
  min-width: 0;
  padding: var(--club-space-6);
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-lg);
  background: var(--club-bg-elevated);
  box-shadow: var(--club-shadow-sm);
}

h3,
p {
  margin: 0;
}

h3 {
  font-size: 17px;
}

p {
  margin-top: 6px;
  color: var(--club-text-secondary);
  line-height: 1.6;
}

ol {
  display: flex;
  margin: 0;
  padding: 0;
  list-style: none;
}

li {
  position: relative;
  display: grid;
  flex: 1;
  gap: 7px;
  justify-items: center;
  min-width: 0;
  color: var(--club-text-secondary);
  font-size: 13px;
  text-align: center;
}

li:not(:last-child)::after {
  position: absolute;
  top: 13px;
  left: calc(50% + 18px);
  width: calc(100% - 36px);
  height: 2px;
  background: linear-gradient(90deg, var(--club-primary), var(--club-accent));
  content: "";
  opacity: 0.35;
}

li span {
  z-index: 1;
  display: grid;
  width: 28px;
  height: 28px;
  place-items: center;
  border: 1px solid color-mix(in srgb, var(--club-primary) 34%, var(--club-border));
  border-radius: 50%;
  color: var(--club-primary-strong);
  background: linear-gradient(135deg, var(--club-primary-soft), var(--club-accent-soft));
  font-weight: 800;
}

@media (max-width: 640px) {
  ol {
    flex-direction: column;
    gap: 12px;
  }

  li {
    grid-template-columns: 28px 1fr;
    align-items: center;
    justify-items: start;
    text-align: left;
  }

  li:not(:last-child)::after {
    top: 31px;
    left: 13px;
    width: 2px;
    height: 10px;
  }
}
</style>
