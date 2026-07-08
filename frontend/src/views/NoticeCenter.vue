<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import { Bell, Check, Plus, Refresh, Search } from "@element-plus/icons-vue";
import { ElMessage, type FormInstance, type FormRules } from "element-plus";
import { type AuthResponse, onSessionChange, readAuth } from "../authSession";

type TargetType = "school" | "club" | "department" | "member";
type NoticeStatus = "draft" | "published" | "expired";

interface ApiError {
  message?: string;
  title?: string;
}

interface Notice {
  id: number;
  clubId: number | null;
  clubName: string | null;
  publisherUserId: number;
  publisherName: string | null;
  noticeType: string;
  title: string;
  content: string;
  targetType: TargetType;
  targetId: number | null;
  targetName: string | null;
  publishAt: string;
  expireAt: string | null;
  noticeStatus: NoticeStatus;
  isRead: boolean;
  readAt: string | null;
  audienceCount: number | null;
  readCount: number;
}

interface Club {
  id: number;
  name: string;
  status: string | null;
  statusText: string;
}

interface ClubMemberRecord {
  memberId: number;
  clubId: number;
  clubName: string;
  userId: number;
  userName: string;
  studentNo: string | null;
  departmentName: string | null;
  groupName: string | null;
  positionName: string | null;
  memberStatus: string | null;
  isCurrent: boolean;
}

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
    clubs.value = await requestJson<Club[]>(`/api/clubs?viewerUserId=${currentUserId.value}`);
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

async function markRead(row: Notice) {
  const userId = currentUserId.value;
  if (!userId || row.isRead) return;

  markingId.value = row.id;
  try {
    await requestJson(`/api/notices/${row.id}/reads`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ currentUserId: userId }),
    });
    row.isRead = true;
    row.readAt = new Date().toISOString();
    row.readCount += 1;
    ElMessage.success("已标记为已读");
  } catch (e) {
    ElMessage.error(e instanceof Error ? e.message : "标记已读失败");
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

function formatDate(value: string | null) {
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
      <el-table-column type="expand">
        <template #default="{ row }">
          <div class="notice-detail">
            <p>{{ row.content }}</p>
            <el-descriptions :column="2" border>
              <el-descriptions-item label="通知类型">{{ row.noticeType }}</el-descriptions-item>
              <el-descriptions-item label="所属社团">
                {{ row.clubName || "-" }}
              </el-descriptions-item>
              <el-descriptions-item label="过期时间">
                {{ formatDate(row.expireAt) }}
              </el-descriptions-item>
              <el-descriptions-item label="已读时间">
                {{ formatDate(row.readAt) }}
              </el-descriptions-item>
            </el-descriptions>
          </div>
        </template>
      </el-table-column>
      <el-table-column label="操作" width="130" fixed="right">
        <template #default="{ row }">
          <el-button
            type="primary"
            plain
            :icon="Check"
            :disabled="row.isRead"
            :loading="markingId === row.id"
            @click="markRead(row)"
          >
            标记已读
          </el-button>
        </template>
      </el-table-column>
    </el-table>

    <el-dialog v-model="publishDialogVisible" title="发布通知" width="680px">
      <el-form ref="publishFormRef" :model="publishForm" :rules="publishRules" label-width="100px">
        <el-form-item label="定向范围" prop="targetType">
          <el-radio-group v-model="publishForm.targetType">
            <el-radio-button v-if="canPublishSchool" label="school">全校</el-radio-button>
            <el-radio-button v-if="canPublishClub" label="club">社团</el-radio-button>
            <el-radio-button v-if="canPublishClub" label="department">部门</el-radio-button>
            <el-radio-button v-if="canPublishClub" label="member">成员</el-radio-button>
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

.notice-detail {
  display: grid;
  gap: 14px;
  padding: 12px 18px;
}

.notice-detail p {
  margin: 0;
  white-space: pre-wrap;
  line-height: 1.7;
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
}
</style>
