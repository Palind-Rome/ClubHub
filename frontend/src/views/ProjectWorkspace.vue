<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { RouterLink, useRoute } from "vue-router";
import { ElMessage } from "element-plus";
import { ProjectProjectStatusEnum, ResponseError, type Club, type Project } from "../api";
import { apiClient as api } from "../apiClient";
import ProjectMembersPanel from "../components/ProjectMembersPanel.vue";

const route = useRoute();
const project = ref<Project | null>(null);
const clubs = ref<Club[]>([]);
const loading = ref(true);
const loadError = ref("");
const activeTab = ref("members");
let workspaceRequestVersion = 0;

const projectId = computed(() => {
  const raw = Array.isArray(route.params.projectId)
    ? route.params.projectId[0]
    : route.params.projectId;
  const value = Number(raw);
  return Number.isInteger(value) && value > 0 ? value : null;
});

const clubName = computed(() => {
  if (!project.value) return "未知社团";
  return clubs.value.find((club) => club.id === project.value?.clubId)?.name ?? "未知社团";
});

const statusLabel: Record<string, string> = {
  [ProjectProjectStatusEnum.Pending]: "待审核",
  [ProjectProjectStatusEnum.Running]: "执行中",
  [ProjectProjectStatusEnum.Finished]: "已完成",
  [ProjectProjectStatusEnum.Delayed]: "已延期",
  [ProjectProjectStatusEnum.Closed]: "已关闭",
};

const statusType: Record<string, "success" | "warning" | "info" | "danger" | "primary"> = {
  [ProjectProjectStatusEnum.Pending]: "warning",
  [ProjectProjectStatusEnum.Running]: "success",
  [ProjectProjectStatusEnum.Finished]: "primary",
  [ProjectProjectStatusEnum.Delayed]: "danger",
  [ProjectProjectStatusEnum.Closed]: "info",
};
const numericStatusMap: Record<string, string> = { "1": "pending", "2": "running", "3": "finished", "4": "delayed", "5": "closed" };
const normalizedStatus = computed(() => project.value ? (numericStatusMap[String(project.value.projectStatus)] ?? String(project.value.projectStatus)) : "");

async function loadWorkspace() {
  const requestVersion = ++workspaceRequestVersion;
  loading.value = true;
  loadError.value = "";
  project.value = null;

  if (!projectId.value) {
    loadError.value = "项目编号无效，请从项目列表重新进入。";
    loading.value = false;
    return;
  }

  try {
    const [projectResult, clubResult] = await Promise.all([
      api.getProjectById({ projectId: projectId.value }),
      api.getClubs(),
    ]);
    if (requestVersion !== workspaceRequestVersion) return;
    project.value = projectResult;
    clubs.value = clubResult;
  } catch (error) {
    if (requestVersion !== workspaceRequestVersion) return;
    loadError.value = await toErrorMessage(error, "项目空间加载失败");
    ElMessage.error(loadError.value);
  } finally {
    if (requestVersion === workspaceRequestVersion) loading.value = false;
  }
}

function formatDate(value?: Date | null) {
  if (!value) return "未设置";
  return new Intl.DateTimeFormat("zh-CN", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
  }).format(value);
}

async function toErrorMessage(error: unknown, fallback: string) {
  if (error instanceof ResponseError) {
    try {
      const body = (await error.response.clone().json()) as { message?: string };
      if (body.message) return `${fallback}：${body.message}`;
    } catch {
      return `${fallback}（HTTP ${error.response.status}）`;
    }
  }

  return fallback;
}

watch(projectId, loadWorkspace, { immediate: true });
</script>

