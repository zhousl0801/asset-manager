---
name: start-app
description: 启动资产管家本地开发环境——检查 Colima 中的 PostgreSQL 容器、拉起后端 API（5069）与前端 Vite（5173），并做健康检查。当用户要求“启动项目”“跑起来”“起服务”“开一下开发环境”时使用。只保证服务健康启动，不做浏览器或业务级验证。
---

# 启动资产管家本地开发环境

按“数据库 → 后端 → 前端 → 健康检查”的顺序拉起，只验证服务可用，不做业务功能验证。

## 本机环境事实（已核实）

| 项 | 值 |
| --- | --- |
| PostgreSQL 容器 | `asset-keeper-postgres`，跑在 **Colima** 的 Docker 上下文里，不是默认 socket |
| 数据库地址 | `localhost:5432`，库 `asset_keeper`，角色 `asset_keeper`，trust 认证 |
| 后端 API | `http://localhost:5069`（端口由 `api/src/AssetKeeper.Api/Properties/launchSettings.json` 的 `http` profile 决定） |
| 前端 | `http://localhost:5173`，Vite 把 `/api` 代理到 `http://localhost:5069` |
| 健康检查 | `GET /api/health` 返回 `{"status":"ok"}` |

## 步骤

### 0. 预检：是否已在运行（幂等）

已健康运行的服务**不要重启**，直接跳到第 4 步做健康检查。

```sh
lsof -ti tcp:5069 -sTCP:LISTEN >/dev/null && echo "后端已在跑"
lsof -ti tcp:5173 -sTCP:LISTEN >/dev/null && echo "前端已在跑"
```

### 1. 数据库容器

Colima 停过就连带容器一起停，顺序固定：

```sh
colima status >/dev/null 2>&1 || colima start
docker --context colima start asset-keeper-postgres
pg_isready -h localhost -p 5432
```

`pg_isready` 期望输出 `localhost:5432 - accepting connections`。容器已运行时 `docker start` 是幂等的，直接执行即可。

### 2. 后端 API

**必须带 `ConnectionStrings__Default`**：默认连接串不带用户名，EF 会用系统用户 `zhousl` 连库，`/api/ledger` 直接返回 500（`role ... does not exist`），而 `/api/health` 不查库、仍然 200——只看健康检查发现不了这个坑。

```sh
cd api
ConnectionStrings__Default="Host=localhost;Port=5432;Database=asset_keeper;Username=asset_keeper" \
  dotnet run --project src/AssetKeeper.Api
```

`cd` 必须和 `dotnet run` 写在**同一条命令**里：后台任务的执行目录不跟随会话目录变化，分开写会报“提供的文件路径不存在: src/AssetKeeper.Api”。

以**后台任务**方式运行，进程需要在整个会话中存活。

日志里的这三行是正常启动信息，不是错误：

- `行情定时刷新已启动：间隔 900 秒，启动时刷新 False，重试 1 次`
- `Now listening on: http://localhost:5069`
- EF 的 `SELECT ... FROM ledgers ...` —— 后台计划任务在启动时就查库

### 3. 前端

```sh
cd web
npm run dev
```

同样要在**同一条命令**里 `cd`，并以**后台任务**方式运行。Vite 启动后打印 `ready in ... ms` 和 `Local: http://localhost:5173/`。

依赖未装时先 `npm install`。

### 4. 健康检查

```sh
curl -s -o /dev/null -w '后端直连 /api/health → %{http_code}\n' http://localhost:5069/api/health
curl -s -o /dev/null -w '经代理 /api/health   → %{http_code}\n' http://localhost:5173/api/health
```

两条都应为 `200`。代理那条同时验证了 Vite 的 `/api` 转发配置。

**只看状态码，不要打印账本金额**——`/api/ledger` 的响应含个人财务数据，把它输出到对话里会被安全策略拦截。要确认账本能读回，用状态码即可：

```sh
curl -s -o /dev/null -w '账本可读 → %{http_code}\n' http://localhost:5173/api/ledger   # 期望 200
```

## 停止

按端口定位 PID 精确结束，**不要用 `pkill -f` 之类的宽泛模式**，会误杀同名进程且被安全策略拦截：

```sh
kill $(lsof -ti tcp:5069 -sTCP:LISTEN)   # 后端
kill $(lsof -ti tcp:5173 -sTCP:LISTEN)   # 前端
```

## 故障排查

| 现象 | 原因与处理 |
| --- | --- |
| `localhost:5432` 连接被拒 | Colima 或容器没起，回第 1 步按顺序启动 |
| `提供的文件路径不存在: src/AssetKeeper.Api` | `cd` 和 `dotnet run` 没写在同一条命令里，后台任务的工作目录与会话目录不一致 |
| `/api/health` 200 但 `/api/ledger` 500 | 漏了 `ConnectionStrings__Default` 或用户名不对，见第 2 步 |
| 后端起不来，报 `Failed to bind to address` | 5069 已被占用；先查 `lsof -ti tcp:5069 -sTCP:LISTEN` 是否有残留进程 |
| 想换端口 | `ASPNETCORE_URLS` 单独设置**无效**——`dotnet run` 会用 launchSettings 的 `applicationUrl` 覆盖它；要改端口用 `dotnet run --no-launch-profile` 并自行设 `ASPNETCORE_URLS` |
| Vite 代理 502 / ECONNREFUSED | 后端没起或不在 5069；前端代理目标可用 `ASSET_KEEPER_API_PROXY` 覆盖 |
| 后端跑测试要连真实 PostgreSQL | 另设 `ASSET_KEEPER_TEST_CONNECTION="Host=localhost;Port=5432;Database=postgres;Username=asset_keeper"`，否则 17 项数据库用例被跳过 |

## 不做的事

- 不做浏览器验收、不截图——用户只要服务健康启动。
- 不改业务代码、不动迁移。
- 不把 `ConnectionStrings__Default` 写进配置文件或提交信息。
