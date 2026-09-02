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

const textContent = computed({
  get: () => props.modelValue,
  set: (value) => emit("update:modelValue", value),
});

async function handleImageUpload(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file || !props.clubId) return;

  isUploading.value = true;
  emit("image-upload-start");

  try {
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
        {{ isUploading ? "上传中..." : "插入图片" }}
      </el-button>
    </div>
    <el-input
      ref="textareaRef"
      v-model="textContent"
      type="textarea"
      :placeholder="placeholder"
      :rows="rows"
      :maxlength="maxlength"
      show-word-limit
    />
    <input
      ref="fileInput"
      type="file"
      accept="image/jpeg,image/png,image/gif,image/webp"
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
</style>
