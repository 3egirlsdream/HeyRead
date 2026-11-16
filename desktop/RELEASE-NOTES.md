小黑盒Community For Mac 0.3.0

- 应用名称、包名、Bundle ID 统一为小黑盒Community For Mac / xiaoheihe-community-formac，保留原有登录、历史、字体和阅读偏好。
- 新增启动时更新检测，以及“关于”页和菜单栏的“检查更新”。
- 点击“自动更新”后，从 GitHub Release 下载对应处理器的 ZIP，验证更新清单签名、SHA-256、应用标识、版本、处理器和代码签名，再退出替换并重新启动。
- 更新保留旧应用作为回滚备份，收到新版启动回执后才删除备份；下载失败或取消不会覆盖当前版本。
- 新增版本号递增自动发布的 GitHub Actions，提供 Apple Silicon / Intel 的 DMG、更新 ZIP、签名清单和校验和。

需要 macOS 15 或更高版本。分发签名沿用 FKFinder 官网 CI 的 ad hoc 方式；此版本未做 Developer ID 公证。账号会话继续保存在本机加密文件中，不使用 Keychain。

本项目为基于 HeyRead 的独立社区客户端，与小黑盒官方无隶属关系。保留上游 MIT 声明。
