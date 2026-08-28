<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { Refresh } from "@element-plus/icons-vue";
import { apiClient } from "../apiClient";
import { onSessionChange, readAuth } from "../authSession";
import BusinessFlow from "../components/ui/BusinessFlow.vue";

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

const flows = [
  {
    title: "活动业务闭环",
    description: "从活动发起到参与数据沉淀，可沿同一条记录逐步推进。",
    steps: ["创建活动", "报名审核", "签到签退", "活动归档"],
  },
  {
    title: "项目协作闭环",
    description: "把立项、人员、任务和成果审核串成完整的协作过程。",
    steps: ["提交立项", "立项审核", "任务推进", "成果验收"],
  },
  {
    title: "评奖评优闭环",
    description: "考核结果进入奖项申请，再经过多级审核与公示归档。",
    steps: ["成员考核", "奖项申请", "分级审核", "公示归档"],
  },
] as const;

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
      <div>
        <h2>运营工作台</h2>
        <p>用实时业务数据概览系统状态，并快速进入需要处理的核心流程。</p>
      </div>
      <el-button :icon="Refresh" :loading="loading" @click="loadDashboard">刷新真实数据</el-button>
    </header>

    <section class="identity-card">
      <div>
        <span class="identity-kicker">CURRENT IDENTITY</span>
        <h3>{{ auth?.user.realName || "当前用户" }}</h3>
        <p>{{ roleSummary }}</p>
      </div>
      <div class="permission-summary">
        <span>权限范围</span>
        <strong>{{
          (auth?.permissions ?? []).includes("*")
            ? "系统全部权限"
            : `${auth?.permissions.length ?? 0} 项业务权限`
        }}</strong>
        <small>页面按钮会继续按当前身份和社团范围显示</small>
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
        <span class="card-kicker">OPERATION CHECKLIST</span>
        <h2>运营提示</h2>
        <ul>
          <li v-for="item in attentionItems" :key="item">{{ item }}</li>
        </ul>
      </article>
      <article class="briefing-card">
        <span class="card-kicker">QUICK ROUTES</span>
        <h2>核心业务入口</h2>
        <div class="quick-routes">
          <router-link to="/activities">活动报名与签到</router-link>
          <router-link to="/projects">项目立项与任务</router-link>
          <router-link to="/awards">考核与评奖评优</router-link>
          <router-link to="/forum">现场发布讨论</router-link>
        </div>
      </article>
    </section>

    <div class="section-heading">
      <div>
        <h2>核心业务闭环</h2>
        <p>从业务目标出发，沿状态变化推进流程，并在关键节点保留可追溯记录。</p>
      </div>
    </div>
    <section class="flow-grid">
      <BusinessFlow v-for="flow in flows" :key="flow.title" v-bind="flow" />
    </section>
  </section>
</template>

<style scoped>
.dashboard-page {
  display: grid;
  gap: var(--club-space-6);
}
.dashboard-head p,
.section-heading p {
  margin: 6px 0 0;
  color: var(--club-text-secondary);
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
.permission-summary {
  display: grid;
  min-width: 210px;
  gap: 4px;
  padding: 16px 18px;
  border: 1px solid var(--club-border);
  border-radius: var(--club-radius-md);
  background: var(--club-surface);
}
.permission-summary span,
.permission-summary small {
  color: var(--club-text-muted);
}
.permission-summary strong {
  font-size: 17px;
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
.section-heading h2 {
  margin: 0;
  font-size: 20px;
}
.flow-grid {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: var(--club-space-4);
}
@media (max-width: 1050px) {
  .metric-grid {
    grid-template-columns: repeat(2, 1fr);
  }
  .flow-grid {
    grid-template-columns: 1fr;
  }
}
@media (max-width: 640px) {
  .identity-card {
    grid-template-columns: 1fr;
  }
  .permission-summary {
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
