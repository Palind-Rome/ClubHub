-- 社团注册申请样例数据：依赖 000_sample_users.sql
MERGE INTO CLUBS target
USING (
  SELECT
    101 AS club_id,
    '人工智能学习社' AS club_name,
    '学术科技' AS category,
    '面向 AI 学习、竞赛实践和项目协作的学生社团。' AS description,
    1 AS applicant_user_id,
    '希望组织同学共同学习机器学习基础、参加创新竞赛并沉淀课程资料。' AS apply_reason,
    'https://example.com/clubhub/ai-club-application.pdf' AS material_url,
    'pending' AS audit_status,
    'pending' AS club_status
  FROM dual
) source
ON (target.club_id = source.club_id)
WHEN NOT MATCHED THEN
  INSERT (
    club_id,
    club_name,
    category,
    description,
    applicant_user_id,
    apply_reason,
    material_url,
    audit_status,
    club_status,
    created_at,
    updated_at
  )
  VALUES (
    source.club_id,
    source.club_name,
    source.category,
    source.description,
    source.applicant_user_id,
    source.apply_reason,
    source.material_url,
    source.audit_status,
    source.club_status,
    SYSDATE,
    SYSDATE
  );

COMMIT;
