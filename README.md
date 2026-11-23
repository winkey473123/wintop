# wintop

Windows工具：窗口置顶。单文件 exe，无第三方依赖。

## 用法

- 热键默认 `Ctrl+T`。
- 设置项：是否显示边框、颜色模式（系统强调色 / 自定义）、不透明度、粗细、圆角、开机自启。

## 安装

- 从仓库的 Releases 页面下载 `wintop.exe`，双击即可（免安装）。
- 或自行编译（只需 Windows 自带的 .NET Framework 4.x）：

```powershell
.\build.ps1           # 编译 + 自检
.\build.ps1 -Install  # 编译 + 开机自启 + 启动
```

## 许可证

[MIT](LICENSE)
