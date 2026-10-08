-- ==========================================================================================
--   GameFrameX PostgreSQL 发现层 TTL 清理可选脚本（C166 T8 AC-4）。
--   Optional pg_cron-based TTL cleanup for the GameFrameX PostgreSQL discovery layer (C166 T8, AC-4).
--
--   部署方式 / Deployment:
--   1. 目标库安装 pg_cron 扩展（需超级用户）：CREATE EXTENSION pg_cron;
--      Install the pg_cron extension on the target database (superuser required).
--   2. 按 pg_cron 版本配置 cron.database_name 为控制库（如 gameframex_control）。
--      Point cron.database_name at the control database (e.g. gameframex_control).
--   3. 在控制库执行本脚本注册两条清理任务（已注册则 no-op，幂等）。
--      Run this script in the control database to register both schedules (idempotent).
--
--   语义 / Semantics: 与进程内 PostgreSqlTtlCleanupJob 等价——server_heartbeat 超 15 秒、
--   player_route 超 30 天未更新的行被删除；Evicted 时序在清理周期内放宽（清理 job 每 5s 一轮）。
--   Equivalent to the in-process PostgreSqlTtlCleanupJob: heartbeat rows older than 15 s and
--   player-route rows older than 30 days are deleted; Evicted timing is relaxed to within one
--   cleanup period (the job runs every 5 s).
--   使用本脚本时无需在进程内启动 PostgreSqlTtlCleanupJob（二选一）。
--   When using this script the in-process job is unnecessary (choose either, not both).
-- ==========================================================================================

-- 心跳过期行清理（15s TTL，替代 Mongo TTL 索引）/ Heartbeat expiry (15 s TTL, replacing the Mongo TTL index)
SELECT cron.schedule(
               'gameframex_server_heartbeat_ttl_cleanup',
               '5 seconds',
               $cron$DELETE FROM server_heartbeat WHERE last_heartbeat < now() - interval '15 seconds'$cron$
       ) WHERE NOT EXISTS (
    SELECT 1 FROM cron.job WHERE jobname = 'gameframex_server_heartbeat_ttl_cleanup'
);

-- 玩家路由过期行清理（30 天 TTL）/ Player-route expiry (30-day TTL)
SELECT cron.schedule(
               'gameframex_player_route_ttl_cleanup',
               '1 hour',
               $cron$DELETE FROM player_route WHERE last_seen_at < now() - interval '30 days'$cron$
       ) WHERE NOT EXISTS (
    SELECT 1 FROM cron.job WHERE jobname = 'gameframex_player_route_ttl_cleanup'
);
