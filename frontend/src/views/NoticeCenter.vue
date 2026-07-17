<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { Bell, Plus, Refresh, Search } from "@element-plus/icons-vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { type AuthResponse, onSessionChange, readAuth } from "../authSession";
import type { Club, ClubMemberRecord, Notice as ApiNotice } from "../api/models";

type TargetType = "school" | "club" | "department" | "member";
type NoticeStatus = "draft" | "published" | "expired";

interface ApiError {
  message?: string;
  title?: string;
}

interface NoticeReadResponse {
  isRead: boolean;
  readAt?: string | null;
}

type Notice = Omit<
  ApiNotice,
  "publishAt" | "expireAt" | "readAt" | "targetType" | "noticeStatus"
> & {
  targetType: TargetType;
  publishAt: string;
  expireAt?: string | null;
  noticeStatus: NoticeStatus;
  readAt?: string | null;
};

interface DepartmentOption {
  memberId: number;
  name: string;
  count: number;
}

const auth = ref<AuthResponse | null>(readAuth());
const notices = ref<Notice[]>([]);
const clubs = ref<Club[]>([]);
const members = ref<ClubMemberRecord[]>([]);
const loading = ref(false);
const clubLoading = ref(false);
const memberLoading = ref(false);
const publishing = ref(false);
const markingId = ref<number | null>(null);
const publishDialogVisible = ref(false);
const detailDialogVisible = ref(false);
const selectedNotice = ref<Notice | null>(null);
const publishFormRef = ref<FormInstance>();
let stopSessionListener: (() => void) | null = null;
let noticeRequestId = 0;
let memberRequestId = 0;

const filters = reactive({
  noticeStatus: "published",
  targetType: "",
  clubId: undefined as number | undefined,
  unreadOnly: false,
});

const publishForm = reactive({
  noticeType: "announcement",
  title: "",
  content: "",
  targetType: "club" as TargetType,
  clubId: undefined as number | undefined,
  departmentMemberId: undefined as number | undefined,
  memberUserId: undefined as number | undefined,
  expireAt: "",
});

const publishRules: FormRules = {
  noticeType: [{ required: true, message: "请填写通知类型", trigger: "blur" }],
  title: [{ required: true, message: "请填写通知标题", trigger: "blur" }],
  content: [{ required: true, message: "请填写通知内容", trigger: "blur" }],
  targetType: [{ required: true, message: "请选择定向范围", trigger: "change" }],
};

const currentUserId = computed(() => auth.value?.user.id);
const permissions = computed(() => auth.value?.permissions ?? []);
const canPublishSchool = computed(
  () => permissions.value.includes("*") || permissions.value.includes("notice:publish:school"),
);
const canPublishClub = computed(
  () =>
    canPublishSchool.value ||
    permissions.value.includes("notice:publish") ||
    auth.value?.roles.some((role) =>
      ["club_officer", "club_leader", "club_president"].includes(role.code.toLowerCase()),
    ) === true,
);
const canPublish = computed(() => canPublishSchool.value || canPublishClub.value);
const managedClubIds = computed(() => {
  if (canPublishSchool.value) return clubs.value.map((club) => club.id);

  const ids = new Set<number>();
  auth.value?.roles.forEach((role) => {
    const roleCode = role.code.toLowerCase();
    const canRolePublish =
      role.permissions?.includes("notice:publish") ||
      ["club_officer", "club_leader", "club_president"].includes(roleCode);
    if (!canRolePublish) return;

    if (role.clubId) ids.add(role.clubId);
    role.clubIds?.forEach((clubId) => ids.add(clubId));
  });
  return [...ids];
});
const publishClubOptions = computed(() =>
  clubs.value.filter(
    (club) => managedClubIds.value.includes(club.id) && club.status !== "inactive",
  ),
);
const departmentOptions = computed<DepartmentOption[]>(() => {
  const groups = new Map<string, DepartmentOption>();
  members.value
    .filter((member) => member.isCurrent && member.departmentName)
    .forEach((member) => {
      const key = member.departmentName!.trim();
      const existing = groups.get(key);
      if (existing) {
        existing.count += 1;
        return;
      }

      groups.set(key, { memberId: member.memberId, name: key, count: 1 });
    });
  return [...groups.values()].sort((a, b) => a.name.localeCompare(b.name, "zh-Hans-CN"));
});
const memberOptions = computed(() =>
  members.value
    .filter((member) => member.isCurrent)
    .filter(
      (member, index, rows) => rows.findIndex((row) => row.userId === member.userId) === index,
    )
    .sort((a, b) => a.userName.localeCompare(b.userName, "zh-Hans-CN")),
);
const readSummary = computed(() => {
  const total = notices.value.length;
  const unread = notices.value.filter((notice) => !notice.isRead).length;
  return { total, unread, read: total - unread };
});

