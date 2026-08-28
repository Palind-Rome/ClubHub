<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { ChatDotRound, Hide, Refresh, Star, View } from "@element-plus/icons-vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import type { Club, UserSummary } from "../api/models";
import { onSessionChange, readAuth } from "../authSession";
import { requestJson } from "../composables/useApiRequest";

type Status = "published" | "hidden";
interface Post {
  id: number;
  title?: string | null;
  content: string;
  userName?: string | null;
  isTop: boolean;
  postStatus: Status;
  createdAt: string;
  replies: Post[];
}

const clubs = ref<Club[]>([]);
const topics = ref<Post[]>([]);
const selectedClubId = ref<number>();
const loading = ref(false);
const showHidden = ref(false);
const replyingTo = ref<Post | null>(null);
const saving = ref(false);
const auth = ref(readAuth());
const currentMemberClubIds = ref(new Set<number>());
const topicFormRef = ref<FormInstance>();
const replyFormRef = ref<FormInstance>();
const topicForm = reactive({ title: "", content: "" });
const replyForm = reactive({ content: "" });
const moderatingPostIds = ref(new Set<number>());
let postsRequestVersion = 0;
let stopSessionListener: (() => void) | null = null;

const canPost = computed(() =>
  (auth.value?.permissions ?? []).some((item) => item === "*" || item === "forum:post"),
);
const canModerate = computed(() =>
  (auth.value?.permissions ?? []).some((item) => item === "*" || item === "forum:moderate"),
);
const canPostToSelectedClub = computed(
  () =>
    canPost.value &&
    Boolean(selectedClubId.value && currentMemberClubIds.value.has(selectedClubId.value)),
);
const topicRules: FormRules = {
  title: [{ required: true, message: "请输入话题标题", trigger: "blur" }],
  content: [{ required: true, message: "请输入话题内容", trigger: "blur" }],
};
const replyRules: FormRules = {
  content: [{ required: true, message: "请输入回复内容", trigger: "blur" }],
};

async function loadClubs() {
  try {
    const [clubResult, userResult] = await Promise.allSettled([
      requestJson<Club[]>("/api/v1/clubs"),
      requestJson<UserSummary[]>("/api/v1/users"),
    ]);
    if (clubResult.status === "rejected") throw clubResult.reason;
    clubs.value = clubResult.value;
    const users = userResult.status === "fulfilled" ? userResult.value : [];
    const currentUser = users.find((user) => user.id === auth.value?.user.id);
    currentMemberClubIds.value = new Set(
      (currentUser?.memberships ?? [])
        .filter((membership) => {
          const status = (membership.memberStatus ?? "active").trim().toLowerCase();
          return ["active", "normal", "enabled", "在任", "正常"].includes(status);
        })
        .map((membership) => membership.clubId),
    );
    selectedClubId.value ??= clubs.value[0]?.id;
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "社团列表加载失败");
  }
}

async function loadPosts() {
  if (!selectedClubId.value) return;
  const requestVersion = ++postsRequestVersion;
  const clubId = selectedClubId.value;
  const includeHidden = showHidden.value;
  loading.value = true;
  try {
    const query = includeHidden ? "?includeHidden=true" : "";
    const posts = await requestJson<Post[]>(`/api/v1/clubs/${clubId}/forum-posts${query}`);
    if (requestVersion === postsRequestVersion) topics.value = posts;
  } catch (error) {
    if (requestVersion === postsRequestVersion) {
      topics.value = [];
      ElMessage.error(error instanceof Error ? error.message : "讨论区加载失败");
    }
  } finally {
    loading.value = false;
  }
}

async function createPost(parentPostId?: number) {
  const form = parentPostId ? replyFormRef.value : topicFormRef.value;
  const valid = await form?.validate().catch(() => false);
  if (!selectedClubId.value || !valid) return;
  saving.value = true;
  try {
    await requestJson(`/api/v1/clubs/${selectedClubId.value}/forum-posts`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(parentPostId ? { parentPostId, content: replyForm.content } : topicForm),
    });
    topicForm.title = "";
    topicForm.content = "";
    replyForm.content = "";
    replyingTo.value = null;
    ElMessage.success(parentPostId ? "回复成功" : "话题发布成功");
    await loadPosts();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "发布失败");
  } finally {
    saving.value = false;
  }
}

