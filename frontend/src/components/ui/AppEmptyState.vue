<script setup lang="ts">
import { Box } from "@element-plus/icons-vue";

withDefaults(
  defineProps<{
    title?: string;
    description?: string;
    actionLabel?: string;
  }>(),
  {
    title: "暂无内容",
    description: "这里还没有可展示的数据。",
    actionLabel: "",
  },
);

const emit = defineEmits<{
  action: [];
}>();
</script>

<template>
  <div class="state-card empty-state">
    <div class="state-icon" aria-hidden="true">
      <el-icon><Box /></el-icon>
    </div>
    <h3>{{ title }}</h3>
    <p>{{ description }}</p>
    <div v-if="$slots.default || actionLabel" class="state-action">
      <slot>
        <el-button v-if="actionLabel" type="primary" plain @click="emit('action')">
          {{ actionLabel }}
        </el-button>
      </slot>
    </div>
  </div>
</template>

<style scoped>
.state-card {
  display: flex;
  min-height: 260px;
  align-items: center;
  justify-content: center;
  padding: var(--club-space-8);
  border: 1px dashed var(--club-border-strong);
  border-radius: var(--club-radius-lg);
  flex-direction: column;
  text-align: center;
  background: color-mix(in srgb, var(--club-surface) 72%, transparent);
}

.state-icon {
  display: grid;
  width: 64px;
  height: 64px;
  margin-bottom: var(--club-space-4);
  place-items: center;
  border-radius: 22px;
  color: var(--club-primary);
  background: linear-gradient(135deg, var(--club-primary-soft), var(--club-accent-soft));
  font-size: 30px;
}

h3 {
  margin: 0;
  color: var(--club-text);
  font-size: 18px;
}

p {
  max-width: 460px;
  margin: var(--club-space-2) 0 0;
  color: var(--club-text-secondary);
  font-size: 13px;
  line-height: 1.7;
}

.state-action {
  margin-top: var(--club-space-5);
}
</style>
