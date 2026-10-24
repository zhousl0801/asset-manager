# 资产管家

个人资产观察与日常记账应用，统一记录现金、基金、公积金和平台积存金。

当前已实现账本初始化、收支记账（新建、修改、撤销）、分类维护（新增、改名、排序、启停）、账本明细 / 图表 / 分类排行、现金页与余额核对、公积金缴存与提取、基金分组与组合视角（同基金 A/C 归集、多平台展开、持仓 / 收益 / 交易 / 定投视图、三项指标、搜索排序）、投资收益走势与收益日历（按净值披露日归属、区间盈亏与持有收益分列）、基金持仓 / 买入待确认 / 买入确认 / 赎回申请 / 赎回确认 / 赎回到账、积存金持仓 / 买入 / 卖出待到账 / 到账、工资 / 公积金 / 基金定投自动计划与后台执行、首页已记录资产与同口径本月变化、资产分布与资产页覆盖确认、底部主导航与「我的」页、数据导出与完整备份恢复、行情定时刷新 / 失败重试 / 批量刷新与运行状态、资产流转联动、本月汇总和分类 / 关键字筛选，以及独立资产起始日期、历史补录重放、更正 / 撤销预览、行情溯源、现金差额关联和基金净值 / 黄金参考报价获取。截图识别和云端部署尚未实现（当前仅本人使用，认证暂缓）；原型全部使用虚构数据，刷新后重置。

分类按账本保存，初始化时建立默认列表；可新增、改名、排序和停用。改名会同步更新历史记录中的分类名称，停用后新记账不再出现该分类，历史记录继续展示并参与统计。投资分组按基金名称归集，同基金的 A/C 类和各平台持仓跟随同一分组；分组只改变查看范围，不改变持仓、成本和收益，删除分组后基金回到「未分组」。基金买入先计入待确认，确认后才转入份额与成本；赎回先冻结份额，确认后转为待到账，到账才进入现金；积存金卖出可直接到账，也可先计入待到账，收款后再进入现金。

## 文档入口

完整分类见[文档索引](docs/README.md)。开始开发前依次阅读：

1. [产品设计 V1.0](docs/product/资产管家-产品设计文档-V1.0.md)：业务范围、数据口径与验收场景。
2. [应用形态与技术选型 V1.0](docs/architecture/资产管家-应用形态与技术选型-V1.0.md)：前后端框架、云端账本、PWA 和 AI 接入边界。
3. [UI 设计索引](docs/design/README.md)：配色、整合线框、首页高保真演示及截图。

开发记录先看[功能迭代总览](docs/development/README.md)；分轮详情：[现金闭环与资产流转](docs/development/2026-10-21-开发记录-现金闭环与资产流转.md)、[G0 正确性收口](docs/development/2026-10-21-开发记录-G0正确性收口.md)、[G1 业务事实与历史重算](docs/development/2026-10-22-开发记录-G1业务事实与历史重算.md)、[G2 基金买入待确认](docs/development/2026-10-22-开发记录-G2基金买入待确认.md)、[G2 基金赎回闭环](docs/development/2026-10-22-开发记录-G2基金赎回闭环.md)、[G2 积存金卖出待到账](docs/development/2026-10-22-开发记录-G2积存金卖出待到账.md)、[真实行情接入](docs/development/2026-10-22-开发记录-真实行情接入.md)、[自动记账计划与后台任务](docs/development/2026-10-22-开发记录-自动记账计划与后台任务.md)、[完整趋势图](docs/development/2026-10-22-开发记录-完整趋势图.md)、[首页原型对齐与本月变化](docs/development/2026-10-22-开发记录-首页原型对齐与本月变化.md)、[分类维护与账本图表](docs/development/2026-10-22-开发记录-分类维护与账本图表.md)、[投资分组与组合视角](docs/development/2026-10-22-开发记录-投资分组与组合视角.md)、[投资收益走势与收益日历](docs/development/2026-10-22-开发记录-投资收益走势与收益日历.md)、[数据导出与完整备份恢复](docs/development/2026-10-22-开发记录-数据导出与完整备份恢复.md)、[行情定时刷新与失败重试](docs/development/2026-10-23-开发记录-行情定时刷新与失败重试.md)、[资产总览口径与首页重构](docs/development/2026-10-23-开发记录-资产总览口径与首页重构.md)。