async function moderate(post: Post, change: Partial<Pick<Post, "isTop" | "postStatus">>) {
  if (!selectedClubId.value) return;
  if (moderatingPostIds.value.has(post.id)) return;
  moderatingPostIds.value = new Set(moderatingPostIds.value).add(post.id);
  try {
    await requestJson(`/api/v1/clubs/${selectedClubId.value}/forum-posts/${post.id}/moderation`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        isTop: change.isTop ?? post.isTop,
        postStatus: change.postStatus ?? post.postStatus,
      }),
    });
    ElMessage.success("管理操作成功");
    await loadPosts();
  } catch (error) {
    ElMessage.error(error instanceof Error ? error.message : "管理操作失败");
  } finally {
    const next = new Set(moderatingPostIds.value);
    next.delete(post.id);
    moderatingPostIds.value = next;
  }
}

const formatTime = (value: string) => new Date(value).toLocaleString("zh-CN", { hour12: false });
watch(selectedClubId, () => void loadPosts());
watch(showHidden, () => void loadPosts());
onMounted(() => {
  stopSessionListener = onSessionChange(() => {
    auth.value = readAuth();
    postsRequestVersion++;
    topics.value = [];
    if (!canModerate.value) showHidden.value = false;
    void loadPosts();
  });
  void loadClubs();
});
onUnmounted(() => stopSessionListener?.());
</script>

