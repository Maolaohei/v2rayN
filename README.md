# v2rayN

基于 [2dust/v2rayN](https://github.com/2dust/v2rayN) 的增强版本，新增 TUN 健康检查、进程流量劫持、日志过滤等功能。

## 特色功能

### TUN 健康检查
- **依赖感知执行**：Layer 1 失败自动跳过后续层，避免无意义等待
- **TUN 流量验证**：检测流量是否真正经过 TUN，防止假阳性
- **规则化诊断引擎**：18 条规则按优先级匹配，给出具体修复建议
- **中文弹窗报告**：实时显示每层检测结果（✓通过 / ⚠警告 / ✗未通过）
- **JSON 导出**：支持敏感信息脱敏，安全分享诊断报告

### 进程流量劫持
- **状态栏一键控制**：启动进程劫持开关 + 设置按钮
- **与 TUN 互斥**：开一个自动关另一个，避免冲突
- **驱动状态实时显示**：驱动文件 / 驱动服务 / NetBridge 运行状态
- **智能进程列表**：
  - 当前运行进程（带搜索框、应用名显示）
  - 目标生效进程（带搜索框、无上限）
  - 默认名单一键添加（浏览器 / 开发工具 / 全部常用）
  - 导入文件夹所有 EXE（含子文件夹）
- **NetBridge 自动恢复**：崩溃检测 + Watchdog 自动重启

### 日志过滤
- **实时过滤**：输入关键字即时筛选，无需回车
- **过滤器变化重渲染**：切换条件后立即重新过滤所有已显示消息

### 其他改进
- **管理员权限启动**：默认以管理员权限运行
- **配置安全**：程序启动时若配置文件不存在才创建，避免覆盖用户配置
- **DNS 通过 Bridge**：进程流量劫持设置中集成 DNS 转发选项

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