<template>
  <div class="page workspace-page">
    <div class="workspace-nav">
      <RouterLink to="/projects" custom v-slot="{ href, navigate }">
        <el-link :href="href" underline="never" @click="navigate">← 返回项目列表</el-link>
      </RouterLink>
    </div>

    <el-skeleton v-if="loading" :rows="8" animated />

    <el-result
      v-else-if="loadError || !project"
      icon="error"
      title="无法打开项目空间"
      :sub-title="loadError || '项目不存在或已不可访问。'"
    >
      <template #extra>
        <el-button type="primary" @click="loadWorkspace">重新加载</el-button>
        <RouterLink to="/projects" custom v-slot="{ href, navigate }">
          <el-link class="result-back-link" :href="href" underline="never" @click="navigate">
            返回项目列表
          </el-link>
        </RouterLink>
      </template>
    </el-result>

    <template v-else>
      <section class="workspace-header">
        <div class="workspace-heading">
          <div class="eyebrow">项目空间 · #{{ project.id }}</div>
          <div class="title-row">
            <h1>{{ project.projectName }}</h1>
            <el-tag :type="statusType[normalizedStatus] || 'info'" effect="plain">
              {{ statusLabel[normalizedStatus] || normalizedStatus }}
            </el-tag>
          </div>
          <p>{{ project.description || "该项目暂未填写简介。" }}</p>
        </div>

        <dl class="project-context">
          <div>
            <dt>所属社团</dt>
            <dd>{{ clubName }}</dd>
          </div>
          <div>
            <dt>项目负责人</dt>
            <dd>{{ project.leaderUserId ? `用户 #${project.leaderUserId}` : "暂未分配" }}</dd>
          </div>
          <div>
            <dt>开始日期</dt>
            <dd>{{ formatDate(project.startDate) }}</dd>
          </div>
          <div>
            <dt>结束日期</dt>
            <dd>{{ formatDate(project.endDate) }}</dd>
          </div>
        </dl>
      </section>

      <el-tabs v-model="activeTab" class="workspace-tabs">
        <el-tab-pane label="项目成员" name="members">
          <ProjectMembersPanel
            :project-id="project.id"
            :club-id="project.clubId"
            :leader-user-id="project.leaderUserId"
            :project-status="normalizedStatus"
          />
        </el-tab-pane>
      </el-tabs>
    </template>
  </div>
</template>

<style scoped>
.workspace-page {
  max-width: 1180px;
}

.workspace-nav {
  margin-bottom: 10px;
}

.result-back-link {
  margin-left: 12px;
}

.workspace-header {
  display: grid;
  grid-template-columns: minmax(0, 1.35fr) minmax(360px, 0.65fr);
  gap: 32px;
  padding: 28px;
  border: 1px solid var(--el-border-color-light);
  background: var(--el-bg-color);
}

.workspace-heading {
  min-width: 0;
}

.eyebrow {
  margin-bottom: 8px;
  color: var(--el-color-primary);
  font-size: 12px;
  font-weight: 700;
  letter-spacing: 0.1em;
}

.title-row {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

.title-row h1 {
  margin: 0;
  color: var(--el-text-color-primary);
  font-size: clamp(24px, 3vw, 34px);
  line-height: 1.2;
}

.workspace-heading p {
  max-width: 720px;
  margin: 14px 0 0;
  color: var(--el-text-color-regular);
  line-height: 1.7;
}

.project-context {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  align-content: start;
  margin: 0;
  border-top: 1px solid var(--el-border-color-lighter);
  border-left: 1px solid var(--el-border-color-lighter);
}

.project-context div {
  min-width: 0;
  padding: 14px 16px;
  border-right: 1px solid var(--el-border-color-lighter);
  border-bottom: 1px solid var(--el-border-color-lighter);
}

.project-context dt {
  margin-bottom: 5px;
  color: var(--el-text-color-secondary);
  font-size: 12px;
}

.project-context dd {
  margin: 0;
  overflow: hidden;
  color: var(--el-text-color-primary);
  font-size: 14px;
  font-weight: 600;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.workspace-tabs {
  margin-top: 18px;
  padding: 0 24px 24px;
  border: 1px solid var(--el-border-color-light);
  background: var(--el-bg-color);
}

@media (max-width: 900px) {
  .workspace-header {
    grid-template-columns: 1fr;
    gap: 24px;
    padding: 20px;
  }
}

@media (max-width: 560px) {
  .project-context {
    grid-template-columns: 1fr;
  }

  .workspace-tabs {
    padding: 0 14px 18px;
  }
}
</style>
