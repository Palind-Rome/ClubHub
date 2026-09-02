<script setup lang="ts">
import { computed } from "vue";
import { marked } from "marked";
import DOMPurify from "dompurify";

interface Props {
  content: string;
}

const props = defineProps<Props>();

// 配置 marked
marked.setOptions({
  breaks: true,
  gfm: true,
});

const renderedHtml = computed(() => {
  if (!props.content) return "";
  try {
    const html = marked(props.content) as string;
    return DOMPurify.sanitize(html, {
      ALLOWED_TAGS: [
        "b",
        "i",
        "em",
        "strong",
        "u",
        "h1",
        "h2",
        "h3",
        "h4",
        "h5",
        "h6",
        "p",
        "br",
        "blockquote",
        "ol",
        "ul",
        "li",
        "code",
        "pre",
        "a",
        "img",
        "table",
        "thead",
        "tbody",
        "tr",
        "th",
        "td",
      ],
      ALLOWED_ATTR: ["href", "title", "src", "alt", "width", "height"],
    });
  } catch (error) {
    console.error("Markdown 渲染失败:", error);
    return DOMPurify.sanitize(props.content);
  }
});
</script>

<template>
  <div class="markdown-renderer" v-html="renderedHtml"></div>
</template>

<style scoped>
.markdown-renderer {
  word-break: break-word;
  overflow-wrap: break-word;
  line-height: 1.65;
}

.markdown-renderer :deep(h1),
.markdown-renderer :deep(h2),
.markdown-renderer :deep(h3),
.markdown-renderer :deep(h4),
.markdown-renderer :deep(h5),
.markdown-renderer :deep(h6) {
  margin: 16px 0 8px 0;
  font-weight: 600;
}

.markdown-renderer :deep(h1) {
  font-size: 24px;
}

.markdown-renderer :deep(h2) {
  font-size: 20px;
}

.markdown-renderer :deep(h3) {
  font-size: 18px;
}

.markdown-renderer :deep(h4) {
  font-size: 16px;
}

.markdown-renderer :deep(p) {
  margin: 8px 0;
}

.markdown-renderer :deep(code) {
  background: var(--el-fill-color-light);
  padding: 2px 6px;
  border-radius: 3px;
  font-family: "Monaco", "Menlo", "Consolas", monospace;
  font-size: 0.9em;
}

.markdown-renderer :deep(pre) {
  background: var(--el-fill-color-light);
  padding: 12px;
  border-radius: 4px;
  overflow-x: auto;
  margin: 12px 0;
}

.markdown-renderer :deep(pre code) {
  background: none;
  padding: 0;
}

.markdown-renderer :deep(blockquote) {
  border-left: 4px solid var(--el-border-color);
  margin: 12px 0;
  padding: 0 12px;
  color: var(--el-text-color-secondary);
}

.markdown-renderer :deep(ul),
.markdown-renderer :deep(ol) {
  margin: 8px 0;
  padding-left: 24px;
}

.markdown-renderer :deep(li) {
  margin: 4px 0;
}

.markdown-renderer :deep(a) {
  color: var(--el-color-primary);
  text-decoration: none;

  &:hover {
    text-decoration: underline;
  }
}

.markdown-renderer :deep(img) {
  max-width: 100%;
  height: auto;
  border-radius: 4px;
  margin: 8px 0;
}

.markdown-renderer :deep(table) {
  border-collapse: collapse;
  margin: 12px 0;
  width: 100%;
}

.markdown-renderer :deep(table th),
.markdown-renderer :deep(table td) {
  border: 1px solid var(--el-border-color);
  padding: 8px;
  text-align: left;
}

.markdown-renderer :deep(table th) {
  background: var(--el-fill-color-light);
  font-weight: 600;
}
</style>
