# OTPBar

<img src="assets/icon/otpbar-app.png" width="128" alt="OTPBar 应用图标">

常驻在 macOS 菜单栏与 Windows 通知区域的动态码工具，让用户在电脑上查看、复制手机验证器中的动态码。

两个平台共用同一套验证码生成、2FAS 备份解析与本地数据规则，界面和本地存储各自使用平台原生的实现。

## 在 macOS 上运行

需要 macOS 14 或更新版本，以及 Swift 6 工具链。

```sh
bash scripts/build-app.sh
open .build/app/OTPBar.app
```

可将生成的 `OTPBar.app` 放入「应用程序」。构建使用本地 ad hoc 签名，自用无需付费开发者账号；这不是经过 Apple 公证的分发版本。

- 左键点击菜单栏图标查看验证码，点击账号复制当前有效码。
- 右键点击图标打开设置或退出；关闭设置窗口后仍驻留菜单栏。
- 本地数据保存在 macOS 钥匙串。

## 在 Windows 上运行

需要 Windows 10 或更新版本。从源码构建需要 .NET 10 SDK；直接使用发布的可执行文件则无需安装任何运行时。

```powershell
powershell -File windows\build.ps1
windows\.build\OTPBar.exe
```

- 左键点击通知区域图标查看验证码，点击账号复制当前有效码。
- 右键点击图标打开设置或退出。新图标默认收在通知区域的溢出面板里，可在任务栏设置中把它固定出来。
- 本地数据保存在 `%LOCALAPPDATA%\OTPBar\vault.v1.dat`，由当前 Windows 账户的 DPAPI 加密：换一个 Windows 账户或换一台机器都解不开。
- 同一份本地数据同时只允许运行一个实例。

## 使用

- 在设置中导入手机 2FAS 的 `.2fas` 备份。支持密码保护的备份，重复项自动跳过。
- Windows 版还可以逐个添加账号：框选屏幕上的注册二维码、选择二维码图片、粘贴 `otpauth://` 链接，或直接填写名称与密钥。填写过程中会实时显示当前验证码，便于与手机核对后再保存。
- 编辑和删除仅影响本机。导入完成后不会与手机持续同步；新增手机账号需要重新导出并导入。
- 不要将真实备份或密钥提交到仓库。

### Windows 上的剪贴板

Windows 会保留剪贴板历史（Win+V），并可能把剪贴板同步到其他设备，仅在验证码过期时清空剪贴板并不足以让它消失。因此每次复制验证码都会同时写入将其排除在剪贴板监视程序、剪贴板历史和云剪贴板之外的标记。

从屏幕添加账号时读取的是选中的屏幕区域，而不是剪贴板里的截图：二维码截图中就是密钥本身，而清空剪贴板并不能移除剪贴板历史中已经留下的条目。

## 发布

推送 `v` 开头的标签会分别在 macOS 与 Windows 上构建、测试，并把两个压缩包附到对应的 Release：

- `OTPBar-<版本>-macOS.zip` —— ad hoc 签名的 `OTPBar.app`
- `OTPBar-<版本>-windows-x64.zip` —— 自包含的 `OTPBar.exe`，目标机器无需安装 .NET

## 项目入口

- [初版需求、技术选择与验证进度](https://github.com/Erugihs/otpbar/issues/1)
- [后续事项：CLI 取码](https://github.com/Erugihs/otpbar/issues/2)
- [开发环境](docs/development.md)
- [图标资源](assets/icon/)

需求、设计决定与进度以对应 Issue 为准。
