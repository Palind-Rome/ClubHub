<script setup lang="ts">
import { computed, ref } from "vue";
import { ElButton, ElInput, ElMessage, type InputInstance } from "element-plus";
import { Picture, Loading } from "@element-plus/icons-vue";
import { requestJson } from "../composables/useApiRequest";
import { ForumImageUploadResponseFromJSON } from "../api/models";

interface Props {
  modelValue: string;
  placeholder?: string;
  rows?: number;
  maxlength?: number;
  uploading?: boolean;
  clubId?: number;
}

interface Emits {
  (e: "update:modelValue", value: string): void;
  (e: "image-upload-start"): void;
  (e: "image-upload-end"): void;
}

const props = withDefaults(defineProps<Props>(), {
  placeholder: "输入内容...",
  rows: 4,
  maxlength: 4000,
});

const emit = defineEmits<Emits>();
const fileInput = ref<HTMLInputElement>();
const textareaRef = ref<InputInstance>();
const isUploading = ref(false);
const isDraggingOver = ref(false);

const textContent = computed({
  get: () => props.modelValue,
  set: (value) => emit("update:modelValue", value),
});

async function handleImageUpload(event: Event) {
  const input = event.target as HTMLInputElement;
  const files = input.files;
  if (!files || files.length === 0 || !props.clubId) return;

  isUploading.value = true;
  emit("image-upload-start");

  try {
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const formData = new FormData();
      formData.append("image", file);

      const response = ForumImageUploadResponseFromJSON(
        await requestJson<unknown>(
          `/api/v1/clubs/${props.clubId}/forum-posts/upload-image`,
          {
            method: "POST",
            body: formData,
          },
          60_000,
        ),
      );

      const markdownImage = `![${response.fileName}](${response.imageUrl})`;
      const textarea = textareaRef.value?.textarea;
      if (textarea) {
        const start = textarea.selectionStart;
        const end = textarea.selectionEnd;
        const before = textContent.value.substring(0, start);
        const after = textContent.value.substring(end);
        const nextContent = `${before}\n${markdownImage}\n${after}`;
        if (nextContent.length > props.maxlength) {
          ElMessage.error(`插入图片后内容不能超过 ${props.maxlength} 个字符`);
          // Clean up the uploaded image
          if (response.storageKey) {
            await requestJson(
              `/api/v1/clubs/${props.clubId}/forum-posts/delete-image?storageKey=${encodeURIComponent(response.storageKey)}`,
              { method: "DELETE" },
            ).catch(() => {
              // Silently ignore cleanup errors
            });
          }
          return;
        }
        textContent.value = nextContent;
        textarea.focus();
        textarea.setSelectionRange(
          start + markdownImage.length + 2,
          start + markdownImage.length + 2,
        );
      }

      ElMessage.success("图片上传成功");
    }
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "图片上传失败");
  } finally {
    isUploading.value = false;
    emit("image-upload-end");
    if (fileInput.value) fileInput.value.value = "";
  }
}

function triggerImageUpload() {
  if (props.clubId && !isUploading.value) {
    fileInput.value?.click();
  }
}

function handleDragOver(event: DragEvent) {
  event.preventDefault();
  event.stopPropagation();
  if (!props.clubId || isUploading.value) return;
  isDraggingOver.value = true;
}

function handleDragLeave(event: DragEvent) {
  event.preventDefault();
  event.stopPropagation();
  isDraggingOver.value = false;
}

async function handleDrop(event: DragEvent) {
  event.preventDefault();
  event.stopPropagation();
  isDraggingOver.value = false;

  if (!props.clubId || isUploading.value) return;

  const files = event.dataTransfer?.files;
  if (!files || files.length === 0) return;

  const imageFiles = Array.from(files).filter((file) => file.type.startsWith("image/"));
  const ignoredCount = files.length - imageFiles.length;

  if (ignoredCount > 0) {
    ElMessage.warning(`已忽略 ${ignoredCount} 个非图片文件`);
  }

  if (imageFiles.length === 0) {
    ElMessage.warning("请拖拽图片文件");
    return;
  }

  // Create a synthetic event to reuse handleImageUpload
  const syntheticInput = document.createElement("input");
  syntheticInput.type = "file";
  const dataTransfer = new DataTransfer();
  imageFiles.forEach((file) => dataTransfer.items.add(file));
  syntheticInput.files = dataTransfer.files;

  const syntheticEvent = new Event("change", { bubbles: true });
  Object.defineProperty(syntheticEvent, "target", {
    value: syntheticInput,
    enumerable: true,
  });

  await handleImageUpload(syntheticEvent);
}
</script>

<template>
  <div class="markdown-editor">
    <div class="toolbar">
      <span class="hint">ClubHub 现已支持 Markdown 格式</span>
      <el-button
        v-if="clubId"
        link
        :icon="isUploading ? Loading : Picture"
        :disabled="isUploading"
        @click="triggerImageUpload"
      >
        {{ isUploading ? "上传中..." : "插入图片（支持拖拽）" }}
      </el-button>
    </div>
    <div
      class="textarea-wrapper"
      :class="{ dragging: isDraggingOver }"
      @dragover="handleDragOver"
      @dragleave="handleDragLeave"
      @drop="handleDrop"
    >
      <el-input
        ref="textareaRef"
        v-model="textContent"
        type="textarea"
        :placeholder="placeholder"
        :rows="rows"
        :maxlength="maxlength"
        show-word-limit
      />
      <div v-if="isDraggingOver" class="drag-overlay">
        <div class="drag-hint">放开即可上传</div>
      </div>
    </div>
    <input
      ref="fileInput"
      type="file"
      accept="image/jpeg,image/png,image/gif,image/webp"
      multiple
      style="display: none"
      @change="handleImageUpload"
    />
  </div>
</template>

<style scoped>
.markdown-editor {
  display: flex;
  flex-direction: column;
  gap: 8px;
  width: 100%;
}

.toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0 4px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.hint {
  font-size: 12px;
}

.textarea-wrapper {
  position: relative;
}

.textarea-wrapper.dragging :deep(.el-input__wrapper) {
  border-color: var(--el-color-primary);
  box-shadow: 0 0 0 2px var(--el-color-primary-light-7);
}

.drag-overlay {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(94, 124, 224, 0.1);
  border: 2px dashed var(--el-color-primary);
  border-radius: var(--el-input-border-radius, var(--el-border-radius-base));
  display: flex;
  align-items: center;
  justify-content: center;
  pointer-events: none;
  z-index: 10;
}

.drag-hint {
  font-size: 14px;
  font-weight: 500;
  color: var(--el-color-primary);
  background: white;
  padding: 8px;
  border-radius: var(--el-input-border-radius, var(--el-border-radius-base));
  box-shadow: 0 2px 12px rgba(0, 0, 0, 0.1);
}
</style>
