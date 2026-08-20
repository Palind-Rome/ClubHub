<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { RouterLink } from "vue-router";
import {
  ArrowRight,
  Bell,
  Calendar,
  Files,
  Key,
  Refresh,
  UserFilled,
} from "@element-plus/icons-vue";
import { GetNoticesNoticeStatusEnum, type Activity, type Notice, type Project } from "../api";
import { apiClient } from "../apiClient";
import { resolveIdentityLabel } from "../authExperience";
import { onSessionChange, readAuth, type AuthResponse } from "../authSession";
import {
  AppEmptyState,
  AppErrorState,
  AppLoadingState,
  AppPageHeader,
  AppPanel,
} from "../components/ui";
import {
  buildDashboardQuickLinks,
  selectAccessibleProjects,
  selectRecentActivities,
  selectUnreadNotices,
} from "../dashboard";

type SectionStatus = "loading" | "ready" | "error";

const auth = ref<AuthResponse | null>(readAuth());
const notices = ref<Notice[]>([]);
const activities = ref<Activity[]>([]);
const projects = ref<Project[]>([]);
const noticeStatus = ref<SectionStatus>("loading");
const activityStatus = ref<SectionStatus>("loading");
const projectStatus = ref<SectionStatus>("loading");
const noticeError = ref("");
const activityError = ref("");
const projectError = ref("");
let stopSessionListener: (() => void) | null = null;
let noticeRequestId = 0;
let activityRequestId = 0;
let projectRequestId = 0;

const user = computed(() => auth.value?.user);
const roleLabels = computed(() =>
  (auth.value?.roles ?? []).map((role) => role.displayName || role.name),
);
const permissions = computed(() => auth.value?.permissions ?? []);
const quickLinks = computed(() => buildDashboardQuickLinks(permissions.value));
const identityLabel = computed(() => resolveIdentityLabel(user.value?.studentNo) || "校园用户");
const greeting = computed(() => {
  const hour = new Date().getHours();
  if (hour < 6) return "夜深了";
  if (hour < 12) return "早上好";
  if (hour < 18) return "下午好";
  return "晚上好";
});

async function loadNotices() {
  const requestId = ++noticeRequestId;
  noticeStatus.value = "loading";
  noticeError.value = "";
  try {
    const result = await apiClient.getNotices({
      noticeStatus: GetNoticesNoticeStatusEnum.Published,
      unreadOnly: true,
    });
    if (requestId !== noticeRequestId) return;
    notices.value = selectUnreadNotices(result);
    noticeStatus.value = "ready";
  } catch (error) {
    if (requestId !== noticeRequestId) return;
    notices.value = [];
    noticeError.value = errorMessage(error, "未读通知加载失败");
    noticeStatus.value = "error";
  }
}

async function loadActivities() {
  const requestId = ++activityRequestId;
  activityStatus.value = "loading";
  activityError.value = "";
  try {
    const result = await apiClient.getActivities({ currentUserId: user.value?.id });
    if (requestId !== activityRequestId) return;
    activities.value = selectRecentActivities(result);
    activityStatus.value = "ready";
  } catch (error) {
    if (requestId !== activityRequestId) return;
    activities.value = [];
    activityError.value = errorMessage(error, "近期活动加载失败");
    activityStatus.value = "error";
  }
}

async function loadProjects() {
  const requestId = ++projectRequestId;
  projectStatus.value = "loading";
  projectError.value = "";
  try {
    const result = await apiClient.getProjects({ page: 1, pageSize: 20 });
    if (requestId !== projectRequestId) return;
    projects.value = selectAccessibleProjects(result);
    projectStatus.value = "ready";
  } catch (error) {
    if (requestId !== projectRequestId) return;
    projects.value = [];
    projectError.value = errorMessage(error, "可访问项目加载失败");
    projectStatus.value = "error";
  }
}

function loadDashboard() {
  void loadNotices();
  void loadActivities();
  void loadProjects();
}

function refreshSession() {
  auth.value = readAuth();
}

function errorMessage(error: unknown, fallback: string) {
  return error instanceof Error && error.message ? error.message : fallback;
}

