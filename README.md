# 抖音续火花 · douyin-spark

Windows 桌面助手，C# WinForms 界面配合内嵌 WebView2 与独立 Playwright 后端，管理好友和群聊。当前版本 **v2.1.1**。

## 界面预览

现代聊天软件布局：左侧导航、中间概览与联系人、右侧详情和消息预览。默认浅灰蓝，也可切换深色；Kitty 仅作为小点缀。截图中的18/23及好友均为测试数据。

![浅色界面（测试数据）](docs/ui-light.png)

![深色界面（测试数据）](docs/ui-dark.png)

## 便携版使用

1. 从 [GitHub Releases](https://github.com/kai666yz/douyin-spark/releases/latest) 下载 Windows x64 ZIP，完整解压。
2. 双击 `抖音续火花.exe`，保留旁边的整个 `spark_backend` 文件夹。
3. 点击“导入会话”。已有缓存可直接勾选；首次点“登录 / 刷新”，在软件内登录并打开抖音消息列表，再点“读取当前会话”。
4. 勾选私信好友和群聊后导入。群聊仅用于管理；没有个人主页的私信显示“待补主页”，需核对补齐后才能发送。类型不明的会话需先手动确认。
5. “设置与更多 → 传统发送浏览器”中选择已安装的 Edge、Chrome 或夸克，复用旧版发送登录。内嵌网页与传统发送登录彼此独立，不复制 Cookie；首次可能分别需要登录。
6. 核对右侧消息预览，再点“发送全部私信”。该按钮发送所有有效私信好友，不是只发当前选中项；群聊始终排除。新包名单为空，不含作者登录状态或私人记录。

便携包已含独立 Python/Playwright 后端及 WebView2 SDK 组件，无需另装 Python、豆包或下载 Playwright 浏览器内核。需要 Windows 10/11、.NET Framework 4.6.2+（推荐4.8），内嵌导入还需 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。传统发送需要上述已安装浏览器之一。

旧版迁移时先退出旧程序，保留自己的 `friends.json`、`config.json`、`conversations.json`、`conversation_candidates.json`、`conversation_profile`、对应 `browser_profile*` 和 `logs`。不要用下载包的空名单覆盖自己的名单。

### 会话导入与登录保留

- 从内嵌消息页面读取已加载会话的名称、类型和稳定标识，不保存聊天正文，不抓取或导出登录 Cookie。
- 在同一软件目录中保留 `conversation_profile`，登录有效时无需重复扫码。上次读取的会话缓存可直接勾选，不启动网页；刷新新会话时才展开网页。
- “读取当前会话”会尝试打开消息并等待读取。已加载的消息面板即使因切换焦点而隐藏，仍可识别其会话元数据；单击复选框即可选择联系人。
- 读取失败会提示原因及下一步，计数诊断保存在 `logs/conversation_read.log`，其中不包含联系人名称、标识、聊天正文或 Cookie。
- 只展示可验证标识或个人主页的条目，不靠昵称猜测身份。相同群名、不同会话标识分别保留。
- 当前读取结果不代表所有历史会话。页面未加载、没有可识别标识、接口/DOM变化时，可能只能读取部分会话；可在消息页继续加载，或使用“关注导入”和手动添加。
- 类型不明的条目需用户确认；私信无主页时仅管理；群聊仅管理，不进入现有发送后端。右键联系人可移出本软件名单，不会解除抖音好友或退出群聊。
- 右侧语录仅为示例，固定消息是本轮统一设置。查看、切换联系人、读取会话和预览都不会自动发送。

### v2.0 关注列表导入

- 软件的浏览器 profile 与日常浏览器相互独立。请在软件弹出的窗口中登录，最多等待3分钟；必要时手动打开自己的关注列表。
- 读取窗口显示阶段及已发现人数，支持取消；关闭读取窗口也会请求取消，等待当前浏览器操作结束。重复点击不会重复启动导入。
- 仅明确的成功接口、结束标记与总数一致时才标为完整；网页链接回退和无法确认总数的结果标为“部分”，需用户确认后再勾选。
- 区分完整、部分、已确认空列表、未登录、失败和取消；0人不会弹空勾选窗。只有接口明确证明0人时才显示“暂无关注”。
- 失败、空列表、部分结果和取消均保留原关注缓存。完整结果仅更新关注缓存；实际好友名单只在点击“导入选中”时追加去重。
- 失败窗口提供重试、手动添加和本次诊断；诊断摘要位于 `logs/following_import.log`，按现有文本日志策略轮换。
- 网站变动、权限限制或接口没有总数时可能只能获得部分数据；软件不会将不确定结果描述为完整。

## 功能与入口

| 功能 | 入口与行为 |
|---|---|
| 添加好友 | “导入会话”选择私信与群聊；“关注导入”备用；“设置与更多 → 手动添加 / 删除好友”可粘贴个人主页短链 |
| 联系人 | 全部 / 私信 / 群聊筛选、搜索、当前联系人详情、消息预览和独立发送记录 |
| 今日概览 | 顶部查看已处理、成功、失败、待发、待确认与下次定时；按每位好友最后一次结果统计 |
| 外观 | 左下角浅色/深色开关，自动保存；三栏布局，Kitty 小点缀 |
| 好友状态 | 查看今天的状态；缺失主页显示“待补”，不会猜测链接 |
| 语录管理 | 勾选分类、编辑自定义语录；支持旧句子库格式与节日优先 |
| 固定消息 | “设置与更多 → 统一固定消息”；留空恢复语录池，保存后对手动和定时任务生效 |
| 定时与自启 | 程序运行且电脑唤醒时按设定时间执行；开机自启使用 `--minimized` |
| 托盘 | 最小化或关闭窗口后继续在托盘运行；右键托盘选择“退出” |
| 历史 | 右侧查看所选联系人的活动记录；设置与更多中导出全部 CSV、清理旧历史 |
| 检查更新 | 查询 GitHub Releases；确认后打开官方下载页，手动更新 |

固定消息绕过语录池和“最近7天句子不重复”，**仍保留当天好友去重**。无需修改句子库，也无需删除日志。历史清理会先备份并保留今天的记录，避免清理历史后重复发送。

## 命令行

在解压目录打开 PowerShell：

```powershell
# 仅预览固定消息；不启动浏览器，不发送。
.\spark_backend\spark_backend.exe --dry-run --message "嘎嘎地拉下"

# 真正发送前，先核对好友和消息。省略 --dry-run 才会执行发送。
.\spark_backend\spark_backend.exe --message "自己填写的问候消息"

# 打开浏览器进行登录检查；不会发送消息。
.\spark_backend\spark_backend.exe --selftest

# 仅用本地 HTML 检查浏览器运行环境，不访问抖音。
.\spark_backend\spark_backend.exe --runtime-check

# 导出历史，不执行发送。
.\spark_backend\spark_backend.exe --export-history ".\发送历史.csv"
```

源码运行时将上面的后端命令改为 `.\.venv\Scripts\python.exe .\douyin_spark.py`，参数相同。`--friend` 可筛选好友；`--force` 明确忽略当天好友去重，用于人工决定的重发。`--cancel-file PATH` 指定的文件出现后会停止后续处理。

## 稳定性与数据保护

- 好友名单使用 JSON 读写，保存时原子替换并保留 `.bak`，支持 BOM 与尾逗号。损坏文件会报错并保留原文。
- CSV 活动日志保留最近14天，其余按月放入 `logs/archive/spark_log`。语录去重和导出仍读取归档。
- 文本日志每份最多约2 MiB，再轮换至 `.1`～`.5`。运行输出在下一次启动时重置。
- 检测到验证码时暂停，提示在浏览器手动处理，最多等待5分钟；页面提示操作频繁时停止任务。
- 未提交失败最多额外重试2次，分别退避2、4秒并重建页面；不切换 UA 或自动解验证码。
- 按下发送键前写入待确认记录；结果不确定时保留 `UNCONFIRMED`，不会自动重发，当天默认跳过该好友。
- 任务锁防止多个发送/导入任务占用同一目录。网站变化、网络及账号状态仍可能影响识别；应核对异常记录。

`friends.json`、配置、关注列表、浏览器登录状态、日志、备份和运行环境均在 `.gitignore` 中。自定义语录存放在句子库中，提交修改前须自行检查；发布脚本会清空“自定义”分类。

## 从源码构建

安装 Python 3.10+ 并加入 PATH，在仓库目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\抖音续火花.exe
```

界面先寻找随包独立后端；未找到时依次使用 `.venv`、`DOUYIN_SPARK_PYTHON` 指定路径、便携 `runtime`、系统 PATH，最后才兼容查找当前用户的豆包沙箱。不存在固定用户名或沙箱版本路径。

生成可分发 ZIP：

```powershell
powershell -ExecutionPolicy Bypass -File .\package.ps1
```

输出在 `dist/douyin-spark-v2.1.1-windows-x64.zip`。使用 PyInstaller 文件夹模式，附带必要依赖与许可证；不直接复制本机 `.venv`。构建时从 NuGet 下载固定版本 WebView2 SDK，并校验 SHA-256；不把本机 Runtime 或登录文件打包。

## 源码结构

```text
src/app.cs              主窗口、定时与运行状态
src/FriendsForm.cs      好友管理与关注导入
src/FollowingImport.cs  登录引导、可取消读取、结果校验与诊断
src/Storage.cs          JSON、CSV与文本日志
src/BackendLauncher.cs  后端选择与进程启动
src/DesktopFeatures.cs 托盘、固定消息、历史和更新检查
src/Theme.cs            Kitty绘制
src/Appearance.cs       明暗配色、圆角与矢量图标
src/MainView.cs         仪表盘布局与数据刷新
src/ChatWorkspace.cs   三栏联系人工作台、搜索筛选与详情
src/RosterList.cs      联系人列表与头像占位
src/RosterModel.cs     好友/群聊统一展示、发送隔离
src/Conversations.cs   会话标识校验、合并及元数据解析
src/ConversationImport.cs 内嵌消息页、缓存与勾选导入
src/DashboardCards.cs  概览和空状态卡片
src/TodaySummary.cs    今日去重统计
src/SparkProgress.cs    进度与完成动画
src/Controls.cs         其他自绘控件
douyin_spark.py         消息流程与命令行参数
fetch_following.py      关注列表读取
spark_support.py       路径、归档、任务锁、人工验证支持
spark_backend.py       独立后端入口
tests/                 离线回归测试
tools/package_release.py 发布打包
```

离线测试：`python -m unittest discover -s tests -p "test_*.py"`。C#存储测试见 `tests/StorageTests.cs`，需与 `src/Storage.cs` 一同编译为控制台程序。

浏览器离线烟测：`python tools/verify_following_browser.py`，需要本机 Edge。全部网页请求由测试拦截，不登录真实账号、不发送消息。

会话 DOM 离线烟测：`python tools/verify_conversation_dom.py`。会话和名单隔离测试见 `tests/ConversationTests.cs`，编译后引用主程序程序集运行。

## 许可证

原创代码采用 [MIT](LICENSE)。第三方依赖按各自许可证分发；Hello Kitty 等名称、形象及相关素材权利归各自权利人，MIT 不授予这些权利。本项目与抖音及相关品牌无隶属关系。请仅向愿意接收消息的好友使用。

本版已进行离线测试、界面渲染及独立后端本地浏览器检查；未进行真实消息发送或在线验证码验证。变更见 [CHANGELOG](CHANGELOG.md)。