async function requestJson<T>(url: string, init?: RequestInit): Promise<T> {
  const res = await fetch(url, init);
  if (!res.ok) {
    let message = `请求失败（${res.status}）`;
    try {
      const body = (await res.json()) as ApiError;
      message = body.message || body.title || message;
    } catch {
      /* 保留默认错误信息 */
    }
    throw new Error(message);
  }

  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

async function loadClubs() {
  if (!currentUserId.value) return;
  clubLoading.value = true;
  try {
    clubs.value = await requestJson<Club[]>(`/api/clubs`);
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "社团列表加载失败");
  } finally {
    clubLoading.value = false;
  }
}

async function loadMembers() {
  const requestId = ++memberRequestId;
  const clubId = publishForm.clubId;
  const userId = currentUserId.value;
  if (!clubId || !userId) {
    members.value = [];
    return;
  }

  memberLoading.value = true;
  try {
    const query = new URLSearchParams({
      viewerUserId: String(userId),
      includeHistory: "false",
    });
    const data = await requestJson<ClubMemberRecord[]>(
      `/api/clubs/${clubId}/members?${query.toString()}`,
    );
    if (requestId === memberRequestId) members.value = data;
  } catch (e) {
    if (requestId === memberRequestId) {
      members.value = [];
      ElMessage.error(e instanceof Error ? e.message : "成员列表加载失败");
    }
  } finally {
    if (requestId === memberRequestId) memberLoading.value = false;
  }
}

async function loadNotices() {
  const requestId = ++noticeRequestId;
  const userId = currentUserId.value;
  if (!userId) {
    notices.value = [];
    return;
  }

  loading.value = true;
  try {
    const query = new URLSearchParams({ viewerUserId: String(userId) });
    if (filters.noticeStatus) query.set("noticeStatus", filters.noticeStatus);
    if (filters.targetType) query.set("targetType", filters.targetType);
    if (filters.clubId) query.set("clubId", String(filters.clubId));
    if (filters.unreadOnly) query.set("unreadOnly", "true");

    const data = await requestJson<Notice[]>(`/api/notices?${query.toString()}`);
    if (requestId === noticeRequestId) notices.value = data;
  } catch (e) {
    if (requestId === noticeRequestId) {
      notices.value = [];
      ElMessage.error(e instanceof Error ? e.message : "通知加载失败");
    }
  } finally {
    if (requestId === noticeRequestId) loading.value = false;
  }
}

function openPublishDialog() {
  resetPublishForm();
  publishDialogVisible.value = true;
}

function resetPublishForm() {
  publishForm.noticeType = "announcement";
  publishForm.title = "";
  publishForm.content = "";
  publishForm.targetType = canPublishClub.value ? "club" : "school";
  publishForm.clubId = publishClubOptions.value[0]?.id;
  publishForm.departmentMemberId = undefined;
  publishForm.memberUserId = undefined;
  publishForm.expireAt = "";
  members.value = [];
  if (publishForm.clubId) void loadMembers();
}

