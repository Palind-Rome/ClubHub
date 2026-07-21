-- 真实感成员任期样例：依赖 000_sample_users.sql 与 001_sample_clubs.sql。
-- 覆盖计算机协会、摄影社、羽毛球协会的当前任期与历史任期，用于演示成员管理、换届和考核场景。
-- 所有新增样例账号密码均为 123456；重复执行会重置样例账号密码与任期数据。

MERGE INTO USERS target
USING (
  SELECT 20 AS user_id, 'zhao_rui' AS username,
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=' AS password_hash,
         '赵睿' AS real_name, '2450020' AS student_no,
         '计算机科学与技术学院' AS college, '计算机科学与技术' AS major,
         '2024' AS grade, 'active' AS account_status
  FROM dual
  UNION ALL
  SELECT 21, 'he_yuqing',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '何雨晴', '2350021',
         '电子与信息工程学院', '数据科学与大数据技术',
         '2023', 'active'
  FROM dual
  UNION ALL
  SELECT 22, 'zhou_zihan',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '周子涵', '2450022',
         '软件学院', '软件工程',
         '2024', 'active'
  FROM dual
  UNION ALL
  SELECT 23, 'lin_kexin',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '林可欣', '2250023',
         '设计创意学院', '视觉传达设计',
         '2022', 'active'
  FROM dual
  UNION ALL
  SELECT 24, 'chen_moyang',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '陈墨阳', '2350024',
         '艺术与传媒学院', '摄影',
         '2023', 'active'
  FROM dual
  UNION ALL
  SELECT 25, 'xu_mengyao',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '许梦瑶', '2450025',
         '设计创意学院', '数字媒体艺术',
         '2024', 'active'
  FROM dual
  UNION ALL
  SELECT 26, 'wang_yichen',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '王奕辰', '2450026',
         '建筑与城市规划学院', '建筑学',
         '2024', 'active'
  FROM dual
  UNION ALL
  SELECT 27, 'ma_siyuan',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '马思远', '2250027',
         '艺术与传媒学院', '广播电视学',
         '2022', 'active'
  FROM dual
  UNION ALL
  SELECT 28, 'shen_yiming',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '沈一鸣', '2350028',
         '体育教学部', '运动训练',
         '2023', 'active'
  FROM dual
  UNION ALL
  SELECT 29, 'ye_qingyang',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '叶清扬', '2450029',
         '经济与管理学院', '工商管理',
         '2024', 'active'
  FROM dual
  UNION ALL
  SELECT 30, 'wu_yutong',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '吴雨桐', '2350030',
         '人文学院', '新闻传播学',
         '2023', 'active'
  FROM dual
  UNION ALL
  SELECT 31, 'zheng_jiayi',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '郑佳怡', '2450031',
         '环境科学与工程学院', '环境工程',
         '2024', 'active'
  FROM dual
  UNION ALL
  SELECT 32, 'jiang_haoran',
         'PBKDF2$600000$vOt5+JaeNdv2ry0AHIV23w==$Ve30fLOAPdDTf8qVoASxJTttCq+gsT9bw5oybCN6e/8=',
         '蒋浩然', '2250032',
         '机械与能源工程学院', '机械设计制造及其自动化',
         '2022', 'active'
  FROM dual
) source
ON (target.user_id = source.user_id)
WHEN MATCHED THEN UPDATE SET
  target.username = source.username,
  target.password_hash = source.password_hash,
  target.real_name = source.real_name,
  target.student_no = source.student_no,
  target.college = source.college,
  target.major = source.major,
  target.grade = source.grade,
  target.account_status = source.account_status,
  target.updated_at = SYSDATE
WHEN NOT MATCHED THEN
  INSERT (user_id, username, password_hash, real_name, student_no, college, major, grade, account_status, created_at, updated_at)
  VALUES (source.user_id, source.username, source.password_hash, source.real_name, source.student_no, source.college, source.major, source.grade, source.account_status, SYSDATE, SYSDATE);

UPDATE CLUBS
SET president_user_id = 23,
    updated_at = SYSDATE
WHERE club_id = 2;

MERGE INTO USER_ROLES target
USING (
  SELECT 120 AS user_role_id, 20 AS user_id, 4 AS role_id, 1 AS club_id FROM dual
  UNION ALL SELECT 121, 21, 4, 1 FROM dual
  UNION ALL SELECT 122, 22, 3, 1 FROM dual
  UNION ALL SELECT 123, 23, 5, 2 FROM dual
  UNION ALL SELECT 124, 24, 4, 2 FROM dual
  UNION ALL SELECT 125, 25, 4, 2 FROM dual
  UNION ALL SELECT 126, 26, 3, 2 FROM dual
  UNION ALL SELECT 127, 28, 4, 3 FROM dual
  UNION ALL SELECT 128, 29, 4, 3 FROM dual
  UNION ALL SELECT 129, 30, 4, 3 FROM dual
  UNION ALL SELECT 130, 31, 3, 3 FROM dual
) source
ON (target.user_role_id = source.user_role_id)
WHEN MATCHED THEN UPDATE SET
  target.user_id = source.user_id,
  target.role_id = source.role_id,
  target.club_id = source.club_id
