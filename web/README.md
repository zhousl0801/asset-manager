# 资产管家 · Web

手机端 Web App / PWA 前端。业务范围与验收依据见[产品设计 V1.0](../docs/product/资产管家-产品设计文档-V1.0.md)。

## 本地开发

```sh
npm install
npm run dev
```

测试专用后端可通过 `ASSET_KEEPER_API_PROXY` 指定代理地址，默认仍为下述本机服务。

Vite 开发服务器把 `/api` 代理到 `http://localhost:5069`，同时启动 `api/src/AssetKeeper.Api` 即可联调。

## 检查

```sh
npm run type-check
npm run test
npm run build
```

配色变量直接引用 `docs/design/ui-color-tokens.css`，不在应用内另存色值。