async function submitNotice() {
  if (!currentUserId.value || !publishFormRef.value) return;
  try {
    await publishFormRef.value.validate();
  } catch {
    return;
  }

  const target = buildTargetPayload();
  if (!target.ok) {
    ElMessage.warning(target.message);
    return;
  }

  publishing.value = true;
  try {
    await requestJson<Notice>("/api/notices", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        currentUserId: currentUserId.value,
        noticeType: publishForm.noticeType,
        title: publishForm.title,
        content: publishForm.content,
        targetType: publishForm.targetType,
        clubId: target.clubId,
        targetId: target.targetId,
        expireAt: publishForm.expireAt || null,
        noticeStatus: "published",
      }),
    });
    ElMessage.success("通知已发布");
    publishDialogVisible.value = false;
    await loadNotices();
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "通知发布失败");
  } finally {
    publishing.value = false;
  }
}

function buildTargetPayload():
  { ok: true; clubId?: number; targetId?: number } | { ok: false; message: string } {
  if (publishForm.targetType === "school") {
    return canPublishSchool.value
      ? { ok: true }
      : { ok: false, message: "当前账号不能发布全校通知" };
  }

  if (publishForm.targetType === "club") {
    return publishForm.clubId
      ? { ok: true, targetId: publishForm.clubId }
      : { ok: false, message: "请选择目标社团" };
  }

  if (publishForm.targetType === "department") {
    return publishForm.departmentMemberId
      ? { ok: true, targetId: publishForm.departmentMemberId }
      : { ok: false, message: "请选择目标部门" };
  }

  if (publishForm.targetType === "member") {
    if (!publishForm.clubId) return { ok: false, message: "请选择成员所属社团" };
    if (!publishForm.memberUserId) return { ok: false, message: "请选择目标成员" };
    return { ok: true, clubId: publishForm.clubId, targetId: publishForm.memberUserId };
  }

  return { ok: false, message: "请选择定向范围" };
}

async function openNoticeDetail(row: Notice) {
  selectedNotice.value = row;
  detailDialogVisible.value = true;
  if (!row.isRead) await markRead(row);
}

async function markRead(row: Notice) {
  const userId = currentUserId.value;
  if (!userId || row.isRead || markingId.value !== null) return;

  markingId.value = row.id;
  try {
    const result = await requestJson<NoticeReadResponse>(`/api/notices/${row.id}/reads`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ currentUserId: userId }),
    });
    row.isRead = result.isRead;
    row.readAt = result.readAt ?? new Date().toISOString();
    row.readCount += 1;

    await loadNotices();
    const refreshed = notices.value.find((notice) => notice.id === row.id);
    if (selectedNotice.value?.id === row.id && refreshed) selectedNotice.value = refreshed;
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "同步已读状态失败，请稍后重试");
  } finally {
    markingId.value = null;
  }
}

function targetTypeText(value: string) {
  return (
    {
      school: "全校",
      club: "社团",
      department: "部门",
      member: "成员",
    }[value] ?? value
  );
}

function noticeStatusText(value: string) {
  return (
    {
      draft: "草稿",
      published: "已发布",
      expired: "已过期",
    }[value] ?? value
  );
}

function noticeStatusType(value: string) {
  if (value === "published") return "success";
  if (value === "expired") return "info";
  return "warning";
}

function noticeTypeText(value: string) {
  return (
    {
      announcement: "公告",
      urgent: "紧急通知",
      event: "活动提醒",
      system: "系统消息",
    }[value] ?? value
  );
}

function formatDate(value: string | null | undefined) {
  if (!value) return "-";
  return new Date(value).toLocaleString();
}

function refreshSession() {
  auth.value = readAuth();
  void loadClubs();
  void loadNotices();
}

watch(
  () => publishForm.targetType,
  () => {
    publishForm.departmentMemberId = undefined;
    publishForm.memberUserId = undefined;
    if (publishForm.targetType === "school") publishForm.clubId = undefined;
    else if (!publishForm.clubId) publishForm.clubId = publishClubOptions.value[0]?.id;
  },
);

