# 批处理与异步 Job 基础设施

## 2026-04-14

### 批处理统一模型
- 新增 `BatchTargetQuery` 作为统一筛选输入，优先覆盖场景对象治理类批处理。
- 批处理统一走 `preview -> confirmToken -> execute -> report` 四段式，不允许新能力直接无预览批改。
- preview 结果持久化到 `Library/UnitySkills/batch_state.json`，支持 token 过期清理。

### 异步执行模型
- 批处理执行统一走 `BatchJobService`，支持 `queued/running/reconnecting/completed/failed/cancelled`。
- Job 按 chunk 执行，执行中产生日志、进度、报告项。
- Domain Reload 后把运行中任务标成 `reconnecting` 并自动恢复运行时上下文。

### 报告与回滚
- 每个 batch job 完成后都生成结构化 report，包含 totals、items、failureGroups。
- 批处理执行期间自动绑定 `WorkflowManager` session，用 sessionId 作为 workflowId 返回。
- 当前回滚粒度是任务/会话级，不支持单 item 选择性回滚。

### 首批接入范围
- 通用：`batch_query_gameobjects`、`batch_query_components`、`batch_preview_*`、`batch_execute`、`batch_report_*`
- Job：`job_status`、`job_logs`、`job_list`、`job_wait`、`job_cancel`
- 治理：`batch_fix_missing_scripts`、`batch_standardize_naming`、`batch_set_render_layer`、`batch_replace_material`、`batch_validate_scene_objects`、`batch_cleanup_temp_objects`