本轮已完成四个重点页面的 [UI 收口与原型对照验收](docs/development/2026-10-23-开发记录-UI收口与原型对照验收.md)，并完成 [资产总览口径与首页重构](docs/development/2026-10-23-开发记录-资产总览口径与首页重构.md)。相关改动已提交：`5d300e4`（UI 收口与原型对照验收）和 `f8fb4c8`（资产总览口径与首页重构）；功能分支 `feat/g2-inflight-transactions` 已合入 `main` 并推送，仓库已加入最小 CI（后端构建 / 测试，前端类型检查 / 测试 / 构建）。尚未部署或执行云端写入。后续范围：截图识别、真实 iPhone / PWA 验收、云端部署；当前仅本人使用，认证暂缓。多实例部署前需补充分布式任务锁和运行监控。

## 技术基线

| 层次 | 选择 |
| --- | --- |
| 应用形态 | 手机端 Web App / PWA，本阶段以 iPhone 体验为主；电脑浏览器沿用手机画幅 |
| 前端 | Vue 3 + TypeScript + Vite；Vue Router + Pinia |
| UI / 图表 | Vant 4 + 产品自定义样式；Apache ECharts |
| 后端 | ASP.NET Core 10 Web API，分层单体 |
| 数据访问 | EF Core 10 + Npgsql |
| 正式存储 | 云端 PostgreSQL |
| AI | 授权只读接口与结构化导出；后续可扩展 DeepSeek 自然语言记账 |

依赖版本由 `web/package-lock.json` 和各项目文件锁定：.NET 10、EF Core 10.0.4、Npgsql Provider 10.0.3；Vue 3.5、Vite 8、TypeScript 6、Pinia 4、Vant 4、ECharts 6。

## 工程结构

```text
api/
  AssetKeeper.slnx
  dotnet-tools.json             本地 dotnet-ef 工具清单
  src/
    AssetKeeper.Api/             ASP.NET Core Web API
    AssetKeeper.Application/     应用服务
    AssetKeeper.Domain/          领域规则
    AssetKeeper.Infrastructure/  数据访问、EF 迁移与外部集成
  tests/AssetKeeper.Tests/       单元测试与 API 集成测试
web/                           Vue 3 + TypeScript + Vite 前端
docs/
  product/                     产品设计与业务验收依据
  architecture/                应用形态与技术选型
  design/                      UI 配色、CSS 变量、线框与设计截图
  research/                    前期竞品研究
prototype/                     静态交互原型
```

## 当前 API

