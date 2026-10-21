# 资产管家

个人资产观察与日常记账应用，统一记录现金、基金、公积金和平台积存金。

当前处于开发准备阶段：已归档产品设计、UI 配色规范、技术选型与交互原型，尚未实现正式账本、云端存储、真实行情、截图识别或后台任务。原型全部使用虚构数据，刷新后重置。

## 文档入口

完整分类见[文档索引](docs/README.md)。开始开发前依次阅读：

1. [产品设计 V1.0](docs/product/资产管家-产品设计文档-V1.0.md)：业务范围、数据口径与验收场景。
2. [应用形态与技术选型 V1.0](docs/architecture/资产管家-应用形态与技术选型-V1.0.md)：前后端框架、云端账本、PWA 和 AI 接入边界。
3. [UI 设计索引](docs/design/README.md)：配色、整合线框、首页高保真演示及截图。

## 技术基线

| 层次 | 选择 |
| --- | --- |
| 应用形态 | 手机优先的响应式 Web App / PWA，适配 iPhone 与 Mac 浏览器 |
| 前端 | Vue 3 + TypeScript + Vite；Vue Router + Pinia |
| UI / 图表 | Vant 4 + 产品自定义样式；Apache ECharts |
| 后端 | ASP.NET Core 10 Web API，分层单体 |
| 数据访问 | EF Core 10 + Npgsql |
| 正式存储 | 云端 PostgreSQL |
| AI | 授权只读接口与结构化导出；后续可扩展 DeepSeek 自然语言记账 |

上述为选型，不代表依赖或运行环境已经初始化。具体依赖版本在工程初始化时锁定并验证。

## 预览现有原型

在仓库根目录执行：

```sh
python3 -m http.server 4173 --bind 127.0.0.1
```

- 首页高保真原型：<http://127.0.0.1:4173/prototype/>。
- 最终整合线框：<http://127.0.0.1:4173/docs/design/wireframes/>。

范围与演示限制见[原型说明](prototype/README.md)。预览服务仅绑定本机，不连接真实财务账户。

## 目录

```text
docs/
  product/        产品设计与业务验收依据
  architecture/   应用形态与技术选型
  design/         UI 配色、CSS 变量、线框与设计截图
  research/       前期竞品研究
prototype/        静态交互原型
```

正式前后端代码将在后续工程初始化时建立。自然语言记账属于已讨论的后续扩展，不改变产品 V1.0 的首版范围。
