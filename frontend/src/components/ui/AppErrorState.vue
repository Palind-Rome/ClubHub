<script setup lang="ts">
import { Refresh, WarningFilled } from "@element-plus/icons-vue";

withDefaults(
  defineProps<{
    title?: string;
    description?: string;
    retryLabel?: string;
  }>(),
  {
    title: "内容加载失败",
    description: "暂时无法获取数据，请检查网络连接后重试。",
    retryLabel: "重新加载",
  },
);

const emit = defineEmits<{
  retry: [];
}>();
</script>

<template>
  <div class="error-state" role="alert">
    <div class="error-icon" aria-hidden="true">
      <el-icon><WarningFilled /></el-icon>
    </div>
    <div class="error-copy">
      <h3>{{ title }}</h3>
      <p>{{ description }}</p>
    </div>
    <el-button v-if="retryLabel" type="primary" plain @click="emit('retry')">
      <el-icon><Refresh /></el-icon>{{ retryLabel }}
    </el-button>
  </div>
</template>

<style scoped>
.error-state {
  display: flex;
  min-height: 120px;
  align-items: center;
  gap: var(--club-space-4);
  padding: var(--club-space-5);
  border: 1px solid color-mix(in srgb, var(--club-danger) 30%, var(--club-border));
  border-radius: var(--club-radius-lg);
  background: color-mix(in srgb, var(--club-danger) 6%, var(--club-surface));
}

.error-icon {
  display: grid;
  width: 44px;
  height: 44px;
  flex: 0 0 auto;
  place-items: center;
  border-radius: 14px;
  color: var(--club-danger);
  background: color-mix(in srgb, var(--club-danger) 12%, transparent);
  font-size: 22px;
}

.error-copy {
  min-width: 0;
  flex: 1;
}

h3 {
  margin: 0;
  color: var(--club-text);
  font-size: 15px;
}

p {
  margin: var(--club-space-1) 0 0;
  color: var(--club-text-secondary);
  font-size: 13px;
  line-height: 1.6;
}

@media (max-width: 560px) {
  .error-state {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
