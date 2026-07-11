# PR #109 角色矩阵截图说明

本目录补充 PR #109 的角色覆盖截图。截图使用本地前端页面渲染，并用固定的测试角色数据模拟接口返回，目的是让审查者能按角色逐一确认入口拆分、按钮显隐、只读/维护边界和任期状态。

## 测试用户

| 用户名            | 学工号    | 角色                        |
| ----------------- | --------- | --------------------------- |
| `stu_public`      | `2450001` | `STUDENT`                   |
| `stu_member`      | `2450002` | `STUDENT`, `CLUB_MEMBER@1`  |
| `stu_officer`     | `2450003` | `STUDENT`, `CLUB_OFFICER@1` |
| `stu_leader`      | `2450004` | `STUDENT`, `CLUB_LEADER@1`  |
| `teacher_advisor` | `05001`   | `TEACHER`, `ADVISOR@1`      |
| `club_admin`      | `05002`   | `TEACHER`, `CLUB_ADMIN`     |
| `system_admin`    | `05003`   | `TEACHER`, `SYSTEM_ADMIN`   |

## 截图清单

| 文件                                           | 覆盖内容                                                    |
| ---------------------------------------------- | ----------------------------------------------------------- |
| `01-stu-public-my-club-empty.png`              | 普通学生在“我的社团”下没有社团身份时的空状态。              |
| `02-stu-public-registration-apply.png`         | 普通学生可进入“社团注册”，查看本人申请并提交注册申请。      |
| `03-stu-public-registration-dialog.png`        | 普通学生提交社团注册申请弹窗。                              |
| `04-stu-member-my-club-identity.png`           | 社团成员在“我的社团”查看个人社团身份。                      |
| `05-stu-member-member-terms-self-service.png`  | 社团成员在“成员管理”查看本人任期，只有自助查看/退出类能力。 |
| `06-stu-officer-member-group-scope.png`        | 社团干部查看本社团名册，并在授权范围内处理小组。            |
| `07-stu-leader-my-club-profile-manage.png`     | 社团负责人在“我的社团”维护本社团档案。                      |
| `08-stu-leader-member-current-governance.png`  | 社团负责人维护当前名册、任期、部门、小组、移出成员。        |
| `09-stu-leader-member-history-future.png`      | 任期历史支持按届筛选，并区分当前、未来、历史状态。          |
| `10-stu-leader-transition-workbench.png`       | 换届管理只处理到期待换届成员。                              |
| `11-stu-leader-member-term-dialog.png`         | 新增成员任期时从既定学年、部门、小组中选择。                |
| `12-teacher-advisor-member-governance.png`     | 指导教师拥有与负责人一致的本社团成员治理能力。              |
| `13-club-admin-registration-review-pool.png`   | 社团管理员查看注册审核池。                                  |
| `14-club-admin-registration-review-dialog.png` | 社团管理员处理社团注册审核。                                |
| `15-club-admin-global-club-readonly.png`       | 社团管理员在“我的社团”拥有全局查看视角。                    |
| `16-club-admin-global-member-readonly.png`     | 社团管理员在“成员管理”拥有全局只读视角，不进入内部维护流。  |
| `17-system-admin-global-club-governance.png`   | 系统管理员查看并治理全校社团档案。                          |
| `18-system-admin-global-member-governance.png` | 系统管理员查看并维护全校成员任期、部门、小组和移出操作。    |

## 审查重点

- “我的社团”只承载社团基本信息和我的社团身份。
- “成员管理”承载成员名册、任期历史、未来任期、换届暂存区、部门/小组维护。
- “社团注册”承载学生申请和平台审核，不再混在社团基础信息页。
- 负责人和指导教师具备本社团维护能力；社团管理员具备全局查看和注册审核能力；系统管理员具备全局治理能力。
- 顶部导航和右上角角色标签完整展示，不省略、不重叠。
