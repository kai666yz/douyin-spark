# v2.2.0 验证记录

日期：2026-10-29。环境：Windows 11 x64，已有Edge与WebView2。使用隔离、虚构的联系人和日志，不发送真实消息。

- Python 3.12.14下22个后端单元测试通过。
- 21项部署与定时断言通过：实际ACL拒绝写入、复制名单与历史、原文件字节保留、粘性备用目录、损坏源拒绝覆盖、迁移暂停持久保存、同位置显式恢复、下一次时间和当日防重、错过窗口不补发。
- 前后端使用同一个备用数据路径，程序位置和数据位置分离后仍由真实发布版启动器定位包内exe。
- 发布版程序集验证主窗口、浅色/深色布局、环境向导、缺组件时一键准备按钮可见性、真实暂停状态与重启读取。验证微软引导安装器正确SHA256及篡改拒绝；未实际执行安装器或改动系统Runtime。
- 原有C#会话/群聊隔离和导入结果解析测试通过。
- 新Python环境下本地浏览器DOM和关注读取六状态回归通过；流量阻断或拦截，无账号登录与发送。
- 最终ZIP通过CRC和296个文件SHA256检查；不含私人好友、定时、profile、缓存或日志；PATH移除Python后验证冻结后端Python 3.12.14/Playwright 1.64.1、Edge本地HTML、Unicode/emoji、不同工作目录、取消和失败保留缓存、CSV导出、移动改名后运行。

SHA256：`a8b777812d71c9431d04718bde1349db3a4b18077652713b009fc8a6c96d6fff`。

未验证：第二台干净电脑/虚拟机、Windows 10、最低.NET版本、全新系统实际安装Runtime、真实跨电脑登录及真实发送、32位/ARM64、U盘与网络盘、超长路径、SmartScreen判定。UI及启动策略通过程序集测试，不将其描述为干净系统完整冷启动测试。

复测工具：`python -m unittest discover -s tests -p test_*.py`，`tests/DeploymentTests.cs`，`tools/verify_conversation_dom.py`，`tools/verify_following_browser.py`，`tools/verify_portable_release.py ZIP --work-dir 可写测试目录`。C#部署测试需在Windows编译并引用当前桌面程序集，参数指定隔离目录；切勿对真实个人数据目录施加测试ACL。