WHEN NOT MATCHED THEN
  INSERT (user_role_id, user_id, role_id, club_id, assigned_at)
  VALUES (source.user_role_id, source.user_id, source.role_id, source.club_id, SYSDATE);

MERGE INTO CLUB_MEMBERS target
USING (
  SELECT 20 AS member_id, 1 AS club_id, 3 AS user_id, '主席团' AS department_name,
         '负责人组' AS group_name, '副会长' AS position_name,
         '2025-2026学年' AS term_name, DATE '2025-09-01' AS term_start,
         DATE '2026-06-30' AS term_end, 'ended' AS member_status,
         DATE '2024-10-12' AS join_at, 89 AS contribution_score
  FROM dual
  UNION ALL
  SELECT 21, 1, 6, '活动部',
         '赛事组', '干事',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-09-18', 78
  FROM dual
  UNION ALL
  SELECT 22, 1, 20, '技术部',
         '后端组', '干事',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-09-20', 82
  FROM dual
  UNION ALL
  SELECT 23, 1, 21, '宣传部',
         '新媒体组', '社员',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-10-08', 74
  FROM dual
  UNION ALL
  SELECT 24, 1, 20, '技术部',
         '后端组', '部长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2025-09-20', 91
  FROM dual
  UNION ALL
  SELECT 25, 1, 21, '宣传部',
         '新媒体组', '部长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2025-10-08', 86
  FROM dual
  UNION ALL
  SELECT 26, 1, 22, '算法部',
         '训练组', '社员',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2026-03-12', 68
  FROM dual
  UNION ALL
  SELECT 27, 2, 27, '主席团',
         '负责人组', '社长',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2024-09-22', 93
  FROM dual
  UNION ALL
  SELECT 28, 2, 23, '主席团',
         '负责人组', '副社长',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2024-10-10', 88
  FROM dual
  UNION ALL
  SELECT 29, 2, 24, '外拍部',
         '校园采风组', '干事',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-09-25', 76
  FROM dual
  UNION ALL
  SELECT 30, 2, 25, '后期部',
         '修图组', '社员',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-10-16', 70
  FROM dual
  UNION ALL
  SELECT 31, 2, 23, '主席团',
         '负责人组', '社长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2024-10-10', 94
  FROM dual
  UNION ALL
  SELECT 32, 2, 24, '外拍部',
         '校园采风组', '部长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2025-09-25', 87
  FROM dual
  UNION ALL
  SELECT 33, 2, 25, '后期部',
         '修图组', '部长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2025-10-16', 84
  FROM dual
  UNION ALL
  SELECT 34, 2, 26, '影像记录部',
         '活动跟拍组', '社员',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2026-03-18', 64
  FROM dual
  UNION ALL
  SELECT 35, 3, 32, '主席团',
         '负责人组', '会长',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2024-09-12', 92
  FROM dual
  UNION ALL
  SELECT 36, 3, 7, '竞训部',
         '校队训练组', '队长',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-09-19', 90
  FROM dual
  UNION ALL
  SELECT 37, 3, 28, '竞训部',
         '校队训练组', '干事',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-09-28', 77
  FROM dual
  UNION ALL
  SELECT 38, 3, 29, '赛事部',
         '裁判组', '社员',
         '2025-2026学年', DATE '2025-09-01',
         DATE '2026-06-30', 'ended',
         DATE '2025-10-09', 71
  FROM dual
  UNION ALL
  SELECT 39, 3, 28, '竞训部',
         '校队训练组', '部长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2025-09-28', 86
  FROM dual
  UNION ALL
  SELECT 40, 3, 29, '赛事部',
         '裁判组', '组长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2025-10-09', 82
  FROM dual
  UNION ALL
  SELECT 41, 3, 30, '宣传部',
         '赛讯组', '部长',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2025-10-20', 79
  FROM dual
  UNION ALL
  SELECT 42, 3, 31, '竞训部',
         '兴趣训练组', '社员',
         '2026-2027学年', DATE '2026-07-01',
         DATE '2027-06-30', 'active',
         DATE '2026-03-15', 63
  FROM dual
) source
ON (target.member_id = source.member_id)
WHEN MATCHED THEN UPDATE SET
  target.club_id = source.club_id,
  target.user_id = source.user_id,
  target.department_name = source.department_name,
  target.group_name = source.group_name,
  target.position_name = source.position_name,
  target.term_name = source.term_name,
  target.term_start = source.term_start,
  target.term_end = source.term_end,
  target.member_status = source.member_status,
  target.join_at = source.join_at,
  target.contribution_score = source.contribution_score
WHEN NOT MATCHED THEN
  INSERT (member_id, club_id, user_id, department_name, group_name, position_name, term_name, term_start, term_end, member_status, join_at, contribution_score)
  VALUES (source.member_id, source.club_id, source.user_id, source.department_name, source.group_name, source.position_name, source.term_name, source.term_start, source.term_end, source.member_status, source.join_at, source.contribution_score);

COMMIT;