| 方法 | 路径 | 说明 |
| --- | --- | --- |
| GET | `/api/ledger/requests/{requestId}` | 查询已完成请求的首次结果，支持网络中断后的恢复 |
| GET | `/api/health` | 健康检查 |
| GET | `/api/ledger` | 读取账本总览；未初始化返回 404 |
| GET | `/api/ledger/month-change` | 读取本月变化及生活结余、公积金缴存、投资盈亏、核对差额构成 |
| POST | `/api/ledger/initialize` | 建立账本；重复建立返回 409，校验失败返回 400 |
| GET | `/api/ledger/entries` | 按周期读取收支；支持 `period`、`anchor`、`kind`、`category`、`search` |
| GET | `/api/ledger/entries/stats` | 按周期读取收支图表、日均和分类排行 |
| GET | `/api/ledger/categories` | 读取收入 / 支出分类 |
| POST | `/api/ledger/categories` | 新增分类 |
| PUT | `/api/ledger/categories/{id}` | 分类改名或启停；改名同步历史记录 |
| PUT | `/api/ledger/categories/order` | 重排同一方向的分类 |
| GET | `/api/ledger/entries/{id}` | 读取单条收支 |
| POST | `/api/ledger/entries` | 新建收支；使用 `requestId` 防重复提交 |
| PUT | `/api/ledger/entries/{id}` | 修改收支，按差额联动现金 |
| POST | `/api/ledger/entries/{id}/reverse` | 撤销收支，恢复现金并保留记录 |
| GET | `/api/ledger/cash` | 读取现金页：余额、本月流入 / 流出 / 调整、净变动与变动列表 |
| POST | `/api/ledger/cash/reconcile` | 核对现金余额；差额生成独立调整记录 |
| GET | `/api/ledger/provident` | 读取公积金余额、本月缴存 / 提取与变动 |
| POST | `/api/ledger/provident/opening` | 独立日期的公积金起始余额，不计缴存 / 生活收入 |
| GET | `/api/ledger/history?asOf=yyyy-MM-dd` | 查询历史事实、指定日资产、行情与更正审计 |
| POST | `/api/ledger/history/preview` | 只读预览更正 / 撤销及后续影响 |
| POST | `/api/ledger/history/change` | 携带原请求与预览签名确认变更 |
| POST | `/api/ledger/provident/deposits` | 补记公积金缴存，不改变现金 |
| POST | `/api/ledger/provident/withdrawals` | 提取公积金到现金，等额联动 |
| GET | `/api/ledger/gold` | 读取积存金持仓、报价、盈亏与记录 |
| POST | `/api/ledger/gold/buys` | 积存金买入，扣减现金并增加克数 / 成本 |
| POST | `/api/ledger/gold/sells` | 积存金卖出，按 `settlement` 直接到账或计入待到账 |
| POST | `/api/ledger/gold/sells/{id}/arrival` | 积存金卖到账，等额转入现金 |
| POST | `/api/ledger/gold/quote` | 更新积存金估值报价 |
| POST | `/api/ledger/gold/holding` | 录入或核对已有积存金持仓 |
| GET | `/api/ledger/funds` | 读取基金持仓、成本、收益、分组与交易记录 |
| GET | `/api/ledger/funds/returns` | 按范围读取投资收益走势、区间盈亏、持有收益和日历数据；支持分组过滤 |
| GET | `/api/ledger/funds/returns/day` | 读取指定日期的各持仓收益和合计 |
| GET | `/api/ledger/fund-groups` | 读取投资分组及基金 / 持仓计数 |
| POST | `/api/ledger/fund-groups` | 新建投资分组 |
| PUT | `/api/ledger/fund-groups/{id}` | 分组改名 |
| DELETE | `/api/ledger/fund-groups/{id}` | 删除分组，组内基金回到未分组 |
| PUT | `/api/ledger/fund-groups/positions/{positionId}` | 调整基金分组，同基金各类别一起移动 |
| POST | `/api/ledger/funds/positions` | 录入已有基金持仓，不扣现金 |
| POST | `/api/ledger/funds/buys` | 记录基金买入扣款，现金减少并计入买入待确认 |
| POST | `/api/ledger/funds/buys/{id}/confirm` | 按确认份额、净值和费用结转待确认，不再次扣款 |
| POST | `/api/ledger/funds/sells` | 申请赎回，冻结份额；不改变现金和持仓成本 |
| POST | `/api/ledger/funds/sells/{id}/confirm` | 确认赎回，减少份额 / 成本并转入待到账 |
| POST | `/api/ledger/funds/sells/{id}/arrival` | 赎回到账，等额转入现金 |
| POST | `/api/ledger/funds/quote` | 手动更新基金净值 |
| POST | `/api/ledger/funds/{id}/quote/refresh` | 从天天基金获取并保存最近已披露净值 |
| POST | `/api/ledger/gold/quote/refresh` | 获取并保存上期所黄金连续参考报价 |
| GET | `/api/ledger/plans` | 读取自动计划、待处理期次和执行记录 |
| POST | `/api/ledger/plans` | 新建或编辑自动计划 |
| POST | `/api/ledger/plans/{id}/pause` | 暂停计划；暂停期间不追补 |
| POST | `/api/ledger/plans/{id}/resume` | 恢复计划，从下一期继续 |
| POST | `/api/ledger/plans/occurrences/{id}/retry` | 重新排队待处理期次 |
| POST | `/api/ledger/plans/occurrences/{id}/skip` | 跳过本期 |
| POST | `/api/ledger/plans/preview` | 按日期和试算现金只读试算 |
| POST | `/api/ledger/plans/run` | 手动触发一次到期执行 |
| GET | `/api/ledger/trend?range=week\|month\|quarter\|year\|all` | 按日读取资产趋势、各类是否已记录和覆盖说明 |
| PUT | `/api/ledger/coverage` | 保存“不持有”标记和“其他资产均已覆盖”确认；返回最新账本概览 |
| GET | `/api/ledger/export/details.csv` | 导出可核对的收支、资产交易、现金核对和计划期次 CSV |
| GET | `/api/ledger/backup` | 下载完整备份 JSON |
| POST | `/api/ledger/backup/validate` | 校验备份格式、关联、重复风险和摘要，返回确认令牌 |
| POST | `/api/ledger/backup/restore` | 携带校验令牌恢复或替换账本 |
| GET | `/api/ledger/quotes/status` | 读取定时刷新配置、最近一次运行和每个持仓的行情状态 |
| POST | `/api/ledger/quotes/refresh` | 批量刷新基金净值 / 积存金参考报价，支持重试 |