<template>
  <section class="forum-page">
    <div class="heading app-page-header">
      <div>
        <h1>社团讨论区</h1>
        <p>来聊点有意思的。一起建设社团的精神家园！</p>
      </div>
      <div class="toolbar">
        <el-select v-model="selectedClubId" placeholder="选择社团" class="club-select">
          <el-option v-for="club in clubs" :key="club.id" :label="club.name" :value="club.id" />
        </el-select>
        <el-checkbox v-if="canModerate" v-model="showHidden">查看隐藏内容</el-checkbox>
        <el-button :icon="Refresh" @click="loadPosts">刷新</el-button>
      </div>
    </div>
    <el-alert
      v-if="!canPost"
      title="当前账号没有讨论区发布权限。"
      type="info"
      :closable="false"
      show-icon
    />
    <el-alert
      v-else-if="canPost && selectedClubId && !canPostToSelectedClub"
      title="你可以浏览该社团讨论，但只有当前有效成员才能发布或回复。"
      type="info"
      :closable="false"
      show-icon
      class="membership-notice"
    />
    <el-card v-if="canPostToSelectedClub" class="composer" shadow="never">
      <template #header>发布话题</template>
      <el-form ref="topicFormRef" :model="topicForm" :rules="topicRules" label-position="top">
        <el-form-item label="标题" prop="title"
          ><el-input v-model="topicForm.title" maxlength="120" show-word-limit
        /></el-form-item>
        <el-form-item label="内容" prop="content"
          ><el-input
            v-model="topicForm.content"
            type="textarea"
            :rows="4"
            maxlength="4000"
            show-word-limit
        /></el-form-item>
        <el-button type="primary" :loading="saving" @click="createPost()">发布话题</el-button>
      </el-form>
    </el-card>
    <el-skeleton v-if="loading" :rows="5" animated />
    <el-empty v-else-if="selectedClubId && !topics.length" description="暂时还没有话题" />
    <article
      v-for="topic in topics"
      :key="topic.id"
      class="topic"
      :class="{ hidden: topic.postStatus === 'hidden' }"
    >
      <header>
        <div>
          <el-icon v-if="topic.isTop"><Star /></el-icon><strong>{{ topic.title }}</strong
          ><el-tag v-if="topic.isTop" type="warning" size="small">置顶</el-tag
          ><el-tag v-if="topic.postStatus === 'hidden'" type="info" size="small">已隐藏</el-tag>
        </div>
        <small>{{ topic.userName || "匿名用户" }} · {{ formatTime(topic.createdAt) }}</small>
      </header>
      <p>{{ topic.content }}</p>
      <div class="actions">
        <el-button
          v-if="canPostToSelectedClub"
          link
          :icon="ChatDotRound"
          :disabled="topic.postStatus === 'hidden'"
          @click="replyingTo = topic"
          >回复</el-button
        >
        <template v-if="canModerate"
          ><el-button
            link
            :icon="Star"
            :disabled="moderatingPostIds.has(topic.id)"
            @click="moderate(topic, { isTop: !topic.isTop })"
            >{{ topic.isTop ? "取消置顶" : "置顶" }}</el-button
          ><el-button
            link
            :icon="topic.postStatus === 'hidden' ? View : Hide"
            :disabled="moderatingPostIds.has(topic.id)"
            @click="
              moderate(topic, {
                postStatus: topic.postStatus === 'hidden' ? 'published' : 'hidden',
              })
            "
            >{{ topic.postStatus === "hidden" ? "恢复显示" : "隐藏" }}</el-button
          ></template
        >
      </div>
      <div
        v-for="reply in topic.replies"
        :key="reply.id"
        class="reply"
        :class="{ hidden: reply.postStatus === 'hidden' }"
      >
        <small>{{ reply.userName || "匿名用户" }} · {{ formatTime(reply.createdAt) }}</small>
        <p>{{ reply.content }}</p>
        <el-button
          v-if="canModerate"
          link
          :icon="reply.postStatus === 'hidden' ? View : Hide"
          @click="
            moderate(reply, { postStatus: reply.postStatus === 'hidden' ? 'published' : 'hidden' })
          "
          >{{ reply.postStatus === "hidden" ? "恢复显示" : "隐藏" }}</el-button
        >
      </div>
    </article>
    <el-dialog
      :model-value="Boolean(replyingTo)"
      title="回复话题"
      width="min(560px, calc(100vw - 32px))"
      @close="replyingTo = null"
    >
      <el-form ref="replyFormRef" :model="replyForm" :rules="replyRules" label-position="top"
        ><el-form-item label="回复内容" prop="content"
          ><el-input
            v-model="replyForm.content"
            type="textarea"
            :rows="5"
            maxlength="4000"
            show-word-limit /></el-form-item
      ></el-form>
      <template #footer
        ><el-button @click="replyingTo = null">取消</el-button
        ><el-button
          type="primary"
          :loading="saving"
          @click="replyingTo && createPost(replyingTo.id)"
          >发布回复</el-button
        ></template
      >
    </el-dialog>
  </section>
</template>

<style scoped>
.forum-page {
  max-width: 1100px;
  margin: 0 auto;
}
.heading,
.toolbar,
.actions {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}
.heading {
  justify-content: space-between;
  margin-bottom: 16px;
}
.club-select {
  width: 240px;
}
.composer {
  margin: 16px 0;
}
.membership-notice {
  --el-alert-bg-color: color-mix(in srgb, var(--club-primary-soft) 78%, var(--club-bg-elevated));
  margin-bottom: 16px;
  border: 1px solid color-mix(in srgb, var(--club-primary) 30%, var(--club-border));
}
.membership-notice :deep(.el-alert__title) {
  color: var(--club-text);
}
.topic {
  border: 1px solid var(--el-border-color-light);
  border-radius: 6px;
  padding: 16px;
  margin-top: 12px;
}
.topic.hidden,
.reply.hidden {
  background: var(--el-fill-color-lighter);
}
.topic header {
  display: flex;
  justify-content: space-between;
  gap: 12px;
}
.topic p {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.65;
}
.reply {
  margin-top: 12px;
  padding: 12px;
  border-left: 3px solid var(--el-border-color);
}
small {
  color: var(--el-text-color-secondary);
}
h1 {
  margin: 0;
  font-size: 24px;
}
@media (max-width: 640px) {
  .topic header {
    display: block;
  }
  .club-select {
    width: 100%;
  }
}
</style>
