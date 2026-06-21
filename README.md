# v2rayN

基于 [2dust/v2rayN](https://github.com/2dust/v2rayN) 的增强版本，新增 TUN 健康检查、进程流量劫持、路由规则测试等功能。

## 特色功能

### TUN 健康检查
- **6 层检查**：驱动加载 → TUN 接口 → DNS 解析 → 出站连通 → 路由验证 → 质量检测
- **依赖感知执行**：Layer 1 失败自动跳过后续层，避免无意义等待
- **TUN 流量验证**：检测流量是否真正经过 TUN，防止假阳性
- **规则化诊断引擎**：18 条规则按优先级匹配，给出具体修复建议
- **中文弹窗报告**：实时显示每层检测结果（✓通过 / ⚠警告 / ✗未通过）
- **JSON 导出**：支持敏感信息脱敏，安全分享诊断报告

### 进程流量劫持 (NetBridge)
- **状态栏一键控制**：启动进程劫持开关 + 设置按钮
- **与 TUN 互斥**：开一个自动关另一个，避免冲突
- **驱动状态实时显示**：驱动文件 / 驱动服务 / NetBridge 运行状态
- **智能进程列表**：
  - 当前运行进程（带搜索框、应用名显示）
  - 目标生效进程（带搜索框、无上限）
  - 默认名单一键添加（浏览器 / 开发工具 / 全部常用）
  - 导入文件夹所有 EXE（含子文件夹）
- **NetBridge 自动恢复**：崩溃检测 + Watchdog 自动重启 + 网络切换自动重启
- **DNS 通过 Bridge**：进程流量劫持设置中集成 DNS 转发选项

### 路由规则测试
- **实时规则匹配**：输入目标地址，即时显示匹配的路由规则
- **内存规则读取**：直接读取当前生效的内存规则，无需重启
- **domain 前缀匹配**：完整支持 `domain:` 前缀匹配规则

### 日志过滤
- **实时过滤**：输入关键字即时筛选，无需回车
- **过滤器变化重渲染**：切换条件后立即重新过滤所有已显示消息
- **正则预编译**：过滤器变化时自动编译正则，提升性能

### 其他改进
- **管理员权限启动**：默认以管理员权限运行
- **配置安全**：程序启动时若配置文件不存在才创建，避免覆盖用户配置
- **GeoTest 工具**：geo 数据解析测试 CLI 工具
- **自动发版**：编译成功自动发布，版本号 `vYYYY.MM.DD`
- **Xray 核心**：使用 [Maolaohei/Bray-Core](https://github.com/Maolaohei/Bray-Core) 最新 Release

## 代码质量

### 线程安全
- `volatile bool` 保证多线程可见性
- `Interlocked` 原子操作替代锁
- `ConcurrentDictionary` 正确使用

### 资源管理
- TcpClient/HttpClient 使用后立即释放
- 事件处理器 Dispose 时取消订阅
- HttpClient 连接池自动清理

### 性能优化
- 热路径 Regex 预编译，避免循环内重复编译
- 日志消息列表改用 LinkedList，删除操作 O(1)
- DNS 查询结果缓存，批量测速避免重复解析

### 错误处理
- 空 `catch {}` → 记录日志
- `_updateFunc` null guard 防 NullReferenceException
- SQL 参数化查询防注入

### 单元测试
- 90 个测试用例覆盖核心功能
- NetBridge 生命周期测试
- 配置序列化往返测试
- 路由规则匹配测试

## 构建

### 环境要求
- .NET 10.0 SDK
- Windows 10.0.19041.0+

### 编译命令

```bash
# Debug 编译
dotnet build v2rayN/v2rayN.csproj

# Release 自包含单文件
dotnet publish v2rayN/v2rayN.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableWindowsTargeting=true -o v2rayN/发布版本/self-contained

# 运行测试
dotnet test v2rayN/ServiceLib.Tests/ServiceLib.Tests.csproj
```

## 致谢

本项目基于以下开源项目：

- **[2dust/v2rayN](https://github.com/2dust/v2rayN)** - v2rayN 主项目（GPL-3.0）
- **[2dust/NetBridge](https://github.com/2dust/NetBridge)** - 进程流量劫持核心（MIT）
- **[DHR60](https://github.com/DHR60)** - 节点检查改进（PR #9603）
- **[Xray-core](https://github.com/XTLS/Xray-core)** - Xray 核心
- **[sing-box](https://github.com/SagerNet/sing-box)** - sing-box 核心
- **[WinDivert](https://github.com/basil00/WinDivert)** - Windows 网络包拦截驱动

## 许可证

[MIT](LICENSE)