初始化请求：

```json
{
  "startDate": "2026-10-01",
  "cashAsOfDate": "2026-10-01",
  "cashBalance": 23400.00
}
```

新建支出请求：

```json
{
  "kind": "expense",
  "category": "餐饮",
  "amount": 35.60,
  "businessDate": "2026-10-21",
  "note": "午餐",
  "requestId": "6f1c2f0e-7a8b-4c1d-9e2f-3a4b5c6d7e8f"
}
```

`kind` 取值为 `income` 或 `expense`；金额必须大于 0、最多两位小数，业务日期不得早于账本起始日期或晚于今天。`requestId` 由客户端生成，同一标识重复提交只保留一笔。

核对现金余额请求（`expectedVersion` 来自 `GET /api/ledger/cash` 的 `version`，勿固定使用示例值）：

```json
{
  "actualBalance": 35300.00,
  "reason": "漏记一笔消费",
  "requestId": "a649b299-9ee6-4277-9570-41f897207e97",
  "expectedVersion": 2
}
```

实际余额与账面余额一致时返回 `isBalanced: true`，不生成 0 元调整；存在差额时生成独立余额调整记录，只更新现金和总资产，不改变生活收入或支出。调整后的余额继续作为后续记账、消费校验的基准。

金额以服务器端精确十进制计算，前端只负责展示和提交。现金流入 / 流出汇总日常收支与资产流转，余额调整单列；页面分别展示“现金流入 − 现金流出 + 余额调整 = 余额净变动”。总资产为现金、基金、公积金与积存金的合计；未录入的资产返回“未录入”状态而不是 0。

资产流转规则：公积金缴存只增加公积金；提取等额转入现金。基金买入先按实际扣款计入待确认，确认后转为份额与成本，两次操作都不重复改变现金；赎回申请只冻结份额，确认后减少份额与成本并转为待到账，到账再等额转入现金；积存金买入扣减现金并增加持仓，卖出增加现金并结转成本；录入已有持仓和更新净值 / 报价不改变现金。所有资产变更请求都携带 `requestId`，重复提交返回同一记录，不重复扣款、加仓或到账。所有现金账户与资产持仓使用 `version` 并发标记，冲突时返回 409。

