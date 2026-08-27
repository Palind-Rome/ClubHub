<script setup lang="ts">
import { Delete, Hide, View, ChatDotRound } from "@element-plus/icons-vue";
import { ElButton } from "element-plus";
import type { ForumPost } from "../api/models";
import MarkdownRenderer from "./MarkdownRenderer.vue";

interface Props {
  reply: ForumPost;
  canModerate: boolean;
  canPost: boolean;
  canDeletePost: (post: ForumPost) => boolean;
  moderatingPostIds: Set<number>;
}

interface Emits {
  (e: "reply-to", post: ForumPost): void;
  (e: "moderate", post: ForumPost, change: Partial<Pick<ForumPost, "postStatus">>): void;
  (e: "delete", post: ForumPost): void;
}

const props = defineProps<Props>();
const emit = defineEmits<Emits>();

const formatTime = (value: Date) => new Date(value).toLocaleString("zh-CN", { hour12: false });
</script>

<template>
  <div class="reply-item" :class="{ hidden: reply.postStatus === 'hidden' }">
    <div class="reply-header">
      <small>{{ reply.userName || "匿名用户" }} · {{ formatTime(reply.createdAt) }}</small>
    </div>
    <MarkdownRenderer :content="reply.content" />
    <div class="reply-actions">
      <el-button
        v-if="canPost"
        link
        :icon="ChatDotRound"
        :disabled="reply.postStatus === 'hidden'"
        @click="emit('reply-to', reply)"
      >
        回复
      </el-button>
      <el-button
        v-if="canModerate"
        link
        :icon="reply.postStatus === 'hidden' ? View : Hide"
        :disabled="moderatingPostIds.has(reply.id)"
        @click="
          emit('moderate', reply, {
            postStatus: reply.postStatus === 'hidden' ? 'published' : 'hidden',
          })
        "
      >
        {{ reply.postStatus === "hidden" ? "恢复显示" : "隐藏" }}
      </el-button>
      <el-button
        v-if="canModerate"
        link
        type="danger"
        :icon="Delete"
        @click="emit('delete', reply)"
      >
        删除
      </el-button>
      <el-button
        v-if="!canModerate && canDeletePost(reply)"
        link
        type="danger"
        :icon="Delete"
        @click="emit('delete', reply)"
      >
        删除
      </el-button>
    </div>

    <!-- 递归显示嵌套回复 -->
    <div v-if="reply.replies && reply.replies.length > 0" class="nested-replies">
      <ReplyItem
        v-for="nestedReply in reply.replies"
        :key="nestedReply.id"
        :reply="nestedReply"
        :can-moderate="canModerate"
        :can-post="canPost"
        :can-delete-post="canDeletePost"
        :moderating-post-ids="moderatingPostIds"
          @reply-to="
            replyingTo = $event;
            replyingToParentId = $event.parentPostId || $event.id;
          "
          @moderate="(post, change) => moderate(post, change)"
          @delete="deletePost($event)"
      />
    </div>
  </div>
</template>

<style scoped>
.reply-item {
  margin-top: 12px;
  padding: 12px;
  border-left: 3px solid var(--el-border-color);
}

.reply-item.hidden {
  background: var(--el-fill-color-lighter);
}

.reply-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.reply-actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

.nested-replies {
  margin-left: 12px;
}

small {
  color: var(--el-text-color-secondary);
}
</style>
