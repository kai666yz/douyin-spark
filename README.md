# douyin-spark · 抖音续火花助手

Windows 桌面小工具，通过抖音网页版向自己配置的好友发送问候消息。C# WinForms 界面搭配 Python + Playwright，当前版本 **v1.8.3**。

## 功能

- 一键导入关注列表并勾选好友；支持手动添加、删除和查看好友今日状态。
- 消息预览、发送进度、取消任务和发送记录。
- 当天发送查重、每日定时与开机自启。
- 粉色猫咪主题、启动动画和进度完成动画。
- Edge、Chrome、夸克浏览器选择；使用软件专用的浏览器目录保存登录状态。
- 语录管理：勾选消息分类，编辑自定义语录；兼容旧版句子库格式。

定时发送需要程序保持运行且电脑处于唤醒状态。网页识别会受到抖音页面变化和账号状态影响，不保证消息送达或火花状态。

## 运行要求

- Windows 10/11，推荐 64 位。
- .NET Framework 4.x，推荐系统自带的 4.8。
- Python 3.10 或更新版本；在安装时勾选加入 PATH。
- 已安装 Edge、Chrome 或夸克，并能访问抖音网页版。

## 从源码启动

在项目目录打开 PowerShell，依次执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\setup.ps1
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\抖音续火花.exe
```

`setup.ps1` 会在本项目创建 `.venv`、安装 Playwright，并在不存在时创建空的好友名单。程序优先使用环境变量 `DOUYIN_SPARK_PYTHON` 指定的 Python，其次查找 `.venv`、便携 `runtime` 和 PATH；不依赖豆包或作者电脑上的路径。

程序使用已安装的浏览器，因此不必执行 `playwright install`。构建后请保留 exe 与两个 Python 脚本、句子库在同一目录。

## 首次使用

1. 在“更多功能 → 浏览器选择”中选择已安装的浏览器。
2. 点击“添加好友”导入关注列表并勾选；也可在“更多功能 → 管理好友”中手动添加主页链接。
3. 在“更多功能 → 登录自检”中扫码登录。普通浏览器窗口中的登录状态不会自动共享到本工具。
4. 在“语录管理”中选好分类，使用“更多功能 → 预览句子”核对消息与名单，确认后再开始发送。

关注列表脚本会打开浏览器，读取当前账号关注列表并保存为本地 `following.json`，不发送消息。页面可能需要先完成登录。

### 命令行

以下示例中的 `--dry-run` 只预览，不启动浏览器、不发送消息：

```powershell
.\.venv\Scripts\python.exe .\douyin_spark.py --dry-run
.\.venv\Scripts\python.exe .\douyin_spark.py --dry-run --friend 示例好友
```

空名单会显示提示并退出。真实使用的 `friends.json` 格式为：

```json
[
  {"name": "示例好友", "url": "https://www.douyin.com/user/替换为真实用户ID"}
]
```

上面是格式示例，须替换为真实主页链接。`sentences_pool.json` 中的 `categories` 保存分类语录，`active_categories` 指定启用的分类；`sentences` 保留用于旧版兼容与回退，`holiday` 可按 `MM-DD` 配置节日消息。自定义内容会保存在本机句子库中；公开分享改动前，请先检查是否包含私人信息。

## 项目结构

```text
douyin-spark/
├── src/app.cs           # WinForms 主程序
├── douyin_spark.py      # 发送与日志脚本
├── fetch_following.py   # 关注列表读取
├── sentences_pool.json # 公开的示例句子库
├── icon.ico
├── build.ps1            # C# 编译
├── setup.ps1            # 本地 Python 环境与依赖
├── requirements.txt
├── LICENSE
└── .gitignore
```

## 数据与开发说明

好友、配置、关注列表、浏览器登录状态和日志仅在本机生成，已加入 `.gitignore`。发布前仍应检查 Git 暂存内容，避免手动强制添加这些文件。

界面“检查更新”读取本地 `update` 目录，当前没有在线更新服务。此次开源准备已检查 C# 编译、Python 语法和离线预览流程，未执行真实消息发送。

## 许可证与使用范围

项目原创代码按 [MIT](LICENSE) 许可证提供。第三方依赖使用各自许可证；Hello Kitty 等第三方名称、角色形象及相关素材权利归各自权利人，MIT 许可不授予这些权利。本项目与抖音及相关品牌无隶属关系。

请仅向愿意接收消息的好友使用，勿用于骚扰、营销或规避平台限制。账号状态及网页变化可能影响运行。