基金总览同时返回按基金名称归集的 `funds`：每只基金下按份额类别分组，类别下再列各平台持仓；A/C 类份额与净值分别展示，不合并份额、不平均净值。`groups` 返回各分组的基金数、持仓数和已确认估值。调整分组按基金名称同步移动该基金的全部类别和平台持仓，属于元数据变更，不生成资产流水；删除分组后归属清空，基金和资产不变。

投资收益走势按净值观测的实际有效日期归属：某日只有披露新净值的持仓才计算当日收益，未披露日期返回 `null` 并在页面显示“—”，不记为 0；累计收益在未披露日沿用上一个有效值并标明覆盖范围。每日收益按相邻净值日之间的持仓资产变化计算，并扣除区间内买入待确认、赎回到账等跨现金与投资的净转入，以及持仓核对差额。区间盈亏、持有收益和已披露累计收益分别命名：区间盈亏用于区间起点至今天的完整资产变化，持有收益是当前已确认估值减成本；两者不能互相替代。区间起始日依据不足时，接口返回 `intervalFrom` 和说明，或返回 `null` 而不是推测值。

明细 CSV 合并收支、现金核对、资产交易和计划期次，保留日期、方向、金额、现金影响、数量 / 净值 / 费用、分类或标的、状态和请求 / 关联标识，已撤销记录也会带状态导出。完整备份 JSON 覆盖账本与起始余额、现金、公积金、积存金、分类、收支、基金分组与持仓、资产交易、行情观测、自动计划、执行期次和请求回执。恢复前先校验格式、字段、关联、重复请求 / 期次 / 顺序号，并用历史重放核对现金与持仓投影；校验返回确认令牌，当前账本变化后令牌失效。恢复采用替换而非追加，在同一事务内删除并重建当前账本，失败时回滚；计划期次和请求回执按备份保留，已执行期次不会再次生成。备份账本标识与当前账本不同时拒绝覆盖。

行情刷新由 `QuoteRefreshScheduler` 配置控制：默认启用、每 900 秒检查一次，启动时不立即刷新；`RetryCount` 和 `RetryDelaySeconds` 控制可重试失败，基金和积存金可分别关闭。批量刷新可只刷新基金或积存金；每次尝试、重试和最终结果写入结构化日志，状态接口返回定时任务配置、下次运行时间、最近一次运行明细和每个持仓的最近来源 / 失败原因。刷新失败保留最近有效净值和日期，连续失败按配置次数停止，不使用 0 或推算值；行情未变化时不重复生成行情记录。

基金买入确认请求：

```json
{
  "shares": 29.8,
  "netValue": 10,
  "netValueDate": "2026-10-22",
  "fee": 2,
  "businessDate": "2026-10-22",
  "note": "份额确认",
  "requestId": "b8d4c0a1-2f3e-4a5b-8c6d-7e8f9a0b1c2d"
}
```

确认沿用原扣款金额，费用从已扣款金额中列示。已确认的买入不能直接更正金额或撤销，需先撤销确认；确认日期不得早于买入日期，净值日期不得晚于确认日期。基金总资产包含待确认金额，待确认不计入已确认持仓成本与收益率分母。

赎回确认请求：

```json
{
  "shares": 10,
  "netValue": 10,
  "netValueDate": "2026-10-22",
  "fee": 1,
  "netAmount": 100,
  "businessDate": "2026-10-22",
  "note": "赎回确认",
  "requestId": "c9e5d1b2-3a4f-4b6c-9d7e-8f0a1b2c3d4e"
}
```

赎回申请只冻结份额，确认后才减少份额与成本并把 `netAmount` 计入待到账；到账请求的 `amount` 必须与该待到账金额一致。已确认的申请不能直接撤销，已到账的确认需先撤销到账；到账不再次确认收益，也不计入生活收入。基金总资产 = 已确认份额估值 + 买入待确认 + 赎回待到账。

