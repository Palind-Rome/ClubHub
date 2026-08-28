<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { Bell, DataBoard, Grid, Refresh } from "@element-plus/icons-vue";
import { apiClient } from "../apiClient";
import { onSessionChange, readAuth } from "../authSession";

type Metric = { label: string; value: number | null; note: string; path: string };

const auth = ref(readAuth());
const loading = ref(false);
const activities = ref<number | null>(null);
const projects = ref<number | null>(null);
const notices = ref<number | null>(null);
const recruitments = ref<number | null>(null);

const roleSummary = computed(() => {
  const roles = auth.value?.roles ?? [];
  return roles.length
    ? roles.map((role) => role.displayName || role.name).join(" · ")
    : "暂无业务角色";
});

const attentionItems = computed(() => {
  const items: string[] = [];
  if (notices.value === null) items.push("通知待办暂时无法读取，请检查后端连接。");
  else if (notices.value > 0) items.push(`还有 ${notices.value} 条未读通知需要查看。`);
  else items.push("当前账号的通知已全部阅读。");

  const hasClubScope = (auth.value?.roles ?? []).some(
    (role) => role.scope === "club" && (Boolean(role.clubId) || role.clubIds.length > 0),
  );
  if (!hasClubScope) {
    items.push("当前身份没有有效社团范围角色；新增、审核等操作需要切换负责人账号。");
  } else {
    items.push("当前身份具备社团业务范围，可按页面按钮继续处理写操作。");
  }
  return items;
});

const metrics = computed<Metric[]>(() => [
  { label: "可见活动", value: activities.value, note: "当前账号可见活动总数", path: "/activities" },
  { label: "协作项目", value: projects.value, note: "当前可见项目总数", path: "/projects" },
  { label: "未读通知", value: notices.value, note: "当前账号未读范围", path: "/notices" },
  { label: "纳新计划", value: recruitments.value, note: "当前可见纳新总数", path: "/recruitments" },
]);

async function loadDashboard() {
  loading.value = true;
  const userId = auth.value?.user?.id;
  const [activityResult, projectResult, noticeResult, recruitmentResult] = await Promise.allSettled(
    [
      apiClient.getActivities({ currentUserId: userId }),
      apiClient.getProjects(),
      apiClient.getNotices({ unreadOnly: true }),
      apiClient.getRecruitments(),
    ],
  );

  activities.value = activityResult.status === "fulfilled" ? activityResult.value.length : null;
  projects.value = projectResult.status === "fulfilled" ? projectResult.value.length : null;
  notices.value = noticeResult.status === "fulfilled" ? noticeResult.value.length : null;
  recruitments.value =
    recruitmentResult.status === "fulfilled" ? recruitmentResult.value.length : null;
  loading.value = false;
}

let stopSessionListener: (() => void) | undefined;

onMounted(() => {
  stopSessionListener = onSessionChange(() => {
    auth.value = readAuth();
    void loadDashboard();
  });
  void loadDashboard();
});

onUnmounted(() => stopSessionListener?.());
</script>

<template>
  <section class="dashboard-page">
    <header class="app-page-header dashboard-head">
      <div class="dashboard-title">
        <el-icon class="dashboard-title-icon"><DataBoard /></el-icon>
        <h2>运营工作台</h2>
      </div>
      <el-button :icon="Refresh" :loading="loading" @click="loadDashboard">刷新数据</el-button>
    </header>

    <section class="identity-card">
      <div class="identity-primary">
        <span class="identity-kicker">CURRENT IDENTITY</span>
        <h3>{{ auth?.user.realName || "当前用户" }}</h3>
        <p>{{ roleSummary }}</p>
      </div>
      <div class="identity-details" aria-label="账号基本信息">
        <div>
          <span>学号</span><strong>{{ auth?.user.studentNo || "未填写" }}</strong>
        </div>
        <div>
          <span>身份</span><strong>{{ roleSummary }}</strong>
        </div>
        <div>
          <span>学院</span><strong>{{ auth?.user.college || "未填写" }}</strong>
        </div>
        <div>
          <span>专业</span><strong>{{ auth?.user.major || "未填写" }}</strong>
        </div>
      </div>
    </section>

    <section class="metric-grid" aria-label="真实业务统计">
      <router-link
        v-for="metric in metrics"
        :key="metric.label"
        :to="metric.path"
        class="metric-card"
      >
        <span>{{ metric.label }}</span>
        <strong>{{ metric.value ?? "—" }}</strong>
        <small>{{ metric.value === null ? "暂时无法读取接口" : metric.note }}</small>
      </router-link>
    </section>

    <section class="briefing-grid">
      <article class="briefing-card">
        <span class="card-kicker">REMINDERS</span>
        <h2>
          <el-icon><Bell /></el-icon>提醒
        </h2>
        <ul>
          <li v-for="item in attentionItems" :key="item">{{ item }}</li>
        </ul>
      </article>
      <article class="briefing-card">
        <span class="card-kicker">COMMON TOOLS</span>
        <h2>
          <el-icon><Grid /></el-icon>常用工具入口
        </h2>
        <div class="quick-routes">
          <router-link to="/activities">活动报名与签到</router-link>
          <router-link to="/projects">项目立项与任务</router-link>
          <router-link to="/awards">考核与评奖评优</router-link>
          <router-link to="/forum">现场发布讨论</router-link>
        </div>
      </article>
    </section>
  </section>
