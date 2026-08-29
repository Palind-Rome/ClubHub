-- Issue #156：新增 Oracle 幂等台账，为 Redis 幂等状态提供最终一致性兜底。
--
-- 执行前：
--   1. 备份目标 schema，并确认连接用户为 CLUBHUB schema 所有者。
--   2. 共享开发库和生产库必须经过人工确认并进入维护窗口；CI 不执行本脚本。
--   3. 部署包含本迁移的后端前，保持 Redis:Features:Idempotency 关闭。
--
-- 本脚本只新增 SEQ_IDEMPOTENCY_RECORDS、IDEMPOTENCY_RECORDS 及其约束，
-- 不修改已有业务表或业务数据。Oracle DDL 会隐式提交，不能依赖 ROLLBACK 回滚。
-- 脚本可重复执行；中断后修复原因并从头重跑。
--
-- 回滚方案（需先备份并进入维护窗口）：
--   1. 关闭 Redis:Features:Idempotency，确认没有进行中的幂等请求。
--   2. DROP TABLE IDEMPOTENCY_RECORDS PURGE;
--   3. DROP SEQUENCE SEQ_IDEMPOTENCY_RECORDS;
-- 影响范围仅为本迁移新增对象，不涉及既有业务表与数据。

WHENEVER SQLERROR EXIT SQL.SQLCODE ROLLBACK;

DECLARE
  sequence_count NUMBER;
BEGIN
  SELECT COUNT(*)
    INTO sequence_count
    FROM user_sequences
   WHERE sequence_name = 'SEQ_IDEMPOTENCY_RECORDS';

  IF sequence_count = 0 THEN
    EXECUTE IMMEDIATE
      'CREATE SEQUENCE SEQ_IDEMPOTENCY_RECORDS ' ||
      'START WITH 1000000 INCREMENT BY 1 NOCACHE NOCYCLE';
  END IF;
END;
/

DECLARE
  table_count NUMBER;
BEGIN
  SELECT COUNT(*)
    INTO table_count
    FROM user_tables
   WHERE table_name = 'IDEMPOTENCY_RECORDS';

  IF table_count = 0 THEN
    EXECUTE IMMEDIATE q'[
      CREATE TABLE IDEMPOTENCY_RECORDS (
        idempotency_id number DEFAULT SEQ_IDEMPOTENCY_RECORDS.NEXTVAL PRIMARY KEY,
        user_id number NOT NULL,
        operation_scope varchar2(100 char) NOT NULL,
        request_key_hash varchar2(64 char) NOT NULL,
        request_hash varchar2(64 char) NOT NULL,
        record_status varchar2(20 char) NOT NULL,
        http_status number(3),
        content_type varchar2(100 char),
        response_headers clob,
        response_body clob,
        expires_at timestamp NOT NULL,
        created_at timestamp DEFAULT SYSTIMESTAMP NOT NULL,
        updated_at timestamp DEFAULT SYSTIMESTAMP NOT NULL,
        CONSTRAINT CK_IDEMPOTENCY_STATUS CHECK (
          record_status IN ('processing', 'succeeded', 'failed')
        ),
        CONSTRAINT UQ_IDEMPOTENCY_USER_SCOPE_KEY UNIQUE (
          user_id, operation_scope, request_key_hash
        ),
        CONSTRAINT FK_IDEMPOTENCY_USER FOREIGN KEY (user_id)
          REFERENCES USERS (user_id) DEFERRABLE INITIALLY IMMEDIATE
      )
    ]';
  END IF;
END;
/

DECLARE
  index_count NUMBER;
BEGIN
  SELECT COUNT(*)
    INTO index_count
    FROM user_indexes
   WHERE index_name = 'IX_IDEMPOTENCY_EXPIRES_AT';

  IF index_count = 0 THEN
    EXECUTE IMMEDIATE
      'CREATE INDEX IX_IDEMPOTENCY_EXPIRES_AT ' ||
      'ON IDEMPOTENCY_RECORDS (expires_at)';
  END IF;
END;
/

SELECT table_name
FROM user_tables
WHERE table_name = 'IDEMPOTENCY_RECORDS';

SELECT sequence_name, last_number
FROM user_sequences
WHERE sequence_name = 'SEQ_IDEMPOTENCY_RECORDS';

SELECT constraint_name, constraint_type, status
FROM user_constraints
WHERE table_name = 'IDEMPOTENCY_RECORDS'
ORDER BY constraint_name;

SELECT index_name, uniqueness, status
FROM user_indexes
WHERE index_name = 'IX_IDEMPOTENCY_EXPIRES_AT';