function formatDate(value?: Date | null, includeTime = false) {
  if (!value) return "时间待定";
  return new Intl.DateTimeFormat("zh-CN", {
    month: "short",
    day: "numeric",
    ...(includeTime ? { hour: "2-digit", minute: "2-digit" } : {}),
  }).format(value);
}

function projectStatusLabel(status: Project["projectStatus"]) {
  return (
    {
      pending: "待审核",
      running: "进行中",
      finished: "已完成",
      delayed: "已延期",
      closed: "已关闭",
    }[status] ?? status
  );
}

onMounted(() => {
  loadDashboard();
  stopSessionListener = onSessionChange(refreshSession);
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <div class="dashboard-page">
    <AppPageHeader
      class="app-page-header"
      eyebrow="Personal workspace"
      :title="`${greeting}，${user?.realName || user?.username || '同学'}`"
      description="从这里快速掌握与你相关的通知、活动和项目进展。"
    >
      <template #meta>
        <el-tag effect="plain">{{ identityLabel }}</el-tag>
        <el-tag v-for="role in roleLabels.slice(0, 3)" :key="role" type="success" effect="plain">
          {{ role }}
        </el-tag>
      </template>
      <template #actions>
        <el-button :icon="Refresh" @click="loadDashboard">刷新工作台</el-button>
      </template>
    </AppPageHeader>

    <section class="identity-overview" aria-label="身份与权限摘要">
      <div class="identity-avatar" aria-hidden="true">{{ user?.realName?.slice(0, 1) || "C" }}</div>
      <div class="identity-copy">
        <span class="identity-kicker">当前身份</span>
        <strong>{{ user?.realName || user?.username || "ClubHub 用户" }}</strong>
        <span
          >{{ user?.studentNo || user?.username }} · {{ user?.college || "学院信息未填写" }}</span
        >
      </div>
      <div class="identity-metrics">
        <div>
          <el-icon><UserFilled /></el-icon>
          <strong>{{ roleLabels.length }}</strong>
          <span>当前角色</span>
        </div>
        <div>
          <el-icon><Key /></el-icon>
          <strong>{{ permissions.length }}</strong>
          <span>可用权限</span>
        </div>
      </div>
    </section>

    <AppPanel title="快捷入口" description="入口会根据当前账号权限自动调整。">
      <div class="quick-link-grid">
        <RouterLink v-for="item in quickLinks" :key="item.path" :to="item.path" class="quick-link">
          <span>{{ item.label }}</span>
          <el-icon><ArrowRight /></el-icon>
        </RouterLink>
      </div>
    </AppPanel>

    <div class="dashboard-grid">
      <AppPanel data-section="notices" padding="none">
        <template #header>
          <div class="section-title">
            <span class="section-icon notice"
              ><el-icon><Bell /></el-icon
            ></span>
            <div>
              <h3>未读通知</h3>
              <p>最多展示最近 5 条</p>
            </div>
          </div>
        </template>
        <template #actions><RouterLink to="/notices">查看全部</RouterLink></template>
        <div class="section-content">
          <AppLoadingState v-if="noticeStatus === 'loading'" title="正在加载未读通知" :rows="3" />
          <AppErrorState
            v-else-if="noticeStatus === 'error'"
            title="通知加载失败"
            :description="noticeError"
            @retry="loadNotices"
          />
          <AppEmptyState
            v-else-if="notices.length === 0"
            title="没有未读通知"
            description="你已经处理完当前可见的全部通知。"
          />
          <ul v-else class="dashboard-list">
            <li v-for="notice in notices" :key="notice.id">
              <RouterLink to="/notices" class="list-link">
                <div class="list-copy">
                  <strong>{{ notice.title }}</strong
                  ><span>{{ notice.clubName || notice.targetName || "全校通知" }}</span>
                </div>
                <time>{{ formatDate(notice.publishAt, true) }}</time>
              </RouterLink>
            </li>
          </ul>
        </div>
      </AppPanel>

      <AppPanel data-section="activities" padding="none">
        <template #header>
          <div class="section-title">
            <span class="section-icon activity"
              ><el-icon><Calendar /></el-icon
            ></span>
            <div>
              <h3>近期活动</h3>
              <p>正在进行或即将开始</p>
            </div>
          </div>
        </template>
        <template #actions><RouterLink to="/activities">活动中心</RouterLink></template>
        <div class="section-content">
          <AppLoadingState v-if="activityStatus === 'loading'" title="正在加载近期活动" :rows="3" />
          <AppErrorState
            v-else-if="activityStatus === 'error'"
            title="活动加载失败"
            :description="activityError"
            @retry="loadActivities"
          />
          <AppEmptyState
            v-else-if="activities.length === 0"
            title="暂无近期活动"
            description="新的活动发布后会显示在这里。"
          />
          <ul v-else class="dashboard-list">
            <li v-for="activity in activities" :key="activity.id">
              <RouterLink to="/activities" class="list-link">
                <div class="list-copy">
                  <strong>{{ activity.title }}</strong
                  ><span>{{ activity.clubName }} · {{ activity.location || "地点待定" }}</span>
                </div>
                <time>{{
                  activity.status === "ongoing" ? "进行中" : formatDate(activity.startTime, true)
                }}</time>
              </RouterLink>
            </li>
          </ul>
        </div>
      </AppPanel>

      <AppPanel data-section="projects" padding="none">
        <template #header>
          <div class="section-title">
            <span class="section-icon project"
              ><el-icon><Files /></el-icon
            ></span>
            <div>
              <h3>可访问项目</h3>
              <p>你可以进入的项目工作区</p>
            </div>
          </div>
        </template>
        <template #actions><RouterLink to="/projects">项目列表</RouterLink></template>
        <div class="section-content">
          <AppLoadingState
            v-if="projectStatus === 'loading'"
            title="正在加载可访问项目"
            :rows="3"
          />
          <AppErrorState
            v-else-if="projectStatus === 'error'"
            title="项目加载失败"
            :description="projectError"
            @retry="loadProjects"
          />
          <AppEmptyState
            v-else-if="projects.length === 0"
            title="暂无可访问项目"
            description="加入项目或成为负责人后，项目会显示在这里。"
          />
          <ul v-else class="dashboard-list">
            <li v-for="project in projects" :key="project.id">
              <RouterLink :to="`/projects/${project.id}/workspace`" class="list-link">
                <div class="list-copy">
                  <strong>{{ project.projectName }}</strong
                  ><span>{{ project.description || `社团项目 #${project.clubId}` }}</span>
                </div>
                <el-tag size="small" effect="plain">{{
                  projectStatusLabel(project.projectStatus)
                }}</el-tag>
              </RouterLink>
            </li>
          </ul>
        </div>
      </AppPanel>
    </div>
  </div>
</template>

<style scoped>
.dashboard-page {
  display: grid;
  gap: var(--club-space-6);
}
.dashboard-page :deep(.page-header) {
  margin-bottom: 0;
}
.identity-overview {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--club-space-5);
  padding: clamp(20px, 3vw, 32px);
  overflow: hidden;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-xl);
  background: linear-gradient(
    120deg,
    var(--club-primary-soft),
    var(--club-surface) 52%,
    var(--club-accent-soft)
  );
  box-shadow: var(--club-shadow-sm);
}
.identity-avatar {
  display: grid;
  width: 68px;
  height: 68px;
  place-items: center;
  border-radius: 24px;
  color: #fff;
  background: linear-gradient(135deg, var(--club-primary), var(--club-accent));
  box-shadow: 0 14px 28px color-mix(in srgb, var(--club-primary) 24%, transparent);
  font-size: 28px;
  font-weight: 800;
}
.identity-copy {
  display: flex;
  min-width: 0;
  flex-direction: column;
}
.identity-kicker {
  margin-bottom: 4px;
  color: var(--club-primary-strong);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.12em;
  text-transform: uppercase;
}
.identity-copy strong {
  overflow: hidden;
  color: var(--club-text);
  font-size: 22px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.identity-copy > span:last-child {
  margin-top: 4px;
  color: var(--club-text-secondary);
  font-size: 13px;
}
.identity-metrics {
  display: grid;
  grid-template-columns: repeat(2, minmax(96px, 1fr));
  gap: var(--club-space-3);
}
.identity-metrics > div {
  display: grid;
  grid-template-columns: auto 1fr;
  align-items: center;
  gap: 2px 8px;
  padding: 14px 16px;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  background: color-mix(in srgb, var(--club-surface-solid) 72%, transparent);
}
.identity-metrics .el-icon {
  grid-row: 1 / 3;
  color: var(--club-primary);
  font-size: 21px;
}
.identity-metrics strong {
  color: var(--club-text);
  font-size: 20px;
  line-height: 1;
}
.identity-metrics span {
  color: var(--club-text-muted);
  font-size: 11px;
}
.quick-link-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--club-space-3);
}
.quick-link {
  display: flex;
  min-height: 54px;
  align-items: center;
  justify-content: space-between;
  gap: var(--club-space-3);
  padding: 0 var(--club-space-4);
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  color: var(--club-text);
  text-decoration: none;
  background: color-mix(in srgb, var(--club-surface-solid) 56%, transparent);
  font-weight: 700;
  transition:
    transform 160ms ease,
    border-color 160ms ease,
    color 160ms ease;
}
.quick-link:hover {
  border-color: color-mix(in srgb, var(--club-primary) 52%, var(--club-border));
  color: var(--club-primary-strong);
  transform: translateY(-2px);
}
.dashboard-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--club-space-5);
}
.dashboard-grid :deep(.panel) {
  min-width: 0;
}
.section-title {
  display: flex;
  align-items: center;
  gap: var(--club-space-3);
}
.section-title h3,
.section-title p {
  margin: 0;
}
.section-title h3 {
  color: var(--club-text);
  font-size: 17px;
}
.section-title p {
  margin-top: 2px;
  color: var(--club-text-muted);
  font-size: 12px;
}
.section-icon {
  display: grid;
  width: 40px;
  height: 40px;
  place-items: center;
  border-radius: 14px;
  font-size: 19px;
}
.section-icon.notice {
  color: var(--club-accent);
  background: var(--club-accent-soft);
}
.section-icon.activity {
  color: var(--club-success);
  background: color-mix(in srgb, var(--club-success) 14%, transparent);
}
.section-icon.project {
  color: var(--club-primary);
  background: var(--club-primary-soft);
}
.section-content {
  min-height: 330px;
  padding: var(--club-space-4);
}
.section-content :deep(.loading-state),
.section-content :deep(.error-state),
.section-content :deep(.state-card) {
  min-height: 296px;
  border: 0;
  background: transparent;
}
.dashboard-list {
  margin: 0;
  padding: 0;
  list-style: none;
}
.dashboard-list li + li {
  border-top: 1px solid var(--club-border);
}
.list-link {
  display: flex;
  min-height: 62px;
  align-items: center;
  justify-content: space-between;
  gap: var(--club-space-3);
  padding: var(--club-space-3);
  border-radius: var(--club-radius-sm);
  color: inherit;
  text-decoration: none;
}
.list-link:hover {
  background: var(--club-surface-hover);
}
.list-copy {
  min-width: 0;
}
.list-copy strong,
.list-copy span {
  display: block;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.list-copy strong {
  color: var(--club-text);
  font-size: 14px;
}
.list-copy span {
  margin-top: 4px;
  color: var(--club-text-muted);
  font-size: 12px;
}
.list-link time {
  flex: 0 0 auto;
  color: var(--club-text-secondary);
  font-size: 11px;
}
.panel-actions a {
  color: var(--club-primary);
  font-size: 13px;
  font-weight: 700;
  text-decoration: none;
}
@media (max-width: 1180px) {
  .dashboard-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }
  .dashboard-grid > :last-child {
    grid-column: 1 / -1;
  }
}
@media (max-width: 760px) {
  .identity-overview {
    grid-template-columns: auto minmax(0, 1fr);
  }
  .identity-metrics {
    grid-column: 1 / -1;
  }
  .quick-link-grid,
  .dashboard-grid {
    grid-template-columns: 1fr;
  }
  .dashboard-grid > :last-child {
    grid-column: auto;
  }
}
@media (max-width: 480px) {
  .identity-overview {
    grid-template-columns: 1fr;
  }
  .identity-avatar {
    width: 58px;
    height: 58px;
    border-radius: 20px;
  }
  .identity-metrics {
    grid-column: auto;
    grid-template-columns: 1fr 1fr;
  }
  .list-link {
    align-items: flex-start;
    flex-direction: column;
  }
}
</style>