积存金卖出请求的 `settlement` 取 `received`（默认，直接到账）或 `pendingArrival`（待到账）。待到账卖出减少克数与成本、计入待到账，现金不变；`POST /api/ledger/gold/sells/{id}/arrival` 的到账金额必须与待到账一致。积存金资产 = 持有克数 × 有效报价 + 卖出待到账。

G0 请求约定：收支新建、修改、撤销、现金核对和资产变更都携带 `requestId`；同 ID 同内容返回首次结果，改内容或跨操作复用返回 409。现金核对及黄金绝对持仓核对另带 `expectedVersion`。保存结果未知时页面保留原请求，并提供查询/重试入口；成功提示以服务器确认为准。

收支查询支持 `page/pageSize`，默认 1/100、单页最多 200，返回 `totalCount`；周期合计按完整范围计算。历史补录按业务日期和已保存的同日顺序重算余额、成本与已实现收益，任何时点的负余额或超卖都会被拒绝。旧日期行情只进入其有效期间，不回退当前最新估值。基金 / 黄金已有持仓必须提供独立 `holdingDate`；旧快照日期未知时须先核对确认。

历史页先预览再确认更正 / 撤销；预览内容变化、账本发生新写入或服务重启后需要重新预览。漏记业务可用 `relatedCashAdjustmentId` 关联此前现金核对，在重算时替换差额，避免重复扣减现金。完整契约见 [G1 开发记录](docs/development/2026-10-22-开发记录-G1业务事实与历史重算.md)。

历史数据或行情依据不足时，不声明完整总资产；历史页返回 `totalAssets: null` 并解释覆盖缺口。

首页与资产页的口径：完整记录之前，首页只显示“已记录资产”，不出现“总资产”；用户可在资产页把没有的公积金、基金或积存金标记为“不持有”，并确认“其他资产均已覆盖”，确认后首页才升级为“总资产”。本月变化只比较月初和今天都已记录的类别（同口径），新增记录单独列出并标注“不计入变化”，分解为市值变动、净存入·转账和新增记录；现金和公积金只称余额变化，基金和积存金的市值变化才称收益。趋势只累计区间起点已记录的类别，新增记录当天用虚线标记；记录不足 7 天时首页显示紧凑进度状态而不是曲线。

行情来源：基金净值取天天基金公开接口的最近已披露值；积存金自动获取的是上期所黄金连续期货参考价，不是平台积存金报价，页面会明确标注并保留手动录入入口。行情失败时保留最近有效值和有效日期，并在基金 / 积存金持仓上显示最近一次失败原因。接口地址可通过 `QuoteProviders:FundBaseUrl`、`QuoteProviders:GoldBaseUrl` 和 `QuoteProviders:TimeoutSeconds` 覆盖，不需要凭据；接口不可用时不生成推算值。

## 数据库

默认连接串为 `Host=localhost;Port=5432;Database=asset_keeper`，不包含凭据。需要指定用户名、口令或云端地址时，通过环境变量覆盖：

```sh
export ConnectionStrings__Default="Host=localhost;Port=5432;Database=asset_keeper;Username=<用户名>;Password=<口令>"
```

不要把口令写入配置文件、脚本或提交信息。

本机开发可用 PostgreSQL 容器。以下示例只绑定 `127.0.0.1` 并使用本地信任认证，仅适合个人开发环境；共享或生产环境必须配置口令或密钥管理。

```sh
docker run -d --name asset-keeper-postgres \
  -p 127.0.0.1:5432:5432 \
  -e POSTGRES_DB=asset_keeper \
  -e POSTGRES_USER=asset_keeper \
  -e POSTGRES_HOST_AUTH_METHOD=trust \
  -v asset-keeper-pgdata:/var/lib/postgresql/data \
  postgres:16
```

