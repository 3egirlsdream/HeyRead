# 小黑盒Community For Mac

用于浏览小黑盒社区内容和 Steam 游戏公告的 macOS 客户端。支持信息流、搜索、原生图文阅读和评论。

这是独立第三方项目，与小黑盒或 Valve 无隶属关系。

## 下载

**macOS 0.3.1** 支持 macOS 15 及以上。两种架构的安装包均采用 Apple Developer ID 签名并通过 Apple 公证。打开 DMG 后，将应用拖到「应用程序」。

| Mac | 安装包 |
| --- | --- |
| Apple Silicon（M 系列） | [下载 arm64 DMG](https://github.com/3egirlsdream/HeyRead/releases/download/mac-v0.3.1/xiaoheihe-community-formac-0.3.1-osx-arm64.dmg) |
| Intel | [下载 x64 DMG](https://github.com/3egirlsdream/HeyRead/releases/download/mac-v0.3.1/xiaoheihe-community-formac-0.3.1-osx-x64.dmg) |

已安装的版本可从应用菜单检查更新。更新包、校验文件和版本说明见 [GitHub Release](https://github.com/3egirlsdream/HeyRead/releases/tag/mac-v0.3.1)。

## 功能

- 默认打开窄窗信息流。点开文章时，右侧展开阅读区，左侧保留原来的宽度和滚动位置。信息流支持下拉刷新和滚动加载。
- 底部液态玻璃导航整合发现、动态、游戏、搜索和账号入口。可按全站、分区或话题搜索小黑盒内容，也可按游戏名或 AppID 查找 Steam 公告。
- 正文使用原生排版，支持中文分段、行内表情、引用、图片图库和原生媒体播放。评论接在正文下方，楼中楼可就地展开。
- 支持系统主题、无图模式和自定义字体。阅读设置可独立调整字号、行高和段距，并即时预览。
- 可先浏览公开内容。登录小黑盒账号后，可读取账号收藏、历史和部分完整内容。账号会话以加密文件保存在本机。

## 从源码运行

需要 macOS 15 或更高版本、.NET SDK 10.0.201 和 Xcode 命令行工具。

```sh
cd desktop
./scripts/build-native.sh
dotnet restore HeyRead.slnx --locked-mode
dotnet run --project src/HeyRead.Desktop
```

打包、自动更新、测试和数据存储说明见 [macOS 开发文档](desktop/README.md)。

## 反馈和许可

反馈问题时请附应用版本、macOS 版本、Mac 型号及复现步骤。涉及账号的问题请先移除 Cookie、二维码、验证码票据和个人信息。

项目源码采用 [MIT License](LICENSE)。依赖和上游归属见 [第三方声明](desktop/docs/THIRD-PARTY-NOTICES.txt)。