</template>

<style scoped>
.dashboard-page {
  display: grid;
  gap: var(--club-space-6);
}
.dashboard-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--club-space-4);
}
.dashboard-title {
  display: flex;
  align-items: center;
  gap: 12px;
}
.dashboard-title-icon {
  width: 42px;
  height: 42px;
  border-radius: 13px;
  color: var(--club-primary-strong);
  background: linear-gradient(135deg, var(--club-primary-soft), var(--club-accent-soft));
  font-size: 23px;
}
.identity-card {
  display: grid;
  grid-template-columns: 1fr auto;
  gap: var(--club-space-6);
  align-items: center;
  padding: clamp(24px, 3vw, 38px);
  overflow: hidden;
  border: 1px solid color-mix(in srgb, var(--club-primary) 18%, var(--club-border));
  border-radius: var(--club-radius-xl);
  background: linear-gradient(
    125deg,
    var(--club-primary-soft),
    var(--club-bg-elevated) 48%,
    var(--club-accent-soft)
  );
  box-shadow: var(--club-shadow-sm);
}
.identity-card h3 {
  margin: 7px 0;
  font-size: clamp(24px, 3vw, 38px);
}
.identity-card p {
  margin: 0;
  color: var(--club-text-secondary);
}
.identity-kicker {
  color: var(--club-primary-strong);
  font-size: 11px;
  font-weight: 800;
  letter-spacing: 0.13em;
}
.identity-details {
  display: grid;
  grid-template-columns: repeat(2, minmax(150px, 1fr));
  min-width: min(520px, 52%);
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  background: var(--club-surface);
}
.identity-details > div {
  display: grid;
  gap: 4px;
  padding: 14px 16px;
}
.identity-details > div:nth-child(odd) {
  border-right: 1px solid var(--club-border);
}
.identity-details > div:nth-child(-n + 2) {
  border-bottom: 1px solid var(--club-border);
}
.identity-details span {
  color: var(--club-text-muted);
  font-size: 12px;
}
.identity-details strong {
  font-size: 14px;
  line-height: 1.4;
}
.metric-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: var(--club-space-4);
}
.metric-card {
  display: grid;
  gap: 6px;
  padding: 20px;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-lg);
  color: inherit;
  background: var(--club-bg-elevated);
  box-shadow: var(--club-shadow-sm);
  text-decoration: none;
  transition:
    transform 160ms ease,
    border-color 160ms ease;
}
.metric-card:hover {
  transform: translateY(-2px);
  border-color: color-mix(in srgb, var(--club-primary) 38%, var(--club-border));
}
.metric-card > span,
.metric-card small {
  color: var(--club-text-secondary);
}
.metric-card strong {
  color: var(--club-primary-strong);
  font-size: 30px;
}
.briefing-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--club-space-4);
}
.briefing-card {
  padding: var(--club-space-6);
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-lg);
  background: var(--club-bg-elevated);
  box-shadow: var(--club-shadow-sm);
}
.briefing-card h2 {
  display: flex;
  align-items: center;
  gap: 7px;
  margin: 5px 0 14px;
  font-size: 18px;
}
.card-kicker {
  color: var(--club-accent);
  font-size: 10px;
  font-weight: 800;
  letter-spacing: 0.14em;
}
.briefing-card ul {
  display: grid;
  gap: 9px;
  margin: 0;
  padding-left: 20px;
  color: var(--club-text-secondary);
  line-height: 1.55;
}
.quick-routes {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}
.quick-routes a {
  padding: 12px 14px;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  color: var(--club-primary-strong);
  background: linear-gradient(120deg, var(--club-primary-soft), var(--club-accent-soft));
  font-weight: 700;
  text-align: center;
  text-decoration: none;
}
@media (max-width: 1050px) {
  .metric-grid {
    grid-template-columns: repeat(2, 1fr);
  }
  .identity-details {
    min-width: min(480px, 58%);
  }
}
@media (max-width: 640px) {
  .identity-card {
    grid-template-columns: 1fr;
  }
  .identity-details {
    min-width: 0;
  }
  .metric-grid {
    grid-template-columns: 1fr;
  }
  .briefing-grid {
    grid-template-columns: 1fr;
  }
  .quick-routes {
    grid-template-columns: 1fr;
  }
}
</style>