使用 Colima 时，在 Docker 命令中加入 `--context colima`，或先切换到对应 context。

迁移位于 `api/src/AssetKeeper.Infrastructure/Persistence/Migrations`。执行：

```sh
cd api
dotnet tool restore
dotnet ef database update --project src/AssetKeeper.Infrastructure --startup-project src/AssetKeeper.Api
```

原五个迁移已在本机开发库应用。G0 新增第六个迁移 `CloseG0Correctness`，G1 新增第七个迁移 `AddChronologicalHistory`，G2 新增第八个迁移 `AddFundBuyPending`，行情接入新增第九个迁移 `AddQuoteSourcesAndSyncStatus`，自动记账新增第十个迁移 `AddAutoPlans`；第 6 至 10 个迁移已在备份后应用到现有开发库。分类维护新增第十一个迁移 `AddEntryCategories`，现有开发库已应用至第 11 个；投资分组新增第十二个迁移 `AddFundGroups`，使用分组接口前需先备份并应用该迁移；资产覆盖新增第十三个迁移 `AddAssetCoverage`（账本表增加“不持有”和“已确认覆盖”四个布尔列），已在备份后应用到现有开发库。发现多个历史账本时迁移会主动拒绝，不删除数据。

## 本地开发

后端（默认 <http://localhost:5069>）：

```sh
dotnet run --project api/src/AssetKeeper.Api
```

前端（另开终端，默认 <http://localhost:5173>）：

```sh
cd web
npm install
npm run dev
```

Vite 把 `/api` 代理到 `http://localhost:5069`；健康检查为 `GET /api/health`。账本接口需要可用的 PostgreSQL；只查看健康检查时无需建库。

API 启动后会同时运行自动记账后台任务，默认每 60 秒检查一次到期计划，不依赖浏览器。可通过 `PlanScheduler:Enabled` 和 `PlanScheduler:IntervalSeconds` 调整；自动化测试默认关闭后台调度。

行情定时刷新默认每 900 秒检查一次，启动时不立即刷新。可通过 `QuoteRefreshScheduler:Enabled`、`IntervalSeconds`、`RunOnStartup`、`RetryCount`、`RetryDelaySeconds`、`IncludeFunds` 和 `IncludeGold` 调整；自动化测试和 `QuoteRefreshScheduler:Enabled=false` 时不请求外部行情。状态页为「我的 → 行情更新状态」。

常用检查：

```sh
dotnet build api/AssetKeeper.slnx
dotnet test api/AssetKeeper.slnx
cd web && npm run type-check && npm run test && npm run build
```

资产总览口径重构后，后端 240 项全部通过（含 17 项真实 PostgreSQL）；前端 87 项测试、类型检查和生产构建通过。普通 API 测试使用 InMemory；关系数据库测试显式设置 `ASSET_KEEPER_TEST_CONNECTION`，仅使用本机独立临时库验证迁移、并发、唯一键、恢复事务回滚和数据完整性。执行方法见 [趋势图记录](docs/development/2026-10-22-开发记录-完整趋势图.md)。

趋势验收使用 8 月起始、分批录入基金 / 积存金 / 公积金和工资的数据：资产齐全前完整总资产留空，仅显示部分资产合计；资产齐全后显示完整总资产。近 1 月、近 3 月、今年、全部范围与空白 / 虚线说明均正常。G0/G1/G2、行情、自动记账与历史核对回归继续保留；验收使用独立临时服务和数据库。真实 iPhone/PWA 设备验收尚未执行。

## 预览现有原型

在仓库根目录执行：

```sh
python3 -m http.server 4173 --bind 127.0.0.1
```

- 首页高保真原型：<http://127.0.0.1:4173/prototype/>。
- 最终整合线框：<http://127.0.0.1:4173/docs/design/wireframes/>。

范围与演示限制见[原型说明](prototype/README.md)。预览服务仅绑定本机，不连接真实财务账户。
