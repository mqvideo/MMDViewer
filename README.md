# MMDViewer

Windows 上的 MMD（MikuMikuDance）模型查看器，使用 C# / .NET 8 / WinForms 编写，OpenGL 渲染。

## 功能

- 加载并渲染 PMX 模型，将 .pmx 文件拖入窗口即可
- 播放 VMD 动作
- 播放 VMD 镜头
- 基于 Bullet 模拟物理解算，可能与MMD的实现不完全一致。
- 逐像素光照（实验性）。对于特定模型光照可能效果奇差，如有能力还请自行修改代码。
- 模型自检：检测废顶点、离群顶点、孤立顶点，以及缺失、损坏、未被引用的纹理等问题，诊断结果写入程序目录下的 `{模型名}_diag.txt`
- 截图：以 3840×2160 离屏渲染，保存为 PNG / JPEG / BMP

## 构建与运行

```bash
git clone https://github.com/MQVideo/MMDViewer.git
cd MMDViewer
dotnet run
```

也可以直接用 Visual Studio 打开 MMDViewer.slnx 生成运行。

## 使用

- 拖入 .pmx 模型加载
- “加载动作”与“加载镜头”按钮分别选择 .vmd 文件
- 默认鼠标操作为左键拖拽旋转，右键拖拽平移，滚轮缩放；勾选“MMD操作风格”后为右键拖拽旋转，滚轮拖拽平移，滚轮缩放
- “报告模型问题”勾选后执行模型自检并生成诊断文件

## 依赖

- OpenTK.GLControl
- PMXParser
- Evergine.BulletSharpPInvoke、Evergine.LibBulletc.Natives

## 致谢

实现参考了 saba、babylon-mmd、MikuMikuFlex 以及 three.js。

## 许可证

MIT 许可证，见 [LICENSE](LICENSE)。
本项目与 MMD 官方毫无关系。

[作者个人主页](https://space.bilibili.com/79427854)
