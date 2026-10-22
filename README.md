# 资产管家

个人资产观察与日常记账应用，统一记录现金、基金、公积金和平台积存金。

当前已实现账本初始化、收支记账（新建、修改、撤销）、现金页与余额核对、公积金缴存与提取、基金持仓与买卖、积存金持仓与买卖、资产流转联动、本月汇总和分类 / 关键字筛选，以及独立资产起始日期、历史补录重放、更正 / 撤销预览、行情溯源和现金差额关联。真实行情、截图识别、基金买入待确认 / 赎回到账、积存金卖出待到账、自动记账计划和后台任务尚未实现；原型全部使用虚构数据，刷新后重置。

当前分类使用固定默认列表，分类的新增、编辑和排序尚未实现。基金与积存金买卖暂按直接成交处理，卖出款直接进入现金。

## 文档入口

完整分类见[文档索引](docs/README.md)。开始开发前依次阅读：

1. [产品设计 V1.0](docs/product/资产管家-产品设计文档-V1.0.md)：业务范围、数据口径与验收场景。
2. [应用形态与技术选型 V1.0](docs/architecture/资产管家-应用形态与技术选型-V1.0.md)：前后端框架、云端账本、PWA 和 AI 接入边界。
3. [UI 设计索引](docs/design/README.md)：配色、整合线框、首页高保真演示及截图。

开发记录先看[功能迭代总览](docs/development/README.md)；分轮详情：[现金闭环与资产流转](docs/development/2026-10-21-开发记录-现金闭环与资产流转.md)、[G0 正确性收口](docs/development/2026-10-21-开发记录-G0正确性收口.md)、[G1 业务事实与历史重算](docs/development/2026-10-22-开发记录-G1业务事实与历史重算.md)。

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
| POST | `/api/ledger/initialize` | 建立账本；重复建立返回 409，校验失败返回 400 |
| GET | `/api/ledger/entries` | 按周期读取收支；支持 `period`、`anchor`、`kind`、`category`、`search` |
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
| POST | `/api/ledger/gold/sells` | 积存金卖出，增加现金并结转成本 |
| POST | `/api/ledger/gold/quote` | 更新积存金估值报价 |
| POST | `/api/ledger/gold/holding` | 录入或核对已有积存金持仓 |
| GET | `/api/ledger/funds` | 读取基金持仓、成本、收益与交易记录 |
| POST | `/api/ledger/funds/positions` | 录入已有基金持仓，不扣现金 |
| POST | `/api/ledger/funds/buys` | 基金买入，扣减现金并增加份额 / 成本 |
| POST | `/api/ledger/funds/sells` | 基金卖出，增加现金并结转成本 |
| POST | `/api/ledger/funds/quote` | 更新基金净值 |

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

资产流转规则：公积金缴存只增加公积金；提取等额转入现金。基金 / 积存金买入扣减现金并增加持仓，卖出增加现金并结转成本；录入已有持仓和更新净值 / 报价不改变现金。所有资产变更请求都携带 `requestId`，重复提交返回同一记录，不重复扣款或加仓。所有现金账户与资产持仓使用 `version` 并发标记，冲突时返回 409。

G0 请求约定：收支新建、修改、撤销、现金核对和资产变更都携带 `requestId`；同 ID 同内容返回首次结果，改内容或跨操作复用返回 409。现金核对及黄金绝对持仓核对另带 `expectedVersion`。保存结果未知时页面保留原请求，并提供查询/重试入口；成功提示以服务器确认为准。

收支查询支持 `page/pageSize`，默认 1/100、单页最多 200，返回 `totalCount`；周期合计按完整范围计算。历史补录按业务日期和已保存的同日顺序重算余额、成本与已实现收益，任何时点的负余额或超卖都会被拒绝。旧日期行情只进入其有效期间，不回退当前最新估值。基金 / 黄金已有持仓必须提供独立 `holdingDate`；旧快照日期未知时须先核对确认。

历史页先预览再确认更正 / 撤销；预览内容变化、账本发生新写入或服务重启后需要重新预览。漏记业务可用 `relatedCashAdjustmentId` 关联此前现金核对，在重算时替换差额，避免重复扣减现金。完整契约见 [G1 开发记录](docs/development/2026-10-22-开发记录-G1业务事实与历史重算.md)。

历史数据或行情依据不足时，不声明完整总资产；首页展示部分资产合计，历史页返回 `totalAssets: null` 并解释覆盖缺口。

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

原五个迁移已在本机开发库应用。G0 新增第六个迁移 `CloseG0Correctness`，G1 新增第七个迁移 `AddChronologicalHistory`；这两项仅在独立测试库验证，未升级现有开发业务库；使用更新后的 API 前须先备份并执行迁移。发现多个历史账本时迁移会主动拒绝，不删除数据。

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

常用检查：

```sh
dotnet build api/AssetKeeper.slnx
dotnet test api/AssetKeeper.slnx
cd web && npm run type-check && npm run test && npm run build
```

G1 收口后，后端 133 项（含 11 项真实 PostgreSQL）、前端 45 项测试通过，前端类型检查与生产构建通过。普通 API 测试使用 InMemory；关系数据库测试显式设置 `ASSET_KEEPER_TEST_CONNECTION`，仅使用本机独立临时库验证迁移、并发、唯一键和事务回滚，不设置时这 11 项会明确跳过。执行方法与覆盖范围见 [G1 开发记录](docs/development/2026-10-22-开发记录-G1业务事实与历史重算.md)。

本轮还完成真实 PostgreSQL + 浏览器验证：独立起始日期、历史补录成本重算、更正 / 撤销预览、关联漏记替换差额、公积金起始余额和指定日行情覆盖。G0 的响应恢复与旧版本核对回归继续保留；验收使用独立临时服务和数据库。真实 iPhone/PWA 设备验收尚未执行。

## 预览现有原型

在仓库根目录执行：

```sh
python3 -m http.server 4173 --bind 127.0.0.1
```

- 首页高保真原型：<http://127.0.0.1:4173/prototype/>。
- 最终整合线框：<http://127.0.0.1:4173/docs/design/wireframes/>。

范围与演示限制见[原型说明](prototype/README.md)。预览服务仅绑定本机，不连接真实财务账户。