watch(
  () => publishForm.clubId,
  () => {
    publishForm.departmentMemberId = undefined;
    publishForm.memberUserId = undefined;
    void loadMembers();
  },
);

watch(filters, () => {
  void loadNotices();
});

onMounted(() => {
  stopSessionListener = onSessionChange(refreshSession);
  void loadClubs();
  void loadNotices();
});

onUnmounted(() => {
  stopSessionListener?.();
});
</script>

<template>
  <div class="notice-page">
    <section class="notice-head">
      <div>
        <h2>公告通知</h2>
        <p>发布定向通知，查看当前账号可见公告，并维护已读记录。</p>
      </div>
      <div class="head-actions">
        <el-button :icon="Refresh" :loading="loading" @click="loadNotices">刷新</el-button>
        <el-button v-if="canPublish" type="primary" :icon="Plus" @click="openPublishDialog">
          发布通知
        </el-button>
      </div>
    </section>

    <section class="summary-band">
      <div>
        <span>全部</span>
        <strong>{{ readSummary.total }}</strong>
      </div>
      <div>
        <span>未读</span>
        <strong>{{ readSummary.unread }}</strong>
      </div>
      <div>
        <span>已读</span>
        <strong>{{ readSummary.read }}</strong>
      </div>
    </section>

    <section class="filter-band">
      <el-select v-model="filters.noticeStatus" class="filter-item" placeholder="通知状态">
        <el-option label="已发布" value="published" />
        <el-option label="草稿" value="draft" />
        <el-option label="已过期" value="expired" />
      </el-select>
      <el-select v-model="filters.targetType" class="filter-item" clearable placeholder="定向范围">
        <el-option label="全校" value="school" />
        <el-option label="社团" value="club" />
        <el-option label="部门" value="department" />
        <el-option label="成员" value="member" />
      </el-select>
      <el-select
        v-model="filters.clubId"
        class="filter-item"
        :loading="clubLoading"
        clearable
        filterable
        placeholder="社团筛选"
      >
        <el-option v-for="club in clubs" :key="club.id" :label="club.name" :value="club.id" />
      </el-select>
      <el-switch v-model="filters.unreadOnly" active-text="仅未读" inactive-text="全部" />
      <el-button :icon="Search" @click="loadNotices">查询</el-button>
    </section>

    <el-table v-loading="loading" :data="notices" border stripe row-key="id" empty-text="暂无通知">
      <el-table-column label="状态" width="110">
        <template #default="{ row }">
          <el-tag :type="row.isRead ? 'info' : 'danger'" effect="plain">
            {{ row.isRead ? "已读" : "未读" }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column prop="title" label="标题" min-width="180" />
      <el-table-column label="范围" width="110">
        <template #default="{ row }">{{ targetTypeText(row.targetType) }}</template>
      </el-table-column>
      <el-table-column prop="targetName" label="目标" min-width="160" />
      <el-table-column prop="publisherName" label="发布人" width="120" />
      <el-table-column label="发布时间" width="180">
        <template #default="{ row }">{{ formatDate(row.publishAt) }}</template>
      </el-table-column>
      <el-table-column label="发布状态" width="110">
        <template #default="{ row }">
          <el-tag :type="noticeStatusType(row.noticeStatus)" effect="plain">
            {{ noticeStatusText(row.noticeStatus) }}
          </el-tag>
        </template>
      </el-table-column>
      <el-table-column label="已读" width="110">
        <template #default="{ row }">
          {{ row.readCount }} / {{ row.audienceCount ?? "-" }}
        </template>
      </el-table-column>
      <el-table-column label="内容" width="100" fixed="right">
        <template #default="{ row }">
          <el-button type="primary" link @click="openNoticeDetail(row)">查看详情</el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog
      v-model="detailDialogVisible"
      title="通知详情"
      width="min(720px, 92vw)"
      append-to-body
      destroy-on-close
    >
      <article v-if="selectedNotice" class="notice-dialog">
        <header class="notice-dialog__heading">
          <div>
            <el-tag type="primary" effect="plain">
              {{ noticeTypeText(selectedNotice.noticeType) }}
            </el-tag>
            <h3>{{ selectedNotice.title }}</h3>
          </div>
          <el-tag :type="selectedNotice.isRead ? 'info' : 'danger'" effect="plain">
            {{ selectedNotice.isRead ? "已读" : "未读" }}
          </el-tag>
        </header>

        <el-descriptions :column="2" border>
          <el-descriptions-item label="发布人">
            {{ selectedNotice.publisherName }}
          </el-descriptions-item>
          <el-descriptions-item label="发布时间">
            {{ formatDate(selectedNotice.publishAt) }}
          </el-descriptions-item>
          <el-descriptions-item label="发布范围">
            {{ targetTypeText(selectedNotice.targetType) }}
          </el-descriptions-item>
          <el-descriptions-item label="发布目标">
            {{ selectedNotice.targetName }}
          </el-descriptions-item>
          <el-descriptions-item label="发布状态">
            {{ noticeStatusText(selectedNotice.noticeStatus) }}
          </el-descriptions-item>
          <el-descriptions-item label="所属社团">
            {{ selectedNotice.clubName || "-" }}
          </el-descriptions-item>
          <el-descriptions-item label="过期时间">
            {{ formatDate(selectedNotice.expireAt) }}
          </el-descriptions-item>
          <el-descriptions-item label="已读时间">
            {{ formatDate(selectedNotice.readAt) }}
          </el-descriptions-item>
        </el-descriptions>

        <section class="notice-dialog__content">
          <span>通知正文</span>
          <p>{{ selectedNotice.content }}</p>
        </section>
      </article>

      <template #footer>
        <div v-if="selectedNotice" class="notice-dialog__footer">
          <span v-if="markingId === selectedNotice.id">正在同步已读状态…</span>
          <span v-else-if="selectedNotice.isRead">
            已于 {{ formatDate(selectedNotice.readAt) }} 阅读
          </span>
          <span v-else>尚未标记已读</span>
          <div>
            <el-button
              v-if="!selectedNotice.isRead"
              type="primary"
              plain
              :loading="markingId === selectedNotice.id"
              @click="markRead(selectedNotice)"
            >
              确认已读
            </el-button>
            <el-button @click="detailDialogVisible = false">关闭</el-button>
          </div>
        </div>
      </template>
    </el-dialog>

    <el-dialog v-model="publishDialogVisible" title="发布通知" width="680px">
      <el-form ref="publishFormRef" :model="publishForm" :rules="publishRules" label-width="100px">
        <el-form-item label="定向范围" prop="targetType">
          <el-radio-group v-model="publishForm.targetType">
            <el-radio-button v-if="canPublishSchool" value="school">全校</el-radio-button>
            <el-radio-button v-if="canPublishClub" value="club">社团</el-radio-button>
            <el-radio-button v-if="canPublishClub" value="department">部门</el-radio-button>
            <el-radio-button v-if="canPublishClub" value="member">成员</el-radio-button>
          </el-radio-group>
        </el-form-item>
        <el-form-item v-if="publishForm.targetType !== 'school'" label="社团">
          <el-select
            v-model="publishForm.clubId"
            :loading="clubLoading"
            filterable
            placeholder="选择社团"
          >
            <el-option
              v-for="club in publishClubOptions"
              :key="club.id"
              :label="club.name"
              :value="club.id"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="publishForm.targetType === 'department'" label="部门">
          <el-select
            v-model="publishForm.departmentMemberId"
            :loading="memberLoading"
            filterable
            placeholder="按成员任期中的部门定向"
          >
            <el-option
              v-for="department in departmentOptions"
              :key="department.memberId"
              :label="`${department.name}（${department.count} 人）`"
              :value="department.memberId"
            />
          </el-select>
        </el-form-item>
        <el-form-item v-if="publishForm.targetType === 'member'" label="成员">
          <el-select
            v-model="publishForm.memberUserId"
            :loading="memberLoading"
            filterable
            placeholder="选择目标成员"
          >
            <el-option
              v-for="member in memberOptions"
              :key="member.userId"
              :label="`${member.userName}${member.studentNo ? ` / ${member.studentNo}` : ''}`"
              :value="member.userId"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="通知类型" prop="noticeType">
          <el-select v-model="publishForm.noticeType">
            <el-option label="公告" value="announcement" />
            <el-option label="紧急通知" value="urgent" />
            <el-option label="活动提醒" value="event" />
            <el-option label="系统消息" value="system" />
          </el-select>
        </el-form-item>
        <el-form-item label="标题" prop="title">
          <el-input v-model="publishForm.title" maxlength="120" show-word-limit />
        </el-form-item>
        <el-form-item label="内容" prop="content">
          <el-input
            v-model="publishForm.content"
            type="textarea"
            :rows="5"
            maxlength="4000"
            show-word-limit
          />
        </el-form-item>
        <el-form-item label="过期时间">
          <el-date-picker
            v-model="publishForm.expireAt"
            type="datetime"
            value-format="YYYY-MM-DDTHH:mm:ss"
            placeholder="可选"
          />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="publishDialogVisible = false">取消</el-button>
        <el-button type="primary" :loading="publishing" :icon="Bell" @click="submitNotice">
          发布
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.notice-page {
  display: flex;
  flex-direction: column;
  gap: 18px;
  padding: 24px;
  color: #20262e;
}

.notice-head,
.summary-band,
.filter-band {
  border: 1px solid #d9e1ea;
  background: #fff;
}

.notice-head {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  padding: 18px 20px;
}

.notice-head h2 {
  margin: 0;
  font-weight: 650;
}

.notice-head p {
  margin: 6px 0 0;
  color: #66727f;
}

.head-actions,
.filter-band {
  display: flex;
  align-items: center;
  gap: 12px;
}

.summary-band {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
}

.summary-band div {
  padding: 14px 18px;
}

.summary-band div + div {
  border-left: 1px solid #d9e1ea;
}

.summary-band span {
  display: block;
  color: #66727f;
  font-size: 13px;
}

.summary-band strong {
  display: block;
  margin-top: 4px;
  font-size: 22px;
}

.filter-band {
  flex-wrap: wrap;
  padding: 14px 16px;
}

.filter-item {
  width: 180px;
}

.notice-dialog {
  display: grid;
  gap: 20px;
}

.notice-dialog__heading {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 20px;
}

.notice-dialog__heading h3 {
  margin: 10px 0 0;
  color: #20262e;
  font-size: 22px;
  line-height: 1.4;
  overflow-wrap: anywhere;
}

.notice-dialog__content {
  padding: 18px 20px;
  border: 1px solid #d9e1ea;
  border-radius: 8px;
  background: #f8fafc;
}

.notice-dialog__content span {
  color: #66727f;
  font-size: 13px;
}

.notice-dialog__content p {
  margin: 0;
  margin-top: 10px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  line-height: 1.7;
}

.notice-dialog__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
  width: 100%;
  color: #66727f;
  font-size: 13px;
}

:deep(.el-dialog__body .el-select),
:deep(.el-dialog__body .el-date-editor) {
  width: 100%;
}

@media (max-width: 900px) {
  .notice-page {
    padding: 14px;
  }

  .notice-head,
  .head-actions,
  .filter-band {
    align-items: stretch;
    flex-direction: column;
  }

  .summary-band {
    grid-template-columns: 1fr;
  }

  .summary-band div + div {
    border-left: none;
    border-top: 1px solid #d9e1ea;
  }

  .filter-item {
    width: 100%;
  }

  .notice-dialog__heading,
  .notice-dialog__footer {
    align-items: stretch;
    flex-direction: column;
  }
}
</style>
